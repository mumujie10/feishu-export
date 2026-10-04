namespace FeishuDocExport;

/// <summary>导出对象的来源。</summary>
public enum DocSourceType
{
    /// <summary>知识库（wiki）。</summary>
    Wiki = 0,

    /// <summary>个人空间云文档（需要 folderToken）。</summary>
    CloudDoc = 1,
}

/// <summary>文档（docx 类型）导出的目标格式。</summary>
public enum ExportFormat
{
    /// <summary>Word 文档。</summary>
    Docx = 0,

    /// <summary>PDF。</summary>
    Pdf = 1,

    /// <summary>Markdown（飞书不直接提供，需要先导出 docx 再本地转换）。</summary>
    Markdown = 2,
}

/// <summary>日志级别。</summary>
public enum ExportLogLevel
{
    Info,
    Success,
    Warning,
    Error,
}

/// <summary>
/// 一次导出所需的全部配置。GUI 与 CLI 共用同一份模型。
/// </summary>
public sealed class ExportOptions
{
    /// <summary>飞书自建应用的 AppId。</summary>
    public string AppId { get; set; } = string.Empty;

    /// <summary>飞书自建应用的 AppSecret。</summary>
    public string AppSecret { get; set; } = string.Empty;

    /// <summary>
    /// 开放平台地址。国内飞书为 https://open.feishu.cn，
    /// Lark（国际版）为 https://open.larksuite.com。
    /// </summary>
    public string BaseUrl { get; set; } = FeishuEndpoints.DefaultOpenApi;

    /// <summary>文档导出的本地目录。</summary>
    public string ExportPath { get; set; } = string.Empty;

    /// <summary>导出知识库还是个人空间云文档。</summary>
    public DocSourceType SourceType { get; set; } = DocSourceType.Wiki;

    /// <summary>知识空间 Id，为空时由调用方先列出知识库让用户选择。</summary>
    public string? WikiSpaceId { get; set; }

    /// <summary>个人空间文件夹 Token，<see cref="SourceType"/> 为 CloudDoc 时必填。</summary>
    public string? FolderToken { get; set; }

    /// <summary>文档保存格式。</summary>
    public ExportFormat Format { get; set; } = ExportFormat.Docx;

    /// <summary>
    /// Aspose.Words 许可证文件路径，仅在导出 markdown 时使用。
    /// 留空时按「环境变量 → 程序目录」的顺序自动查找；找不到则进入评估模式（会加水印），但不会崩溃。
    /// </summary>
    public string? AsposeLicensePath { get; set; }

    /// <summary>单个导出任务允许等待的最长时间。</summary>
    public TimeSpan ExportTaskTimeout { get; set; } = TimeSpan.FromMinutes(10);

    /// <summary>单个 HTTP 请求失败后的重试次数（针对限流与 5xx）。</summary>
    public int MaxRetryCount { get; set; } = 3;

    /// <summary>目标文件已存在时是否跳过。适合中断后重跑的大型知识库。</summary>
    public bool SkipExistingFiles { get; set; }

    /// <summary>
    /// 增量模式：只导出新增和改动过的文档，依据是导出目录里
    /// <see cref="ExportStateStore.FileName"/> 记录的飞书编辑时间。
    /// 与 <see cref="SkipExistingFiles"/> 的区别在于后者只看文件在不在，
    /// 内容改过的文档会被误跳过。
    /// </summary>
    public bool Incremental { get; set; }

    /// <summary>验证配置是否完整，返回人类可读的错误列表（为空表示校验通过）。</summary>
    public IReadOnlyList<string> Validate()
    {
        var errors = new List<string>();

        if (string.IsNullOrWhiteSpace(AppId))
        {
            errors.Add("AppId 不能为空。");
        }

        if (string.IsNullOrWhiteSpace(AppSecret))
        {
            errors.Add("AppSecret 不能为空。");
        }

        if (string.IsNullOrWhiteSpace(BaseUrl))
        {
            errors.Add("开放平台地址不能为空。");
        }
        else if (!Uri.TryCreate(BaseUrl, UriKind.Absolute, out var uri) ||
                 (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps))
        {
            errors.Add($"开放平台地址不是合法的 URL：{BaseUrl}");
        }

        if (string.IsNullOrWhiteSpace(ExportPath))
        {
            errors.Add("导出目录不能为空。");
        }
        else if (!Path.IsPathRooted(ExportPath))
        {
            errors.Add($"导出目录需要使用绝对路径：{ExportPath}");
        }

        if (SourceType == DocSourceType.CloudDoc && string.IsNullOrWhiteSpace(FolderToken))
        {
            errors.Add("导出个人空间云文档时，folderToken 必填。");
        }

        if (SourceType == DocSourceType.Wiki && string.IsNullOrWhiteSpace(WikiSpaceId))
        {
            errors.Add("请先选择要导出的知识库。");
        }

        return errors;
    }
}

/// <summary>飞书开放平台相关的常量。</summary>
public static class FeishuEndpoints
{
    /// <summary>飞书（国内版）开放平台地址。</summary>
    public const string DefaultOpenApi = "https://open.feishu.cn";

    /// <summary>Lark（国际版）开放平台地址。</summary>
    public const string LarkOpenApi = "https://open.larksuite.com";
}
