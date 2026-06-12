using System.Text.Json.Serialization;

namespace NarutoCode.Domain.Configurations;

[JsonSourceGenerationOptions(WriteIndented = true, PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase, AllowTrailingCommas = true)]
[JsonSerializable(typeof(AppConfiguration))]
internal partial class AppConfigurationContext : JsonSerializerContext
{
}

/// <summary>
/// 程序配置，聚合运行时需要读取的核心配置。
/// </summary>
public sealed class AppConfiguration
{
    /// <summary>
    /// LLM 模型配置。
    /// </summary>
    public LlmConfiguration Llm { get; set; } = new();

    /// <summary>
    /// 系统运行配置。
    /// </summary>
    public SystemConfiguration System { get; set; } = new();

    /// <summary>
    /// 是否开启工具调用审批，默认关闭。
    /// </summary>
    public bool EnableApproval { get; set; }

    /// <summary>
    /// 最大交互次数
    /// </summary>
    public int MaxTurnCount { get; set; } = 10;
}