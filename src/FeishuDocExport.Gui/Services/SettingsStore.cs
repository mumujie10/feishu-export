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

    public bool SkipExistingFiles { get; set; }
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
