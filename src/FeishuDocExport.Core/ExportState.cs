using System.Text.Json;
using System.Text.Json.Serialization;

namespace FeishuDocExport;

/// <summary>状态文件里一篇文档的记录。</summary>
public sealed class ExportStateEntry
{
    /// <summary>导出时飞书给的编辑时间，下次比对用。</summary>
    [JsonPropertyName("edit")]
    public string EditTime { get; set; } = string.Empty;

    /// <summary>落盘路径（相对导出目录）。存相对路径是为了换机器、换挂载点仍然认得出来。</summary>
    [JsonPropertyName("path")]
    public string RelativePath { get; set; } = string.Empty;

    /// <summary>导出完成时间。</summary>
    [JsonPropertyName("at")]
    public DateTimeOffset ExportedAt { get; set; }
}

/// <summary>
/// 导出目录里的一份增量状态快照，供「只导出新增和改动过的文档」使用。
/// 文件放在导出目录内而不是用户配置目录，这样 Docker 挂 volume、换机器都不会丢。
/// </summary>
public sealed class ExportState
{
    [JsonPropertyName("version")]
    public int Version { get; set; } = ExportStateStore.CurrentVersion;

    [JsonPropertyName("docs")]
    public Dictionary<string, ExportStateEntry> Docs { get; set; } = new(StringComparer.Ordinal);
}

/// <summary>状态文件的读写。读写失败一律不影响导出本身。</summary>
public static class ExportStateStore
{
    /// <summary>状态文件名，放在导出目录根部。</summary>
    public const string FileName = ".feishu-export-state.json";

    /// <summary>当前状态文件格式版本，对不上时当作没有历史重新全量导出。</summary>
    public const int CurrentVersion = 1;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
    };

    public static string PathOf(string exportPath) => Path.Combine(exportPath, FileName);

    /// <summary>读状态文件。不存在、格式损坏或版本不认识时返回空状态，等价于做一次全量导出。</summary>
    public static ExportState Load(string exportPath)
    {
        var path = PathOf(exportPath);

        try
        {
            if (!File.Exists(path))
            {
                return new ExportState();
            }

            var state = JsonSerializer.Deserialize<ExportState>(File.ReadAllText(path), JsonOptions);

            // 版本对不上时不猜格式，当作没有历史，重新导一遍最安全
            if (state is null || state.Version != CurrentVersion)
            {
                return new ExportState();
            }

            state.Docs ??= new Dictionary<string, ExportStateEntry>(StringComparer.Ordinal);
            return state;
        }
        catch (Exception)
        {
            return new ExportState();
        }
    }

    /// <summary>原子写：先写临时文件再改名覆盖，避免中断时留下半个 JSON。</summary>
    public static void Save(string exportPath, ExportState state)
    {
        var path = PathOf(exportPath);
        var temp = path + ".tmp";

        try
        {
            Directory.CreateDirectory(exportPath);
            File.WriteAllText(temp, JsonSerializer.Serialize(state, JsonOptions));
            File.Move(temp, path, overwrite: true);
        }
        catch (Exception)
        {
            try
            {
                File.Delete(temp);
            }
            catch (IOException)
            {
                // 临时文件都删不掉就不用管了
            }
        }
    }

    /// <summary>把绝对落盘路径转成相对导出目录的路径，用于跨机器比对。</summary>
    public static string ToRelativePath(string exportPath, string filePath)
        => Path.GetRelativePath(exportPath, filePath).Replace('\\', '/');

    /// <summary>把状态里记录的相对路径还原成当前环境下的绝对路径。</summary>
    public static string ResolvePath(string exportPath, ExportStateEntry entry)
        => Path.GetFullPath(Path.Combine(exportPath, entry.RelativePath));
}
