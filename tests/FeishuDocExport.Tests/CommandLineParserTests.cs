using FeishuDocExport.Cli;
using Xunit;

namespace FeishuDocExport.Tests;

public class CommandLineParserTests
{
    [Fact]
    public void 不带参数时进入交互式向导()
    {
        var parsed = CommandLineParser.Parse(Array.Empty<string>());

        Assert.True(parsed.Interactive);
        Assert.Null(parsed.Error);
    }

    [Fact]
    public void 支持等号写法()
    {
        var parsed = CommandLineParser.Parse(new[]
        {
            "--appId=cli_1",
            "--appSecret=s1",
            $"--exportPath={Path.Combine(Path.GetTempPath(), "out")}",
            "--spaceId=sp1",
        });

        Assert.Null(parsed.Error);
        Assert.Equal("cli_1", parsed.Options.AppId);
        Assert.Equal("s1", parsed.Options.AppSecret);
        Assert.Equal("sp1", parsed.Options.WikiSpaceId);
    }

    [Fact]
    public void 支持空格分隔写法()
    {
        var parsed = CommandLineParser.Parse(new[]
        {
            "--appId", "cli_1",
            "--appSecret", "s1",
            "--exportPath", Path.Combine(Path.GetTempPath(), "out"),
            "--spaceId", "sp1",
        });

        Assert.Null(parsed.Error);
        Assert.Equal("cli_1", parsed.Options.AppId);
        Assert.Equal("sp1", parsed.Options.WikiSpaceId);
    }

    [Fact]
    public void apiEndpoint会被真正解析出来()
    {
        // 旧实现里 --apiEndpoint 从头到尾没有被读取过，是一个「文档里写了但完全无效」的参数
        var parsed = CommandLineParser.Parse(new[]
        {
            "--appId=1", "--appSecret=2",
            $"--exportPath={Path.Combine(Path.GetTempPath(), "out")}",
            "--spaceId=sp1",
            "--apiEndpoint=https://open.larksuite.com",
        });

        Assert.Null(parsed.Error);
        Assert.Equal("https://open.larksuite.com", parsed.Options.BaseUrl);
    }

    [Fact]
    public void 未指定apiEndpoint时使用飞书默认地址()
    {
        var parsed = CommandLineParser.Parse(new[]
        {
            "--appId=1", "--appSecret=2",
            $"--exportPath={Path.Combine(Path.GetTempPath(), "out")}",
            "--spaceId=sp1",
        });

        Assert.Equal(FeishuEndpoints.DefaultOpenApi, parsed.Options.BaseUrl);
    }

    [Theory]
    [InlineData("md", ExportFormat.Markdown)]
    [InlineData("markdown", ExportFormat.Markdown)]
    [InlineData("pdf", ExportFormat.Pdf)]
    [InlineData("docx", ExportFormat.Docx)]
    [InlineData("", ExportFormat.Docx)]
    [InlineData("不认识的值", ExportFormat.Docx)]
    public void saveType解析并对非法值回退到docx(string saveType, ExportFormat expected)
    {
        var parsed = CommandLineParser.Parse(new[]
        {
            "--appId=1", "--appSecret=2",
            $"--exportPath={Path.Combine(Path.GetTempPath(), "out")}",
            "--spaceId=sp1",
            $"--saveType={saveType}",
        });

        Assert.Equal(expected, parsed.Options.Format);
    }

    [Theory]
    [InlineData("cloudDoc", DocSourceType.CloudDoc)]
    [InlineData("clouddoc", DocSourceType.CloudDoc)]
    [InlineData("CLOUDDOC", DocSourceType.CloudDoc)]
    [InlineData("wiki", DocSourceType.Wiki)]
    [InlineData("", DocSourceType.Wiki)]
    public void type解析不区分大小写(string type, DocSourceType expected)
    {
        var parsed = CommandLineParser.Parse(new[]
        {
            "--appId=1", "--appSecret=2",
            $"--exportPath={Path.Combine(Path.GetTempPath(), "out")}",
            "--spaceId=sp1", "--folderToken=ft1",
            $"--type={type}",
        });

        Assert.Equal(expected, parsed.Options.SourceType);
    }

    [Fact]
    public void 缺少必填参数时返回错误而不是直接退出进程()
    {
        // 旧实现遇到缺参数会打印帮助然后 Environment.Exit(0)：
        // 既没法在单元测试里验证，出错也返回成功退出码
        var parsed = CommandLineParser.Parse(new[] { "--appId=only-id" });

        Assert.NotNull(parsed.Error);
        Assert.Contains("AppSecret", parsed.Error);
    }

    [Fact]
    public void 缺少spaceId不算错误因为可以交互选择()
    {
        var parsed = CommandLineParser.Parse(new[]
        {
            "--appId=1", "--appSecret=2",
            $"--exportPath={Path.Combine(Path.GetTempPath(), "out")}",
        });

        Assert.Null(parsed.Error);
        Assert.Null(parsed.Options.WikiSpaceId);
    }

    [Fact]
    public void help参数触发帮助输出()
    {
        Assert.True(CommandLineParser.Parse(new[] { "--help" }).ShowHelp);
        Assert.True(CommandLineParser.Parse(new[] { "-h" }).ShowHelp);
    }

    [Fact]
    public void quit与skipExisting标志位会被识别()
    {
        var parsed = CommandLineParser.Parse(new[]
        {
            "--appId=1", "--appSecret=2",
            $"--exportPath={Path.Combine(Path.GetTempPath(), "out")}",
            "--spaceId=sp1",
            "--quit", "--skipExisting",
        });

        Assert.True(parsed.Quit);
        Assert.True(parsed.Options.SkipExistingFiles);
    }

