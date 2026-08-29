using System.Text.Json.Serialization;

namespace NarutoCode.Infrastructure.AIAgents.AIContextProviders;

/// <summary>
/// 图片识别工具返回结果。
/// </summary>
internal sealed class VisionRecognitionToolResult
{
    /// <summary>
    /// 识别是否成功。
    /// </summary>
    [JsonPropertyName("success")]
    public bool Success { get; init; }

    /// <summary>
    /// 视觉模型输出的图片描述文本。
    /// </summary>
    [JsonPropertyName("description")]
    public string? Description { get; init; }

    /// <summary>
    /// 失败时的错误信息。
    /// </summary>
    [JsonPropertyName("error")]
    public string? Error { get; init; }

    /// <summary>
    /// 图片来源（路径或 URL 原文）。
    /// </summary>
    [JsonPropertyName("source")]
    public string? Source { get; init; }

    /// <summary>
    /// 图片媒体类型（如 image/png）。
    /// </summary>
    [JsonPropertyName("media_type")]
    public string? MediaType { get; init; }

    /// <summary>
    /// 图片字节数。
    /// </summary>
    [JsonPropertyName("bytes")]
    public int Bytes { get; init; }
}
