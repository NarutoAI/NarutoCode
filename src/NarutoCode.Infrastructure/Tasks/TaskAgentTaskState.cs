using System.Text.Json.Serialization;
using Microsoft.Agents.AI;
using NarutoCode.Domain;
using NarutoCode.Domain.Enums;
using NarutoCode.Domain.Models;

namespace NarutoCode.Infrastructure.Tasks;

public class TaskAgentTaskState
{
    /// <summary>
    /// </summary>
    [JsonPropertyName("items")]
    public List<TaskAgentTask> Items { get; set; } = [];

    /// <summary>
    /// 创建新任务。
    /// </summary>
    /// <param name="subject">任务标题。</param>
    /// <param name="description">任务描述。</param>
    /// <param name="activeForm">执行中展示文案。</param>
    /// <param name="metadata">附加元数据。</param>
    /// <returns>创建后的任务快照。</returns>
    public TaskAgentTask Create(
        string subject,
        string description,
        string? activeForm,
        IReadOnlyDictionary<string, object?>? metadata)
    {
        var now = DateTimeOffset.UtcNow;
        var task = new TaskAgentTask
        {
            Id = CreateTaskId(),
            Subject = subject,
            Description = description,
            ActiveForm = string.IsNullOrWhiteSpace(activeForm) ? null : activeForm.Trim(),
            Status = TaskAgentTaskStatus.Pending,
            CreatedAt = now,
            UpdatedAt = now
        };

        if (metadata is not null)
        {
            foreach (var item in metadata)
            {
                task.Metadata[item.Key] = item.Value;
            }
        }

        this.Items.Add(task);
        return task;
    }

    /// <summary>
    /// 根据任务 ID 获取任务快照。
    /// </summary>
    /// <param name="taskId">任务 ID。</param>
    /// <returns>任务快照；不存在时返回 null。</returns>
    public TaskAgentTask? Get(string taskId)
    {
        return this.Items.FirstOrDefault(a => a.Id == taskId);
    }


    /// <summary>
    /// 更新指定任务，并同步刷新更新时间。
    /// </summary>
    /// <param name="taskId">任务 ID。</param>
    /// <param name="update">更新委托。</param>
    /// <returns>更新后的任务快照；不存在时返回 null。</returns>
    public TaskAgentTask? Update(string taskId, Action<TaskAgentTask> update)
    {
        var task = Get(taskId);
        if (task == null)
        {
            return null;
        }

        update(task);
        task.UpdatedAt = DateTimeOffset.UtcNow;
        return task;
    }

    /// <summary>
    /// 删除指定任务并清理其它任务中的依赖引用。
    /// </summary>
    /// <param name="taskId">任务 ID。</param>
    /// <returns>是否删除成功。</returns>
    public bool Delete(string taskId)
    {
        var oldTask = Get(taskId);
        if (oldTask == null)
        {
            return false;
        }

        //移除任务
        this.Items.Remove(oldTask);

        foreach (var task in Items)
        {
            task.Blocks.Remove(taskId);
            task.BlockedBy.Remove(taskId);
            task.UpdatedAt = DateTimeOffset.UtcNow;
        }

        return true;
    }

    /// <summary>
    /// 添加当前任务阻塞目标任务的依赖关系。
    /// </summary>
    /// <param name="taskId">当前任务 ID。</param>
    /// <param name="blockedTaskId">被当前任务阻塞的任务 ID。</param>
    public void AddBlocks(string taskId, string blockedTaskId)
    {
        var task = Get(taskId);
        if (task == null)
        {
            return;
        }

        AddUnique(task.Blocks, blockedTaskId);
        task.UpdatedAt = DateTimeOffset.UtcNow;
        //获取阻塞的任务
        var blockedTask = Get(blockedTaskId);
        if (blockedTask != null)
        {
            AddUnique(blockedTask.BlockedBy, taskId);
            blockedTask.UpdatedAt = DateTimeOffset.UtcNow;
        }
    }

    /// <summary>
    /// 添加当前任务被前置任务阻塞的依赖关系。
    /// </summary>
    /// <param name="taskId">当前任务 ID。</param>
    /// <param name="blockingTaskId">阻塞当前任务的任务 ID。</param>
    public void AddBlockedBy(string taskId, string blockingTaskId)
    {
        var task = Get(taskId);
        if (task == null)
        {
            return;
        }

        AddUnique(task.BlockedBy, blockingTaskId);
        task.UpdatedAt = DateTimeOffset.UtcNow;

        //获取阻塞的任务
        var blockingTask = Get(blockingTaskId);
        if (blockingTask != null)
        {
            AddUnique(blockingTask.Blocks, taskId);
            blockingTask.UpdatedAt = DateTimeOffset.UtcNow;
        }
    }

    /// <summary>
    /// 生成短任务 ID。
    /// </summary>
    /// <returns>8 位任务 ID。</returns>
    private static string CreateTaskId()
    {
        return SnowflakeIdHelper.Instance.NextId().ToString();
    }

    /// <summary>
    /// 向列表追加唯一值。
    /// </summary>
    /// <param name="values">目标列表。</param>
    /// <param name="value">待追加值。</param>
    private static void AddUnique(List<string> values, string value)
    {
        if (!string.IsNullOrWhiteSpace(value) && !values.Contains(value, StringComparer.Ordinal))
        {
            values.Add(value);
        }
    }
}