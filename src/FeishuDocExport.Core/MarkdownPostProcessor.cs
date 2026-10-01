using System.Text.RegularExpressions;

namespace FeishuDocExport;

/// <summary>
/// docx 转成 markdown 之后的收尾处理。
///
/// 这些方法都是纯函数，方便单元测试覆盖（旧实现把它们和 Aspose 的调用混在一起，
/// 其中 <c>ReplaceCodeToMdFormat</c> 在内容里没有 '|' 时会抛 ArgumentOutOfRangeException）。
/// </summary>
public static partial class MarkdownPostProcessor
{
    /// <summary>markdown 图片语法：![alt](url)。</summary>
    [GeneratedRegex(@"!\[(?<alt>[^\]]*)\]\((?<url>[^)\r\n]+)\)", RegexOptions.IgnoreCase)]
    private static partial Regex ImageRegex();

    /// <summary>
    /// 飞书文档链接：https://xxx.feishu.cn/wiki/xxxx、/docx/xxxx、/sheets/xxxx 等。
    /// 旧实现只匹配 feishu.cn/wiki，导致 docx 直链和 Lark（larksuite.com）链接都不会被本地化。
    /// </summary>
    [GeneratedRegex(
        @"\[(?<text>[^\]]*)\]\((?<url>https?://[^/\s)]+/(?:wiki|docx|docs|sheets|base|file|drive|space)/(?<token>[A-Za-z0-9]+)[^)\s]*)\)",
        RegexOptions.IgnoreCase)]
    private static partial Regex DocLinkRegex();

    /// <summary>
    /// Aspose 把飞书代码块渲染成单行伪表格（<c>|内容\n| :- |</c>），这里还原成围栏代码块。
    /// </summary>
    [GeneratedRegex(@"\|(?<content>[^\n]+)\n\|\s*:\s*-\s*\|")]
    private static partial Regex CodeBlockRegex();

    /// <summary>依次执行全部收尾处理。</summary>
    /// <param name="markdown">markdown 正文。</param>
    /// <param name="markdownPath">当前 markdown 文件将保存到的完整路径。</param>
    /// <param name="resolveDocRef">按 token 解析本地文件，返回 null 表示保持原链接。</param>
    public static string Process(
        string markdown,
        string markdownPath,
        Func<string, ExportTarget?>? resolveDocRef = null)
    {
        ArgumentNullException.ThrowIfNull(markdown);
        ArgumentNullException.ThrowIfNull(markdownPath);

        var content = ReplaceImagePaths(markdown, markdownPath);

        if (resolveDocRef is not null)
        {
            content = ReplaceDocumentReferences(content, markdownPath, resolveDocRef);
        }

        return FixCodeBlocks(content);
    }

    /// <summary>
    /// 把 Aspose 输出的绝对图片路径改成相对 markdown 文件的路径。
    /// </summary>
    public static string ReplaceImagePaths(string markdown, string markdownPath)
    {
        var directory = Path.GetDirectoryName(markdownPath) ?? string.Empty;

        return ImageRegex().Replace(markdown, match =>
        {
            var url = match.Groups["url"].Value.Trim();

            // ![alt](<path with spaces>) 这种尖括号写法
            if (url.Length > 1 && url[0] == '<' && url[^1] == '>')
            {
                url = url[1..^1];
            }

            if (!Path.IsPathRooted(url))
            {
                return match.Value;
            }

            var relative = Path.GetRelativePath(directory, url).Replace('\\', '/');
            return $"![{match.Groups["alt"].Value}]({relative})";
        });
    }

    /// <summary>
    /// 把markdown 里指向「本次已导出的其他文档」的飞书链接，改写成本地相对路径。
    /// 指向其他知识库或外链的保持原样。
    /// </summary>
    public static string ReplaceDocumentReferences(
        string markdown,
        string markdownPath,
        Func<string, ExportTarget?> resolveDocRef)
    {
        var directory = Path.GetDirectoryName(markdownPath) ?? string.Empty;

        return DocLinkRegex().Replace(markdown, match =>
        {
            var token = match.Groups["token"].Value;
            var target = resolveDocRef(token);

            if (target is null)
            {
                return match.Value;
            }

            // 旧实现用「当前文档」的扩展名去拼被引用文档的路径，
            // 被引用的是表格（.xlsx）或另一个格式的文档时链接就是错的。
            var relative = Path.GetRelativePath(directory, target.FilePath).Replace('\\', '/');
            return $"[{match.Groups["text"].Value}]({relative})";
        });
    }

    /// <summary>
    /// 把 Aspose 输出的单行伪表格还原成 markdown 围栏代码块。
    /// 与旧实现语义一致，但当内容里没有 '|' 时不再抛异常。
    /// </summary>
    public static string FixCodeBlocks(string markdown)
    {
        return CodeBlockRegex().Replace(markdown, match =>
        {
            var content = match.Groups["content"].Value;

            var lastPipe = content.LastIndexOf('|');
            if (lastPipe >= 0)
            {
                content = content.Remove(lastPipe, 1);
            }

            content = content.Replace("<br>", "\n").Replace("`", string.Empty);
            return $"```{content}```";
        });
    }
}
