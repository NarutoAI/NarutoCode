using System.ComponentModel;
using System.Text;
using Microsoft.Agents.AI;
using Microsoft.Agents.AI.Tools.Shell;
using Microsoft.Extensions.AI;
using NarutoCode.Infrastructure.AIAgents.SubAgents;
using NarutoCode.Infrastructure.JsonSerializerContexts;

namespace NarutoCode.Infrastructure.AIAgents.AIContextProviders;

/// <summary>
/// 为当前固定工作目录提供子 Agent 委派能力。
/// </summary>
internal sealed class SubAgentAiContextProvider : AIContextProvider
{
    private readonly AIContext _context;

    /// <summary>
    /// 创建绑定指定根工作目录的编排 Provider，构造时一次性构建上下文。
    /// </summary>
    /// <param name="rootWorkspace">当前根工作目录。</param>
    /// <param name="registry">子 Agent 注册表。</param>
    /// <param name="shellFactory">会话 Shell 工厂：子 Agent 临时 Shell 纳入会话跟踪，随会话兜底回收。</param>
    /// <param name="createAgent">按工作目录创建不持久化历史的子 Agent 的工厂委托。</param>
    public SubAgentAiContextProvider(
        string rootWorkspace,
        SubAgentRegistry registry,
        IShellExecutorFactory shellFactory,
        Func<string, ShellExecutor, AIAgent> createAgent)
    {
        _context = BuildContext(rootWorkspace, registry, shellFactory, createAgent);
    }

    /// <inheritdoc />
    protected override ValueTask<AIContext> ProvideAIContextAsync(InvokingContext context,
        CancellationToken cancellationToken = default)
    {
        return ValueTask.FromResult(_context);
    }

    /// <summary>
    /// 在构造时按当前根工作目录的可见子 Agent 构建不可变的 AI 上下文。
    /// </summary>
    private static AIContext BuildContext(
        string rootWorkspace, SubAgentRegistry registry, IShellExecutorFactory shellFactory,
        Func<string, ShellExecutor, AIAgent> createAgent)
    {
        var agents = registry.GetAvailableAgents(rootWorkspace);
        if (agents.Count == 0) return new AIContext();

        var catalog = string.Join(Environment.NewLine, agents.Select(x =>
            $"""
               - **{x.Id}**
                 - 名称：{x.Name}
                 - 职责：{x.Description}
                 - 目标工作目录：{x.Workspace}
             """));

        var instructions =
            $"""
             # 子 Agent 委派

             你当前工作目录下配置了 **{agents.Count} 个子 Agent**，它们各自在独立的工作目录中执行，拥有独立的文件系统、Shell 和会话上下文。

             ## 可用子 Agent

             {catalog}

             ## 何时委派

             - 任务需要操作**另一个工作目录**中的代码或文件时，委派给对应子 Agent。
             - 任务**超出当前工作目录范围**，且该子 Agent 的职责描述明确匹配时，委派。
             - 任务简单、仅涉及当前工作目录、或你自己能更快完成时，**不要委派**。

             ## 如何委派

             使用 `delegate_agents` 工具，参数说明：

             | 字段 | 说明 |
             |---|---|
             | `mode` | `sequential`（按顺序执行）或 `parallel`（同时执行） |
             | `tasks[].agentId` | 上方列表中的子 Agent **id**，必须精确匹配 |
             | `tasks[].prompt` | 交给子 Agent 的**完整、独立的任务说明**，不要预设依赖前一个子 Agent 的输出 |

             ### 调度规则

             - **parallel**：仅用于互不依赖的独立任务。同一并行请求中**不允许重复 agentId**。
             - **sequential**：用于有先后顺序的任务。如果后续任务依赖前一个子 Agent 的真实输出，应**先完成第一轮调用，读取结果后再发起下一轮**。

             ## 委派后

             子 Agent 返回的是**内部执行结果**，不是面向最终用户的回复。你必须：
             1. 校验结果是否满足原始需求。
             2. 汇总多个子 Agent 的结果。
             3. 以你的角色向最终用户回复，不要直接透传子 Agent 的原始输出。
             """;
        //构建工具
        var tool = AIFunctionFactory.Create((Func<DelegateAgentsRequest, CancellationToken, Task<string>>) Handler,
            name: "delegate_agents",
            serializerOptions: AIContentJsonSerializerContext.Default.DelegateAgentsRequest.Options);

        return new AIContext {Instructions = instructions, Tools = [tool]};

        // 创建绑定当前根工作目录的工具委托
        Task<string> Handler(DelegateAgentsRequest request, CancellationToken cancellationToken) =>
            DelegateAgents(rootWorkspace, registry, shellFactory, createAgent, request, cancellationToken);
    }