    [Fact]
    public void 结束地址末尾的斜杠会被去掉()
    {
        var parsed = CommandLineParser.Parse(new[]
        {
            "--appId=1", "--appSecret=2",
            $"--exportPath={Path.Combine(Path.GetTempPath(), "out")}",
            "--spaceId=sp1",
            "--apiEndpoint=https://open.feishu.cn/",
        });

        Assert.Equal("https://open.feishu.cn", parsed.Options.BaseUrl);
    }

    [Fact]
    public void 凭证可以从环境变量读取()
    {
        // 避免 AppSecret 出现在 shell 历史和进程列表里
        Environment.SetEnvironmentVariable(CommandLineParser.AppIdEnvironmentVariable, "env-app-id");
        Environment.SetEnvironmentVariable(CommandLineParser.AppSecretEnvironmentVariable, "env-secret");

        try
        {
            var parsed = CommandLineParser.Parse(new[]
            {
                $"--exportPath={Path.Combine(Path.GetTempPath(), "out")}",
                "--spaceId=sp1",
            });

            Assert.Null(parsed.Error);
            Assert.Equal("env-app-id", parsed.Options.AppId);
            Assert.Equal("env-secret", parsed.Options.AppSecret);
        }
        finally
        {
            Environment.SetEnvironmentVariable(CommandLineParser.AppIdEnvironmentVariable, null);
            Environment.SetEnvironmentVariable(CommandLineParser.AppSecretEnvironmentVariable, null);
        }
    }

    [Fact]
    public void 命令行参数优先于环境变量()
    {
        Environment.SetEnvironmentVariable(CommandLineParser.AppIdEnvironmentVariable, "env-app-id");

        try
        {
            var parsed = CommandLineParser.Parse(new[]
            {
                "--appId=cli-app-id", "--appSecret=2",
                $"--exportPath={Path.Combine(Path.GetTempPath(), "out")}",
                "--spaceId=sp1",
            });

            Assert.Equal("cli-app-id", parsed.Options.AppId);
        }
        finally
        {
            Environment.SetEnvironmentVariable(CommandLineParser.AppIdEnvironmentVariable, null);
        }
    }

    private static string[] BaseArgs => new[]
    {
        "--appId=cli_1",
        "--appSecret=s1",
        $"--exportPath={Path.Combine(Path.GetTempPath(), "out")}",
        "--spaceId=sp1",
    };

    [Fact]
    public void 增量开关只影响增量字段()
    {
        var parsed = CommandLineParser.Parse(BaseArgs.Append("--incremental").ToArray());

        Assert.Null(parsed.Error);
        Assert.True(parsed.Options.Incremental);
        Assert.False(parsed.Scheduled);
    }

    [Theory]
    [InlineData("45s", 45)]
    [InlineData("30m", 1800)]
    [InlineData("6h", 21600)]
    [InlineData("1d", 86400)]
    public void interval支持秒分时天(string text, double seconds)
    {
        var parsed = CommandLineParser.Parse(BaseArgs.Append($"--interval={text}").ToArray());

        Assert.Null(parsed.Error);
        Assert.Equal(TimeSpan.FromSeconds(seconds), parsed.Interval);
        Assert.True(parsed.Scheduled);
    }

    [Fact]
    public void 定时模式自动启用增量()
    {
        var parsed = CommandLineParser.Parse(BaseArgs.Append("--interval=6h").ToArray());

        Assert.True(parsed.Options.Incremental);
    }

    [Theory]
    [InlineData("6x")]
    [InlineData("h6")]
    [InlineData("0s")]
    [InlineData("-1h")]
    public void 非法interval报参数错误(string text)
    {
        var parsed = CommandLineParser.Parse(BaseArgs.Append($"--interval={text}").ToArray());

        Assert.NotNull(parsed.Error);
        Assert.Contains("--interval", parsed.Error);
    }

    [Fact]
    public void at解析成每天的时刻()
    {
        var parsed = CommandLineParser.Parse(BaseArgs.Append("--at=03:00").ToArray());

        Assert.Null(parsed.Error);
        Assert.Equal(new TimeOnly(3, 0), parsed.DailyAt);
        Assert.True(parsed.Scheduled);
    }

    [Fact]
    public void 非法at报参数错误()
    {
        var parsed = CommandLineParser.Parse(BaseArgs.Append("--at=25:99").ToArray());

        Assert.NotNull(parsed.Error);
        Assert.Contains("--at", parsed.Error);
    }

    [Fact]
    public void interval与at只能二选一()
    {
        var parsed = CommandLineParser.Parse(
            BaseArgs.Concat(new[] { "--at=03:00", "--interval=6h" }).ToArray());

        Assert.NotNull(parsed.Error);
        Assert.Contains("二选一", parsed.Error);
    }

    [Fact]
    public void 定时模式必须显式给出知识库Id()
    {
        // 常驻进程没人能在控制台里替它选知识库，所以 spaceId 不能留空
        var parsed = CommandLineParser.Parse(new[]
        {
            "--appId=cli_1",
            "--appSecret=s1",
            $"--exportPath={Path.Combine(Path.GetTempPath(), "out")}",
            "--at=03:00",
        });

        Assert.NotNull(parsed.Error);
        Assert.Contains("spaceId", parsed.Error);
    }
}
