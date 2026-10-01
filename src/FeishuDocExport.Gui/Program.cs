using Avalonia;

namespace FeishuDocExport.Gui;

internal static class Program
{
    // Avalonia 需要在任何第三方库（包括 Aspose.Words）之前完成初始化，
    // 因此这里不要做其它初始化工作。
    [STAThread]
    public static void Main(string[] args) => BuildAvaloniaApp()
        .StartWithClassicDesktopLifetime(args);

    public static AppBuilder BuildAvaloniaApp()
        => AppBuilder.Configure<App>()
            .UsePlatformDetect()
            .WithInterFont()
            .LogToTrace();
}
