using System.Text.Json;
using System.Text.Json.Serialization;

namespace NarutoCode.Infrastructure.AIAgents.SubAgents;

/// <summary>
/// 子 Agent 编排配置根节点。
/// </summary>
internal sealed class SubAgentsConfiguration
{
    /// <summary>
    /// 委派调用的全局限制。
    /// </summary>
    public DelegationConfiguration Delegation { get; set; } = new();

    /// <summary>
    /// 按根工作目录绑定的子 Agent 集合。
    /// </summary>
    public List<WorkspaceSubAgentsConfiguration> Workspaces { get; set; } = [];
}

/// <summary>
/// 子 Agent 委派限制的可配置 JSON 模型。
/// </summary>
internal sealed class DelegationConfiguration
{

    /// <summary>
    /// 单个子 Agent 任务允许执行的最长秒数。
    /// </summary>
    public int AgentExecutionTimeoutSeconds { get; set; } = 600;
}

/// <summary>
/// 一个根工作目录及其可调用子 Agent 配置。
/// </summary>
internal sealed class WorkspaceSubAgentsConfiguration
{
    /// <summary>
    /// 根 Agent 可见子 Agent 的工作目录。
    /// </summary>
    public string Workspace { get; set; } = string.Empty;

    /// <summary>
    /// 该根工作目录可调用的子 Agent。
    /// </summary>
    public List<SubAgentConfigurationItem> SubAgents { get; set; } = [];
}

/// <summary>
/// 单个子 Agent 的 JSON 配置。
/// </summary>
internal sealed class SubAgentConfigurationItem
{
    /// <summary>
    /// 在当前根工作目录内唯一的子 Agent 标识。
    /// </summary>
    public string Id { get; set; } = string.Empty;

    /// <summary>
    /// 面向模型和日志展示的子 Agent 名称。
    /// </summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// 子 Agent 的职责说明。
    /// </summary>
    public string Description { get; set; } = string.Empty;

    /// <summary>
    /// 子 Agent 实际执行任务的目标工作目录。
    /// </summary>
    public string Workspace { get; set; } = string.Empty;
}

/// <summary>
/// 经校验和路径规范化后的子 Agent 定义。
/// </summary>
/// <param name="Id">当前根工作目录内唯一的子 Agent 标识。</param>
/// <param name="Name">面向模型和日志展示的名称。</param>
/// <param name="Description">职责说明。</param>
/// <param name="RootWorkspace">决定可见性的根工作目录。</param>
/// <param name="Workspace">实际执行任务的目标工作目录。</param>
public sealed record SubAgentDefinition(
    string Id,
    string Name,
    string Description,
    string RootWorkspace,
    string Workspace);

/// <summary>
/// 经校验后的委派运行限制。
/// </summary>
/// <param name="AgentExecutionTimeoutSeconds">单子任务最大执行秒数。</param>
public sealed record DelegationLimits(
    int AgentExecutionTimeoutSeconds)
{
    /// <summary>
    /// 缺失配置文件时使用的默认限制。
    /// </summary>
    public static DelegationLimits Default { get; } = new(600);
}

/// <summary>
/// 子 Agent 编排配置的源生成 JSON 序列化上下文。
/// </summary>
[JsonSourceGenerationOptions(
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    PropertyNameCaseInsensitive = true,
    AllowTrailingCommas = true,
    ReadCommentHandling = JsonCommentHandling.Skip)]
[JsonSerializable(typeof(SubAgentsConfiguration))]
internal sealed partial class SubAgentsConfigurationJsonContext : JsonSerializerContext;