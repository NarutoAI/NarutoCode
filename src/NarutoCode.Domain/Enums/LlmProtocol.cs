namespace NarutoCode.Domain.Enums;

/// <summary>
/// 大模型客户端接入协议。
/// </summary>
public enum LlmProtocol
{
    /// <summary>
    /// OpenAI Chat Completions 兼容协议，可用于 OpenAI、GLM、DeepSeek 等兼容服务。
    /// </summary>
    OpenAIChat,

    /// <summary>
    /// OpenAI Responses API 协议。
    /// </summary>
    OpenAIResponses,
    /// <summary>
    /// 
    /// </summary>
    Anthropic
}
