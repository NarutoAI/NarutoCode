using System.Text.Json.Serialization;
using NarutoCode.Domain.Models;

namespace NarutoCode.Infrastructure.AIAgents.AIContextProviders;

/// <summary>
/// Task 工具通用返回结果。
/// </summary>
internal class TaskToolResult
{
    /// <summary>
    /// 操作是否成功。
    /// </summary>
    [JsonPropertyName("success")]
    public bool Success { get; init; }

    /// <summary>
    /// 任务 ID。
    /// </summary>
    [JsonPropertyName("task_id")]
    public string? TaskId { get; init; }

    /// <summary>
    /// 错误信息。
    /// </summary>
    [JsonPropertyName("error")]
    public string? ErrorMessage { get; init; }

    /// <summary>
    /// 创建失败结果。
    /// </summary>
    /// <param name="errorMessage">错误信息。</param>
    /// <param name="taskId">任务 ID。</param>
    /// <returns>失败结果。</returns>
    public static TaskToolResult Error(string? errorMessage, string? taskId = null)
    {
        return new TaskToolResult
        {
            Success = false,
            TaskId = taskId,
            ErrorMessage = errorMessage
        };
    }
}

/// <summary>
/// TaskCreate 工具返回结果。
/// </summary>
internal sealed class TaskCreateToolResult : TaskToolResult
{
    /// <summary>
    /// 任务详情。
    /// </summary>
    [JsonPropertyName("task")]
    public TaskDetailedToolResult? Task { get; init; }

    /// <summary>
    /// 操作消息。
    /// </summary>
    [JsonPropertyName("message")]
    public string? Message { get; init; }
}

/// <summary>
/// TaskGet 工具返回结果。
/// </summary>
internal sealed class TaskGetToolResult : TaskToolResult
{
    /// <summary>
    /// 任务详情。
    /// </summary>
    [JsonPropertyName("task")]
    public TaskDetailedToolResult? Task { get; init; }
}

/// <summary>
/// TaskList 工具返回结果。
/// </summary>
internal sealed class TaskListToolResult : TaskToolResult
{
    /// <summary>
    /// 任务总数。
    /// </summary>
    [JsonPropertyName("total")]
    public int Total { get; init; }

    /// <summary>
    /// 等待执行任务数量。
    /// </summary>
    [JsonPropertyName("pending")]
    public int Pending { get; init; }

    /// <summary>
    /// 执行中任务数量。
    /// </summary>
    [JsonPropertyName("in_progress")]
    public int InProgress { get; init; }

    /// <summary>
    /// 已完成任务数量。
    /// </summary>
    [JsonPropertyName("completed")]
    public int Completed { get; init; }

    /// <summary>
    /// 已停止任务数量。
    /// </summary>
    [JsonPropertyName("stopped")]
    public int Stopped { get; init; }

    /// <summary>
    /// 任务摘要列表。
    /// </summary>
    [JsonPropertyName("tasks")]
    public TaskListItemToolResult[] Tasks { get; init; } = [];
}

/// <summary>
/// TaskUpdate 工具返回结果。
/// </summary>
internal sealed class TaskUpdateToolResult : TaskToolResult
{
    /// <summary>
    /// 已更新字段。
    /// </summary>
    [JsonPropertyName("updated_fields")]
    public string[] UpdatedFields { get; init; } = [];

    /// <summary>
    /// 状态变更。
    /// </summary>
    [JsonPropertyName("status_change")]
    public TaskStatusChangeToolResult? StatusChange { get; init; }

    /// <summary>
    /// 任务详情。
    /// </summary>
    [JsonPropertyName("task")]
    public TaskDetailedToolResult? Task { get; init; }

    /// <summary>
    /// 操作消息。
    /// </summary>
    [JsonPropertyName("message")]
    public string? Message { get; init; }
}

/// <summary>
/// TaskStop 工具返回结果。
/// </summary>
internal sealed class TaskStopToolResult : TaskToolResult
{
    /// <summary>
    /// 操作消息。
    /// </summary>
    [JsonPropertyName("message")]
    public string? Message { get; init; }

    /// <summary>
    /// 任务详情。
    /// </summary>
    [JsonPropertyName("task")]
    public TaskDetailedToolResult? Task { get; init; }
}

/// <summary>
/// 任务详情工具返回模型。
/// </summary>
internal sealed class TaskDetailedToolResult
{
    /// <summary>
    /// 任务 ID。
    /// </summary>
    [JsonPropertyName("id")]
    public required string Id { get; init; }

    /// <summary>
    /// 任务标题。
    /// </summary>
    [JsonPropertyName("subject")]
    public required string Subject { get; init; }

