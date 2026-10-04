using System.Text.RegularExpressions;

namespace FeishuDocExport;

/// <summary>
/// 从用户手里真正拿到的东西（飞书链接）里提取 Token。
/// 用户不会去开发者后台查什么是 spaceId / folderToken，他们只有地址栏里那条链接。
/// </summary>
public static class DocLinkParser
{
    /// <summary>知识库链接：…/wiki/space/&lt;数字 Id&gt;。</summary>
    private static readonly Regex SpaceLinkPattern = new(
        @"wiki/space/(?<id>\d{6,})",
        RegexOptions.CultureInvariant | RegexOptions.IgnoreCase);

    /// <summary>文件夹分享链接：…/drive/folder/&lt;token&gt; 或 …/folder/&lt;token&gt;。</summary>
    private static readonly Regex FolderLinkPattern = new(
        @"/folder/(?<token>[A-Za-z0-9_-]{8,})",
        RegexOptions.CultureInvariant);

    /// <summary>看起来是单篇文档的链接：…/wiki/&lt;nodeToken&gt;（没有 space/ 段）。</summary>
    private static readonly Regex DocLinkPattern = new(
        @"/wiki/[A-Za-z0-9_-]{8,}",
        RegexOptions.CultureInvariant);

    private static bool LooksLikeUrl(string value)
        => value.Contains("://", StringComparison.Ordinal) || value.StartsWith("www.", StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// 取出知识库 Id。裸 Id 原样返回；贴的是链接就从中提取。
    /// 无法提取时返回 null，并通过 <paramref name="error"/> 给出可以直接展示给用户的提示。
    /// </summary>
    public static string? ExtractSpaceId(string? input, out string? error)
    {
        error = null;
        var value = input?.Trim();

        if (string.IsNullOrEmpty(value))
        {
            return null;
        }

        var match = SpaceLinkPattern.Match(value);
        if (match.Success)
        {
            return match.Groups["id"].Value;
        }

        // 不是链接、也没匹配到 space 段：当成用户手填的裸 Id 放行
        if (!LooksLikeUrl(value))
        {
            return value;
        }

        error = DocLinkPattern.IsMatch(value)
            ? "这看起来是某一篇文档的链接，不是知识库链接。请打开知识库主页，地址栏形如 …/wiki/space/7123456789012345678，把那一条贴进来。"
            : "没能从这条链接里找到知识库 Id，请确认贴的是知识库主页链接（地址里带 /wiki/space/）。";

        return null;
    }

    /// <summary>取出文件夹 Token，规则与 <see cref="ExtractSpaceId"/> 相同。</summary>
    public static string? ExtractFolderToken(string? input, out string? error)
    {
        error = null;
        var value = input?.Trim();

        if (string.IsNullOrEmpty(value))
        {
            return null;
        }

        var match = FolderLinkPattern.Match(value);
        if (match.Success)
        {
            return match.Groups["token"].Value;
        }

        if (!LooksLikeUrl(value))
        {
            return value;
        }

        // 知识库的链接（含主页和里面的某一页）经常被贴到这里，说清楚该用哪个方式
        error = value.Contains("/wiki/", StringComparison.OrdinalIgnoreCase)
            ? "这是知识库（wiki）的链接，不是云文档文件夹。要导知识库请改用上面的「知识库（wiki）」方式；"
              + "知识库里的子目录不用单独填，整个库会按原来的层级一起导出来。"
            : "没能从这条链接里找到文件夹 Token，请确认贴的是云文档文件夹的分享链接（地址里带 /folder/）。";

        return null;
    }
}
