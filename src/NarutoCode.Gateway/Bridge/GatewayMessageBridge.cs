using System.Text;
using Microsoft.Extensions.Logging;
using NarutoCode.Domain.Conversations;
using NarutoCode.Domain.Messages;
using NarutoCode.Gateway;
using NarutoCode.Gateway.Channels;

namespace NarutoCode.Gateway.Bridge;

/// <summary>
/// 消息桥接器：将通道入站消息转发给 Agent 会话，收集响应后回复通道。
/// </summary>
public sealed class GatewayMessageBridge
{
    private readonly IConversationService _conversationService;
    private readonly ILogger<GatewayMessageBridge> _logger;

    public GatewayMessageBridge(
        IConversationService conversationService,
        ILogger<GatewayMessageBridge> logger)
    {
        _conversationService = conversationService;
        _logger = logger;
    }

    /// <summary>
    /// 处理一条入站消息：发送给 Agent → 收集 Content 响应 → 回复通道。
    /// </summary>
    /// <param name="channel">来源通道，用于回复。</param>
    /// <param name="inbound">入站消息。</param>
    /// <param name="sessionId">固定工作目录对应的会话标识。</param>
    /// <param name="ct">取消令牌。</param>
    public async Task HandleAsync(
        IGatewayChannel channel,
        GatewayInboundMessage inbound,
        ConversationSessionId sessionId,
        CancellationToken ct)
    {
        try
        {
            // 1. 构造 AgentMessage 并发送
            var agentMessage = new AgentMessage(AgentMessageType.Content, inbound.Text);
            var responseBuilder = new StringBuilder();

            // 2. 流式收集最终输出内容
            await foreach (var response in _conversationService.SendMessageAsync(sessionId, agentMessage, ct))
            {
                if (response.Type == AgentMessageType.Content)
                    responseBuilder.Append(response.Content);
            }

            // 3. 合并完整回复并回发通道
            var replyText = responseBuilder.ToString();
            if (string.IsNullOrWhiteSpace(replyText))
                return;

            // 群聊回复群，单聊回复发送者
            var recipientId = inbound.IsGroup && !string.IsNullOrEmpty(inbound.GroupId)
                ? inbound.GroupId!
                : inbound.SenderId;

            await channel.SendAsync(recipientId, replyText, ct);
        }
        catch (Exception ex)
        {
            Log.BridgeHandleFailed(_logger, channel.ChannelId, inbound.SenderId, ex);
        }
    }
}
