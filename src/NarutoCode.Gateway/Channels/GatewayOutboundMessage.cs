namespace NarutoCode.Gateway.Channels;

/// <summary>
/// 网关通道出站消息，支持向具备流式能力的通道持续更新同一条回复。
/// </summary>
/// <param name="RecipientId">接收者标识（用户ID或群聊ID）。</param>
/// <param name="StreamId">回复流标识，同一条回复的所有片段必须保持一致。</param>
/// <param name="Text">当前完整的累计回复文本。</param>
/// <param name="IsCompleted">是否为流的最终完成帧。</param>
public sealed record GatewayOutboundMessage(
    string RecipientId,
    string StreamId,
    string Text,
    bool IsCompleted);