    /// <summary>
    /// 任务描述。
    /// </summary>
    [JsonPropertyName("description")]
    public required string Description { get; init; }

    /// <summary>
    /// 执行中展示文案。
    /// </summary>
    [JsonPropertyName("active_form")]
    public string? ActiveForm { get; init; }

    /// <summary>
    /// 任务状态。
    /// </summary>
    [JsonPropertyName("status")]
    public required string Status { get; init; }

    /// <summary>
    /// 任务所有者。
    /// </summary>
    [JsonPropertyName("owner")]
    public string? Owner { get; init; }

    /// <summary>
    /// 被当前任务阻塞的任务 ID 集合。
    /// </summary>
    [JsonPropertyName("blocks")]
    public string[] Blocks { get; init; } = [];

    /// <summary>
    /// 阻塞当前任务的任务 ID 集合。
    /// </summary>
    [JsonPropertyName("blocked_by")]
    public string[] BlockedBy { get; init; } = [];

    /// <summary>
    /// 附加元数据。
    /// </summary>
    [JsonPropertyName("metadata")]
    public Dictionary<string, object?> Metadata { get; init; } = [];

    /// <summary>
    /// 任务输出。
    /// </summary>
    [JsonPropertyName("output")]
    public string? Output { get; init; }

    /// <summary>
    /// 错误信息。
    /// </summary>
    [JsonPropertyName("error")]
    public string? Error { get; init; }

    /// <summary>
    /// 创建时间。
    /// </summary>
    [JsonPropertyName("created_at")]
    public DateTimeOffset CreatedAt { get; init; }

    /// <summary>
    /// 更新时间。
    /// </summary>
    [JsonPropertyName("updated_at")]
    public DateTimeOffset UpdatedAt { get; init; }

    /// <summary>
    /// 从任务实体创建工具返回模型。
    /// </summary>
    /// <param name="task">任务实体。</param>
    /// <returns>任务详情工具返回模型。</returns>
    public static TaskDetailedToolResult FromTask(TaskAgentTask task)
    {
        return new TaskDetailedToolResult
        {
            Id = task.Id,
            Subject = task.Subject,
            Description = task.Description,
            ActiveForm = task.ActiveForm,
            Status = task.ToWireStatus(),
            Owner = task.Owner,
            Blocks = task.Blocks.ToArray(),
            BlockedBy = task.BlockedBy.ToArray(),
            Metadata = new Dictionary<string, object?>(task.Metadata, StringComparer.Ordinal),
            Output = task.Output,
            Error = task.Error,
            CreatedAt = task.CreatedAt,
            UpdatedAt = task.UpdatedAt
        };
    }
}

/// <summary>
/// 任务列表项工具返回模型。
/// </summary>
internal sealed class TaskListItemToolResult
{
    /// <summary>
    /// 任务 ID。
    /// </summary>
    [JsonPropertyName("id")]
    public required string Id { get; init; }

    /// <summary>
    /// 任务标题。
    /// </summary>
    [JsonPropertyName("subject")]
    public required string Subject { get; init; }

    /// <summary>
    /// 任务状态。
    /// </summary>
    [JsonPropertyName("status")]
    public required string Status { get; init; }

    /// <summary>
    /// 任务所有者。
    /// </summary>
    [JsonPropertyName("owner")]
    public string? Owner { get; init; }

    /// <summary>
    /// 未完成的阻塞任务 ID 集合。
    /// </summary>
    [JsonPropertyName("blocked_by")]
    public string[] BlockedBy { get; init; } = [];

    /// <summary>
    /// 从任务实体创建列表项模型。
    /// </summary>
    /// <param name="task">任务实体。</param>
    /// <param name="completedTaskIds">已完成任务 ID 集合。</param>
    /// <returns>任务列表项工具返回模型。</returns>
    public static TaskListItemToolResult FromTask(TaskAgentTask task, IReadOnlySet<string> completedTaskIds)
    {
        return new TaskListItemToolResult
        {
            Id = task.Id,
            Subject = task.Subject,
            Status = task.ToWireStatus(),
            Owner = task.Owner,
            BlockedBy = task.BlockedBy.Where(id => !completedTaskIds.Contains(id)).ToArray()
        };
    }
}

/// <summary>
/// 任务状态变更工具返回模型。
/// </summary>
internal sealed class TaskStatusChangeToolResult
{
    /// <summary>
    /// 原状态。
    /// </summary>
    [JsonPropertyName("from")]
    public required string From { get; init; }

    /// <summary>
    /// 新状态。
    /// </summary>
    [JsonPropertyName("to")]
    public required string To { get; init; }
}
