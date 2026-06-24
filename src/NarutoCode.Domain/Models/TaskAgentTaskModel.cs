using NarutoCode.Domain.Enums;

namespace NarutoCode.Domain.Models;

/// <summary>
/// Agent 任务实体，保存任务列表工具所需的运行时状态。
/// </summary>
public sealed class TaskAgentTask
{
    /// <summary>
    /// 任务唯一标识。
    /// </summary>
    public required string Id { get; init; }

    /// <summary>
    /// 任务标题。
    /// </summary>
    public required string Subject { get; set; }

    /// <summary>
    /// 任务描述。
    /// </summary>
    public required string Description { get; set; }

    /// <summary>
    /// 任务处于执行中时展示给用户的进行时文案。
    /// </summary>
    public string? ActiveForm { get; set; }

    /// <summary>
    /// 当前任务状态。
    /// </summary>
    public TaskAgentTaskStatus Status { get; set; }

    /// <summary>
    /// 任务所有者或执行者。
    /// </summary>
    public string? Owner { get; set; }

    /// <summary>
    /// 当前任务完成后可解除阻塞的任务 ID 集合。
    /// </summary>
    public List<string> Blocks { get; init; } = [];

    /// <summary>
    /// 当前任务依赖的前置任务 ID 集合。
    /// </summary>
    public List<string> BlockedBy { get; init; } = [];

    /// <summary>
    /// 附加元数据。
    /// </summary>
    public Dictionary<string, object?> Metadata { get; init; } = new(StringComparer.Ordinal);

    /// <summary>
    /// 任务输出内容，供 TaskOutput 读取。
    /// </summary>
    public string? Output { get; set; }

    /// <summary>
    /// 任务错误信息，供 TaskOutput 读取。
    /// </summary>
    public string? Error { get; set; }

    /// <summary>
    /// 任务创建时间。
    /// </summary>
    public DateTimeOffset CreatedAt { get; init; }

    /// <summary>
    /// 任务最后更新时间。
    /// </summary>
    public DateTimeOffset UpdatedAt { get; set; }

    /// <summary>
    /// 将任务状态转换为工具结果使用的字符串。
    /// </summary>
    public string ToWireStatus()
    {
        return this.Status switch
        {
            TaskAgentTaskStatus.Pending => TaskWireStatus.Pending,
            TaskAgentTaskStatus.InProgress => TaskWireStatus.InProgress,
            TaskAgentTaskStatus.WaitingAck => TaskWireStatus.WaitingAck,
            TaskAgentTaskStatus.Completed => TaskWireStatus.Completed,
            TaskAgentTaskStatus.Stopped => TaskWireStatus.Stopped,
            _ => "unknown"
        };
    }
}

/// <summary>
/// 任务状态字符串常量
/// </summary>
public static class TaskWireStatus
{
    public const string Pending = "pending";
    public const string InProgress = "in_progress";
    public const string WaitingAck = "waiting_ack";
    public const string Completed = "completed";
    public const string Stopped = "stopped";
    public const string Deleted = "deleted";
}
