using System.Text.Json;

namespace NarutoCode.Infrastructure.AIAgents.SubAgents;

/// <summary>
/// 从 <c>subagents.json</c> 加载并按根工作目录查询子 Agent 定义。
/// </summary>
public sealed class SubAgentRegistry(string configurationPath)
{
    private IReadOnlyDictionary<string, IReadOnlyList<SubAgentDefinition>> _agentsByRootWorkspace =
        new Dictionary<string, IReadOnlyList<SubAgentDefinition>>(StringComparer.Ordinal);

    /// <summary>
    /// 当前已加载并校验的委派限制。
    /// </summary>
    public DelegationLimits Limits { get; private set; } = DelegationLimits.Default;

    /// <summary>
    /// 加载配置文件，并在启动阶段校验目录绑定与子 Agent 标识。
    /// </summary>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>异步加载任务。</returns>
    public async Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        if (!File.Exists(configurationPath))
        {
            _agentsByRootWorkspace = new Dictionary<string, IReadOnlyList<SubAgentDefinition>>(StringComparer.Ordinal);
            Limits = DelegationLimits.Default;
            return;
        }

        await using var stream = new FileStream(
            configurationPath,
            FileMode.Open,
            FileAccess.Read,
            FileShare.Read,
            bufferSize: 4096,
            FileOptions.Asynchronous | FileOptions.SequentialScan);
        var configuration = await JsonSerializer.DeserializeAsync(
            stream,
            SubAgentsConfigurationJsonContext.Default.SubAgentsConfiguration,
            cancellationToken);
        if (configuration is null)
        {
            throw new InvalidOperationException("子 Agent 配置文件无效，请检查 JSON 格式。");
        }

        Limits = CreateLimits(configuration.Delegation ?? new DelegationConfiguration());
        _agentsByRootWorkspace = CreateAgentIndex(configuration.Workspaces ?? []);
    }

    /// <summary>
    /// 获取指定根工作目录可见的子 Agent。
    /// </summary>
    /// <param name="rootWorkspace">当前根 Agent 的工作目录。</param>
    /// <returns>当前根目录可调用的子 Agent；未配置时为空集合。</returns>
    public IReadOnlyList<SubAgentDefinition> GetAvailableAgents(string rootWorkspace)
    {
        return _agentsByRootWorkspace.TryGetValue(NormalizeWorkspace(rootWorkspace), out var agents)
            ? agents
            : [];
    }

    /// <summary>
    /// 将工作目录转换为用于配置和锁的稳定比较键。
    /// </summary>
    /// <param name="workspace">待规范化的工作目录。</param>
    /// <returns>不含末尾目录分隔符的绝对路径。</returns>
    public static string NormalizeWorkspace(string workspace)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(workspace);
        return Path.TrimEndingDirectorySeparator(Path.GetFullPath(workspace));
    }

    private static DelegationLimits CreateLimits(DelegationConfiguration configuration)
    {
        ValidatePositive(configuration.AgentExecutionTimeoutSeconds,
            nameof(configuration.AgentExecutionTimeoutSeconds));

        return new DelegationLimits(
            configuration.AgentExecutionTimeoutSeconds);
    }

    private static IReadOnlyDictionary<string, IReadOnlyList<SubAgentDefinition>> CreateAgentIndex(
        IReadOnlyList<WorkspaceSubAgentsConfiguration> workspaceConfigurations)
    {
        var index = new Dictionary<string, IReadOnlyList<SubAgentDefinition>>(StringComparer.Ordinal);
        foreach (var workspaceConfiguration in workspaceConfigurations)
        {
            var rootWorkspace = NormalizeWorkspace(RequireValue(
                workspaceConfiguration.Workspace,
                "workspaces[].workspace"));
            if (index.ContainsKey(rootWorkspace))
            {
                throw new InvalidOperationException($"子 Agent 配置中的根工作目录重复：{rootWorkspace}");
            }

            var agentIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var agents = new List<SubAgentDefinition>(workspaceConfiguration.SubAgents.Count);
            foreach (var agentConfiguration in workspaceConfiguration.SubAgents)
            {
                var agentId = RequireValue(agentConfiguration.Id, "subAgents[].id");
                if (!agentIds.Add(agentId))
                {
                    throw new InvalidOperationException(
                        $"根工作目录 {rootWorkspace} 中的子 Agent 标识重复：{agentId}");
                }

                agents.Add(new SubAgentDefinition(
                    agentId,
                    RequireValue(agentConfiguration.Name, "subAgents[].name"),
                    RequireValue(agentConfiguration.Description, "subAgents[].description"),
                    rootWorkspace,
                    NormalizeWorkspace(RequireValue(agentConfiguration.Workspace, "subAgents[].workspace"))));
            }

            index.Add(rootWorkspace, agents);
        }

        return index;
    }

    private static string RequireValue(string value, string fieldName)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new InvalidOperationException($"子 Agent 配置缺少 {fieldName}。");
        }

        return value.Trim();
    }

    private static void ValidatePositive(int value, string propertyName)
    {
        if (value <= 0)
        {
            throw new InvalidOperationException($"子 Agent 配置 {propertyName} 必须大于 0。");
        }
    }
}