using System.ComponentModel;
using System.Text;
using System.Text.Json;
using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;
using NarutoCode.Domain.Enums;
using NarutoCode.Domain.Models;
using NarutoCode.Infrastructure.JsonSerializerContexts;
using NarutoCode.Infrastructure.Tasks;
using NarutoCode.Infrastructure.Tools;

namespace NarutoCode.Infrastructure.AIAgents.AIContextProviders;

/// <summary>
/// 任务上下文
/// </summary>
public class TaskProvider : AIContextProvider
{
    /// <summary>
    /// 任务的提示词
    /// </summary>
    private const string Instructions =
        """
        你可以使用 Task 系列工具维护当前会话的结构化任务列表。任务列表用于跟踪复杂、多步骤、需要持续推进的工作，让用户能够理解当前进度；它是会话状态，不是新的用户请求。始终优先处理用户最新指令，只有在任务状态确实变化时才更新任务。

        ## 总体规则

        - 复杂任务才使用任务工具：当请求包含 3 个以上明确步骤、非平凡实现、计划模式、用户明确要求任务列表、或用户一次给出多个事项时，应创建并维护任务。
        - 简单任务不要创建任务：单一步骤、纯问答、信息解释、很快能完成的直接操作，不需要任务列表。
        - 创建任务前先检查现有任务，避免重复创建同一目标的任务。
        - 开始执行某个任务前，先用 TaskUpdate 将其标记为 in_progress；完成后再标记为 completed。
        - 只有完全完成任务时才能标记 completed；如果测试失败、实现不完整、存在未解决错误、缺少必要文件或依赖，不得标记 completed，应保持 in_progress 或创建阻塞任务。
        - 任务依赖必须显式维护：当前任务阻塞其它任务时使用 addBlocks；当前任务依赖其它任务时使用 addBlockedBy。
        - 删除任务是永久操作，仅当任务创建错误、已不相关或被明确取代时使用 deleted。
        - 任务列表来自运行时状态，仅作为上下文参考；不要仅因为存在 pending 或 in_progress 任务就忽略用户最新问题。

        ## TaskCreate

        用于在当前会话中创建新任务，适合拆分复杂工作、记录计划模式中的实施步骤、捕获用户新需求或补充后续事项。

        字段要求：
        - title/subject：简短、明确、可执行的任务标题，必须包含足够上下文，使其无需额外描述也能理解。
        - description：需要完成的具体内容和上下文；如果工具没有 description 参数，应将关键信息写入标题或 metadata。
        - activeForm：任务处于 in_progress 时展示的进行时文案，例如“Running tests”。
        - metadata：附加结构化信息。

        创建后的任务默认是 pending。创建多个任务后，应根据需要用 TaskUpdate 设置依赖关系。

        ## TaskGet

        用于按任务 ID 获取任务完整信息，包括标题、描述、状态、负责人和依赖关系。

        使用场景：
        - 开始处理某个任务前，需要读取最新任务状态和完整要求。
        - 需要确认 blockedBy 是否为空，判断任务是否可开始。
        - 需要查看当前任务阻塞了哪些后续任务。

        更新任务前应优先读取最新状态，避免基于过期信息修改任务。

        ## TaskList

        用于列出当前会话全部任务，查看整体进度、可执行任务、阻塞任务和依赖关系。

        使用场景：
        - 查找状态为 pending、未被阻塞、可开始的任务。
        - 检查项目整体进度。
        - 完成一个任务后，查看是否有新解锁的后续任务。
        - 多个可用任务同时存在时，优先处理较早创建或 ID 顺序更靠前的任务，因为前置任务通常为后续任务提供上下文。

        TaskList 只提供摘要；需要完整要求时使用 TaskGet。

        ## TaskUpdate

        用于更新任务状态、标题、描述、activeForm、owner、metadata，或维护 blocks/blockedBy 依赖关系。

        状态流转：
        - pending：尚未开始。
        - in_progress：正在执行；开始工作前必须设置。
        - completed：已经完整完成；只有验证通过或确认无需验证时才能设置。
        - deleted：永久删除任务。

        使用规则：
        - 开始任务：设置 status 为 in_progress，可同时设置 activeForm。
        - 完成任务：确认工作完整、错误已解决、必要验证已完成后设置 completed。
        - 遇到阻塞：不要标记 completed；保留 in_progress，并创建或关联描述阻塞事项的任务。
        - 更新 metadata：合并指定键；值为 null 表示删除该键。
        - 维护依赖：addBlocks 表示当前任务完成前目标任务不能开始；addBlockedBy 表示当前任务必须等待指定任务完成。
        - 不要为了展示进度而频繁无意义更新；只有状态、标题、依赖、负责人或元数据实际变化时才调用。

        ## TaskStop

        用于停止正在运行的后台任务。

        使用规则：
        - 只停止明确指定且仍在运行的任务。
        - 当任务长时间运行、用户要求终止、执行方向错误或继续运行会产生风险时使用。
        - 停止前确认 task_id 正确；不要停止无关任务。
        - 停止成功后，根据实际情况用 TaskUpdate 更新关联工作状态或创建后续处理任务。
        """;

