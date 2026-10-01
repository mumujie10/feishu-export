using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using FeishuDocExport.Models;

namespace FeishuDocExport;

/// <summary>
/// 飞书开放平台客户端。
///
/// 相比旧实现（WebApiClientCore + 硬编码 HttpHost）的改动：
/// - 基础地址可配置，真正支持 Lark 国际版（旧实现的 --apiEndpoint 参数从头到尾没被读取过）；
/// - 所有失败都以 <see cref="FeishuApiException"/> 抛出，不再「吞掉异常返回 null」；
/// - 统一处理 tenant_access_token 缓存、限流重试、超时和取消；
/// - 二进制下载会先判断响应是不是 JSON 错误体，避免把错误 JSON 当成文档存到磁盘。
/// </summary>
public sealed class FeishuApiClient : IDisposable
{
    private const int PageSize = 50;

    /// <summary>飞书限流错误码。</summary>
    private const int RateLimitCode = 99991400;

    private static readonly TimeSpan ApiTimeout = TimeSpan.FromSeconds(120);
    private static readonly TimeSpan DownloadTimeout = TimeSpan.FromMinutes(30);
    private static readonly TimeSpan PollRequestTimeout = TimeSpan.FromSeconds(60);

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        NumberHandling = System.Text.Json.Serialization.JsonNumberHandling.AllowReadingFromString,
    };

    private readonly HttpClient _http;
    private readonly bool _ownsHttpClient;
    private readonly ExportOptions _options;
    private readonly string _baseUrl;
    private readonly SemaphoreSlim _tokenLock = new(1, 1);

    private string? _accessToken;
    private DateTimeOffset _accessTokenExpiresAt = DateTimeOffset.MinValue;

    public FeishuApiClient(ExportOptions options, HttpClient? httpClient = null)
    {
        ArgumentNullException.ThrowIfNull(options);

        _options = options;
        _baseUrl = (options.BaseUrl ?? FeishuEndpoints.DefaultOpenApi).TrimEnd('/');

        if (httpClient is null)
        {
            _http = new HttpClient(new SocketsHttpHandler
            {
                // 下载大文件时连接可能长时间空闲，交给每个请求自己的超时控制
                PooledConnectionIdleTimeout = TimeSpan.FromMinutes(2),
            })
            {
                Timeout = Timeout.InfiniteTimeSpan,
            };
            _ownsHttpClient = true;
        }
        else
        {
            _http = httpClient;
        }
    }

    #region 认证

    /// <summary>获取（并缓存）tenant_access_token。</summary>
    public async Task<string> GetTenantAccessTokenAsync(CancellationToken ct)
    {
        // 提前 5 分钟过期，避免边界上拿到一个马上就失效的 token
        if (_accessToken is not null && DateTimeOffset.UtcNow < _accessTokenExpiresAt - TimeSpan.FromMinutes(5))
        {
            return _accessToken;
        }

        await _tokenLock.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            if (_accessToken is not null && DateTimeOffset.UtcNow < _accessTokenExpiresAt - TimeSpan.FromMinutes(5))
            {
                return _accessToken;
            }

            var payload = JsonSerializer.SerializeToUtf8Bytes(new
            {
                app_id = _options.AppId,
                app_secret = _options.AppSecret,
            }, JsonOptions);

            using var request = new HttpRequestMessage(HttpMethod.Post, Url("/open-apis/auth/v3/tenant_access_token/internal"))
            {
                Content = new ByteArrayContent(payload),
            };
            request.Content.Headers.ContentType = new MediaTypeHeaderValue("application/json") { CharSet = "utf-8" };

            var body = await SendRawAsync(request, ApiTimeout, ct).ConfigureAwait(false);

            TenantAccessTokenResponse? parsed;
            try
            {
                parsed = JsonSerializer.Deserialize<TenantAccessTokenResponse>(body, JsonOptions);
            }
            catch (JsonException ex)
            {
                throw new FeishuApiException("获取 tenant_access_token 时返回了无法解析的内容。", -1, body, ex);
            }

            if (parsed is null || parsed.Code != 0 || string.IsNullOrWhiteSpace(parsed.TenantAccessToken))
            {
                // 旧实现拿到 code=10003 也照样往下跑，导致后面所有请求 401，报错信息完全对不上真实原因
                throw new FeishuApiException(
                    parsed?.Msg ?? "获取 tenant_access_token 失败。",
                    parsed?.Code ?? -1,
                    body);
            }

            _accessToken = parsed.TenantAccessToken;
            _accessTokenExpiresAt = DateTimeOffset.UtcNow.AddSeconds(parsed.Expire > 0 ? parsed.Expire : 7200);
            return _accessToken;
        }
        finally
        {
            _tokenLock.Release();
        }
    }

    #endregion

    #region 知识库

    /// <summary>分页获取租户下所有知识库。</summary>
    public async Task<IReadOnlyList<WikiSpace>> GetWikiSpacesAsync(CancellationToken ct)
    {
        var spaces = new List<WikiSpace>();
        string? pageToken = null;

        do
        {
            var query = $"?page_size={PageSize}";
            if (!string.IsNullOrWhiteSpace(pageToken))
            {
                query += $"&page_token={Uri.EscapeDataString(pageToken)}";
            }

            var data = await GetAsync<WikiSpacePagedList>($"/open-apis/wiki/v2/spaces{query}", ct).ConfigureAwait(false);
            if (data is null)
            {
                break;
            }

            spaces.AddRange(data.Items);
            pageToken = data.PageToken;
            if (!PagedListHelper.ShouldContinue(data.HasMore, pageToken))
            {
                break;
            }
        }
        while (true);

        return spaces;
    }

    /// <summary>获取知识库名称，失败时返回 null（仅用于界面展示，不应影响导出）。</summary>
    public async Task<string?> TryGetWikiSpaceNameAsync(string spaceId, CancellationToken ct)
    {
        try
        {
            var info = await GetAsync<WikiSpaceInfo>(
                $"/open-apis/wiki/v2/spaces/{Uri.EscapeDataString(spaceId)}", ct).ConfigureAwait(false);
            return info?.Space?.Name;
        }
        catch (FeishuApiException)
        {
            return null;
        }
    }

    /// <summary>
    /// 递归拉取知识库下的全部节点。
    /// </summary>
    public async Task<IReadOnlyList<WikiNode>> GetAllWikiNodesAsync(
        string spaceId,
        IProgress<ExportProgress>? progress,
        CancellationToken ct)
    {
        var all = new List<WikiNode>();
        var queue = new Queue<string?>();
        queue.Enqueue(null); // null 表示顶层

        while (queue.Count > 0)
        {
            ct.ThrowIfCancellationRequested();
            var parentNodeToken = queue.Dequeue();

            string? pageToken = null;
            do
            {
                ct.ThrowIfCancellationRequested();

                var query = new StringBuilder($"?page_size={PageSize}");
                if (!string.IsNullOrWhiteSpace(pageToken))
                {
                    query.Append($"&page_token={Uri.EscapeDataString(pageToken)}");
                }

                if (!string.IsNullOrWhiteSpace(parentNodeToken))
                {
                    query.Append($"&parent_node_token={Uri.EscapeDataString(parentNodeToken)}");
                }

                var data = await GetAsync<WikiNodePagedList>(
                    $"/open-apis/wiki/v2/spaces/{Uri.EscapeDataString(spaceId)}/nodes{query}", ct).ConfigureAwait(false);

                if (data is null)
                {
                    break;
                }

                all.AddRange(data.Items);

                foreach (var node in data.Items)
                {
                    if (node.HasChild && !string.IsNullOrWhiteSpace(node.NodeToken))
                    {
                        queue.Enqueue(node.NodeToken);
                    }
                }

                progress?.Report(new ExportProgress($"已读取 {all.Count} 个知识库节点…"));
                pageToken = data.PageToken;
                if (!PagedListHelper.ShouldContinue(data.HasMore, pageToken))
                {
                    break;
                }
            }
            while (true);
        }

        return all;
    }

    #endregion

    #region 个人空间云文档

    /// <summary>获取文件夹名称，失败时返回 null。</summary>
    public async Task<string?> TryGetFolderNameAsync(string folderToken, CancellationToken ct)
    {
        try
        {
            var meta = await GetAsync<CloudDocFolderMeta>(
                $"/open-apis/drive/explorer/v2/folder/{Uri.EscapeDataString(folderToken)}/meta", ct).ConfigureAwait(false);
            return meta?.Name;
        }
        catch (FeishuApiException)
        {
            return null;
        }
    }

    /// <summary>递归拉取某个文件夹下的全部文件与子文件夹。</summary>
    public async Task<IReadOnlyList<CloudDocFile>> GetFolderChildrenRecursiveAsync(
        string folderToken,
        IProgress<ExportProgress>? progress,
        CancellationToken ct)
    {
        var all = new List<CloudDocFile>();
        var queue = new Queue<string>();
        queue.Enqueue(folderToken);

        while (queue.Count > 0)
        {
            ct.ThrowIfCancellationRequested();
            var current = queue.Dequeue();

            string? pageToken = null;
            do
            {
                ct.ThrowIfCancellationRequested();

                var query = new StringBuilder($"?folder_token={Uri.EscapeDataString(current)}&page_size={PageSize}");
                if (!string.IsNullOrWhiteSpace(pageToken))
                {
                    query.Append($"&page_token={Uri.EscapeDataString(pageToken)}");
                }

                var data = await GetAsync<CloudDocPagedList>($"/open-apis/drive/v1/files{query}", ct).ConfigureAwait(false);
                if (data is null)
                {
                    break;
                }

                all.AddRange(data.Items);

                foreach (var file in data.Items)
                {
                    if (ObjTypeMapper.IsContainer(file.Type) && !string.IsNullOrWhiteSpace(file.Token))
                    {
                        queue.Enqueue(file.Token);
                    }
                }

                progress?.Report(new ExportProgress($"已读取 {all.Count} 个云文档条目…"));
                pageToken = data.EffectivePageToken;
                if (!PagedListHelper.ShouldContinue(data.HasMore, pageToken))
                {
                    break;
                }
            }
            while (true);
        }

        return all;
    }

    #endregion

    #region 导出任务

    /// <summary>创建导出任务，返回 ticket。</summary>
    public async Task<string> CreateExportTaskAsync(string fileExtension, string token, string type, CancellationToken ct)
    {
        var data = await PostAsync<ExportTaskTicket>("/open-apis/drive/v1/export_tasks", new
        {
            file_extension = fileExtension,
            token,
            type,
        }, ct).ConfigureAwait(false);

        if (data is null || string.IsNullOrWhiteSpace(data.Ticket))
        {
            throw new FeishuApiException("飞书没有返回导出任务票据（ticket）。");
        }

        return data.Ticket;
    }

    /// <summary>
    /// 轮询导出任务直到完成。
    /// 旧实现是 `do { ... } while (status != 0)`，没有任何超时和次数上限，
    /// 飞书侧任务卡住时程序会永久挂起。这里加上总超时和退避。
    /// </summary>
    public async Task<ExportTaskResult> WaitForExportTaskAsync(
        string ticket,
        string token,
        string documentTitle,
        IProgress<ExportProgress>? progress,
        CancellationToken ct)
    {
        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeoutCts.CancelAfter(_options.ExportTaskTimeout);

        var delay = TimeSpan.FromMilliseconds(300);

        try
        {
            while (true)
            {
                var url = $"/open-apis/drive/v1/export_tasks/{Uri.EscapeDataString(ticket)}" +
                          $"?token={Uri.EscapeDataString(token)}";

                var data = await GetAsync<ExportTaskQueryData>(url, timeoutCts.Token).ConfigureAwait(false);
                var result = data?.Result
                    ?? throw new FeishuApiException($"查询导出任务「{documentTitle}」时飞书未返回结果。");

                switch (result.JobStatus)
                {
                    case 0:
                        if (string.IsNullOrWhiteSpace(result.FileToken))
                        {
                            throw new FeishuApiException(
                                $"导出任务「{documentTitle}」状态为成功，但没有返回文件 token。原始信息：{result.JobErrorMsg}");
                        }

                        if (!string.Equals(result.JobErrorMsg, "success", StringComparison.OrdinalIgnoreCase) &&
                            !string.IsNullOrWhiteSpace(result.JobErrorMsg))
                        {
                            progress?.Report(new ExportProgress(
                                $"导出「{documentTitle}」返回了非 success 的提示：{result.JobErrorMsg}",
                                ExportLogLevel.Warning));
                        }

                        return result;

                    case 1:
                    case 2:
                        await Task.Delay(delay, timeoutCts.Token).ConfigureAwait(false);
                        delay = TimeSpan.FromMilliseconds(Math.Min(delay.TotalMilliseconds * 1.5, 2000));
                        break;

                    default:
                        throw new FeishuApiException(
                            $"导出「{documentTitle}」失败：{result.JobErrorMsg ?? "未知原因"}（job_status={result.JobStatus}）",
                            result.JobStatus);
                }
            }
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            throw new FeishuApiException(
                $"导出「{documentTitle}」等待超时（超过 {_options.ExportTaskTimeout.TotalMinutes:0.#} 分钟），已跳过。");
        }
    }

    /// <summary>下载导出任务产出的文件。</summary>
    public Task<DownloadedFile> DownloadExportFileAsync(string fileToken, CancellationToken ct)
        => DownloadBinaryAsync($"/open-apis/drive/v1/export_tasks/file/{Uri.EscapeDataString(fileToken)}/download", ct);

    /// <summary>原样下载云空间里的文件。</summary>
    public Task<DownloadedFile> DownloadFileAsync(string fileToken, CancellationToken ct)
        => DownloadBinaryAsync($"/open-apis/drive/v1/files/{Uri.EscapeDataString(fileToken)}/download", ct);

    #endregion

    #region 底层请求

    private string Url(string path) => _baseUrl + path;

    private async Task<T?> GetAsync<T>(string path, CancellationToken ct)
    {
        var token = await GetTenantAccessTokenAsync(ct).ConfigureAwait(false);
        using var request = new HttpRequestMessage(HttpMethod.Get, Url(path));
        request.Headers.TryAddWithoutValidation("Authorization", "Bearer " + token);
        return await SendJsonAsync<T>(request, path, ct).ConfigureAwait(false);
    }

    private async Task<T?> PostAsync<T>(string path, object payload, CancellationToken ct)
    {
        var token = await GetTenantAccessTokenAsync(ct).ConfigureAwait(false);

        // 显式按运行时类型序列化，避免匿名对象被当成 object 序列化出空对象
        var json = JsonSerializer.SerializeToUtf8Bytes(payload, payload.GetType(), JsonOptions);

        using var request = new HttpRequestMessage(HttpMethod.Post, Url(path))
        {
            Content = new ByteArrayContent(json),
        };
        request.Content.Headers.ContentType = new MediaTypeHeaderValue("application/json") { CharSet = "utf-8" };
        request.Headers.TryAddWithoutValidation("Authorization", "Bearer " + token);

        return await SendJsonAsync<T>(request, path, ct).ConfigureAwait(false);
    }

    /// <summary>发送请求、解析统一响应体，并把业务错误码转成异常。</summary>
    private async Task<T?> SendJsonAsync<T>(HttpRequestMessage request, string path, CancellationToken ct)
    {
        var attempts = Math.Max(1, _options.MaxRetryCount);

        for (var attempt = 1; ; attempt++)
        {
            // 飞书的限流是 HTTP 200 + 业务错误码 99991400，需要在业务层再重试一次
            var current = attempt == 1 ? request : await CloneAsync(request, ct).ConfigureAwait(false);
            var body = await SendRawAsync(current, ApiTimeout, ct).ConfigureAwait(false);

            FeishuResponse<T>? parsed;
            try
            {
                parsed = JsonSerializer.Deserialize<FeishuResponse<T>>(body, JsonOptions);
            }
            catch (JsonException ex)
            {
                throw new FeishuApiException($"接口 {path} 返回了无法解析的内容。", -1, Truncate(body), ex);
            }

            if (parsed is null)
            {
                throw new FeishuApiException($"接口 {path} 返回了空响应。", -1, Truncate(body));
            }

            if (parsed.Code == RateLimitCode && attempt < attempts)
            {
                await Task.Delay(Backoff(attempt), ct).ConfigureAwait(false);
                continue;
            }

            if (parsed.Code != 0)
            {
                throw new FeishuApiException(
                    string.IsNullOrWhiteSpace(parsed.Msg) ? $"接口 {path} 调用失败。" : parsed.Msg!,
                    parsed.Code,
                    Truncate(body));
            }

            return parsed.Data;
        }
    }

    private async Task<string> SendRawAsync(HttpRequestMessage request, TimeSpan timeout, CancellationToken ct)
    {
        // 超时用独立的 CTS 控制，并在正文读取完成之后才释放，避免读到一半句柄被回收
        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeoutCts.CancelAfter(timeout);

        using var response = await SendWithRetryAsync(request, timeoutCts.Token).ConfigureAwait(false);
        var body = await response.Content.ReadAsStringAsync(timeoutCts.Token).ConfigureAwait(false);

        if (!response.IsSuccessStatusCode)
        {
            throw BuildHttpException(response.StatusCode, body, request.RequestUri);
        }

        return body;
    }

    /// <summary>
    /// 下载二进制内容。故意先看 Content-Type：
    /// 飞书在出错时会用 HTTP 200 + JSON 错误体响应下载接口，
    /// 旧实现会把这段 JSON 直接当成文档写到磁盘上。
    /// </summary>
    private async Task<DownloadedFile> DownloadBinaryAsync(string path, CancellationToken ct)
    {
        var token = await GetTenantAccessTokenAsync(ct).ConfigureAwait(false);

        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeoutCts.CancelAfter(DownloadTimeout);

        using var request = new HttpRequestMessage(HttpMethod.Get, Url(path));
        request.Headers.TryAddWithoutValidation("Authorization", "Bearer " + token);
        using var response = await SendWithRetryAsync(request, timeoutCts.Token).ConfigureAwait(false);

        var mediaType = response.Content.Headers.ContentType?.MediaType;

        if (!response.IsSuccessStatusCode || (mediaType is not null && mediaType.Contains("json", StringComparison.OrdinalIgnoreCase)))
        {
            var text = await response.Content.ReadAsStringAsync(timeoutCts.Token).ConfigureAwait(false);

            if (response.IsSuccessStatusCode)
            {
                var parsed = TryParseError(text);
                throw new FeishuApiException(
                    parsed?.Msg ?? "下载文件失败：飞书返回了错误信息。",
                    parsed?.Code ?? -1,
                    Truncate(text));
            }

            throw BuildHttpException(response.StatusCode, text, request.RequestUri);
        }

        var bytes = await response.Content.ReadAsByteArrayAsync(timeoutCts.Token).ConfigureAwait(false);
        if (bytes.Length == 0)
        {
            throw new FeishuApiException("下载到的文件内容为空。");
        }

        var fileName = response.Content.Headers.ContentDisposition?.FileNameStar
                       ?? response.Content.Headers.ContentDisposition?.FileName;

        return new DownloadedFile(bytes, string.IsNullOrWhiteSpace(fileName) ? null : fileName.Trim('"'));
    }

    /// <summary>
    /// 带重试地发送请求，只负责「拿到响应」，超时由调用方通过 <paramref name="ct"/> 控制。
    /// 旧实现遇到限流或 5xx 直接失败，700 篇文档的导出里这是很常见的掉队原因。
    /// </summary>
    private async Task<HttpResponseMessage> SendWithRetryAsync(HttpRequestMessage request, CancellationToken ct)
    {
        var attempts = Math.Max(1, _options.MaxRetryCount);
        Exception? lastError = null;

        for (var attempt = 1; attempt <= attempts; attempt++)
        {
            ct.ThrowIfCancellationRequested();

            // HttpRequestMessage 不能重复发送，重试时克隆一份
            var current = attempt == 1 ? request : await CloneAsync(request, ct).ConfigureAwait(false);

            try
            {
                var response = await _http
                    .SendAsync(current, HttpCompletionOption.ResponseHeadersRead, ct)
                    .ConfigureAwait(false);

                if (IsTransient(response.StatusCode) && attempt < attempts)
                {
                    response.Dispose();
                    await Task.Delay(Backoff(attempt), ct).ConfigureAwait(false);
                    continue;
                }

                return response;
            }
            catch (HttpRequestException ex) when (attempt < attempts)
            {
                lastError = ex;
                await Task.Delay(Backoff(attempt), ct).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (!ct.IsCancellationRequested && attempt < attempts)
            {
                lastError = new TimeoutException("请求超时。");
                await Task.Delay(Backoff(attempt), ct).ConfigureAwait(false);
            }
        }

        throw new FeishuApiException(
            $"请求飞书接口失败，已重试 {attempts} 次。",
            -1,
            null,
            lastError);
    }

    /// <summary>克隆一个请求（含认证头与正文），用于重试。</summary>
    private static async Task<HttpRequestMessage> CloneAsync(HttpRequestMessage source, CancellationToken ct)
    {
        var clone = new HttpRequestMessage(source.Method, source.RequestUri);

        foreach (var header in source.Headers)
        {
            clone.Headers.TryAddWithoutValidation(header.Key, header.Value);
        }

        if (source.Content is not null)
        {
            var bytes = await source.Content.ReadAsByteArrayAsync(ct).ConfigureAwait(false);
            clone.Content = new ByteArrayContent(bytes);
            foreach (var header in source.Content.Headers)
            {
                clone.Content.Headers.TryAddWithoutValidation(header.Key, header.Value);
            }
        }

        return clone;
    }

    private static bool IsTransient(HttpStatusCode status)
        => status is HttpStatusCode.RequestTimeout
            or HttpStatusCode.TooManyRequests
            or HttpStatusCode.InternalServerError
            or HttpStatusCode.BadGateway
            or HttpStatusCode.ServiceUnavailable
            or HttpStatusCode.GatewayTimeout;

    private static TimeSpan Backoff(int attempt)
        => TimeSpan.FromMilliseconds(Math.Min(500 * Math.Pow(2, attempt - 1), 8000));

    private static FeishuApiException BuildHttpException(HttpStatusCode status, string body, Uri? uri)
    {
        var parsed = TryParseError(body);
        var detail = parsed?.Msg ?? Truncate(body);

        return new FeishuApiException(
            $"HTTP {(int)status} 调用 {uri?.AbsolutePath} 失败：{detail}",
            parsed?.Code is > 0 ? parsed!.Code : (int)status,
            Truncate(body));
    }

    private static FeishuErrorBody? TryParseError(string body)
    {
        try
        {
            return JsonSerializer.Deserialize<FeishuErrorBody>(body, JsonOptions);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static string Truncate(string? value, int max = 2000)
        => value is null ? string.Empty
            : value.Length <= max ? value : value[..max] + "…";

    #endregion

    public void Dispose()
    {
        _tokenLock.Dispose();
        if (_ownsHttpClient)
        {
            _http.Dispose();
        }
    }
}

/// <summary>下载到的文件内容与文件名。</summary>
/// <param name="Content">文件字节。</param>
/// <param name="FileName">服务端给出的文件名，可能为空。</param>
public sealed record DownloadedFile(byte[] Content, string? FileName);
