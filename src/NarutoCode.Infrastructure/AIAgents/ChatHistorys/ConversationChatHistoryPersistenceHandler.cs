using NarutoCode.Infrastructure.Stores;

namespace NarutoCode.Infrastructure.AIAgents.ChatHistorys;

/// <summary>
/// 基于对话仓储协调器的聊天历史持久化处理器。
/// </summary>
/// <param name="conversationRepositoryCoordinator">对话仓储协调器。</param>
public sealed class ConversationChatHistoryPersistenceHandler(
    ConversationRepositoryCoordinator conversationRepositoryCoordinator)
    : IChatHistoryPersistenceHandler
{
    /// <inheritdoc />
    public Task PersistAsync(
        ChatHistoryPersistenceContext context,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ArgumentNullException.ThrowIfNull(context);

        return PersistCoreAsync(context, cancellationToken);
    }

    /// <summary>
    /// 在同一事务中持久化 UI 追加历史和 LLM 运行时覆盖历史。
    /// </summary>
    /// <param name="context">聊天历史持久化上下文。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    private Task PersistCoreAsync(
        ChatHistoryPersistenceContext context,
        CancellationToken cancellationToken)
    {
        return conversationRepositoryCoordinator.PersistHistoriesAsync(
            context.SessionId,
            context.Messages,
            context.RuntimeMessages,
            context.TotalUsage,
            context.InputTokenCount,
            cancellationToken);
    }
}
