using System.Text.Json.Serialization;

namespace FeishuDocExport.Models;

/// <summary>创建导出任务的响应。</summary>
public sealed class ExportTaskTicket
{
    [JsonPropertyName("ticket")]
    public string? Ticket { get; set; }
}

/// <summary>导出任务的结果。</summary>
public sealed class ExportTaskResult
{
    [JsonPropertyName("file_extension")]
    public string? FileExtension { get; set; }

    [JsonPropertyName("type")]
    public string? Type { get; set; }

    [JsonPropertyName("file_name")]
    public string? FileName { get; set; }

    [JsonPropertyName("file_token")]
    public string? FileToken { get; set; }

    [JsonPropertyName("file_size")]
    public long FileSize { get; set; }

    [JsonPropertyName("job_error_msg")]
    public string? JobErrorMsg { get; set; }

    /// <summary>0=成功，1=初始化，2=处理中，其余为失败。</summary>
    [JsonPropertyName("job_status")]
    public int JobStatus { get; set; }
}

/// <summary>查询导出任务的响应体。</summary>
public sealed class ExportTaskQueryData
{
    [JsonPropertyName("result")]
    public ExportTaskResult? Result { get; set; }
}

/// <summary>租户 access token 响应。</summary>
public sealed class TenantAccessTokenResponse
{
    [JsonPropertyName("code")]
    public int Code { get; set; }

    [JsonPropertyName("msg")]
    public string? Msg { get; set; }

    [JsonPropertyName("tenant_access_token")]
    public string? TenantAccessToken { get; set; }

    /// <summary>过期时间，单位秒。</summary>
    [JsonPropertyName("expire")]
    public int Expire { get; set; }
}

/// <summary>
/// 飞书出错时的最小响应体（HTTP 层错误、下载接口返回 JSON 错误体时用它解析 code / msg）。
/// </summary>
public sealed class FeishuErrorBody
{
    [JsonPropertyName("code")]
    public int Code { get; set; }

    [JsonPropertyName("msg")]
    public string? Msg { get; set; }
}
