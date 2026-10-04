using System.Text.Json;
using System.Text.Json.Serialization;

namespace FeishuDocExport.Gui.Services;

/// <summary>
/// 界面配置。保存在用户配置目录下的 settings.json，下次启动自动恢复。
///
/// 注意：AppSecret 会以明文写入该文件（不写入系统密钥链，避免引入平台相关依赖）。
/// 如果介意，可以不勾选「记住 AppSecret」，或直接删除该文件。
/// </summary>
public sealed class AppSettings
{
    public string AppId { get; set; } = string.Empty;

    public string AppSecret { get; set; } = string.Empty;

    public bool RememberAppSecret { get; set; } = true;

    public string BaseUrl { get; set; } = FeishuEndpoints.DefaultOpenApi;

    public int EndpointIndex { get; set; }

    public bool IsWikiSource { get; set; } = true;

    public string WikiSpaceId { get; set; } = string.Empty;

    public string FolderToken { get; set; } = string.Empty;

    public string ExportPath { get; set; } = string.Empty;

    public int FormatIndex { get; set; }

    public string LicensePath { get; set; } = string.Empty;

    /// <summary>同步方式：0=全部重新导出，1=跳过已存在的文件，2=只导新增和改动的。</summary>
    public int SyncModeIndex { get; set; } = 2;

    /// <summary>是否开启自动同步（需要程序保持开着）。</summary>
    public bool AutoSyncEnabled { get; set; }

    /// <summary>自动同步方式：0=固定间隔，1=每天定点。</summary>
    public int AutoSyncModeIndex { get; set; } = 1;

    /// <summary>固定间隔的小时数。</summary>
    public int AutoSyncIntervalHours { get; set; } = 6;

    /// <summary>每天定点的 24 小时制时刻，形如 03:00。</summary>
    public string AutoSyncAtTime { get; set; } = "03:00";

    /// <summary>0=跟随系统，1=浅色，2=深色。</summary>
    public int ThemeMode { get; set; }

    /// <summary>是否已经看完首启引导。</summary>
    public bool OnboardingSeen { get; set; }
}

/// <summary>负责配置文件的读写。</summary>
public static class SettingsStore
{
    private static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.Never,
    };

    /// <summary>配置文件所在目录。</summary>
    public static string DirectoryPath { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "FeishuDocExport");

    /// <summary>配置文件完整路径。</summary>
    public static string FilePath { get; } = Path.Combine(DirectoryPath, "settings.json");

    public static AppSettings Load()
    {
        try
        {
            if (!File.Exists(FilePath))
            {
                return new AppSettings();
            }

            var json = File.ReadAllText(FilePath);
            return JsonSerializer.Deserialize<AppSettings>(json, Options) ?? new AppSettings();
        }
        catch (Exception)
        {
            // 配置文件损坏时退回默认值，不要让界面起不来
            return new AppSettings();
        }
    }

    public static void Save(AppSettings settings)
    {
        try
        {
            Directory.CreateDirectory(DirectoryPath);
            var json = JsonSerializer.Serialize(settings, Options);
            File.WriteAllText(FilePath, json);
        }
        catch (Exception)
        {
            // 保存失败不影响使用
        }
    }
}