    private readonly ProviderSessionState<TaskAgentTaskState> _sessionState;
    private AITool[]? _tools;

    public TaskProvider()
    {
        this._sessionState = new ProviderSessionState<TaskAgentTaskState>(
            _ => new TaskAgentTaskState(),
            this.GetType().Name);
    }

    protected override ValueTask<AIContext> ProvideAIContextAsync(InvokingContext context,
        CancellationToken cancellationToken = new CancellationToken())
    {
        var aiContext = new AIContext
        {
            Instructions = Instructions,
            Tools = this._tools ??= this.CreateTools(),
            Messages = [new ChatMessage(ChatRole.User, FormatTaskListMessage())]
        };
        return ValueTask.FromResult(aiContext);
    }

    /// <summary>
    /// 获取任务集合
    /// </summary>
    /// <returns></returns>
    private string FormatTaskListMessage()
    {
        var taskState = GetTaskState();
        //获取没有完成的任务
        var tasks = taskState.Items.Where(a=>a.Status is TaskAgentTaskStatus.Pending or TaskAgentTaskStatus.InProgress).ToList();
        if (tasks is not {Count:>0})
        {
            return "### Current Task List\n- none yet";
        }
        var sb = new StringBuilder("### Current Task List\n");

        foreach (var task in tasks)
        {
            sb.Append($"- {task.Id} [{task.Status.ToString()}] {task.Subject}");
            if (!string.IsNullOrWhiteSpace(task.Description))
            {
                sb.Append($": {task.Description}");
            }

            sb.AppendLine();
        }
        
        return sb.ToString();
    }


    private AITool[] CreateTools()
    {
        return
        [
            AIFunctionFactory.Create(TaskCreate, serializerOptions: AIContentJsonSerializerContext.Default.Options),
            AIFunctionFactory.Create(TaskGet, serializerOptions: AIContentJsonSerializerContext.Default.Options),
            AIFunctionFactory.Create(TaskList, serializerOptions: AIContentJsonSerializerContext.Default.Options),
            AIFunctionFactory.Create(TaskUpdate, serializerOptions: AIContentJsonSerializerContext.Default.Options),
            AIFunctionFactory.Create(TaskStop, serializerOptions: AIContentJsonSerializerContext.Default.Options)
        ];
    }

