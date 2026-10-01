using System.Text;
using System.Text.RegularExpressions;

namespace FeishuDocExport;

/// <summary>
/// 文件名清洗工具。
///
/// 旧实现有两个问题：一是名字超过 64 字符时用 PadLeft 生成「截断后」的名字，
/// 但 PadLeft 对超长字符串不会截断，等于什么都没做；二是没有任何同名去重，
/// 同一目录下的同名文档会互相覆盖。
/// </summary>
public static class FileNameHelper
{
    /// <summary>
    /// 单个路径片段允许的最大 UTF-8 字节数。
    /// Linux/macOS 的文件名上限是 255 字节，留出扩展名和 " (2)" 后缀的余量。
    /// </summary>
    public const int DefaultMaxBytes = 200;

    private static readonly Regex InvalidCharsRegex = new(@"[\\/:*?""<>|\x00-\x1F]", RegexOptions.Compiled);

    private static readonly Regex WhitespaceRegex = new(@"\s+", RegexOptions.Compiled);

    private static readonly HashSet<string> ReservedNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "CON", "PRN", "AUX", "NUL",
        "COM1", "COM2", "COM3", "COM4", "COM5", "COM6", "COM7", "COM8", "COM9",
        "LPT1", "LPT2", "LPT3", "LPT4", "LPT5", "LPT6", "LPT7", "LPT8", "LPT9",
    };

    /// <summary>
    /// 把文档标题清洗成可以安全落盘的路径片段：
    /// 替换非法字符、压缩空白、去掉结尾的点、规避 Windows 保留名，并按字节数截断。
    /// </summary>
    /// <param name="name">原始标题。</param>
    /// <param name="fallback">标题为空或清洗后为空时使用的兜底名字。</param>
    /// <param name="maxBytes">单个片段允许的最大 UTF-8 字节数。</param>
    public static string Sanitize(string? name, string fallback = "未命名文档", int maxBytes = DefaultMaxBytes)
    {
        var value = name ?? string.Empty;
        value = InvalidCharsRegex.Replace(value, "-");
        value = WhitespaceRegex.Replace(value, " ").Trim();
        // Windows 上以点或空格结尾的文件名会被静默截断，这里主动去掉
        value = value.TrimEnd('.', ' ');

        if (value.Length == 0)
        {
            value = fallback;
        }

        // Windows 保留设备名：CON、NUL、COM1 等（带扩展名同样被保留）
        var stem = value.Split('.')[0];
        if (ReservedNames.Contains(stem))
        {
            value = "_" + value;
        }

        return TruncateToUtf8Bytes(value, maxBytes);
    }

    /// <summary>
    /// 按 UTF-8 字节数截断字符串，不会把一个字符（含代理对）截成半个。
    /// </summary>
    public static string TruncateToUtf8Bytes(string value, int maxBytes)
    {
        if (string.IsNullOrEmpty(value) || maxBytes <= 0)
        {
            return value;
        }

        if (Encoding.UTF8.GetByteCount(value) <= maxBytes)
        {
            return value;
        }

        var builder = new StringBuilder(value.Length);
        var used = 0;

        for (var i = 0; i < value.Length; i++)
        {
            var ch = value[i];
            int charBytes;
            int charCount = 1;

            if (char.IsHighSurrogate(ch) && i + 1 < value.Length && char.IsLowSurrogate(value[i + 1]))
            {
                charBytes = 4;
                charCount = 2;
            }
            else if (char.IsSurrogate(ch))
            {
                // 落单的代理字符无法编码成合法 UTF-8，直接丢弃
                continue;
            }
            else
            {
                charBytes = Utf8Length(ch);
            }

            if (used + charBytes > maxBytes)
            {
                break;
            }

            builder.Append(ch);
            if (charCount == 2)
            {
                builder.Append(value[i + 1]);
                i++;
            }

            used += charBytes;
        }

        return builder.ToString();
    }

    /// <summary>
    /// 返回一个在当前目录内不重复的名字；重名时依次追加 " (2)"、" (3)"……
    /// </summary>
    /// <param name="name">候选名字。</param>
    /// <param name="usedNames">该目录下已经占用的名字集合（调用方需使用忽略大小写的比较器）。</param>
    public static string MakeUnique(string name, ISet<string> usedNames)
    {
        if (usedNames.Add(name))
        {
            return name;
        }

        for (var i = 2; i < 10_000; i++)
        {
            var candidate = $"{name} ({i})";
            if (usedNames.Add(candidate))
            {
                return candidate;
            }
        }

        var unique = $"{name} ({Guid.NewGuid():N})";
        usedNames.Add(unique);
        return unique;
    }

    private static int Utf8Length(char ch) => ch switch
    {
        < '\u0080' => 1,
        < '\u0800' => 2,
        _ => 3,
    };
}
