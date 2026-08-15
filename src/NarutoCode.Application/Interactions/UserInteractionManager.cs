using System.Collections.Concurrent;
using NarutoCode.Domain.Interactions;

namespace NarutoCode.Application.Interactions;

/// <summary>
/// 用户交互管理器默认实现：
/// 落库 Pending → 触发前端事件 → TaskCompletionSource 挂起等待 → 前端 CompleteAsync 唤醒。
/// 取消令牌触发时交互落库为 Cancelled 并向等待方抛出 <see cref="OperationCanceledException" />。
/// </summary>
public sealed class UserInteractionManager(IUserInteractionStore store) : IUserInteractionManager
{
    // 交互等待器：雪花 Id → TCS；RunContinuationsAsynchronously 避免完成回调内联在 UI 线程执行业务延续
    private readonly ConcurrentDictionary<long, TaskCompletionSource<UserInteractionResult>> waiters = new();

    // 当前异步调用链的会话作用域：由 CLI 业务循环在发送消息外层设置，工具执行线程读取
    private static readonly AsyncLocal<long> CurrentSession = new();

    /// <inheritdoc />
    public event Func<UserInteractionRequest, CancellationToken, Task>? InteractionRequested;

    /// <inheritdoc />
    public event Action<UserInteractionResult>? InteractionCompleted;

    /// <inheritdoc />
    public long CurrentSessionId => CurrentSession.Value;

    /// <inheritdoc />
    public IDisposable BeginSessionScope(long sessionId)
    {
        // 记录外层值，释放时恢复而不是清零，支持嵌套作用域
        var previous = CurrentSession.Value;
        CurrentSession.Value = sessionId;
        return new SessionScopeRestore(() => CurrentSession.Value = previous);
    }

    /// <inheritdoc />
    public async Task<UserInteractionResult> RequestAsync(
        UserInteractionRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        // 前端未订阅时直接返回取消语义：避免工具无限挂起（桌面端未启用交互工具，正常不会走到这里）
        if (InteractionRequested is null)
        {
            return new UserInteractionResult(request.Id, UserInteractionStatus.Cancelled, "当前没有可用的交互前端。");
        }

        // 先落库再通知前端：数据库是状态来源，进程重启后可审计与清理
        await store.SaveAsync(request, cancellationToken);

        var waiter = new TaskCompletionSource<UserInteractionResult>(TaskCreationOptions.RunContinuationsAsynchronously);
        waiters[request.Id] = waiter;

        try
        {
            // 通知前端渲染交互 UI；前端回调抛出异常视为交互不可用，转为取消结果（不中断会话）
            await InteractionRequested.Invoke(request, cancellationToken);

            // 挂起等待：用户应答经 CompleteAsync 唤醒；取消令牌触发时向工具抛取消
            return await waiter.Task.WaitAsync(cancellationToken);
        }
        catch (OperationCanceledException)
        {
            // Ctrl+C / 运行取消：落库 Cancelled 后向调用方抛出取消异常，走现有取消链路
            await MarkCancelledAsync(request.Id);
            throw;
        }
        catch (Exception)
        {
            // 前端渲染失败：落库 Cancelled，向工具返回取消语义文本
            await MarkCancelledAsync(request.Id);
            return new UserInteractionResult(request.Id, UserInteractionStatus.Cancelled, "交互界面不可用。");
        }
        finally
        {
            waiters.TryRemove(request.Id, out _);
        }
    }

    /// <inheritdoc />
    public async Task CompleteAsync(
        long interactionId,
        UserInteractionResult result,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(result);

        // 终态落库失败不阻断唤醒：等待中的工具线程不能因审计记录缺失而永久挂起
        try
        {
            await store.CompleteAsync(result, cancellationToken);
        }
        catch (Exception)
        {
            // 忽略落库失败：优先保证唤醒与事件广播
        }

        if (waiters.TryRemove(interactionId, out var waiter))
        {
            waiter.TrySetResult(result);
        }

        InteractionCompleted?.Invoke(result);
    }

    /// <inheritdoc />
    public async Task<int> CancelPendingAsync(long sessionId, CancellationToken cancellationToken = default)
    {
        // 启动清理：本会话遗留 Pending 全部取消（当前无 Run 级恢复能力，重启即作废）
        return await store.CancelPendingAsync(sessionId, cancellationToken);
    }

    /// <summary>
    /// 将交互落库为 Cancelled；清理失败不向上抛，避免掩盖等待方的原始异常。
    /// </summary>
    private async Task MarkCancelledAsync(long interactionId)
    {
        var result = new UserInteractionResult(interactionId, UserInteractionStatus.Cancelled, string.Empty);
        try
        {
            await store.CompleteAsync(result, CancellationToken.None);
        }
        catch (Exception)
        {
            // 忽略清理失败：等待方已经走取消/失败路径
        }

        // 广播取消终态：前端据此关闭仍打开的弹窗并留痕
        InteractionCompleted?.Invoke(result);
    }

    /// <summary>
    /// 会话作用域恢复句柄：释放时恢复进入作用域前的值。
    /// </summary>
    private sealed class SessionScopeRestore(Action restore) : IDisposable
    {
        public void Dispose() => restore();
    }
}
