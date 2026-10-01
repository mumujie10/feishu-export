namespace FeishuDocExport;

/// <summary>一个节点的导出方案。</summary>
/// <param name="RequestExtension">向飞书申请导出时使用的文件扩展名。</param>
/// <param name="SaveExtension">落盘时使用的扩展名。</param>
/// <param name="ConvertToMarkdown">是否需要在下载后把 docx 转换成 markdown。</param>
/// <param name="IsDirectFileDownload">是否属于「原样下载文件」，不需要走导出任务。</param>
public sealed record ExportFormatPlan(
    string RequestExtension,
    string SaveExtension,
    bool ConvertToMarkdown,
    bool IsDirectFileDownload);

/// <summary>
/// 飞书文档类型 → 导出方案的映射。
///
/// 旧实现把这张表放在 GlobalConfig 的静态字典里，且对「不支持的类型」和
/// 「需要原样下载的文件类型」用了同一个 "file" 魔法字符串，导致下载失败后
/// 代码会继续往下走、用一个文件 token 去创建导出任务。
/// 这里把三种情况（可导出 / 原样下载 / 不支持）显式区分开。
/// </summary>
public static class ObjTypeMapper
{
    /// <summary>
    /// 解析某个文档类型在当前保存格式下的导出方案。
    /// </summary>
    /// <returns>返回 null 表示该类型不支持导出。</returns>
    public static ExportFormatPlan? Resolve(string? objType, ExportFormat format)
    {
        if (string.IsNullOrWhiteSpace(objType))
        {
            return null;
        }

        switch (objType.Trim().ToLowerInvariant())
        {
            case "doc":
            case "docx":
            case "docs":
                return format switch
                {
                    ExportFormat.Pdf => new ExportFormatPlan("pdf", "pdf", false, false),
                    ExportFormat.Markdown => new ExportFormatPlan("docx", "md", true, false),
                    _ => new ExportFormatPlan("docx", "docx", false, false),
                };

            case "sheet":
            case "bitable":
                // 表格类统一导出为 xlsx；多维表格飞书也支持 csv，这里沿用旧行为用 xlsx
                return new ExportFormatPlan("xlsx", "xlsx", false, false);

            case "file":
                // 云空间里的普通文件（pdf、zip、图片……）直接调下载接口，扩展名以服务端返回的文件名为准
                return new ExportFormatPlan(string.Empty, string.Empty, false, true);

            default:
                return null;
        }
    }

    /// <summary>该类型是否只是一个目录容器（没有内容可导出）。</summary>
    public static bool IsContainer(string? objType)
        => string.Equals(objType, "folder", StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// 归一化成飞书导出接口能接受的 type 参数。
    /// 「docs」是老文档类型的历史写法，导出接口只认「docx」。
    /// </summary>
    public static string NormalizeRequestType(string? objType)
    {
        var value = (objType ?? string.Empty).Trim().ToLowerInvariant();
        return value switch
        {
            "docs" => "docx",
            _ => value,
        };
    }

    /// <summary>给用户看的不支持原因。</summary>
    public static string DescribeUnsupported(string? objType) => objType switch
    {
        "mindnote" => "思维笔记暂不支持通过开放接口导出，请在飞书中手动导出。",
        "slides" => "幻灯片暂不支持通过开放接口导出，请在飞书中手动导出。",
        "minutes" => "妙记暂不支持通过开放接口导出，请在飞书中手动导出。",
        null or "" => "接口未返回文档类型，无法导出。",
        _ => $"暂不支持导出类型「{objType}」的文档，请在飞书中手动导出。",
    };
}
