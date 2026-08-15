namespace NarutoCode.Domain.Interactions;

/// <summary>
/// 用户交互持久化仓储抽象：数据库是交互等待态的状态来源，
/// TaskCompletionSource 只解决进程内异步唤醒，进程重启后以仓储记录为准。
/// </summary>
public interface IUserInteractionStore
{
    /// <summary>
    /// 保存新交互（Pending 初始态），Payload 由实现方序列化请求得到。
    /// </summary>
    /// <param name="request">交互请求。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>表示异步保存操作的任务。</returns>
    Task SaveAsync(UserInteractionRequest request, CancellationToken cancellationToken = default);

    /// <summary>
    /// 获取指定会话所有等待中的交互（从 Payload 反序列化还原请求）。
    /// </summary>
    /// <param name="sessionId">会话标识。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>Pending 状态的交互请求集合。</returns>
    Task<IReadOnlyList<UserInteractionRequest>> GetPendingAsync(long sessionId, CancellationToken cancellationToken = default);

    /// <summary>
    /// 写入交互终态（Completed/Cancelled/Expired），Result 由实现方序列化结果得到。
    /// </summary>
    /// <param name="result">交互结果。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>表示异步写入操作的任务。</returns>
    Task CompleteAsync(UserInteractionResult result, CancellationToken cancellationToken = default);

    /// <summary>
    /// 将指定会话所有等待中的交互标记为取消（进程重启后的启动清理）。
    /// </summary>
    /// <param name="sessionId">会话标识。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>被取消的交互数量。</returns>
    Task<int> CancelPendingAsync(long sessionId, CancellationToken cancellationToken = default);
}
