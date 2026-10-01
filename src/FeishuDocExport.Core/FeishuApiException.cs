namespace FeishuDocExport;

/// <summary>
/// 调用飞书开放平台失败时抛出。把「HTTP 状态码 / 飞书业务错误码 / 原始响应体」都带出来，
/// 避免像旧实现那样把异常吞掉后返回 null，导致文档静默丢失。
/// </summary>
public sealed class FeishuApiException : Exception
{
    public FeishuApiException(string message, int code = -1, string? responseBody = null, Exception? inner = null)
        : base(message, inner)
    {
        Code = code;
        ResponseBody = responseBody;
    }

    /// <summary>飞书业务错误码；HTTP 层失败时为 HTTP 状态码；未知为 -1。</summary>
    public int Code { get; }

    /// <summary>原始响应体，便于排查。</summary>
    public string? ResponseBody { get; }

    /// <summary>无阅读/导出权限。属于「跳过并提示用户手动下载」的场景，而不是致命错误。</summary>
    public bool IsPermissionDenied => Code == 1069902 || Code == 1061002 || Code == 1061003;

    /// <summary>应用凭证无效（AppId / AppSecret 填错）。</summary>
    public bool IsInvalidCredential => Code is 10003 or 10014 or 99991663 or 99991664 or 99991661;

    /// <summary>触发限流。</summary>
    public bool IsRateLimited => Code is 99991400 or 429;

    /// <summary>给用户看的友好描述。</summary>
    public string ToUserMessage()
    {
        if (IsInvalidCredential)
        {
            return $"应用凭证无效（错误码 {Code}）：请检查 AppId / AppSecret 是否正确，以及应用是否已发布。原始信息：{Message}";
        }

        if (IsPermissionDenied)
        {
            return $"没有阅读或导出权限（错误码 {Code}），请把文档/知识库授权给自建应用，或手动下载。";
        }

        if (IsRateLimited)
        {
            return $"请求过于频繁被限流（错误码 {Code}），稍后重试。";
        }

        return string.IsNullOrWhiteSpace(Message) ? $"调用飞书接口失败（错误码 {Code}）。" : Message;
    }
}
