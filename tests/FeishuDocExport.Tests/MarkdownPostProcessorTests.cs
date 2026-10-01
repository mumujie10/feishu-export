using Xunit;

namespace FeishuDocExport.Tests;

public class MarkdownPostProcessorTests
{
    private static readonly string DocPath = Path.Combine("/out", "产品文档", "需求说明.md");

    [Fact]
    public void 绝对图片路径会替换成相对路径()
    {
        var markdown = "![image](" + Path.Combine("/out", "产品文档", "需求说明.assets", "image.001.png") + ")";

        var result = MarkdownPostProcessor.ReplaceImagePaths(markdown, DocPath);

        Assert.Equal("![image](需求说明.assets/image.001.png)", result);
    }

    [Fact]
    public void 已经是相对路径的图片保持不变()
    {
        var markdown = "![image](需求说明.assets/image.001.png)";

        var result = MarkdownPostProcessor.ReplaceImagePaths(markdown, DocPath);

        Assert.Equal(markdown, result);
    }

    [Fact]
    public void 尖括号包裹的带空格路径也能正确处理()
    {
        var absolute = Path.Combine("/out", "产品文档", "需求说明.assets", "my image.png");
        var markdown = $"![image](<{absolute}>)";

        var result = MarkdownPostProcessor.ReplaceImagePaths(markdown, DocPath);

        Assert.Equal("![image](需求说明.assets/my image.png)", result);
    }

    [Fact]
    public void 已导出的文档引用会被改写成本地相对路径()
    {
        var markdown = "详见 [另一个文档](https://xxx.feishu.cn/wiki/NODE123) 的说明。";

        var result = MarkdownPostProcessor.ReplaceDocumentReferences(
            markdown,
            DocPath,
            token => token == "NODE123"
                ? new ExportTarget(Path.Combine("/out", "产品文档", "另一个文档"), "docx")
                : null);

        Assert.Equal("详见 [另一个文档](另一个文档.docx) 的说明。", result);
    }

    [Fact]
    public void 跨目录的文档引用会带上相对路径()
    {
        var markdown = "[目标](https://xxx.feishu.cn/wiki/OTHER)";

        var result = MarkdownPostProcessor.ReplaceDocumentReferences(
            markdown,
            DocPath,
            _ => new ExportTarget(Path.Combine("/out", "其它目录", "目标文档"), "pdf"));

        Assert.Equal("[目标](../其它目录/目标文档.pdf)", result);
    }

    [Fact]
    public void 引用的文档没有导出时保留原链接()
    {
        var markdown = "[外部文档](https://xxx.feishu.cn/wiki/UNKNOWN)";

        var result = MarkdownPostProcessor.ReplaceDocumentReferences(markdown, DocPath, _ => null);

        Assert.Equal(markdown, result);
    }

    [Fact]
    public void 引用被引用文档的扩展名以真实落盘格式为准()
    {
        // 旧实现用「当前文档」的扩展名去拼被引用文档的路径，
        // 引用一个表格（xlsx）时会生成 .md 结尾的错误链接
        var markdown = "[数据表](https://xxx.feishu.cn/sheets/SHEET1)";

        var result = MarkdownPostProcessor.ReplaceDocumentReferences(
            markdown,
            DocPath,
            _ => new ExportTarget(Path.Combine("/out", "产品文档", "数据表"), "xlsx"));

        Assert.Equal("[数据表](数据表.xlsx)", result);
    }

    [Fact]
    public void 支持Lark国际版域名的文档链接()
    {
        var markdown = "[文档](https://xxx.larksuite.com/docx/ABC123)";

        var result = MarkdownPostProcessor.ReplaceDocumentReferences(
            markdown,
            DocPath,
            _ => new ExportTarget(Path.Combine("/out", "产品文档", "文档"), "docx"));

        Assert.Equal("[文档](文档.docx)", result);
    }

    [Fact]
    public void 普通外链不会被改写()
    {
        var markdown = "[官网](https://www.example.com/wiki/ABC)";

        var result = MarkdownPostProcessor.ReplaceDocumentReferences(markdown, DocPath, _ => null);

        Assert.Equal(markdown, result);
    }

    [Fact]
    public void 代码块还原_内容不含竖线时不再抛异常()
    {
        // 旧实现是 replacement.Remove(replacement.LastIndexOf('|'), 1)，
        // 内容里没有 '|' 时 LastIndexOf 返回 -1，会抛 ArgumentOutOfRangeException
        var markdown = "|hello\n| : - |";

        var result = MarkdownPostProcessor.FixCodeBlocks(markdown);

        Assert.Equal("```hello```", result);
    }

    [Fact]
    public void 代码块还原_去掉行内反引号并还原换行()
    {
        var markdown = "|`line1<br>line2`|\n| : - |";

        var result = MarkdownPostProcessor.FixCodeBlocks(markdown);

        Assert.Equal("```line1\nline2```", result);
    }

    [Fact]
    public void 代码块还原_普通表格不受影响()
    {
        var markdown = "| 姓名 | 年龄 |\n| --- | --- |\n| 张三 | 18 |";

        var result = MarkdownPostProcessor.FixCodeBlocks(markdown);

        Assert.Equal(markdown, result);
    }

    [Fact]
    public void Process_会依次执行全部处理()
    {
        var absolute = Path.Combine("/out", "产品文档", "需求说明.assets", "image.001.png");
        var markdown = $"![图]({absolute})\n\n详见 [文档](https://x.feishu.cn/wiki/N1)\n";

        var result = MarkdownPostProcessor.Process(
            markdown,
            DocPath,
            token => token == "N1" ? new ExportTarget(Path.Combine("/out", "产品文档", "文档"), "docx") : null);

        Assert.Contains("![图](需求说明.assets/image.001.png)", result);
        Assert.Contains("[文档](文档.docx)", result);
    }
}
