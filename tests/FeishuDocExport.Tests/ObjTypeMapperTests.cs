using Xunit;

namespace FeishuDocExport.Tests;

public class ObjTypeMapperTests
{
    [Theory]
    [InlineData("docx", ExportFormat.Docx, "docx", "docx", false)]
    [InlineData("doc", ExportFormat.Docx, "docx", "docx", false)]
    [InlineData("docx", ExportFormat.Pdf, "pdf", "pdf", false)]
    [InlineData("docx", ExportFormat.Markdown, "docx", "md", true)]
    [InlineData("sheet", ExportFormat.Docx, "xlsx", "xlsx", false)]
    [InlineData("bitable", ExportFormat.Markdown, "xlsx", "xlsx", false)]
    public void 文档类型映射到正确的导出方案(
        string objType,
        ExportFormat format,
        string requestExtension,
        string saveExtension,
        bool convertToMarkdown)
    {
        var plan = ObjTypeMapper.Resolve(objType, format);

        Assert.NotNull(plan);
        Assert.Equal(requestExtension, plan!.RequestExtension);
        Assert.Equal(saveExtension, plan.SaveExtension);
        Assert.Equal(convertToMarkdown, plan.ConvertToMarkdown);
        Assert.False(plan.IsDirectFileDownload);
    }

    [Fact]
    public void 文件类型走原样下载分支()
    {
        var plan = ObjTypeMapper.Resolve("file", ExportFormat.Docx);

        Assert.NotNull(plan);
        Assert.True(plan!.IsDirectFileDownload);
    }

    [Theory]
    [InlineData("mindnote")]
    [InlineData("slides")]
    [InlineData("minutes")]
    [InlineData("")]
    [InlineData(null)]
    public void 不支持的类型返回null(string? objType)
        => Assert.Null(ObjTypeMapper.Resolve(objType, ExportFormat.Docx));

    [Fact]
    public void 目录不是可导出的类型()
    {
        Assert.True(ObjTypeMapper.IsContainer("folder"));
        Assert.True(ObjTypeMapper.IsContainer("Folder"));
        Assert.False(ObjTypeMapper.IsContainer("docx"));
    }

    [Fact]
    public void 导出接口的type参数会把docs归一化成docx()
    {
        // 「docs」是老文档类型的历史写法，导出接口只认「docx」
        Assert.Equal("docx", ObjTypeMapper.NormalizeRequestType("docs"));
        Assert.Equal("sheet", ObjTypeMapper.NormalizeRequestType("SHEET"));
        Assert.Equal("", ObjTypeMapper.NormalizeRequestType(null));
    }
}

public class ExportOptionsTests
{
    private static ExportOptions Valid() => new()
    {
        AppId = "cli_xxx",
        AppSecret = "secret",
        ExportPath = Path.Combine(Path.GetTempPath(), "feishu-export"),
        SourceType = DocSourceType.Wiki,
        WikiSpaceId = "space-1",
    };

    [Fact]
    public void 合法配置校验通过()
        => Assert.Empty(Valid().Validate());

    [Fact]
    public void 缺少AppId和AppSecret会报错()
    {
        var options = Valid();
        options.AppId = "";
        options.AppSecret = "  ";

        var errors = options.Validate();

        Assert.Contains(errors, e => e.Contains("AppId"));
        Assert.Contains(errors, e => e.Contains("AppSecret"));
    }

    [Fact]
    public void 云文档模式必须有folderToken()
    {
        var options = Valid();
        options.SourceType = DocSourceType.CloudDoc;
        options.FolderToken = null;

        Assert.Contains(options.Validate(), e => e.Contains("folderToken"));
    }

    [Fact]
    public void 知识库模式必须有spaceId()
    {
        var options = Valid();
        options.WikiSpaceId = null;

        Assert.Contains(options.Validate(), e => e.Contains("知识库"));
    }

    [Fact]
    public void 导出目录必须是绝对路径()
    {
        var options = Valid();
        options.ExportPath = "relative/path";

        Assert.Contains(options.Validate(), e => e.Contains("绝对路径"));
    }

    [Fact]
    public void 非法开放平台地址会被拦下()
    {
        var options = Valid();
        options.BaseUrl = "not-a-url";

        Assert.Contains(options.Validate(), e => e.Contains("合法"));
    }

    [Fact]
    public void Lark地址是合法的()
    {
        var options = Valid();
        options.BaseUrl = FeishuEndpoints.LarkOpenApi;

        Assert.Empty(options.Validate());
    }
}