    /// <summary>
    /// 创建当前 Agent 会话使用的任务。
    /// </summary>
    /// <param name="subject">任务标题，要求简短且可执行。</param>
    /// <param name="description">任务描述，说明需要完成的具体工作。</param>
    /// <param name="activeForm">任务执行中展示的进行时文案。</param>
    /// <param name="metadata">任务附加元数据。</param>
    /// <returns>结构化 JSON 工具结果。</returns>
    [Description("创建一个任务，用于跟踪复杂多步骤工作的进度")]
    private string TaskCreate(
        [Description("任务标题，要求简短且可执行")] string subject,
        [Description("任务描述，说明需要完成的具体工作")] string description,
        [Description("任务执行中展示的进行时文案，例如 Running tests")]
        string? activeForm = null,
        [Description("任务附加元数据")] Dictionary<string, object?>? metadata = null)
    {
        if (string.IsNullOrWhiteSpace(subject))
        {
            return Serialize(TaskToolResult.Error("TaskCreate requires a non-empty subject."));
        }

        if (string.IsNullOrWhiteSpace(description))
        {
            return Serialize(TaskToolResult.Error("TaskCreate requires a non-empty description."));
        }

        var taskState = GetTaskState();
        var task = taskState.Create(subject.Trim(), description.Trim(), activeForm, metadata);
        return Serialize(new TaskCreateToolResult
        {
            Success = true,
            Task = TaskDetailedToolResult.FromTask(task),
            Message = $"Task #{task.Id} created successfully: {task.Subject}"
        });
    }

    /// <summary>
    /// 获取任务的状态信息
    /// </summary>
    /// <returns></returns>
    private TaskAgentTaskState GetTaskState()
    {
        //获取当前的会话上下文
        var currentSession = AIAgent.CurrentRunContext!.Session;

        var state = this._sessionState.GetOrInitializeState(currentSession);
        return state;
    }

    /// <summary>
    /// 根据任务 ID 获取任务详情。
    /// </summary>
    /// <param name="taskId">要查询的任务 ID。</param>
    /// <returns>结构化 JSON 工具结果。</returns>
    [Description("根据任务 ID 获取任务标题、描述、状态、所有者和依赖关系")]
    private string TaskGet([Description("要查询的任务 ID")] string taskId)
    {
        if (string.IsNullOrWhiteSpace(taskId))
        {
            return Serialize(TaskToolResult.Error("TaskGet requires a non-empty taskId."));
        }

        var taskState = GetTaskState();
        var task = taskState.Get(taskId.Trim());

        if (task is null)
        {
            return Serialize(TaskToolResult.Error($"Task \"{taskId}\" not found."));
        }

        return Serialize(new TaskGetToolResult
        {
            Success = true,
            Task = TaskDetailedToolResult.FromTask(task)
        });
    }

    /// <summary>
    /// 列出当前进程内所有 Agent 任务。
    /// </summary>
    /// <returns>结构化 JSON 工具结果。</returns>
    [Description("列出所有任务，包括任务 ID、标题、状态、所有者和未完成前置依赖")]
    private string TaskList()
    {
        var taskState = GetTaskState();
        var tasks = taskState.Items;
        var completedTaskIds = tasks
            .Where(task => task.Status == TaskAgentTaskStatus.Completed)
            .Select(task => task.Id)
            .ToHashSet(StringComparer.Ordinal);

        return Serialize(new TaskListToolResult
        {
            Success = true,
            Total = tasks.Count,
            Pending = tasks.Count(task => task.Status == TaskAgentTaskStatus.Pending),
            InProgress = tasks.Count(task => task.Status == TaskAgentTaskStatus.InProgress),
            Completed = tasks.Count(task => task.Status == TaskAgentTaskStatus.Completed),
            Stopped = tasks.Count(task => task.Status == TaskAgentTaskStatus.Stopped),
            Tasks = tasks.Select(task => TaskListItemToolResult.FromTask(task, completedTaskIds)).ToArray()
        });
    }

