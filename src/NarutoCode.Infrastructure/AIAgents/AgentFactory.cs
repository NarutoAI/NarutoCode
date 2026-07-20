using System.Collections.Concurrent;
using System.Runtime.InteropServices;
using Microsoft.Agents.AI;
using Microsoft.Agents.AI.Tools.Shell;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;
using NarutoCode.Domain;
using NarutoCode.Domain.Messages;
using NarutoCode.Domain.Workspaces;
using NarutoCode.Infrastructure.AIAgents.AIContextProviders;
using NarutoCode.Infrastructure.AIAgents.ChatHistorys;
using NarutoCode.Infrastructure.AIAgents.CompactionStrategys;
using NarutoCode.Infrastructure.AIAgents.DelegatingChatClients;
using NarutoCode.Infrastructure.AIAgents.LoopEvaluators;
using NarutoCode.Infrastructure.AIAgents.Mcp;
using NarutoCode.Infrastructure.AIAgents.Skills;

namespace NarutoCode.Infrastructure.AIAgents;

/// <summary>
/// Agent 工厂，按规范化工作目录维护会话级 Agent、Session 与持久 Shell 运行时。
/// </summary>
public sealed class AgentFactory(
    IWorkspaceContextAccessor workspaceContextAccessor,
    IChatHistoryPersistenceHandler chatHistoryPersistenceHandler,
    ILoggerFactory loggerFactory,
    CompactionStrategyCoordinator compactionStrategyCoordinator,
    DynamicChatClient dynamicChatClient,
    McpClientManager mcpClientManager) : IAgentFactory, IAsyncDisposable
{
    private static readonly TimeSpan IdlePoolTimeout = TimeSpan.FromMinutes(30);

    private readonly ConcurrentDictionary<string, WorkspaceAgentPool> _workspacePools = new(StringComparer.Ordinal);
    private readonly ILogger<AgentFactory> _logger = loggerFactory.CreateLogger<AgentFactory>();
    private int _disposed;
    

    /// <inheritdoc />
    public async ValueTask<IConversationAgentLease> AcquireCurrentConversationAsync(
        ConversationSessionId sessionId,
        CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        await EvictIdlePoolsAsync(cancellationToken);

        var workingDirectory = WorkspacePath.Normalize(workspaceContextAccessor.Current.WorkingDirectory);
        while (true)
        {
            var pool = _workspacePools.GetOrAdd(workingDirectory, CreateWorkspacePool);
            try
            {
                return await pool.AcquireAsync(sessionId, cancellationToken);
            }
            catch (WorkspaceAgentPoolEvictedException)
            {
                // 清理器与新租约竞争时，已回收池不能再复用，重新获取当前目录的新池。
                Log.WorkspaceAgentPoolReacquiring(_logger, workingDirectory);
                _workspacePools.TryRemove(new KeyValuePair<string, WorkspaceAgentPool>(workingDirectory, pool));
            }
        }
    }

    /// <inheritdoc />
    public void ResetCurrentConversation(ConversationSessionId sessionId)
    {
        if (Volatile.Read(ref _disposed) != 0)
        {
            return;
        }

        var workingDirectory = WorkspacePath.Normalize(workspaceContextAccessor.Current.WorkingDirectory);
        if (_workspacePools.TryGetValue(workingDirectory, out var pool))
        {
            pool.InvalidateConversation(sessionId);
            return;
        }

        Log.WorkspaceAgentPoolNotFoundForReset(_logger, workingDirectory, sessionId.Value);
    }

    /// <inheritdoc />
    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
        {
            return;
        }

        Log.AgentFactoryDisposing(_logger, _workspacePools.Count);

        foreach (var pool in _workspacePools.Values)
        {
            await pool.DisposeAsync();
        }

        _workspacePools.Clear();
        
        Log.AgentFactoryDisposed(_logger);
    }

    private WorkspaceAgentPool CreateWorkspacePool(string workingDirectory)
    {
        Log.WorkspaceAgentPoolCreated(_logger, workingDirectory);
        return new WorkspaceAgentPool(
            workingDirectory,
            () => CreateConversationRuntime(workingDirectory),
            loggerFactory.CreateLogger<WorkspaceAgentPool>());
    }

    private ConversationAgentRuntime CreateConversationRuntime(string workingDirectory)
    {
        var persistentShell = ShellExecutorFactory.Create();
        try
        {
            var runtime = new ConversationAgentRuntime(CreateAgent(workingDirectory, persistentShell), persistentShell);
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

#pragma warning disable MAAI001
    private AIAgent CreateAgent(string workingDirectory, LocalShellExecutor persistentShell)
    {
        var workspaceContext = new WorkspaceContext(workingDirectory);
        var fixedWorkspaceAccessor = new FixedWorkspaceContextAccessor(workspaceContext);
        var skillsProvider = new AgentSkillsProvider(
            [ProjectConstant.SkillsDirectory],
            scriptRunner: SkillSubprocessScriptRunner.RunAsync,
            loggerFactory: loggerFactory);

        var persistenceChatHistoryProvider = new PersistenceChatHistoryProvider(
            chatHistoryPersistenceHandler,
            compactionStrategyCoordinator);
        var fileStore = new FileSystemAgentFileStore(workingDirectory);
        var fileAccessProvider = new FileAccessProvider(
            fileStore,
            new FileAccessProviderOptions
            {
                Instructions =
                    """
                    ## 文件访问
                    您可以通过 `file_access_*` 工具访问当前工作目录中的文件。
                    - 除非用户明确要求，否则切勿删除或覆盖现有文件。
                    - `fileName` 或 `directory` 参数必须使用相对工作目录的路径。

                    ## 使用 `edit_file` 工具规则
                    - 如果 `old_string` 在文件中不唯一，则编辑会失败。请提供更长的上下文，或使用 `replace_all`。
                    """
            });
        var memoryPath = Path.Combine(workingDirectory, ProjectConstant.ConfigurationDirectory, "memory");
        var agentMd = ReadAgentsMd(workingDirectory);

        return dynamicChatClient.AsHarnessAgent(new HarnessAgentOptions
        {
            AgentModeProviderOptions = new AgentModeProviderOptions
            {
                Instructions =
                    """
                    ## Agent Mode
                    - 每次用户输入后使用 mode_get 检查当前模式。
                    - 用户明确指示或允许时才可使用 mode_set。
                    - 需求不明确、设计不清晰或存在多种有效方案时，主动进入 plan 模式并沟通确认。
                    - 您当前正在运行 {current_mode} 模式。

                    {available_modes}
                    """,
                Modes = null,
                DefaultMode = "execute"
            },
            HarnessInstructions =
                $"""
                你是一位强大的软件架构师和产品专家。

                ## 沟通准则
                - 行动前理解意图、定位代码、规划最小改动并验证。
                - 保持简洁直接，仅在必要时澄清。
                - 修改已有文件前先阅读，遵守项目现有命名、格式和模式。
                - 完成后简要说明结果和验证状态。

                ## 工作目录地址
                - {workingDirectory}

                ## 其它信息
                - 当前操作系统：`{RuntimeInformation.OSDescription}`
                - 除非用户明确要求，否则必须使用中文回复。

                ## 安全红线
                - 未获当前对话明确授权时，不得修改系统目录、全局配置目录、凭据目录或其它敏感路径。
                - 仅在当前工作目录或用户明确指定的项目目录中进行文件操作。

                {agentMd}
                """,
            Name = "NarutoCode",
            DisableFileMemory = true,
            ChatHistoryProvider = persistenceChatHistoryProvider,
            ChatOptions = new ChatOptions
            {
                Reasoning = new() { Output = ReasoningOutput.Summary }
            },
            ShellExecutor = persistentShell,
            DisableAgentSkillsProvider = true,
            AIContextProviders =
            [
                skillsProvider,
                ToolContinuationSkippingAiContextProvider.Wrap(new TaskProvider()),
                new CodeReviewAIContextProvider(dynamicChatClient, [fileAccessProvider]),
                new FSTollsAiContextProvider(fixedWorkspaceAccessor),
                fileAccessProvider,
                new SvgRenderProvider(workingDirectory),
                ToolContinuationSkippingAiContextProvider.Wrap(new FileMemoryProvider(
                    new FileSystemAgentFileStore(memoryPath),
                    _ => new FileMemoryState { WorkingFolder = string.Empty },
                    new FileMemoryProviderOptions
                    {
                        Instructions =
                            """
                            ## 基于文件的内存
                            - file_memory_* 仅用于当前会话的工作内存，与其他会话隔离。
                            - 开始新任务前使用 list 和 search 检查已有相关记忆。
                            - 用户明确偏好、约束或纠正必须以简洁中文要点保存。
                            """
                    })),
                ToolContinuationSkippingAiContextProvider.Wrap(new TodoProvider()),
                new McpToolsAIContextProvider(mcpClientManager),
                new CollectApprovalToolAiContextProvider()
            ],
            DisableTodoProvider = true,
            DisableFileAccess = true,
            DisableCompaction = true,
            ToolApprovalAgentOptions = new ToolApprovalAgentOptions
            {
                AutoApprovalRules = [ToolApprovalAgent.AllToolsAutoApprovalRule]
            },
            LoopEvaluators =
            [
                new TodoCompletionLoopEvaluator(new TodoCompletionLoopEvaluatorOptions { Modes = ["execute"] }),
                new TaskLoopEvaluator()
            ]
        }, loggerFactory);
    }
#pragma warning restore MAAI001

    private async ValueTask EvictIdlePoolsAsync(CancellationToken cancellationToken)
    {
        var now = DateTime.UtcNow;
        foreach (var pair in _workspacePools)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!pair.Value.CanEvict(now, IdlePoolTimeout) ||
                !_workspacePools.TryRemove(new KeyValuePair<string, WorkspaceAgentPool>(pair.Key, pair.Value)))
            {
                continue;
            }

            Log.WorkspaceAgentPoolEvicting(_logger, pair.Key);
            await pair.Value.DisposeAsync();
        }
    }

    private void ThrowIfDisposed()
    {
        ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposed) != 0, this);
    }

    private static string ReadAgentsMd(string workingDirectory)
    {
        var agentPath = Path.Combine(workingDirectory, "AGENTS.md");
        return File.Exists(agentPath)
            ? $"## 项目信息\n{File.ReadAllText(agentPath)}"
            : string.Empty;
    }

    /// <summary>
    /// 将会话 Runtime 绑定到固定工作目录，避免缓存 Agent 在 AsyncLocal scope 切换后读错目录。
    /// </summary>
    private sealed class FixedWorkspaceContextAccessor(WorkspaceContext workspaceContext) : IWorkspaceContextAccessor
    {
        /// <inheritdoc />
        public WorkspaceContext Current { get; } = workspaceContext;
    }
}
