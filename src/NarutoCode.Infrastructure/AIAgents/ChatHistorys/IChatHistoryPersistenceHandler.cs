namespace NarutoCode.Infrastructure.AIAgents.ChatHistorys;

/// <summary>
/// 聊天历史持久化处理器，负责将 Agent 运行过程中产生的新增消息写入外部存储。
/// </summary>
public interface IChatHistoryPersistenceHandler
{
    /// <summary>
    /// 持久化一次 Agent 调用后产生的新增聊天消息。
    /// </summary>
    /// <param name="context">聊天历史持久化上下文。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>异步持久化任务。</returns>
    Task PersistAsync(
        ChatHistoryPersistenceContext context,
        CancellationToken cancellationToken = default);
}