    /// <summary>
    /// 更新任务标题、描述、状态、所有者、输出、依赖或元数据。
    /// </summary>
    /// <param name="taskId">要更新的任务 ID。</param>
    /// <param name="subject">新的任务标题。</param>
    /// <param name="description">新的任务描述。</param>
    /// <param name="activeForm">新的执行中展示文案。</param>
    /// <param name="status">新的任务状态，支持 pending、in_progress、completed、stopped、deleted。</param>
    /// <param name="addBlocks">当前任务阻塞的任务 ID 集合。</param>
    /// <param name="addBlockedBy">阻塞当前任务的任务 ID 集合。</param>
    /// <param name="owner">新的任务所有者。</param>
    /// <param name="metadata">要合并的元数据，值为 null 时删除对应键。</param>
    /// <param name="output">任务输出内容，供 TaskOutput 读取。</param>
    /// <param name="error">任务错误信息，供 TaskOutput 读取。</param>
    /// <returns>结构化 JSON 工具结果。</returns>
    [Description("更新任务状态、标题、描述、所有者、依赖、元数据或输出；status=deleted 表示删除任务")]
    private string TaskUpdate(
        [Description("要更新的任务 ID")] string taskId,
        [Description("新的任务标题")] string? subject = null,
        [Description("新的任务描述")] string? description = null,
        [Description("新的执行中展示文案，例如 Running tests")]
        string? activeForm = null,
        [Description("新的任务状态，支持 pending、in_progress、completed、stopped、deleted")]
        string? status = null,
        [Description("当前任务阻塞的任务 ID 集合")] string[]? addBlocks = null,
        [Description("阻塞当前任务的任务 ID 集合")] string[]? addBlockedBy = null,
        [Description("新的任务所有者")] string? owner = null,
        [Description("要合并的元数据，值为 null 时删除对应键")]
        Dictionary<string, object?>? metadata = null,
        [Description("任务输出内容，供 TaskOutput 读取")]
        string? output = null,
        [Description("任务错误信息，供 TaskOutput 读取")]
        string? error = null)
    {
        if (string.IsNullOrWhiteSpace(taskId))
        {
            return Serialize(TaskToolResult.Error("TaskUpdate requires a non-empty taskId."));
        }

        var taskState = GetTaskState();
        var normalizedTaskId = taskId.Trim();
        var existingTask = taskState.Get(normalizedTaskId);
        if (existingTask is null)
        {
            return Serialize(TaskToolResult.Error("Task not found.", normalizedTaskId));
        }

        if (IsDeletedStatus(status))
        {
            var deleted = taskState.Delete(normalizedTaskId);
            return Serialize(new TaskUpdateToolResult
            {
                Success = deleted,
                TaskId = normalizedTaskId,
                UpdatedFields = deleted ? ["deleted"] : [],
                StatusChange = deleted
                    ? new TaskStatusChangeToolResult {From = existingTask.ToWireStatus(), To = "deleted"}
                    : null,
                ErrorMessage = deleted ? null : "Failed to delete task."
            });
        }

        if (!TryParseStatus(status, out var parsedStatus, out var statusError))
        {
            return Serialize(TaskToolResult.Error(statusError, normalizedTaskId));
        }

        var updatedFields = new List<string>();
        var updatedTask = taskState.Update(normalizedTaskId, task =>
        {
            ApplyBasicUpdates(
                task,
                subject,
                description,
                activeForm,
                owner,
                parsedStatus,
                metadata,
                output,
                error,
                updatedFields);
        });

        foreach (var blockedTaskId in NormalizeIds(addBlocks))
        {
            taskState.AddBlocks(normalizedTaskId, blockedTaskId);
            AddUpdatedField(updatedFields, "blocks");
        }

        foreach (var blockingTaskId in NormalizeIds(addBlockedBy))
        {
            taskState.AddBlockedBy(normalizedTaskId, blockingTaskId);
            AddUpdatedField(updatedFields, "blockedBy");
        }

        updatedTask = taskState.Get(normalizedTaskId) ?? updatedTask;
        return Serialize(new TaskUpdateToolResult
        {
            Success = true,
            TaskId = normalizedTaskId,
            UpdatedFields = updatedFields.ToArray(),
            StatusChange = parsedStatus is null
                ? null
                : new TaskStatusChangeToolResult
                    {From = existingTask.ToWireStatus(), To = ToWireStatus(parsedStatus.Value)},
            Task = updatedTask is null ? null : TaskDetailedToolResult.FromTask(updatedTask),
            Message = updatedFields.Count == 0
                ? $"Task #{normalizedTaskId} unchanged."
                : $"Updated task #{normalizedTaskId}: {string.Join(", ", updatedFields)}"
        });
    }


