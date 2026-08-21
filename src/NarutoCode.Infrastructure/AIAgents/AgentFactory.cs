using Microsoft.Agents.AI;
using Microsoft.Agents.AI.Tools.Shell;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;
using NarutoCode.Domain.Messages;
using NarutoCode.Domain.Workspaces;
using NarutoCode.Infrastructure.AIAgents.Composition;
using NarutoCode.Infrastructure.AIAgents.DelegatingChatClients;

namespace NarutoCode.Infrastructure.AIAgents;

/// <summary>
/// Agent 工厂：按规范化工作目录与会话缓存 Agent Runtime，编排要素由 <see cref="AgentComposer"/> 动态装配。
/// </summary>
public sealed class AgentFactory : IAgentFactory, IAsyncDisposable
{
    private readonly IWorkspaceContextAccessor _workspaceContextAccessor;
    private readonly DynamicChatClient _dynamicChatClient;
    private readonly AgentComposer _agentComposer;
    private readonly ConversationRuntimeCache _runtimeCache;
    private readonly ILoggerFactory _loggerFactory;
    private readonly ILogger<AgentFactory> _logger;
    private int _disposed;

    /// <summary>
    /// 创建 Agent 工厂。
    /// </summary>
    public AgentFactory(
        IWorkspaceContextAccessor workspaceContextAccessor,
        DynamicChatClient dynamicChatClient,
        AgentComposer agentComposer,
        ConversationRuntimeCache runtimeCache,
        ILoggerFactory loggerFactory)
    {
        _workspaceContextAccessor = workspaceContextAccessor;
        _dynamicChatClient = dynamicChatClient;
        _agentComposer = agentComposer;
        _runtimeCache = runtimeCache;
        _loggerFactory = loggerFactory;
        _logger = loggerFactory.CreateLogger<AgentFactory>();
    }

    /// <inheritdoc />
    public ValueTask<IConversationAgentLease> AcquireCurrentConversationAsync(
        ConversationSessionId sessionId,
        CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        var workingDirectory = WorkspacePath.Normalize(_workspaceContextAccessor.Current.WorkingDirectory);
        return _runtimeCache.AcquireAsync(
            workingDirectory,
            sessionId,
            () => CreateConversationRuntime(workingDirectory),
            cancellationToken);
    }

    /// <inheritdoc />
    public void ResetCurrentConversation(ConversationSessionId sessionId)
    {
        if (Volatile.Read(ref _disposed) != 0)
        {
            return;
        }

        var workingDirectory = WorkspacePath.Normalize(_workspaceContextAccessor.Current.WorkingDirectory);
        _runtimeCache.Invalidate(workingDirectory, sessionId);
    }

    /// <inheritdoc />
    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
        {
            return;
        }

        // 缓存释放时统一驱逐全部条目并释放持久 Shell
        Log.AgentFactoryDisposing(_logger, _runtimeCache.RuntimeCount);
        await _runtimeCache.DisposeAsync();
        Log.AgentFactoryDisposed(_logger);
    }

    /// <summary>
    /// 创建会话 Runtime：持久 Shell 与会话级 Agent 绑定，创建失败时回收 Shell。
    /// </summary>
    private ConversationAgentRuntime CreateConversationRuntime(string workingDirectory)
    {
        var persistentShell = ShellExecutorFactory.Create(workingDirectory);
        try
        {
            var runtime = new ConversationAgentRuntime(
                CreateAgent(workingDirectory, persistentShell, AgentProfile.Session),
                persistentShell);
            Log.ConversationAgentRuntimeCreated(_logger, workingDirectory);
            return runtime;
        }
        catch (Exception exception)
        {
            Log.ConversationAgentRuntimeCreationFailed(_logger, exception, workingDirectory);
            persistentShell.DisposeAsync().AsTask().GetAwaiter().GetResult();
            throw;
        }
    }

    /// <summary>
    /// 按身份档案装配 Agent：全部编排要素（指令、Provider、评估器、工具、历史）来自贡献者装配结果。
    /// </summary>
#pragma warning disable MAAI001
    private AIAgent CreateAgent(string workingDirectory, ShellExecutor persistentShell, AgentProfile profile)
    {
        // 装配上下文携带子 Agent 递归工厂：子 Agent 复用同一装配管道（SubAgent 档案）
        var composition = _agentComposer.Compose(new AgentCompositionContext(
            workingDirectory,
            persistentShell,
            profile,
            (dir, shell) => CreateAgent(dir, shell, AgentProfile.SubAgent)));

        return _dynamicChatClient.AsHarnessAgent(new HarnessAgentOptions
        {
            AgentModeProviderOptions = composition.AgentModeProviderOptions ?? new AgentModeProviderOptions(),
            HarnessInstructions = composition.Instructions,
            Name = "NarutoCode",
            DisableFileMemory = true,
            ChatHistoryProvider = composition.ChatHistoryProvider ?? new InMemoryChatHistoryProvider(),
            ChatOptions = new ChatOptions
            {
                Reasoning = new() { Output = ReasoningOutput.Summary },
                Tools = [.. composition.Tools]
            },
            DisableAgentSkillsProvider = true,
            AIContextProviders = [.. composition.AIContextProviders],
            DisableTodoProvider = true,
            DisableCompaction = true,
            ToolApprovalAgentOptions = new ToolApprovalAgentOptions
            {
                AutoApprovalRules = [ToolApprovalAgent.AllToolsAutoApprovalRule]
            },
            LoopEvaluators = [.. composition.LoopEvaluators]
        }, _loggerFactory);
    }
#pragma warning restore MAAI001

    private void ThrowIfDisposed()
    {
        ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposed) != 0, this);
    }
}
