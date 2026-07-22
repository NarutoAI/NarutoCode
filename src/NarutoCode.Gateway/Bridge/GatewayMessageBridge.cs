using System.Text;
using Microsoft.Extensions.Logging;
using NarutoCode.Domain.Conversations;
using NarutoCode.Domain.Messages;
using NarutoCode.Gateway;
using NarutoCode.Gateway.Channels;

namespace NarutoCode.Gateway.Bridge;

/// <summary>
/// 消息桥接器：将通道入站消息转发给 Agent 会话，流式推送响应给通道。
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
    /// 处理一条入站消息：发送给 Agent → 逐段流式回复通道。
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
            // 将通道附件转换为 Agent 附件（图片等），直接传递内存字节
            var attachments = inbound.Attachments
                .Select(a => new AgentMessageAttachment(a.Data, a.MediaType))
                .ToArray();

            var agentMessage = new AgentMessage(AgentMessageType.Content, inbound.Text, attachments: attachments);

            // 群聊回复群，单聊回复发送者
            var recipientId = inbound.IsGroup && !string.IsNullOrEmpty(inbound.GroupId)
                ? inbound.GroupId!
                : inbound.SenderId;

            // 每条入站消息分配一个独立的流标识，企业微信用同一个 stream.id 更新同一条消息
            var streamId = Guid.NewGuid().ToString("N");
            var responseBuilder = new StringBuilder();
            var streamStarted = false;

            // ── 流式推送循环 ──
            // 企业微信流式协议（aibot_respond_msg / msgtype=stream）的关键规则：
            // 每次帧的 stream.content 必须携带「当前完整内容」，服务端用它「全量替换」整条消息展示，
            // 而非把新内容追加到旧内容后面。
            //
            // 因此这里必须用 StringBuilder 逐步累计，每次推送累计后的完整文本。
            // 如果直接发送 Agent 的增量片段 response.Content，企业微信会用最后一片段覆盖之前的内容，
            // 用户最终只能看到最后一个片段，而不是完整的打字机效果。
            //
            // 示例：Agent 依次输出 "你"、"好"、"！"
            //   第 1 帧 → content="你",       finish=false
            //   第 2 帧 → content="你好",     finish=false
            //   第 3 帧 → content="你好！",   finish=false
            //   结束帧  → content="你好！",   finish=true
            await foreach (var response in _conversationService.SendMessageAsync(sessionId, agentMessage, ct))
            {
                // 只处理最终输出内容，忽略 Thinking/ToolCall 等中间态
                if (response.Type != AgentMessageType.Content || string.IsNullOrEmpty(response.Content))
                    continue;

                // 将增量片段累计到完整内容（全量替换协议要求）
                responseBuilder.Append(response.Content);
                streamStarted = true;

                // 推送当前完整内容，finish=false 表示流尚未结束
                await channel.SendAsync(
                    new GatewayOutboundMessage(recipientId, streamId, responseBuilder.ToString(), IsCompleted: false),
                    ct);
            }

            // Agent 输出结束后发送完成帧，finish=true 通知企业微信结束本次流式消息
            if (streamStarted)
            {
                await channel.SendAsync(
                    new GatewayOutboundMessage(recipientId, streamId, responseBuilder.ToString(), IsCompleted: true),
                    ct);
            }
        }
        catch (Exception ex)
        {
            Log.BridgeHandleFailed(_logger, channel.ChannelId, inbound.SenderId, ex);
        }
    }
}