    /// <summary>
    /// 停止一个未完成的任务。
    /// </summary>
    /// <param name="taskId">要停止的任务 ID。</param>
    /// <returns>结构化 JSON 工具结果。</returns>
    [Description("停止一个 pending 或 in_progress 任务，并将其状态标记为 stopped")]
    private string TaskStop([Description("要停止的任务 ID")] string taskId)
    {
        if (string.IsNullOrWhiteSpace(taskId))
        {
            return Serialize(TaskToolResult.Error("TaskStop requires a non-empty taskId."));
        }

        var taskState = GetTaskState();
        var normalizedTaskId = taskId.Trim();
        var existingTask = taskState.Get(normalizedTaskId);
        if (existingTask is null)
        {
            return Serialize(TaskToolResult.Error($"Task \"{normalizedTaskId}\" not found."));
        }

        if (existingTask.Status == TaskAgentTaskStatus.Completed)
        {
            return Serialize(TaskToolResult.Error(
                $"Task {normalizedTaskId} is already completed and cannot be stopped.",
                normalizedTaskId));
        }

        var stoppedTask = taskState.Update(normalizedTaskId, task =>
        {
            task.Status = TaskAgentTaskStatus.Stopped;
            task.Output = AppendOutput(task.Output, "Task stopped by TaskStop.");
        });

        return Serialize(new TaskStopToolResult
        {
            Success = true,
            Message = $"Successfully stopped task: {normalizedTaskId}",
            TaskId = normalizedTaskId,
            Task = stoppedTask is null ? null : TaskDetailedToolResult.FromTask(stoppedTask)
        });
    }


    /// <summary>
    /// 获取所有任务快照。
    /// </summary>
    /// <returns>任务快照列表。</returns>
    public IReadOnlyList<TaskAgentTask> GetList(AgentSession agentSession)
    {
        var taskState = this._sessionState.GetOrInitializeState(agentSession);
        return taskState.Items;
    }

    /// <summary>
    /// 序列化工具结果。
    /// </summary>
    private static string Serialize<TValue>(TValue value)
    {
        return value switch
        {
            TaskCreateToolResult result => JsonSerializer.Serialize(result,
                AIContentJsonSerializerContext.Default.TaskCreateToolResult),
            TaskGetToolResult result => JsonSerializer.Serialize(result,
                AIContentJsonSerializerContext.Default.TaskGetToolResult),
            TaskListToolResult result => JsonSerializer.Serialize(result,
                AIContentJsonSerializerContext.Default.TaskListToolResult),
            TaskUpdateToolResult result => JsonSerializer.Serialize(result,
                AIContentJsonSerializerContext.Default.TaskUpdateToolResult),
            TaskStopToolResult result => JsonSerializer.Serialize(result,
                AIContentJsonSerializerContext.Default.TaskStopToolResult),
            TaskToolResult result => JsonSerializer.Serialize(result,
                AIContentJsonSerializerContext.Default.TaskToolResult),
            _ => throw new InvalidOperationException($"Unsupported task tool result type: {typeof(TValue).FullName}.")
        };
    }

    /// <summary>
    /// 追加任务输出内容。
    /// </summary>
    private static string AppendOutput(string? currentOutput, string appendedOutput)
    {
        return string.IsNullOrWhiteSpace(currentOutput)
            ? appendedOutput
            : $"{currentOutput.TrimEnd()}{Environment.NewLine}{appendedOutput}";
    }
    
    /// <summary>
    /// 判断状态字符串是否表示删除任务。
    /// </summary>
    private static bool IsDeletedStatus(string? status)
    {
        return string.Equals(NormalizeStatus(status), "deleted", StringComparison.Ordinal);
    }


    /// <summary>
    /// 规范化任务状态字符串。
    /// </summary>
    private static string? NormalizeStatus(string? status)
    {
        return string.IsNullOrWhiteSpace(status)
            ? null
            : status.Trim().ToLowerInvariant().Replace('-', '_').Replace(' ', '_');
    }

