using System.Text.Json.Serialization;

namespace FeishuDocExport.Models;

/// <summary>
/// 飞书开放平台统一响应包装：{ "code": 0, "msg": "success", "data": { ... } }。
/// </summary>
/// <typeparam name="T">data 字段的类型。</typeparam>
public sealed class FeishuResponse<T>
{
    [JsonPropertyName("code")]
    public int Code { get; set; }

    [JsonPropertyName("msg")]
    public string? Msg { get; set; }

    [JsonPropertyName("data")]
    public T? Data { get; set; }
}

/// <summary>
/// 带分页信息的列表响应。
/// </summary>
/// <typeparam name="T">列表项类型。</typeparam>
public interface IPagedList<out T>
{
    /// <summary>当前页的数据项。</summary>
    IReadOnlyList<T> Items { get; }

    /// <summary>下一页的 token，没有更多数据时为 null。</summary>
    string? PageToken { get; }

    /// <summary>是否还有下一页。</summary>
    bool HasMore { get; }
}

internal static class PagedListHelper
{
    /// <summary>
    /// 只有「还有下一页」且「拿到了非空 page_token」时才继续翻页，
    /// 避免飞书侧返回 has_more=true 却不给 token 时死循环。
    /// </summary>
    public static bool ShouldContinue(bool hasMore, string? pageToken)
        => hasMore && !string.IsNullOrWhiteSpace(pageToken);
}
