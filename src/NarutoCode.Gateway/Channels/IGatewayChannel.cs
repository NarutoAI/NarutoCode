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
    /// 发送或更新一条回复消息。
    /// 支持通道基于同一 StreamId 持续展示流式内容。
    /// </summary>
    /// <param name="message">待发送的出站消息。</param>
    /// <param name="ct">取消令牌。</param>
    ValueTask SendAsync(GatewayOutboundMessage message, CancellationToken ct);

    /// <summary>
    /// 入站消息事件，网关宿主订阅后桥接到 Agent 会话。
    /// </summary>
    event Func<GatewayInboundMessage, CancellationToken, ValueTask>? OnMessageReceived;
}
