using System.Runtime.InteropServices;

namespace FeishuDocExport.Cli;

/// <summary>把导出进度直接输出到控制台。</summary>
internal sealed class ConsoleProgress : IProgress<ExportProgress>
{
    private readonly object _gate = new();

    public void Report(ExportProgress value)
    {
        lock (_gate)
        {
            var previous = Console.ForegroundColor;
            Console.ForegroundColor = value.Level switch
            {
                ExportLogLevel.Success => ConsoleColor.Green,
                ExportLogLevel.Warning => ConsoleColor.Yellow,
                ExportLogLevel.Error => ConsoleColor.Red,
                _ => previous,
            };

            Console.WriteLine(value.Level == ExportLogLevel.Error ? $"【ERROR】{value.Message}" : value.Message);
            Console.ForegroundColor = previous;
        }
    }
}

internal static class Program
{
    private const int ExitSuccess = 0;
    private const int ExitInvalidArguments = 1;
    private const int ExitPartialFailure = 2;
    private const int ExitFatal = 3;

    private static async Task<int> Main(string[] args)
    {
        TrySetUtf8Output();

        var parsed = CommandLineParser.Parse(args);

        if (parsed.ShowHelp)
        {
            Console.WriteLine(CommandLineParser.Usage);
            return ExitSuccess;
        }

        if (parsed.Error is not null)
        {
            Console.Error.WriteLine("参数有误：");
            Console.Error.WriteLine(parsed.Error);
            Console.Error.WriteLine();
            Console.Error.WriteLine(CommandLineParser.Usage);
            return ExitInvalidArguments;
        }

        var options = parsed.Options;

        if (parsed.Interactive && !RunInteractiveWizard(options))
        {
            return ExitInvalidArguments;
        }

        using var cts = new CancellationTokenSource();

        Console.CancelKeyPress += (_, e) =>
        {
            e.Cancel = true;
            Console.WriteLine("收到中断信号，正在安全退出…");
            cts.Cancel();
        };

        // 容器里本进程是 PID 1，docker stop 发的是 SIGTERM，不接住就来不及写状态文件
        PosixSignalRegistration? sigTerm = null;
        if (!OperatingSystem.IsWindows())
        {
            sigTerm = PosixSignalRegistration.Create(PosixSignal.SIGTERM, ctx =>
            {
                ctx.Cancel = true;
                Console.WriteLine("收到 SIGTERM，正在安全退出…");
                cts.Cancel();
            });
        }

        try
        {
            return parsed.Scheduled
                ? await RunLoopAsync(options, parsed, cts.Token)
                : await RunOnceAsync(options, interactive: true, ct: cts.Token);
        }
        finally
        {
            sigTerm?.Dispose();
        }
    }

