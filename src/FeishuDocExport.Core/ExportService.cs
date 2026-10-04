using System.Diagnostics;
using FeishuDocExport.Models;

namespace FeishuDocExport;

/// <summary>
/// 导出编排：读取文档树 → 生成落盘路径 → 逐个导出 → 汇总结果。
///
/// 旧实现把这段逻辑直接写在 Program.Main 里，cloudDoc 和 wiki 两个分支几乎逐行重复了 110 行，
/// 而且「原样下载文件」的分支在 catch 里漏了 continue，下载失败后会继续用一个文件 token
/// 去创建导出任务，产生第二条报错并把同一个文档重复记进失败清单。
/// </summary>
public sealed class ExportService
{
    private readonly FeishuApiClient _client;

    /// <summary>增量模式下每成功导出多少篇就把状态文件落一次盘。</summary>
    private const int StateCheckpointEvery = 20;

    public ExportService(FeishuApiClient client)
    {
        _client = client ?? throw new ArgumentNullException(nameof(client));
    }

    /// <summary>列出所有知识库，供界面选择。</summary>
    public Task<IReadOnlyList<WikiSpace>> GetWikiSpacesAsync(CancellationToken ct)
        => _client.GetWikiSpacesAsync(ct);

    /// <summary>获取知识库名称（仅用于界面展示）。</summary>
    public Task<string?> TryGetWikiSpaceNameAsync(string spaceId, CancellationToken ct)
        => _client.TryGetWikiSpaceNameAsync(spaceId, ct);

    /// <summary>获取个人空间文件夹名称（仅用于界面展示）。</summary>
    public Task<string?> TryGetFolderNameAsync(string folderToken, CancellationToken ct)
        => _client.TryGetFolderNameAsync(folderToken, ct);

    /// <summary>按配置读取文档树，供「预览将导出哪些文档」使用。</summary>
    public Task<IReadOnlyList<DocNode>> FetchNodesAsync(
        ExportOptions options,
        IProgress<ExportProgress>? progress,
        CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(options);
        return FetchNodesCoreAsync(options, progress, ct);
    }

