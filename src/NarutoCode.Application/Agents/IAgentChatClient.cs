using NarutoCode.Domain.Messages;

namespace NarutoCode.Application.Agents;

/// <summary>
/// Agent 对话客户端，负责向底层 Agent 会话发送用户消息并返回响应流。
/// </summary>
public interface IAgentChatClient
{
    /// <summary>
    /// 向指定会话发送用户消息，并按生成顺序返回助手输出片段。
    /// </summary>
    /// <param name="sessionId">会话标识。</param>
    /// <param name="message">用户输入消息，普通输入和工具审批响应都通过该消息表达。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>助手输出的文本片段流。</returns>
    IAsyncEnumerable<AgentMessage> SendMessageAsync(
        ConversationSessionId sessionId,
        AgentMessage message,
        CancellationToken cancellationToken = default);
}
