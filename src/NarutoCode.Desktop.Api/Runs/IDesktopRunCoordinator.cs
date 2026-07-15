using NarutoCode.Domain.Messages;

namespace NarutoCode.Desktop.Api.Runs;

/// <summary>
/// Desktop Run 协调器抽象，管理 Run 生命周期、事件流和审批。
/// </summary>
internal interface IDesktopRunCoordinator
{
    /// <summary>
    /// 为指定会话启动一个新 Run。
    /// </summary>
    /// <param name="conversationId">会话标识。</param>
    /// <param name="message">用户输入消息。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>创建的 Run 实例。</returns>
    Task<DesktopRun> StartAsync(
        ConversationSessionId conversationId,
        AgentMessage message,
        CancellationToken cancellationToken);

    /// <summary>
    /// 获取指定 Run 的事件流。
    /// </summary>
    /// <param name="runId">Run 标识。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>事件异步枚举。</returns>
    IAsyncEnumerable<RunEvent> ReadEventsAsync(
        string runId,
        CancellationToken cancellationToken);

    /// <summary>
    /// 解决工具审批。
    /// </summary>
    /// <param name="runId">Run 标识。</param>
    /// <param name="approvalId">审批调用 ID。</param>
    /// <param name="approved">是否批准。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    Task ResolveApprovalAsync(
        string runId,
        string approvalId,
        bool approved,
        CancellationToken cancellationToken);

    /// <summary>
    /// 取消指定 Run。
    /// </summary>
    /// <param name="runId">Run 标识。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    Task CancelAsync(string runId, CancellationToken cancellationToken);
}
