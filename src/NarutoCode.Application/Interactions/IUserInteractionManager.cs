using NarutoCode.Domain.Interactions;

namespace NarutoCode.Application.Interactions;

/// <summary>
/// 用户交互管理器：Agent 工具与前端（TUI/桌面端）之间的结构化交互桥梁。
/// 工具通过 <see cref="RequestAsync" /> 发起交互并异步等待；前端订阅事件渲染 UI 并调用
/// <see cref="CompleteAsync" /> 回写结果。数据库（IUserInteractionStore）是等待态的状态来源。
/// </summary>
public interface IUserInteractionManager
{
    /// <summary>
    /// 发起一次用户交互：落库 Pending → 通知前端 → 挂起等待用户应答。
    /// 取消令牌触发时交互落库为 Cancelled 并向调用方抛出 <see cref="OperationCanceledException" />。
    /// </summary>
    /// <param name="request">交互请求。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>用户应答或取消的交互结果。</returns>
    Task<UserInteractionResult> RequestAsync(UserInteractionRequest request, CancellationToken cancellationToken = default);

    /// <summary>
    /// 完成一次交互：回写终态、唤醒等待中的工具调用并广播 <see cref="InteractionCompleted" />。
    /// </summary>
    /// <param name="interactionId">交互标识。</param>
    /// <param name="result">交互结果。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>表示异步完成操作的任务。</returns>
    Task CompleteAsync(long interactionId, UserInteractionResult result, CancellationToken cancellationToken = default);

    /// <summary>
    /// 取消指定会话下所有等待中的交互（进程重启后的启动清理）。
    /// </summary>
    /// <param name="sessionId">会话标识。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>被取消的交互数量。</returns>
    Task<int> CancelPendingAsync(long sessionId, CancellationToken cancellationToken = default);

    /// <summary>
    /// 前端订阅交互请求：渲染交互 UI（订阅者内部自行调度到 UI 线程，不应长时间阻塞调用方）。
    /// </summary>
    event Func<UserInteractionRequest, CancellationToken, Task>? InteractionRequested;

    /// <summary>
    /// 交互终态广播：前端用于关闭 UI 并在对话流留痕。
    /// </summary>
    event Action<UserInteractionResult>? InteractionCompleted;

    /// <summary>
    /// 开启当前异步调用链的会话作用域：MAF 工具执行线程无法从参数获得 SessionId，
    /// 由调用方在每次 SendMessageAsync 外层设置，工具体通过 <see cref="CurrentSessionId" /> 读取。
    /// </summary>
    /// <param name="sessionId">会话标识。</param>
    /// <returns>释放时恢复原作用域的句柄。</returns>
    IDisposable BeginSessionScope(long sessionId);

    /// <summary>
    /// 当前异步调用链的会话标识；未设置作用域时为 0。
    /// </summary>
    long CurrentSessionId { get; }
}
