using System.Text.Json.Serialization;
using NarutoCode.Domain.Enums;

namespace NarutoCode.Domain.Configurations;

/// <summary>
/// 应用运行时设置，用于保存跨启动生效的用户选择。
/// </summary>
public sealed class AppSettings
{
    /// <summary>
    /// 当前默认使用的 LLM provider。
    /// </summary>
    public string Provider { get; set; } = string.Empty;

    /// <summary>
    /// 当前默认使用的 LLM 推理强度。
    /// </summary>
    [JsonConverter(typeof(JsonStringEnumConverter<LlmEffort>))]
    public LlmEffort Effort { get; set; } = LlmEffort.Medium;
}
