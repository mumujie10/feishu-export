using Avalonia;
using Avalonia.Styling;

namespace FeishuDocExport.Gui.Services;

/// <summary>把界面里的主题设置应用到整个应用。</summary>
public static class ThemeManager
{
    /// <summary>0=跟随系统，1=浅色，2=深色。其余值按跟随系统处理。</summary>
    public static void Apply(int themeMode)
    {
        if (Application.Current is not { } app)
        {
            return;
        }

        app.RequestedThemeVariant = themeMode switch
        {
            1 => ThemeVariant.Light,
            2 => ThemeVariant.Dark,
            _ => ThemeVariant.Default,
        };
    }
}
