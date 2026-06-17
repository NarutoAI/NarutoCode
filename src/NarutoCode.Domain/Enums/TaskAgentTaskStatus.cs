namespace NarutoCode.Domain.Enums;

/// <summary>
/// 任务状态
/// </summary>
public enum TaskAgentTaskStatus
{
    /// <summary>
    /// 任务已创建，等待执行。
    /// </summary>
    Pending,

    /// <summary>
    /// 任务正在执行。
    /// </summary>
    InProgress,

    /// <summary>
    /// 等待确认
    /// </summary>
    WaitingAck,
    /// <summary>
    /// 任务已完成。
    /// </summary>
    Completed,

    /// <summary>
    /// 任务已停止。
    /// </summary>
    Stopped
}