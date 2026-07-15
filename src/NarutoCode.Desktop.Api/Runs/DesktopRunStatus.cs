namespace NarutoCode.Desktop.Api.Runs;

/// <summary>
/// Desktop Run 生命周期状态。
/// </summary>
internal enum DesktopRunStatus
{
    /// <summary>已入队，尚未开始。</summary>
    Queued,
    /// <summary>正在执行。</summary>
    Running,
    /// <summary>等待工具审批。</summary>
    WaitingApproval,
    /// <summary>已完成。</summary>
    Completed,
    /// <summary>已取消。</summary>
    Cancelled,
    /// <summary>执行失败。</summary>
    Failed
}
