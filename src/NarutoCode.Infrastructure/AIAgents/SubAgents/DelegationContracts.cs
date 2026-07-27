namespace NarutoCode.Infrastructure.AIAgents.SubAgents;

/// <summary>
/// 子 Agent 任务调度方式。
/// </summary>
internal enum DelegationExecutionMode
{
    Sequential,
    Parallel
}

/// <summary>
/// 委派多个子 Agent 的工具请求。
/// </summary>
internal sealed class DelegateAgentsRequest
{
    /// <summary>
    /// 子任务执行方式。
    /// </summary>
    public DelegationExecutionMode Mode { get; set; } = DelegationExecutionMode.Sequential;

    /// <summary>
    /// 待执行的子任务。
    /// </summary>
    public List<DelegateAgentTaskRequest> Tasks { get; set; } = [];
}

/// <summary>
/// 单个子 Agent 任务请求。
/// </summary>
internal sealed class DelegateAgentTaskRequest
{
    /// <summary>
    /// 当前根目录可见的子 Agent 标识。
    /// </summary>
    public string AgentId { get; set; } = string.Empty;

    /// <summary>
    /// 交给子 Agent 的完整任务说明。
    /// </summary>
    public string Prompt { get; set; } = string.Empty;
}

/// <summary>
/// 单个子 Agent 的执行结果。
/// </summary>
internal sealed record DelegateAgentTaskResult(
    string AgentId,
    string AgentName,
    bool Succeeded,
    string Output);