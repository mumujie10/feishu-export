namespace FeishuDocExport.Gui.ViewModels;

/// <summary>日志面板中的一行。</summary>
public sealed class LogEntry
{
    public LogEntry(DateTime time, ExportLogLevel level, string message)
    {
        Time = time.ToString("HH:mm:ss");
        Level = level switch
        {
            ExportLogLevel.Success => "成功",
            ExportLogLevel.Warning => "警告",
            ExportLogLevel.Error => "错误",
            _ => "信息",
        };
        Message = message;
        IsError = level == ExportLogLevel.Error;
        IsWarning = level == ExportLogLevel.Warning;
    }

    public string Time { get; }

    public string Level { get; }

    public string Message { get; }

    public bool IsError { get; }

    public bool IsWarning { get; }

    public override string ToString() => $"[{Time}] {Message}";
}

/// <summary>未导出清单中的一行。</summary>
public sealed class FailureItem
{
    public FailureItem(string title, string reason, bool isSkipped)
    {
        Title = title;
        Reason = reason;
        Kind = isSkipped ? "跳过" : "失败";
    }

    public string Title { get; }

    public string Reason { get; }

    public string Kind { get; }
}