    /// <summary>
    /// 尝试解析任务状态。
    /// </summary>
    private static bool TryParseStatus(
        string? status,
        out TaskAgentTaskStatus? parsedStatus,
        out string? error)
    {
        parsedStatus = null;
        error = null;
        var normalized = NormalizeStatus(status);
        if (string.IsNullOrWhiteSpace(normalized))
        {
            return true;
        }

        parsedStatus = normalized switch
        {
            "pending" => TaskAgentTaskStatus.Pending,
            "in_progress" => TaskAgentTaskStatus.InProgress,
            "completed" => TaskAgentTaskStatus.Completed,
            "stopped" => TaskAgentTaskStatus.Stopped,
            _ => null
        };

        if (parsedStatus is not null)
        {
            return true;
        }

        error = "Unsupported task status. Use pending, in_progress, completed, stopped, or deleted.";
        return false;
    }

    // <summary>
    /// 应用任务基础字段更新。
    /// </summary>
    private static void ApplyBasicUpdates(
        TaskAgentTask task,
        string? subject,
        string? description,
        string? activeForm,
        string? owner,
        TaskAgentTaskStatus? status,
        IReadOnlyDictionary<string, object?>? metadata,
        string? output,
        string? error,
        List<string> updatedFields)
    {
        if (!string.IsNullOrWhiteSpace(subject) && task.Subject != subject.Trim())
        {
            task.Subject = subject.Trim();
            AddUpdatedField(updatedFields, "subject");
        }

        if (!string.IsNullOrWhiteSpace(description) && task.Description != description.Trim())
        {
            task.Description = description.Trim();
            AddUpdatedField(updatedFields, "description");
        }

        if (activeForm is not null && task.ActiveForm != NormalizeNullableText(activeForm))
        {
            task.ActiveForm = NormalizeNullableText(activeForm);
            AddUpdatedField(updatedFields, "activeForm");
        }

        if (owner is not null && task.Owner != NormalizeNullableText(owner))
        {
            task.Owner = NormalizeNullableText(owner);
            AddUpdatedField(updatedFields, "owner");
        }

        if (status is not null && task.Status != status.Value)
        {
            task.Status = status.Value;
            AddUpdatedField(updatedFields, "status");
        }

        if (metadata is not null)
        {
            MergeMetadata(task.Metadata, metadata);
            AddUpdatedField(updatedFields, "metadata");
        }

        if (output is not null && task.Output != output)
        {
            task.Output = output;
            AddUpdatedField(updatedFields, "output");
        }

        if (error is not null && task.Error != error)
        {
            task.Error = error;
            AddUpdatedField(updatedFields, "error");
        }
    }

    /// <summary>
    /// 规范化可空文本。
    /// </summary>
    private static string? NormalizeNullableText(string value)
    {
        return string.IsNullOrWhiteSpace(value) ? null : value.Trim();
    }

    /// <summary>
    /// 追加唯一更新字段。
    /// </summary>
    private static void AddUpdatedField(List<string> updatedFields, string field)
    {
        if (!updatedFields.Contains(field, StringComparer.Ordinal))
        {
            updatedFields.Add(field);
        }
    }

    /// <summary>
    /// 规范化任务 ID 集合。
    /// </summary>
    private static IEnumerable<string> NormalizeIds(IEnumerable<string>? ids)
    {
        return ids?
            .Where(id => !string.IsNullOrWhiteSpace(id))
            .Select(id => id.Trim())
            .Distinct(StringComparer.Ordinal) ?? [];
    }


    /// <summary>
    /// 将任务状态转换为工具结果使用的字符串。
    /// </summary>
    private static string ToWireStatus(TaskAgentTaskStatus status)
    {
        return status switch
        {
            TaskAgentTaskStatus.Pending => "pending",
            TaskAgentTaskStatus.InProgress => "in_progress",
            TaskAgentTaskStatus.Completed => "completed",
            TaskAgentTaskStatus.Stopped => "stopped",
            _ => "unknown"
        };
    }

    /// <summary>
    /// 合并元数据，值为 null 时删除对应键。
    /// </summary>
    private static void MergeMetadata(Dictionary<string, object?> target, IReadOnlyDictionary<string, object?> metadata)
    {
        foreach (var item in metadata)
        {
            if (item.Value is null)
            {
                target.Remove(item.Key);
                continue;
            }

            target[item.Key] = item.Value;
        }
    }
}