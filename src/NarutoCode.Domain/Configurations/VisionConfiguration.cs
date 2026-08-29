namespace NarutoCode.Domain.Configurations;

/// <summary>
/// 独立视觉模型配置：主模型不支持视觉时，可配置一个小视觉模型负责识别图片并返回文本描述。
/// </summary>
public sealed class VisionConfiguration
{
    /// <summary>
    /// 是否启用独立视觉模型；为 false 时图片识别工具不挂载。
    /// </summary>
    public bool Enabled { get; set; }

    /// <summary>
    /// 视觉模型接入协议，支持 OpenAIChat、OpenAIResponses 与 Anthropic。
    /// </summary>
    public string Protocol { get; set; } = "OpenAIChat";

    /// <summary>
    /// 视觉模型服务地址，必须是有效的绝对 URL。
    /// </summary>
    public string Address { get; set; } = string.Empty;

    /// <summary>
    /// 视觉模型服务访问密钥。
    /// </summary>
    public string ApiKey { get; set; } = string.Empty;

    /// <summary>
    /// 视觉模型名称，例如 qwen-vl-plus、glm-4v-flash。
    /// </summary>
    public string Model { get; set; } = string.Empty;

    /// <summary>
    /// 单次识别最大输出 Token 数，视觉描述无需过长，默认 1024。
    /// </summary>
    public int MaxOutputTokens { get; set; } = 1024;

    /// <summary>
    /// 视觉模型调用超时时间（秒），默认 60。
    /// </summary>
    public int TimeoutSeconds { get; set; } = 60;

    /// <summary>
    /// 配置是否完整可用：已启用且地址、ApiKey、模型名均已填写。
    /// </summary>
    public bool IsValid =>
        Enabled
        && !string.IsNullOrWhiteSpace(Address)
        && !string.IsNullOrWhiteSpace(ApiKey)
        && !string.IsNullOrWhiteSpace(Model);
}
