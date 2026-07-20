using Microsoft.Agents.AI;
using NarutoCode.Domain.Messages;

namespace NarutoCode.Infrastructure.AIAgents;

/// <summary>
/// Agent 工厂，负责工作目录与会话级运行时隔离。
/// </summary>
public interface IAgentFactory
{
    /// <summary>
    /// 获取当前工作目录中指定会话的独占运行时租约。
    /// </summary>
    /// <param name="sessionId">会话标识。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>覆盖完整 Agent Run 生命周期的会话租约。</returns>
    ValueTask<IConversationAgentLease> AcquireCurrentConversationAsync(
        ConversationSessionId sessionId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// 使当前工作目录中指定会话的运行时失效；下一次执行将依据持久化历史重建。
    /// </summary>
    /// <param name="sessionId">会话标识。</param>
    void ResetCurrentConversation(ConversationSessionId sessionId);
}