using System.Text.Json.Serialization;

namespace FeishuDocExport.Models;

/// <summary>
/// 个人空间云文档中的一个文件或文件夹。
/// </summary>
public sealed class CloudDocFile
{
    [JsonPropertyName("token")]
    public string? Token { get; set; }

    [JsonPropertyName("name")]
    public string? Name { get; set; }

    /// <summary>doc / docx / sheet / bitable / file / folder / mindnote ...</summary>
    [JsonPropertyName("type")]
    public string? Type { get; set; }

    [JsonPropertyName("parent_token")]
    public string? ParentToken { get; set; }

    [JsonPropertyName("url")]
    public string? Url { get; set; }

    [JsonPropertyName("created_time")]
    public string? CreatedTime { get; set; }

    [JsonPropertyName("modified_time")]
    public string? ModifiedTime { get; set; }

    [JsonPropertyName("owner_id")]
    public string? OwnerId { get; set; }
}

/// <summary>云文档文件夹列表的分页响应。</summary>
public sealed class CloudDocPagedList : IPagedList<CloudDocFile>
{
    [JsonPropertyName("files")]
    public List<CloudDocFile>? FileList { get; set; }

    [JsonPropertyName("page_token")]
    public string? PageToken { get; set; }

    [JsonPropertyName("next_page_token")]
    public string? NextPageToken { get; set; }

    [JsonPropertyName("has_more")]
    public bool HasMore { get; set; }

    public IReadOnlyList<CloudDocFile> Items => FileList ?? (IReadOnlyList<CloudDocFile>)Array.Empty<CloudDocFile>();

    /// <summary>飞书在不同接口上分别用 page_token / next_page_token，这里统一取。</summary>
    public string? EffectivePageToken => string.IsNullOrWhiteSpace(PageToken) ? NextPageToken : PageToken;
}

/// <summary>文件夹元信息。</summary>
public sealed class CloudDocFolderMeta
{
    [JsonPropertyName("id")]
    public string? Id { get; set; }

    [JsonPropertyName("name")]
    public string? Name { get; set; }

    [JsonPropertyName("token")]
    public string? Token { get; set; }

    [JsonPropertyName("parent_id")]
    public string? ParentId { get; set; }
}
