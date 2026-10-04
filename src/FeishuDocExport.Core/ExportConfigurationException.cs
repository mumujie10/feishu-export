namespace FeishuDocExport;

/// <summary>
/// 配置本身没问题、但按这份配置什么也导不出来时抛出。
/// 单独一个类型是为了让命令行和界面都能把它当「用户可修的错」显示，
/// 而不是打印一堆堆栈。
/// </summary>
public sealed class ExportConfigurationException(string message) : InvalidOperationException(message);
