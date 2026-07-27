using NarutoCode.Domain.Enums;
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
    /// 按工作目录获取或创建项目，但不创建会话。
    /// </summary>
    /// <param name="workDirectory">项目工作目录。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>项目摘要。</returns>
    Task<WorkspaceSummary> GetOrCreateWorkspaceAsync(
        string workDirectory,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// 列出指定项目下可供用户选择的会话摘要。
    /// </summary>
    /// <param name="projectId">项目主键。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>按最近更新时间倒序排列的会话摘要。</returns>
    Task<IReadOnlyList<ConversationSummary>> ListProjectConversationsAsync(
        long projectId,
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
    /// 为指定项目创建新的空会话并返回历史对象。
    /// </summary>
    /// <param name="projectId">项目主键。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>新会话历史。</returns>
    Task<ConversationHistory> CreateProjectConversationAsync(
        long projectId,
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

    /// <summary>
    /// 按最近更新时间倒序列出包含历史会话的工作区。
    /// </summary>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>按工作目录聚合的工作区摘要集合。</returns>
    Task<IReadOnlyList<WorkspaceSummary>> ListWorkspacesAsync(
        CancellationToken cancellationToken = default);

    /// <summary>
    /// 规范化工作目录并幂等打开：存在会话时加载最近一条，否则创建首个会话。
    /// </summary>
    /// <param name="workDirectory">待打开的工作目录。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>打开后的会话历史及是否新建标志。</returns>
    Task<OpenWorkspaceResult> OpenWorkspaceAsync(
        string workDirectory,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// 打开或创建指定来源类型的通道会话（如企业微信）。
    /// 按 Source 过滤，不在 TUI 和桌面端显示。
    /// </summary>
    /// <param name="workDirectory">工作目录。</param>
    /// <param name="source">会话来源类型。</param>
    /// <param name="sourceId">会话来源标识，用于区分同一来源类型下的不同通道会话。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>打开后的会话历史及是否新建标志。</returns>
    Task<OpenWorkspaceResult> OpenWorkspaceBySourceAsync(
        string workDirectory,
        ConversationSource source,
        string sourceId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// 按工作目录、来源类型和来源标识获取或创建会话，仅返回会话标识。
    /// 用于网关通道按来源（如企业微信的 chatId/senderId）动态绑定独立会话，不加载完整历史。
    /// </summary>
    /// <param name="workDirectory">工作目录。</param>
    /// <param name="source">会话来源类型。</param>
    /// <param name="sourceId">会话来源标识。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>匹配的会话标识。</returns>
    Task<ConversationSessionId> GetOrCreateSessionIdBySourceAsync(
        string workDirectory,
        ConversationSource source,
        string sourceId,
        CancellationToken cancellationToken = default);
}
