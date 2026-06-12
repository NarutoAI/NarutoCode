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

        // 
        return conversationRepositoryCoordinator.AddAsync(
            context.SessionId,
            context.Messages.ToList(),
            context.TotalUsage,cancellationToken);
    }
}
