using FeishuDocExport;
using FeishuDocExport.Cli;
using Xunit;

namespace FeishuDocExport.Tests;

public class DocLinkParserTests
{
    /// <summary>用户实际从地址栏复制出来的那条链接（带 query 参数）。</summary>
    private const string RealSpaceLink =
        "https://example.feishu.cn/wiki/space/7100000000000000001?ccm_open_type=lark_wiki_spaceLink&open_tab_from=wiki_home";

    [Fact]
    public void 从知识库链接里取出spaceId()
    {
        var id = DocLinkParser.ExtractSpaceId(RealSpaceLink, out var error);

        Assert.Null(error);
        Assert.Equal("7100000000000000001", id);
    }

    [Fact]
    public void 链接前后有空格也能取()
    {
        Assert.Equal("7100000000000000001", DocLinkParser.ExtractSpaceId("  " + RealSpaceLink + "\n", out _));
    }

    [Fact]
    public void 裸Id原样返回()
    {
        Assert.Equal("6872123456789012345", DocLinkParser.ExtractSpaceId("6872123456789012345", out var error));
        Assert.Null(error);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void 空输入返回空且不报错(string? input)
    {
        Assert.Null(DocLinkParser.ExtractSpaceId(input, out var error));
        Assert.Null(error);
    }

    [Fact]
    public void 贴成单篇文档链接时给出可执行的提示()
    {
        var id = DocLinkParser.ExtractSpaceId(
            "https://example.feishu.cn/wiki/RsMlwH0niiI8F5kKJvQcV8hmnOb", out var error);

        Assert.Null(id);
        Assert.NotNull(error);
        Assert.Contains("文档", error);
        Assert.Contains("/wiki/space/", error);
    }

    [Fact]
    public void 认不出的链接也报错而不是照单全收()
    {
        var id = DocLinkParser.ExtractSpaceId("https://example.com/console", out var error);

        Assert.Null(id);
        Assert.NotNull(error);
    }

    [Fact]
    public void 从文件夹分享链接取出token()
    {
        var token = DocLinkParser.ExtractFolderToken(
            "https://example.feishu.cn/drive/folder/FmzOfmDlNlSbLcdLKbncpTmOhQd?from=from_copylink", out var error);

        Assert.Null(error);
        Assert.Equal("FmzOfmDlNlSbLcdLKbncpTmOhQd", token);
    }

    [Fact]
    public void 裸文件夹token原样返回()
    {
        Assert.Equal("FmzOfmDlNlSbLcdLKbncpTmOhQd", DocLinkParser.ExtractFolderToken("FmzOfmDlNlSbLcdLKbncpTmOhQd", out var error));
        Assert.Null(error);
    }

    [Fact]
    public void 文件夹链接里没有folder段时报错()
    {
        Assert.Null(DocLinkParser.ExtractFolderToken("https://x.feishu.cn/wiki/space/1234567890", out var error));
        Assert.NotNull(error);
        Assert.Contains("文件夹", error);
    }

    [Fact]
    public void 命令行传链接也会被归一化成Id()
    {
        var parsed = CommandLineParser.Parse(new[]
        {
            "--appId=cli_1",
            "--appSecret=s1",
            $"--exportPath={Path.Combine(Path.GetTempPath(), "out")}",
            $"--spaceId={RealSpaceLink}",
        });

        Assert.Null(parsed.Error);
        Assert.Equal("7100000000000000001", parsed.Options.WikiSpaceId);
    }

    [Fact]
    public void 命令行贴错链接时报参数错误()
    {
        var parsed = CommandLineParser.Parse(new[]
        {
            "--appId=cli_1",
            "--appSecret=s1",
            $"--exportPath={Path.Combine(Path.GetTempPath(), "out")}",
            "--spaceId=https://x.feishu.cn/wiki/RsMlwH0niiI8F5kKJvQcV8hmnOb",
        });

        Assert.NotNull(parsed.Error);
        Assert.Contains("文档", parsed.Error);
    }
}
