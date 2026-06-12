using NarutoCode.Domain.Enums;

namespace NarutoCode.Domain.Configurations;

/// <summary>
/// LLM 模型配置，描述模型厂商、接入协议、服务地址、访问密钥和模型名称。
/// </summary>
public sealed class LlmConfiguration
{
    /// <summary>
    /// 模型厂商名称，例如 OpenAI、GLM、DeepSeek 或 Custom。
    /// </summary>
    public string Provider { get; set; } = "OpenAI";

    /// <summary>
    /// 模型接入协议，例如 OpenAIChat 或 OpenAIResponses。
    /// </summary>
    public string Protocol { get; set; } = nameof(LlmProtocol.OpenAIChat);

    /// <summary>
    /// 模型服务地址。
    /// </summary>
    public string Address { get; set; } = string.Empty;

    /// <summary>
    /// 模型服务访问密钥。
    /// </summary>
    public string ApiKey { get; set; } = string.Empty;

    /// <summary>
    /// 模型名称。
    /// </summary>
    public string Model { get; set; } = string.Empty;

    /// <summary>
    /// 最大上下文窗口
    /// </summary>
    public int MaxContextWindowTokens { get; set; }
}