using System.Text.Json.Serialization;

namespace FeishuDocExport.Models;

/// <summary>
/// 知识空间（知识库）。
/// </summary>
public sealed class WikiSpace
{
    [JsonPropertyName("space_id")]
    public string? SpaceId { get; set; }

    [JsonPropertyName("name")]
    public string? Name { get; set; }

    [JsonPropertyName("description")]
    public string? Description { get; set; }

    [JsonPropertyName("space_type")]
    public string? SpaceType { get; set; }

    [JsonPropertyName("visibility")]
    public string? Visibility { get; set; }

    public override string ToString() => Name ?? SpaceId ?? "(未命名知识库)";
}

/// <summary>获取知识空间详情的响应体。</summary>
public sealed class WikiSpaceInfo
{
    [JsonPropertyName("space")]
    public WikiSpace? Space { get; set; }
}

/// <summary>
/// 知识空间下的一个节点。飞书的一个节点既是「一篇文档」，也可能同时是「一个目录」。
/// </summary>
public sealed class WikiNode
{
    [JsonPropertyName("space_id")]
    public string? SpaceId { get; set; }

    [JsonPropertyName("node_token")]
    public string? NodeToken { get; set; }

    [JsonPropertyName("obj_token")]
    public string? ObjToken { get; set; }

    [JsonPropertyName("obj_type")]
    public string? ObjType { get; set; }

    [JsonPropertyName("parent_node_token")]
    public string? ParentNodeToken { get; set; }

    [JsonPropertyName("node_type")]
    public string? NodeType { get; set; }

    [JsonPropertyName("origin_node_token")]
    public string? OriginNodeToken { get; set; }

    [JsonPropertyName("origin_space_id")]
    public string? OriginSpaceId { get; set; }

    [JsonPropertyName("has_child")]
    public bool HasChild { get; set; }

    [JsonPropertyName("title")]
    public string? Title { get; set; }

    [JsonPropertyName("obj_create_time")]
    public string? ObjCreateTime { get; set; }

    [JsonPropertyName("obj_edit_time")]
    public string? ObjEditTime { get; set; }

    [JsonPropertyName("node_create_time")]
    public string? NodeCreateTime { get; set; }
}

/// <summary>知识空间节点列表的分页响应。</summary>
public sealed class WikiNodePagedList : IPagedList<WikiNode>
{
    [JsonPropertyName("items")]
    public List<WikiNode>? ItemList { get; set; }

    [JsonPropertyName("page_token")]
    public string? PageToken { get; set; }

    [JsonPropertyName("has_more")]
    public bool HasMore { get; set; }

    // Items 是派生属性，不参与 JSON。不标 JsonIgnore 的话，在
    // PropertyNameCaseInsensitive 下它会和映射成 "items" 的 ItemList 判为同名冲突，
    // 反序列化直接抛 InvalidOperationException。
    [JsonIgnore]
    public IReadOnlyList<WikiNode> Items => ItemList ?? (IReadOnlyList<WikiNode>)Array.Empty<WikiNode>();
}

/// <summary>知识空间列表的分页响应。</summary>
public sealed class WikiSpacePagedList : IPagedList<WikiSpace>
{
    [JsonPropertyName("items")]
    public List<WikiSpace>? ItemList { get; set; }

    [JsonPropertyName("page_token")]
    public string? PageToken { get; set; }

    [JsonPropertyName("has_more")]
    public bool HasMore { get; set; }

    [JsonIgnore]
    public IReadOnlyList<WikiSpace> Items => ItemList ?? (IReadOnlyList<WikiSpace>)Array.Empty<WikiSpace>();
}
