namespace NarutoCode.Gateway.Channels;

/// <summary>
/// 网关通道统一抽象，所有通道（企业微信、WebSocket 服务端等）实现此接口。
/// </summary>
public interface IGatewayChannel : IAsyncDisposable
{
    /// <summary>
    /// 通道标识（如 "wecom"）。
    /// </summary>
    string ChannelId { get; }

    /// <summary>
    /// 启动通道，开始监听入站消息。
    /// </summary>
    /// <param name="ct">取消令牌。</param>
    Task StartAsync(CancellationToken ct);

    /// <summary>
    /// 发送回复消息给指定接收者。
    /// </summary>
    /// <param name="recipientId">接收者标识（用户ID或群聊ID）。</param>
    /// <param name="text">回复文本。</param>
    /// <param name="ct">取消令牌。</param>
    ValueTask SendAsync(string recipientId, string text, CancellationToken ct);

    /// <summary>
    /// 入站消息事件，网关宿主订阅后桥接到 Agent 会话。
    /// </summary>
    event Func<GatewayInboundMessage, CancellationToken, ValueTask>? OnMessageReceived;
}