    /// <summary>
    /// 校验请求、调度串行或并行子任务并汇总结果。
    /// </summary>
    [Description("将明确子任务委派给当前工作目录允许使用的子 Agent。")]
    private static async Task<string> DelegateAgents(
        string rootWorkspace,
        SubAgentRegistry registry,
        IShellExecutorFactory shellFactory,
        Func<string, ShellExecutor, AIAgent> createAgent,
        DelegateAgentsRequest request,
        CancellationToken cancellationToken)
    {
        // 空请求直接拒绝
        if (request.Tasks.Count == 0) return "子 Agent 任务不能为空。";

        // 并行模式下不允许重复 agentId
        if (request.Mode == DelegationExecutionMode.Parallel && request.Tasks
                .GroupBy(x => x.AgentId, StringComparer.OrdinalIgnoreCase)
                .Any(x => x.Count() > 1))
            return "并行子 Agent 请求中不允许重复 agentId。";

        // 解析当前根目录可见的子 Agent
        var available = registry.GetAvailableAgents(rootWorkspace)
            .ToDictionary(x => x.Id, StringComparer.OrdinalIgnoreCase);

        // 构造任务列表：未知 agentId 直接返回失败结果
        var tasks = request.Tasks.Select(x => available.TryGetValue(x.AgentId, out var agent)
            ? ExecuteOneAsync(agent, x.Prompt, registry, shellFactory, createAgent, cancellationToken)
            : Task.FromResult(new DelegateAgentTaskResult(x.AgentId, x.AgentId, false,
                "当前根工作目录中不可用的子 Agent。"))).ToArray();

        // 按模式调度
        var results = request.Mode == DelegationExecutionMode.Parallel
            ? await Task.WhenAll(tasks)
            : await ExecuteSequentiallyAsync(tasks);

        // 汇总为 Markdown
        var builder = new StringBuilder();
        foreach (var result in results)
            builder.AppendLine($"## {result.AgentName} ({result.AgentId})")
                .AppendLine(result.Succeeded ? result.Output : $"失败：{result.Output}");
        return builder.ToString();
    }

    /// <summary>
    /// 串行执行所有任务，保持输入顺序。
    /// </summary>
    private static async Task<DelegateAgentTaskResult[]> ExecuteSequentiallyAsync(Task<DelegateAgentTaskResult>[] tasks)
    {
        var results = new DelegateAgentTaskResult[tasks.Length];
        for (var i = 0; i < tasks.Length; i++) results[i] = await tasks[i];
        return results;
    }

    /// <summary>
    /// 执行单个子任务：创建受限 Shell 和临时 Agent、超时控制、流式收集输出。
    /// </summary>
    private static async Task<DelegateAgentTaskResult> ExecuteOneAsync(
        SubAgentDefinition definition, string prompt,
        SubAgentRegistry registry, IShellExecutorFactory shellFactory,
        Func<string, ShellExecutor, AIAgent> createAgent,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(prompt))
            return new(definition.Id, definition.Name, false, "子 Agent 任务说明不能为空。");

        try
        {
            // 创建超时令牌，不影响调用方的取消令牌
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(TimeSpan.FromSeconds(registry.Limits.AgentExecutionTimeoutSeconds));

            // 创建受限 Shell 并构造临时子 Agent（不持久化历史）；任务结束（含超时/异常）归还工厂并同步移除跟踪引用
            var shell = shellFactory.Create(definition.Workspace);
            try
            {
                var agent = createAgent(definition.Workspace, shell);
                var session = await agent.CreateSessionAsync(timeout.Token);

                // 流式收集子 Agent 输出
                var output = new StringBuilder();
                await foreach (var update in agent.RunStreamingAsync(
                                   new ChatMessage(ChatRole.User, prompt), session, cancellationToken: timeout.Token))
                {
                    if (!string.IsNullOrEmpty(update.Text)) output.Append(update.Text);
                }

                return new DelegateAgentTaskResult(definition.Id, definition.Name, true, output.ToString());
            }
            finally
            {
                // 归还释放：先从工厂跟踪列表移除引用，再关闭底层子进程，避免已释放 Shell 的引用残留到会话结束
                await shellFactory.ReleaseAsync(shell);
            }
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return new(definition.Id, definition.Name, false, "子任务执行超时。");
        }
        catch (Exception exception)
        {
            return new(definition.Id, definition.Name, false, exception.Message);
        }
    }
}