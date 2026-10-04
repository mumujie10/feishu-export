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

    /// <summary>来自 <c>--interval</c> 的定时间隔。</summary>
    public TimeSpan? Interval { get; init; }

    /// <summary>来自 <c>--at</c> 的每天固定运行时刻（本地时区）。</summary>
    public TimeOnly? DailyAt { get; init; }

    /// <summary>指定了 --interval 或 --at 时为 true，程序会常驻循环。</summary>
    public bool Scheduled => Interval is not null || DailyAt is not null;
}

/// <summary>
/// 命令行参数解析。
///
/// 相比旧实现的改进：
/// - 同时支持 <c>--appId=xxx</c> 和 <c>--appId xxx</c> 两种写法，且不区分大小写；
/// - 参数缺失时不再直接 <c>Environment.Exit(0)</c>（旧实现出错也返回成功退出码，脚本没法判断）；
/// - 新增 <c>--licensePath</c>、<c>--skipExisting</c>、<c>--help</c>、
///   <c>--incremental</c>、<c>--interval</c>、<c>--at</c>。
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
          --spaceId         知识库 Id，也可以直接粘贴知识库链接（…/wiki/space/xxx）
                            不传则列出所有知识库由你选择
          --folderToken     个人空间文件夹 Token，type=cloudDoc 时必填
                            同样支持直接粘贴文件夹分享链接
          --saveType        文档保存格式：docx（默认）、pdf、md
          --apiEndpoint     开放平台地址，默认 https://open.feishu.cn
                            国际版 Lark 请传 https://open.larksuite.com
          --licensePath     Aspose.Words 许可证文件路径（仅导出 md 时需要）
          --skipExisting    目标文件已存在时跳过，便于中断后重跑
          --incremental     增量导出：只导新增和改动过的文档，依据是导出目录里
                            .feishu-export-state.json 记录的飞书编辑时间
          --interval        常驻定时：每隔多久跑一次，如 45s / 30m / 6h / 1d
          --at              常驻定时：每天固定时刻跑一次，如 03:00（本地时区）
                            --interval 与 --at 二选一；定时模式自动启用增量，
                            且必须显式给出 --spaceId 或 --folderToken
          --quit            执行完直接退出，不等待按键

        示例：
          # 手动跑一次增量
          feishu-doc-export --spaceId=xxx --exportPath=/data/docs --incremental

          # 常驻：每天 03:00 增量导出，凭证用环境变量给
          FEISHU_APP_ID=xxx FEISHU_APP_SECRET=xxx \\
            feishu-doc-export --spaceId=xxx --exportPath=/data/docs --at=03:00

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
        ["interval"] = "interval",
        ["at"] = "at",
    };

    private static readonly HashSet<string> Flags = new(StringComparer.OrdinalIgnoreCase)
    {
        "skipExisting",
        "incremental",
        "quit",
        "help",
        "h",
    };

    /// <summary>定时间隔写法：数字 + 单位，如 45s / 30m / 6h / 1d。</summary>
    private static readonly System.Text.RegularExpressions.Regex IntervalPattern = new(
        @"^(?<n>\d+)(?<u>[smhd])$",
        System.Text.RegularExpressions.RegexOptions.CultureInvariant |
        System.Text.RegularExpressions.RegexOptions.IgnoreCase);

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
            Incremental = flags.Contains("incremental"),
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

        // 用户手里的通常是链接而不是裸 Token，先归一化再校验
        if (DocLinkParser.ExtractSpaceId(options.WikiSpaceId, out var spaceLinkError) is { } spaceId)
        {
            options.WikiSpaceId = spaceId;
        }
        else if (spaceLinkError is not null)
        {
            errors.Add(spaceLinkError);
        }

        if (DocLinkParser.ExtractFolderToken(options.FolderToken, out var folderLinkError) is { } folderToken)
        {
            options.FolderToken = folderToken;
        }
        else if (folderLinkError is not null)
        {
            errors.Add(folderLinkError);
        }

        TimeSpan? interval = null;
        TimeOnly? dailyAt = null;

        var intervalText = NullIfEmpty(Get("interval"));
        if (intervalText is not null)
        {
            interval = ParseInterval(intervalText);
            if (interval is null)
            {
                errors.Add($"--interval 格式不对：{intervalText}（支持 45s / 30m / 6h / 1d）");
            }
        }

        var atText = NullIfEmpty(Get("at"));
        if (atText is not null)
        {
            if (TimeOnly.TryParse(atText, System.Globalization.CultureInfo.InvariantCulture, out var at))
            {
                dailyAt = at;
            }
            else
            {
                errors.Add($"--at 需要 HH:mm 格式，例如 03:00：{atText}");
            }
        }

        if (interval is not null && dailyAt is not null)
        {
            errors.Add("--interval 和 --at 只能二选一。");
        }

        if (interval is not null || dailyAt is not null)
        {
            // 常驻模式没有人在终端前盯着：目标必须写死，并且默认只做增量
            options.Incremental = true;

            if (options.SourceType == DocSourceType.Wiki && string.IsNullOrWhiteSpace(options.WikiSpaceId))
            {
                errors.Add("定时模式必须显式指定 --spaceId，不能靠控制台选知识库。");
            }
        }

        return new ParsedCommandLine
        {
            Options = options,
            Error = errors.Count > 0 ? string.Join(Environment.NewLine, errors) : null,
            Quit = flags.Contains("quit"),
            Interval = interval,
            DailyAt = dailyAt,
        };
    }

    /// <summary>解析 45s / 30m / 6h / 1d 这类间隔写法，不合法返回 null。</summary>
    private static TimeSpan? ParseInterval(string text)
    {
        var match = IntervalPattern.Match(text);

        if (!match.Success || !int.TryParse(match.Groups["n"].Value, out var number) || number <= 0)
        {
            return null;
        }

        return match.Groups["u"].Value.ToLowerInvariant() switch
        {
            "s" => TimeSpan.FromSeconds(number),
            "m" => TimeSpan.FromMinutes(number),
            "h" => TimeSpan.FromHours(number),
            "d" => TimeSpan.FromDays(number),
            _ => null,
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
