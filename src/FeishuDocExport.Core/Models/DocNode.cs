namespace FeishuDocExport.Models;

/// <summary>
/// 统一的文档节点模型，把「知识库节点」和「个人空间云文档」归一化成同一种结构，
/// 这样目录树生成、路径去重、引用解析只需要写一份。
/// </summary>
public sealed class DocNode
{
    /// <summary>
    /// 节点在树中的唯一标识。
    /// wiki 下是 node_token，云文档下是文件 token。
    /// </summary>
    public required string NodeKey { get; init; }

    /// <summary>
    /// 文档本身的 token，用于调用导出/下载接口，以及按文档去重。
    /// wiki 下是 obj_token（同一个文档可能以快捷方式挂在多个节点下），云文档下与 <see cref="NodeKey"/> 相同。
    /// </summary>
    public required string DocKey { get; init; }

    /// <summary>节点标题（文档名）。</summary>
    public required string Title { get; init; }

    /// <summary>文档类型：doc / docx / sheet / bitable / file / folder ...</summary>
    public required string ObjType { get; init; }

    /// <summary>父节点标识，根节点为 null。</summary>
    public string? ParentKey { get; init; }

    /// <summary>是否为纯目录（云文档的 folder）。</summary>
    public bool IsFolder => string.Equals(ObjType, "folder", StringComparison.OrdinalIgnoreCase);

    public override string ToString() => $"{Title} ({ObjType})";
}
