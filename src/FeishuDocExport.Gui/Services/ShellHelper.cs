using System.Diagnostics;

namespace FeishuDocExport.Gui.Services;

/// <summary>调用系统能力的小工具。</summary>
public static class ShellHelper
{
    /// <summary>用系统文件管理器打开目录。</summary>
    public static bool TryOpenFolder(string path, out string? error)
    {
        error = null;

        try
        {
            Directory.CreateDirectory(path);

            if (OperatingSystem.IsWindows())
            {
                Process.Start(new ProcessStartInfo("explorer.exe", $"\"{path}\"") { UseShellExecute = true });
            }
            else if (OperatingSystem.IsMacOS())
            {
                Process.Start("open", new[] { path });
            }
            else
            {
                Process.Start("xdg-open", new[] { path });
            }

            return true;
        }
        catch (Exception ex)
        {
            error = ex.Message;
            return false;
        }
    }
}