    /// <summary>
    /// 执行一次完整导出。用户取消时不会抛异常，而是返回 <see cref="ExportResult.Canceled"/> 为 true 的结果。
    /// </summary>
    public async Task<ExportResult> RunAsync(
        ExportOptions options,
        IProgress<ExportProgress>? progress,
        CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(options);

        var validationErrors = options.Validate();
        if (validationErrors.Count > 0)
        {
            throw new ArgumentException(string.Join(Environment.NewLine, validationErrors), nameof(options));
        }

        var stopwatch = Stopwatch.StartNew();
        var failures = new List<ExportFailure>();
        var succeeded = 0;
        var total = 0;
        var canceled = false;

        Directory.CreateDirectory(options.ExportPath);

        var nodes = await FetchNodesCoreAsync(options, progress, ct).ConfigureAwait(false);

        progress?.Report(new ExportProgress($"共读取到 {nodes.Count} 个节点，正在生成目录结构…"));

        var pathBuilder = ExportPathBuilder.Build(
            nodes,
            options.ExportPath,
            node => SelectExtension(node, options));

        // 每个文档只保留首次出现的位置，避免同一个文档被导出多次
        var nodeByDocKey = new Dictionary<string, DocNode>(StringComparer.Ordinal);
        foreach (var node in nodes)
        {
            if (!node.IsFolder && !nodeByDocKey.ContainsKey(node.DocKey))
            {
                nodeByDocKey[node.DocKey] = node;
            }
        }

        // 不支持导出的类型：单独报一次，不混进「导出失败」清单
        foreach (var node in nodeByDocKey.Values)
        {
            if (pathBuilder.Targets.ContainsKey(node.DocKey))
            {
                continue;
            }

            var reason = ObjTypeMapper.DescribeUnsupported(node.ObjType);
            failures.Add(new ExportFailure(node.Title, reason, IsSkipped: true));
            progress?.Report(new ExportProgress($"跳过「{node.Title}」：{reason}", ExportLogLevel.Warning));
        }

        var documents = pathBuilder.DocumentOrder;

        // 增量模式：先照状态文件把没变过的挑出去，进度条就只表示真正要干的活
        ExportState? state = options.Incremental ? ExportStateStore.Load(options.ExportPath) : null;
        var pending = new List<PendingDoc>();
        var unchanged = 0;

        foreach (var docKey in documents)
        {
            var node = nodeByDocKey[docKey];
            var target = pathBuilder.GetTargetByDocKey(docKey)
                ?? throw new InvalidOperationException($"内部错误：文档 {docKey} 没有生成落盘路径。");

            if (state is not null && IsUnchanged(state, options.ExportPath, node, target))
            {
                unchanged++;
                continue;
            }

            pending.Add(new PendingDoc(node, target, IsNew: state is not null && !state.Docs.ContainsKey(docKey)));
        }

        total = pending.Count;

        if (state is not null)
        {
            progress?.Report(new ExportProgress(
                $"共 {documents.Count} 篇可导出文档：{unchanged} 篇没有变动，{total} 篇需要导出。",
                total == 0 ? ExportLogLevel.Success : ExportLogLevel.Info));
        }

        MarkdownConverter? markdownConverter = null;
        if (options.Format == ExportFormat.Markdown && total > 0)
        {
            markdownConverter = new MarkdownConverter(options.AsposeLicensePath);
            var status = markdownConverter.LicenseStatus;
            progress?.Report(new ExportProgress(
                status.Message,
                status.IsLicensed ? ExportLogLevel.Info : ExportLogLevel.Warning));
        }

        // markdown 里的跨文档引用：先按 wiki 节点 token 找，再按文档 token 找
        ExportTarget? ResolveReference(string token)
            => pathBuilder.GetTargetByNodeKey(token) ?? pathBuilder.GetTargetByDocKey(token);

        var index = 0;
        var added = 0;
        var updated = 0;

        try
        {
            foreach (var item in pending)
            {
                ct.ThrowIfCancellationRequested();

                var node = item.Node;
                var target = item.Target;

                index++;

                if (options.SkipExistingFiles && File.Exists(target.FilePath))
                {
                    succeeded++;
                    progress?.Report(new ExportProgress(
                        $"已存在，跳过：{node.Title}",
                        ExportLogLevel.Info, index, total, node.Title));
                    continue;
                }

                progress?.Report(new ExportProgress(
                    $"正在导出（{index}/{total}）：{node.Title}",
                    ExportLogLevel.Info, index, total, node.Title));

                try
                {
                    await ExportOneAsync(node, target, options, markdownConverter, ResolveReference, progress, ct)
                        .ConfigureAwait(false);

                    succeeded++;

                    if (state is not null)
                    {
                        if (item.IsNew)
                        {
                            added++;
                        }
                        else
                        {
                            updated++;
                        }

                        RecordExported(state, options.ExportPath, node, target);

                        // 每若干篇落一次盘，中断或容器被杀时不至于白跑
                        if (succeeded % StateCheckpointEvery == 0)
                        {
                            ExportStateStore.Save(options.ExportPath, state);
                        }
                    }

                    progress?.Report(new ExportProgress(
                        $"导出成功：{node.Title}",
                        ExportLogLevel.Success, index, total, node.Title));
                }
                catch (OperationCanceledException)
                {
                    throw;
                }
                catch (FeishuApiException ex) when (ex.IsPermissionDenied)
                {
                    failures.Add(new ExportFailure(node.Title, ex.ToUserMessage(), IsSkipped: true));
                    progress?.Report(new ExportProgress(
                        $"跳过「{node.Title}」：{ex.ToUserMessage()}",
                        ExportLogLevel.Warning, index, total, node.Title));
                }
                catch (Exception ex)
                {
                    // 单篇失败不影响整体，记录下来继续下一篇（旧实现也是这个策略，但会漏报）
                    failures.Add(new ExportFailure(node.Title, ex.Message, IsSkipped: false));
                    progress?.Report(new ExportProgress(
                        $"导出「{node.Title}」失败：{ex.Message}",
                        ExportLogLevel.Error, index, total, node.Title));
                }
            }
        }
        catch (OperationCanceledException)
        {
            canceled = true;
            progress?.Report(new ExportProgress("导出已被用户取消。", ExportLogLevel.Warning, index, total));
        }
        finally
        {
            // 取消、抛异常、正常结束都要把状态留住的，否则下次又从头全导一遍
            if (state is not null)
            {
                ExportStateStore.Save(options.ExportPath, state);
            }
        }

        stopwatch.Stop();

        return new ExportResult(succeeded, total, failures, stopwatch.Elapsed, canceled, added, updated, unchanged);
    }

    /// <summary>增量判定后待导出的一项。</summary>
    private sealed record PendingDoc(DocNode Node, ExportTarget Target, bool IsNew);

    /// <summary>
    /// 判断文档自上次导出以来有没有变过：编辑时间没变、落盘路径没变、文件确实还在，才算没变。
    /// 任一条不满足或拿不到编辑时间，都当作变过重新导出——宁可多导，不能漏导。
    /// </summary>
    private static bool IsUnchanged(ExportState state, string exportPath, DocNode node, ExportTarget target)
    {
        if (!state.Docs.TryGetValue(node.DocKey, out var entry))
        {
            return false;
        }

        if (string.IsNullOrEmpty(node.EditTime)
            || !string.Equals(entry.EditTime, node.EditTime, StringComparison.Ordinal))
        {
            return false;
        }

        // 只改标题时编辑时间也可能不变，靠路径比对兜住
        if (!string.Equals(
                entry.RelativePath,
                ExportStateStore.ToRelativePath(exportPath, target.FilePath),
                StringComparison.Ordinal))
        {
            return false;
        }

        return File.Exists(ExportStateStore.ResolvePath(exportPath, entry));
    }

    private static void RecordExported(
        ExportState state,
        string exportPath,
        DocNode node,
        ExportTarget target)
    {
        state.Docs[node.DocKey] = new ExportStateEntry
        {
            EditTime = node.EditTime ?? string.Empty,
            RelativePath = ExportStateStore.ToRelativePath(exportPath, target.FilePath),
            ExportedAt = DateTimeOffset.Now,
        };
    }

