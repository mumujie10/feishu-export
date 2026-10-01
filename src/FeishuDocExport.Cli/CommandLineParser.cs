namespace FeishuDocExport.Cli;

/// <summary>命令行参数解析结果。</summary>
public sealed class ParsedCommandLine
{
    public ExportOptions Options { get; init; } = new();

    /// <summary>解析成功时为空；否则是需要展示给用户的错误信息。</summary>
    public string? Error { get; init; }

    /// <summary>是否请求显示帮助。</summary>
    public bool ShowHelp { get; init; }

    /// <summary>执行完是否直接退出（不等待按键）。</summary>
    public bool Quit { get; init; }

    /// <summary>完全没传参数时为 true，进入交互式向导。</summary>
    public bool Interactive { get; init; }
}

/// <summary>
/// 命令行参数解析。
///
/// 相比旧实现的改进：
/// - 同时支持 <c>--appId=xxx</c> 和 <c>--appId xxx</c> 两种写法，且不区分大小写；
/// - 参数缺失时不再直接 <c>Environment.Exit(0)</c>（旧实现出错也返回成功退出码，脚本没法判断）；
/// - 新增 <c>--licensePath</c>、<c>--skipExisting</c>、<c>--help</c>。
/// </summary>
public static class CommandLineParser
{
    public const string Usage = """
        用法：
          feishu-doc-export --appId=<AppId> --appSecret=<AppSecret> --exportPath=<目录> [其它参数]
          feishu-doc-export                       # 不带参数时进入交互式向导
          feishu-doc-export --help                # 显示本帮助

        必填参数：
          --appId           飞书自建应用的 AppId
          --appSecret       飞书自建应用的 AppSecret
          --exportPath      文档导出的本地目录（需为绝对路径，不存在会自动创建）

        可选参数：
          --type            导出对象：wiki（知识库，默认）或 cloudDoc（个人空间云文档）
          --spaceId         知识库 Id；不传则列出所有知识库由你选择
          --folderToken     个人空间文件夹 Token，type=cloudDoc 时必填
          --saveType        文档保存格式：docx（默认）、pdf、md
          --apiEndpoint     开放平台地址，默认 https://open.feishu.cn
                            国际版 Lark 请传 https://open.larksuite.com
          --licensePath     Aspose.Words 许可证文件路径（仅导出 md 时需要）
          --skipExisting    目标文件已存在时跳过，便于中断后重跑
          --quit            执行完直接退出，不等待按键

        也可以通过环境变量提供凭证，避免密钥进入 shell 历史与进程列表：
          FEISHU_APP_ID / FEISHU_APP_SECRET
        """;

    private static readonly Dictionary<string, string> Aliases = new(StringComparer.OrdinalIgnoreCase)
    {
        ["appid"] = "appid",
        ["appsecret"] = "appsecret",
        ["exportpath"] = "exportpath",
        ["type"] = "type",
        ["spaceid"] = "spaceid",
        ["foldertoken"] = "foldertoken",
        ["savetype"] = "savetype",
        ["apiendpoint"] = "apiendpoint",
        ["licensepath"] = "licensepath",
    };

    private static readonly HashSet<string> Flags = new(StringComparer.OrdinalIgnoreCase)
    {
        "skipExisting",
        "quit",
        "help",
        "h",
    };

    /// <summary>AppId 的环境变量名。命令行没传时从这里取，避免密钥留在 shell 历史和进程列表里。</summary>
    public const string AppIdEnvironmentVariable = "FEISHU_APP_ID";

    /// <summary>AppSecret 的环境变量名。</summary>
    public const string AppSecretEnvironmentVariable = "FEISHU_APP_SECRET";

    public static ParsedCommandLine Parse(string[] args)
    {
        if (args is null || args.Length == 0)
        {
            return new ParsedCommandLine { Interactive = true };
        }

        var values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var flags = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        for (var i = 0; i < args.Length; i++)
        {
            var arg = args[i];
            if (string.IsNullOrWhiteSpace(arg) || !arg.StartsWith('-'))
            {
                continue;
            }

            var trimmed = arg.TrimStart('-');
            var separator = trimmed.IndexOf('=');
            var key = separator >= 0 ? trimmed[..separator] : trimmed;
            var value = separator >= 0 ? trimmed[(separator + 1)..] : null;

            if (value is null && !Flags.Contains(key) && i + 1 < args.Length && !args[i + 1].StartsWith('-'))
            {
                // 支持 "--appId xxx" 写法
                value = args[++i];
            }

            if (Flags.Contains(key))
            {
                flags.Add(key);
                continue;
            }

            if (Aliases.ContainsKey(key))
            {
                values[Aliases[key]] = value ?? string.Empty;
            }
        }

        if (flags.Contains("help") || flags.Contains("h"))
        {
            return new ParsedCommandLine { ShowHelp = true };
        }

        string? Get(string name) => values.TryGetValue(name, out var v) ? v : null;

        var options = new ExportOptions
        {
            // 命令行参数优先，其次读环境变量（避免 AppSecret 出现在 shell 历史和进程列表里）
            AppId = NullIfEmpty(Get("appid"))
                    ?? NullIfEmpty(Environment.GetEnvironmentVariable(AppIdEnvironmentVariable))
                    ?? string.Empty,
            AppSecret = NullIfEmpty(Get("appsecret"))
                        ?? NullIfEmpty(Environment.GetEnvironmentVariable(AppSecretEnvironmentVariable))
                        ?? string.Empty,
            ExportPath = Get("exportpath") ?? string.Empty,
            BaseUrl = NormalizeEndpoint(Get("apiendpoint")),
            SourceType = ParseSourceType(Get("type")),
            WikiSpaceId = NullIfEmpty(Get("spaceid")),
            FolderToken = NullIfEmpty(Get("foldertoken")),
            Format = ParseFormat(Get("savetype")),
            AsposeLicensePath = NullIfEmpty(Get("licensepath")),
            SkipExistingFiles = flags.Contains("skipExisting"),
        };

        if (options.SourceType == DocSourceType.CloudDoc)
        {
            options.WikiSpaceId = null;
        }
        else
        {
            options.FolderToken = null;
        }

        var errors = options.Validate().ToList();
        // 知识库 Id 允许为空（后续在控制台上列出知识库让用户选）
        errors.RemoveAll(e => e.Contains("请先选择要导出的知识库"));

        return new ParsedCommandLine
        {
            Options = options,
            Error = errors.Count > 0 ? string.Join(Environment.NewLine, errors) : null,
            Quit = flags.Contains("quit"),
        };
    }

    private static string NormalizeEndpoint(string? value)
        => string.IsNullOrWhiteSpace(value) ? FeishuEndpoints.DefaultOpenApi : value.Trim().TrimEnd('/');

    private static string? NullIfEmpty(string? value)
        => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static DocSourceType ParseSourceType(string? value)
        => string.Equals(value?.Trim(), "cloudDoc", StringComparison.OrdinalIgnoreCase)
            ? DocSourceType.CloudDoc
            : DocSourceType.Wiki;

    /// <summary>解析保存格式；非法值按旧行为回退到 docx。</summary>
    private static ExportFormat ParseFormat(string? value) => value?.Trim().ToLowerInvariant() switch
    {
        "pdf" => ExportFormat.Pdf,
        "md" or "markdown" => ExportFormat.Markdown,
        _ => ExportFormat.Docx,
    };
}
