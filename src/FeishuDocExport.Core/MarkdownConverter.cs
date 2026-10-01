using Aspose.Words;
using Aspose.Words.Drawing;
using Aspose.Words.Saving;

namespace FeishuDocExport;

/// <summary>Aspose 许可证的加载状态。</summary>
/// <param name="IsLicensed">是否成功加载了许可证。</param>
/// <param name="Message">给用户看的说明。</param>
/// <param name="Path">实际使用的许可证路径，未加载时为 null。</param>
public sealed record AsposeLicenseStatus(bool IsLicensed, string Message, string? Path);

/// <summary>
/// 用 Aspose.Words 把飞书导出的 docx 转成 markdown。
///
/// 关于许可证：旧实现把路径硬编码成 <c>/private/tmp/License.lic</c>（macOS 本地调试残留），
/// 在 Windows 上会被解析到 <c>C:\private\tmp\License.lic</c>，用户拿到发布包必然失败。
/// 现在按「显式配置 → 环境变量 → 程序目录同名文件」的顺序查找，
/// 找不到就进入评估模式（会有水印）并给出明确提示，绝不因此崩溃。
/// </summary>
public sealed class MarkdownConverter
{
    /// <summary>环境变量名，便于自动化部署时注入许可证。</summary>
    public const string LicenseEnvironmentVariable = "ASPOSE_WORDS_LICENSE";

    private static readonly string[] DefaultLicenseFileNames =
    {
        "Aspose.Words.lic",
        "License.lic",
    };

    private static readonly object LicenseLock = new();
    private static AsposeLicenseStatus? _status;
    private static string? _lastAttemptedPath;

    /// <summary>当前许可证状态（首次调用时尝试加载）。</summary>
    public AsposeLicenseStatus LicenseStatus => EnsureLicense(_requestedLicensePath);

    private readonly string? _requestedLicensePath;

    /// <param name="licensePath">许可证文件路径，可为空。</param>
    public MarkdownConverter(string? licensePath = null)
    {
        _requestedLicensePath = string.IsNullOrWhiteSpace(licensePath) ? null : licensePath.Trim();
        EnsureLicense(_requestedLicensePath);
    }

    /// <summary>
    /// 提前初始化许可证。GUI 可以在启动时调用，以便尽早把状态展示给用户。
    /// </summary>
    public static AsposeLicenseStatus InitializeLicense(string? licensePath = null)
        => EnsureLicense(string.IsNullOrWhiteSpace(licensePath) ? null : licensePath.Trim());

    private static AsposeLicenseStatus EnsureLicense(string? explicitPath)
    {
        lock (LicenseLock)
        {
            // 已经成功加载过就不再重复设置
            if (_status is { IsLicensed: true })
            {
                return _status;
            }

            // 上一次已经用同一个路径尝试过且失败，直接复用结论；
            // 用户在界面上换了路径时会重新尝试。
            if (_status is not null && string.Equals(_lastAttemptedPath, explicitPath, StringComparison.Ordinal))
            {
                return _status;
            }

            _lastAttemptedPath = explicitPath;
            _status = LoadLicense(explicitPath);
            return _status;
        }
    }

    private static AsposeLicenseStatus LoadLicense(string? explicitPath)
    {
        var candidates = new List<string>();

        if (!string.IsNullOrWhiteSpace(explicitPath))
        {
            candidates.Add(explicitPath);
        }

        var fromEnvironment = Environment.GetEnvironmentVariable(LicenseEnvironmentVariable);
        if (!string.IsNullOrWhiteSpace(fromEnvironment))
        {
            candidates.Add(fromEnvironment.Trim());
        }

        foreach (var fileName in DefaultLicenseFileNames)
        {
            candidates.Add(Path.Combine(AppContext.BaseDirectory, fileName));
        }

        foreach (var candidate in candidates)
        {
            if (!File.Exists(candidate))
            {
                continue;
            }

            try
            {
                var license = new License();
                license.SetLicense(candidate);
                return new AsposeLicenseStatus(
                    true,
                    $"已加载 Aspose.Words 许可证：{candidate}",
                    candidate);
            }
            catch (Exception ex)
            {
                return new AsposeLicenseStatus(
                    false,
                    $"许可证文件 {candidate} 加载失败：{ex.Message}。导出 markdown 时会带上评估水印。",
                    candidate);
            }
        }

        var searched = string.Join("、", candidates);
        return new AsposeLicenseStatus(
            false,
            $"未找到 Aspose.Words 许可证（已查找：{searched}）。" +
            "导出 markdown 时文档会被加上评估水印并限制长度，建议在「高级设置」中指定许可证文件。",
            null);
    }

    /// <summary>
    /// 把 docx 字节转换成 markdown 文件。
    /// </summary>
    /// <param name="docxBytes">docx 文件内容。</param>
    /// <param name="markdownPath">markdown 目标路径。</param>
    /// <param name="resolveDocRef">解析跨文档引用。</param>
    /// <param name="ct">取消令牌。</param>
    public async Task ConvertAsync(
        byte[] docxBytes,
        string markdownPath,
        Func<string, ExportTarget?>? resolveDocRef,
        CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(docxBytes);
        ArgumentNullException.ThrowIfNull(markdownPath);

        ct.ThrowIfCancellationRequested();

        var directory = Path.GetDirectoryName(markdownPath)
            ?? throw new InvalidOperationException($"markdown 路径不合法：{markdownPath}");

        Directory.CreateDirectory(directory);

        // 每篇文档用独立的图片目录。旧实现把所有文档的图片都塞进同一个 images 目录，
        // Aspose 导出的图片名固定是 image.001.png 这种，同一目录下两篇文档的图片必然互相覆盖。
        var assetsDirectoryName = Path.GetFileNameWithoutExtension(markdownPath) + ".assets";
        var assetsDirectory = Path.Combine(directory, assetsDirectoryName);
        Directory.CreateDirectory(assetsDirectory);

        // Aspose 是同步 API，放到线程池执行避免阻塞 UI 线程
        await Task.Run(() =>
        {
            using var stream = new MemoryStream(docxBytes, writable: false);
            var document = new Document(stream);

            // 清空图片描述，避免 alt 文本里混入飞书生成的临时路径
            foreach (Shape shape in document.GetChildNodes(NodeType.Shape, true))
            {
                if (shape.HasImage)
                {
                    shape.AlternativeText = string.Empty;
                }
            }

            var saveOptions = new MarkdownSaveOptions
            {
                ImagesFolder = assetsDirectory,
            };

            document.Save(markdownPath, saveOptions);
        }, ct).ConfigureAwait(false);

        var content = await File.ReadAllTextAsync(markdownPath, ct).ConfigureAwait(false);
        var processed = MarkdownPostProcessor.Process(content, markdownPath, resolveDocRef);
        await File.WriteAllTextAsync(markdownPath, processed, ct).ConfigureAwait(false);
    }
}