    private static string? NullIfBlank(string? value)
        => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private async Task ExportOneAsync(
        DocNode node,
        ExportTarget target,
        ExportOptions options,
        MarkdownConverter? markdownConverter,
        Func<string, ExportTarget?> resolveReference,
        IProgress<ExportProgress>? progress,
        CancellationToken ct)
    {
        var plan = ObjTypeMapper.Resolve(node.ObjType, options.Format)
            ?? throw new InvalidOperationException(ObjTypeMapper.DescribeUnsupported(node.ObjType));

        // 分支 1：云空间里的普通文件，直接下载，不走导出任务
        if (plan.IsDirectFileDownload)
        {
            var file = await _client.DownloadFileAsync(node.DocKey, ct).ConfigureAwait(false);
            await SaveAsync(target.FilePath, file.Content, ct).ConfigureAwait(false);
            return;
        }

        // 分支 2：文档 / 表格，先创建导出任务再下载产物
        var ticket = await _client
            .CreateExportTaskAsync(
                plan.RequestExtension,
                node.DocKey,
                ObjTypeMapper.NormalizeRequestType(node.ObjType),
                ct)
            .ConfigureAwait(false);

        var taskResult = await _client
            .WaitForExportTaskAsync(ticket, node.DocKey, node.Title, progress, ct)
            .ConfigureAwait(false);

        var downloaded = await _client
            .DownloadExportFileAsync(taskResult.FileToken!, ct)
            .ConfigureAwait(false);

        if (plan.ConvertToMarkdown)
        {
            var converter = markdownConverter
                ?? throw new InvalidOperationException("markdown 转换器未初始化。");

            await converter.ConvertAsync(downloaded.Content, target.FilePath, resolveReference, ct)
                .ConfigureAwait(false);
            return;
        }

        await SaveAsync(target.FilePath, downloaded.Content, ct).ConfigureAwait(false);
    }

    private static async Task SaveAsync(string filePath, byte[] content, CancellationToken ct)
    {
        var directory = Path.GetDirectoryName(filePath);
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }

        await File.WriteAllBytesAsync(filePath, content, ct).ConfigureAwait(false);
    }

    private async Task<IReadOnlyList<DocNode>> FetchNodesCoreAsync(
        ExportOptions options,
        IProgress<ExportProgress>? progress,
        CancellationToken ct)
    {
        if (options.SourceType == DocSourceType.CloudDoc)
        {
            var folderToken = options.FolderToken!;
            var files = await _client
                .GetFolderChildrenRecursiveAsync(folderToken, progress, ct)
                .ConfigureAwait(false);

            return files
                .Where(f => !string.IsNullOrWhiteSpace(f.Token))
                .Select(f => new DocNode
                {
                    NodeKey = f.Token!,
                    DocKey = f.Token!,
                    Title = string.IsNullOrWhiteSpace(f.Name) ? f.Token! : f.Name!,
                    ObjType = (f.Type ?? string.Empty).Trim().ToLowerInvariant(),
                    EditTime = NullIfBlank(f.ModifiedTime),
                    // 顶层文件的 parent_token 就是传入的 folderToken，归一化成根节点
                    ParentKey = string.IsNullOrWhiteSpace(f.ParentToken) ||
                                string.Equals(f.ParentToken, folderToken, StringComparison.Ordinal)
                        ? null
                        : f.ParentToken,
                })
                .ToList();
        }

        var spaceId = options.WikiSpaceId!;
        var wikiNodes = await _client
            .GetAllWikiNodesAsync(spaceId, progress, ct)
            .ConfigureAwait(false);

        return wikiNodes
            .Where(n => !string.IsNullOrWhiteSpace(n.NodeToken))
            .Select(n => new DocNode
            {
                NodeKey = n.NodeToken!,
                DocKey = string.IsNullOrWhiteSpace(n.ObjToken) ? n.NodeToken! : n.ObjToken!,
                Title = string.IsNullOrWhiteSpace(n.Title) ? n.NodeToken! : n.Title!,
                ObjType = (n.ObjType ?? string.Empty).Trim().ToLowerInvariant(),
                EditTime = NullIfBlank(n.ObjEditTime),
                ParentKey = string.IsNullOrWhiteSpace(n.ParentNodeToken) ? null : n.ParentNodeToken,
            })
            .ToList();
    }

    /// <summary>
    /// 计算节点落盘使用的扩展名。返回 null 表示该节点不落盘（目录或不支持的类型）。
    /// </summary>
    private static string? SelectExtension(DocNode node, ExportOptions options)
    {
        if (node.IsFolder)
        {
            return null;
        }

        var plan = ObjTypeMapper.Resolve(node.ObjType, options.Format);
        if (plan is null)
        {
            return null;
        }

        // 原样下载的文件：扩展名跟着原文件名走
        return plan.IsDirectFileDownload
            ? Path.GetExtension(node.Title).TrimStart('.')
            : plan.SaveExtension;
    }
}