    /// <summary>跑一轮完整导出，返回退出码。</summary>
    private static async Task<int> RunOnceAsync(ExportOptions options, bool interactive, CancellationToken ct)
    {
        try
        {
            using var client = new FeishuApiClient(options);
            var service = new ExportService(client);
            var progress = new ConsoleProgress();

            if (interactive && options.SourceType == DocSourceType.Wiki && string.IsNullOrWhiteSpace(options.WikiSpaceId))
            {
                options.WikiSpaceId = await SelectWikiSpaceAsync(service, ct);
            }

            var validationErrors = options.Validate();
            if (validationErrors.Count > 0)
            {
                Console.Error.WriteLine("参数有误：");
                foreach (var error in validationErrors)
                {
                    Console.Error.WriteLine("  " + error);
                }

                return ExitInvalidArguments;
            }

            var result = await service.RunAsync(options, progress, ct);

            Console.WriteLine();
            Console.WriteLine(new string('—', 60));

            var stats = options.Incremental
                ? $"（新增 {result.Added}、更新 {result.Updated}、未变动跳过 {result.Unchanged}）"
                : string.Empty;

            Console.WriteLine(result.Canceled
                ? $"导出已取消：成功 {result.Succeeded}/{result.Total}{stats}，耗时 {result.Elapsed.TotalSeconds:0} 秒。"
                : $"导出完成：成功 {result.Succeeded}/{result.Total}{stats}，耗时 {result.Elapsed.TotalSeconds:0} 秒。");

            if (result.Failures.Count > 0)
            {
                Console.WriteLine();
                Console.WriteLine("以下文档未能导出（含不支持的类型与导出异常）：");
                for (var i = 0; i < result.Failures.Count; i++)
                {
                    var failure = result.Failures[i];
                    Console.WriteLine($"  {i + 1}. [{(failure.IsSkipped ? "跳过" : "失败")}] {failure.Title} —— {failure.Reason}");
                }
            }

            if (result.Canceled)
            {
                return ExitFatal;
            }

            return result.Failures.Count == 0 ? ExitSuccess : ExitPartialFailure;
        }
        catch (OperationCanceledException)
        {
            Console.WriteLine("已取消。");
            return ExitFatal;
        }
        catch (FeishuApiException ex)
        {
            Console.Error.WriteLine($"【ERROR】{ex.ToUserMessage()}");
            return ExitFatal;
        }
        catch (ArgumentException ex)
        {
            Console.Error.WriteLine($"【ERROR】{ex.Message}");
            return ExitInvalidArguments;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"【ERROR】程序执行失败：{ex.Message}");
            Console.Error.WriteLine(ex.StackTrace);
            return ExitFatal;
        }
    }

    /// <summary>
    /// 常驻定时模式：按 --interval 或 --at 一轮接一轮地跑增量导出。
    /// 单轮失败只记日志不退出，否则容器会被重启策略反复拉起、每轮都从头再来。
    /// </summary>
    private static async Task<int> RunLoopAsync(ExportOptions options, ParsedCommandLine parsed, CancellationToken ct)
    {
        Console.WriteLine(parsed.Interval is { } span
            ? $"定时模式：每隔 {FormatInterval(span)} 增量导出一次。"
            : $"定时模式：每天 {parsed.DailyAt!.Value:HH:mm} 增量导出一次（本地时区）。");
        Console.WriteLine($"导出目录：{options.ExportPath}");
        Console.WriteLine("按 Ctrl+C 停止。");
        Console.WriteLine();

        var round = 0;

        while (!ct.IsCancellationRequested)
        {
            round++;
            Console.WriteLine($"===== 第 {round} 轮 · {DateTime.Now:yyyy-MM-dd HH:mm:ss} =====");

            await RunOnceAsync(options, interactive: false, ct);

            if (ct.IsCancellationRequested)
            {
                break;
            }

            var wait = TimeUntil(parsed.Interval, parsed.DailyAt, DateTimeOffset.Now);
            Console.WriteLine($"下一轮：{DateTime.Now.Add(wait):yyyy-MM-dd HH:mm:ss}（{FormatInterval(wait)} 后）");
            Console.WriteLine();

            try
            {
                await Task.Delay(wait, ct);
            }
            catch (OperationCanceledException)
            {
                break;
            }
        }

        Console.WriteLine("定时任务已停止。");
        return ExitSuccess;
    }

    private static TimeSpan TimeUntil(TimeSpan? interval, TimeOnly? dailyAt, DateTimeOffset now)
    {
        if (interval is { } span)
        {
            return span;
        }

        var local = now.LocalDateTime;
        var next = local.Date + dailyAt!.Value.ToTimeSpan();

        if (next <= local)
        {
            next = next.AddDays(1);
        }

        return next - local;
    }

    private static string FormatInterval(TimeSpan value)
    {
        var parts = new List<string>();

        if (value.Days > 0)
        {
            parts.Add($"{value.Days} 天");
        }

        if (value.Hours > 0)
        {
            parts.Add($"{value.Hours} 小时");
        }

        if (value.Minutes > 0)
        {
            parts.Add($"{value.Minutes} 分钟");
        }

        if (value.Seconds > 0 || parts.Count == 0)
        {
            parts.Add($"{value.Seconds} 秒");
        }

        return string.Join(" ", parts);
    }

    private static void TrySetUtf8Output()
    {
        try
        {
            Console.OutputEncoding = System.Text.Encoding.UTF8;
        }
        catch (Exception)
        {
            // 某些终端不支持修改编码，忽略即可
        }
    }

    private static async Task<string> SelectWikiSpaceAsync(ExportService service, CancellationToken ct)
    {
        var spaces = await service.GetWikiSpacesAsync(ct);
        if (spaces.Count == 0)
        {
            throw new FeishuApiException("当前应用没有任何可导出的知识库，请检查应用权限与知识库授权。");
        }

        Console.WriteLine("以下是所有可导出的知识库：");
        for (var i = 0; i < spaces.Count; i++)
        {
            Console.WriteLine($"  {i + 1}. {spaces[i].Name}");
        }

        while (true)
        {
            Console.Write("请输入要导出的知识库序号：");
            var input = Console.ReadLine();

            // 旧实现是 int.Parse + 直接索引，输入非数字或越界都会抛未捕获异常
            if (int.TryParse(input?.Trim(), out var index) && index >= 1 && index <= spaces.Count)
            {
                return spaces[index - 1].SpaceId!;
            }

            Console.WriteLine($"请输入 1 到 {spaces.Count} 之间的数字。");
        }
    }

    private static bool RunInteractiveWizard(ExportOptions options)
    {
        Console.WriteLine("=== 飞书导出 ===");
        Console.WriteLine();

        options.AppId = PromptRequired("请输入飞书自建应用的 AppId：");
        options.AppSecret = PromptRequired("请输入飞书自建应用的 AppSecret：");

        Console.Write("请输入导出格式（docx / pdf / md，直接回车默认为 docx）：");
        options.Format = Console.ReadLine()?.Trim().ToLowerInvariant() switch
        {
            "pdf" => ExportFormat.Pdf,
            "md" or "markdown" => ExportFormat.Markdown,
            _ => ExportFormat.Docx,
        };

        Console.Write("请输入导出对象类型（wiki / cloudDoc，直接回车默认为 wiki）：");
        var type = Console.ReadLine()?.Trim();
        options.SourceType = string.Equals(type, "cloudDoc", StringComparison.OrdinalIgnoreCase)
            ? DocSourceType.CloudDoc
            : DocSourceType.Wiki;

        if (options.SourceType == DocSourceType.CloudDoc)
        {
            Console.Write("请输入要导出的文件夹 Token，或直接粘贴文件夹分享链接：");
            options.FolderToken = NullIfEmpty(DocLinkParser.ExtractFolderToken(Console.ReadLine(), out var folderError));

            if (folderError is not null)
            {
                Console.WriteLine(folderError);
                return false;
            }

            if (string.IsNullOrWhiteSpace(options.FolderToken))
            {
                Console.WriteLine("该项不能为空。");
                return false;
            }
        }
        else
        {
            Console.Write("请输入知识库 Id，或直接粘贴知识库链接（直接回车则列出全部知识库供选择）：");
            options.WikiSpaceId = NullIfEmpty(DocLinkParser.ExtractSpaceId(Console.ReadLine(), out var spaceError));

            if (spaceError is not null)
            {
                Console.WriteLine(spaceError);
                return false;
            }
        }

        options.ExportPath = PromptRequired("请输入文档导出的目录（绝对路径，不存在会自动创建）：");

        if (options.Format == ExportFormat.Markdown)
        {
            Console.Write("请输入 Aspose.Words 许可证文件路径（直接回车则自动查找，找不到会用评估版加水印）：");
            options.AsposeLicensePath = NullIfEmpty(Console.ReadLine());
        }

        return true;
    }

    private static string PromptRequired(string prompt)
    {
        while (true)
        {
            Console.Write(prompt);
            var value = Console.ReadLine();
            if (!string.IsNullOrWhiteSpace(value))
            {
                return value.Trim();
            }

            Console.WriteLine("该项不能为空，请重新输入。");
        }
    }

    private static string? NullIfEmpty(string? value)
        => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
