namespace NarutoCode.Domain.Interactions;

/// <summary>
/// 用户交互状态：一次交互从发起到终结的生命周期。
/// </summary>
public enum UserInteractionStatus
{
    /// <summary>
    /// 等待用户应答中。
    /// </summary>
    Pending = 0,

    /// <summary>
    /// 用户已提交应答。
    /// </summary>
    Completed = 1,

    /// <summary>
    /// 用户取消（Esc）或运行取消（Ctrl+C / 进程退出后启动清理）。
    /// </summary>
    Cancelled = 2,

    /// <summary>
    /// 预留：交互超时未应答。
    /// </summary>
    Expired = 3
}
