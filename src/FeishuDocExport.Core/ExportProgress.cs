namespace FeishuDocExport;

/// <summary>
/// 导出进度通知。GUI 用它刷新进度条与日志，CLI 用它输出控制台日志。
/// </summary>
/// <param name="Message">日志文本。</param>
/// <param name="Level">日志级别。</param>
/// <param name="Current">已处理的文档数（从 1 开始），未知为 null。</param>
/// <param name="Total">文档总数，未知为 null。</param>
/// <param name="CurrentDocument">当前正在处理的文档标题。</param>
public sealed record ExportProgress(
    string Message,
    ExportLogLevel Level = ExportLogLevel.Info,
    int? Current = null,
    int? Total = null,
    string? CurrentDocument = null);

/// <summary>单个文档的失败/跳过记录。</summary>
/// <param name="Title">文档标题。</param>
/// <param name="Reason">失败或跳过原因。</param>
/// <param name="IsSkipped">true 表示「不支持的类型」这类预期内跳过；false 表示导出过程出错。</param>
public sealed record ExportFailure(string Title, string Reason, bool IsSkipped);

/// <summary>一次导出的最终结果。</summary>
public sealed class ExportResult
{
    public ExportResult(
        int succeeded,
        int total,
        IReadOnlyList<ExportFailure> failures,
        TimeSpan elapsed,
        bool canceled,
        int added = 0,
        int updated = 0,
        int unchanged = 0)
    {
        Succeeded = succeeded;
        Total = total;
        Failures = failures;
        Elapsed = elapsed;
        Canceled = canceled;
        Added = added;
        Updated = updated;
        Unchanged = unchanged;
    }

    /// <summary>成功导出的文档数。</summary>
    public int Succeeded { get; }

    /// <summary>参与导出的文档总数（不含被跳过的、以及纯目录节点）。</summary>
    public int Total { get; }

    /// <summary>失败与被跳过的文档。</summary>
    public IReadOnlyList<ExportFailure> Failures { get; }

    /// <summary>总耗时。</summary>
    public TimeSpan Elapsed { get; }

    /// <summary>是否由用户主动取消。</summary>
    public bool Canceled { get; }

    /// <summary>增量模式下本次新出现的文档数。</summary>
    public int Added { get; }

    /// <summary>增量模式下本次内容或文件名变了的文档数。</summary>
    public int Updated { get; }

    /// <summary>增量模式下判定为没变过、直接跳过的文档数。</summary>
    public int Unchanged { get; }

    /// <summary>是否全部成功。</summary>
    public bool AllSucceeded => Failures.Count == 0 && !Canceled;
}
