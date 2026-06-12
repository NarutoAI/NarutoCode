using NarutoCode.Domain.Messages;

namespace NarutoCode.Domain.Conversations;

/// <summary>
/// 对话服务抽象，定义向指定会话发送用户消息并接收助手流式输出的能力。
/// </summary>
public interface IConversationService
{
    /// <summary>
    /// 加载当前工作目录最近一次对话历史；如果不存在则创建新的空对话。
    /// </summary>
    /// <param name="workDirectory">当前工作目录。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>当前工作目录对应的对话历史。</returns>
    Task<ConversationHistory> LoadWorkspaceHistoryAsync(
        string workDirectory,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// 列出当前工作目录下可供用户选择的会话摘要。
    /// </summary>
    /// <param name="workDirectory">当前工作目录。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>按最近更新时间倒序排列的会话摘要。</returns>
    Task<IReadOnlyList<ConversationSummary>> ListWorkspaceConversationsAsync(
        string workDirectory,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// 为当前工作目录创建新的空会话并返回历史对象。
    /// </summary>
    /// <param name="workDirectory">当前工作目录。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>新会话历史。</returns>
    Task<ConversationHistory> CreateWorkspaceConversationAsync(
        string workDirectory,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// 按指定会话标识加载会话历史。
    /// </summary>
    /// <param name="conversationId">会话标识。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>指定会话历史。</returns>
    Task<ConversationHistory> LoadConversationHistoryAsync(
        ConversationSessionId conversationId,
        CancellationToken cancellationToken = default);


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

    /// <summary>
    /// 重置指定对话的运行时 Agent 会话，避免取消后复用半截工具调用上下文。
    /// </summary>
    /// <param name="sessionId">需要重置的会话标识。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>表示异步重置操作的任务。</returns>
    Task ResetRuntimeSessionAsync(
        ConversationSessionId sessionId,
        CancellationToken cancellationToken = default);
}
