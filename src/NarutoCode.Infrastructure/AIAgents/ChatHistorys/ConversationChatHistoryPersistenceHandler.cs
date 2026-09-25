using NarutoCode.Infrastructure.Stores;

namespace NarutoCode.Infrastructure.AIAgents.ChatHistorys;

/// <summary>
/// 基于聊天消息写入器的聊天历史持久化处理器。
/// </summary>
/// <param name="chatMessageWriter">聊天历史与运行时上下文写入器。</param>
public sealed class ConversationChatHistoryPersistenceHandler(
    AgentChatMessageWriter chatMessageWriter)
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
    /// 在同一事务中持久化新增聊天消息与 LLM 运行时覆盖上下文。
    /// </summary>
    /// <param name="context">聊天历史持久化上下文。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    private Task PersistCoreAsync(
        ChatHistoryPersistenceContext context,
        CancellationToken cancellationToken)
    {
        return chatMessageWriter.PersistHistoriesAsync(
            context.SessionId,
            context.Messages,
            context.RuntimeMessages,
            context.TotalUsage,
            context.InputTokenCount,
            cancellationToken);
    }
}
