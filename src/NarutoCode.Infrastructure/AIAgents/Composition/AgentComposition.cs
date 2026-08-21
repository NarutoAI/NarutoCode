using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;

#pragma warning disable MAAI001

namespace NarutoCode.Infrastructure.AIAgents.Composition;

/// <summary>
/// Agent 装配结果：HarnessAgentOptions 各扩展点的最终值，由构建器聚合产出。
/// </summary>
/// <param name="AIContextProviders">AI 上下文提供器集合，按贡献顺序排列。</param>
/// <param name="Instructions">聚合后的系统指令。</param>
/// <param name="LoopEvaluators">循环评估器集合，按贡献顺序排列。</param>
/// <param name="Tools">ChatOptions 工具集合，按贡献顺序排列。</param>
/// <param name="ChatHistoryProvider">聊天历史提供器；未贡献时由调用方使用默认值。</param>
/// <param name="AgentModeProviderOptions">Agent 模式提供器选项；未贡献时由调用方使用默认值。</param>
public sealed class AgentComposition(
    IReadOnlyList<AIContextProvider> aiContextProviders,
    string instructions,
    IReadOnlyList<LoopEvaluator> loopEvaluators,
    IReadOnlyList<AITool> tools,
    ChatHistoryProvider? chatHistoryProvider,
    AgentModeProviderOptions? agentModeProviderOptions)
{
    /// <summary>AI 上下文提供器集合，按贡献顺序排列。</summary>
    public IReadOnlyList<AIContextProvider> AIContextProviders { get; } = aiContextProviders;

    /// <summary>聚合后的系统指令。</summary>
    public string Instructions { get; } = instructions;

    /// <summary>循环评估器集合，按贡献顺序排列。</summary>
    public IReadOnlyList<LoopEvaluator> LoopEvaluators { get; } = loopEvaluators;

    /// <summary>ChatOptions 工具集合，按贡献顺序排列。</summary>
    public IReadOnlyList<AITool> Tools { get; } = tools;

    /// <summary>聊天历史提供器；未贡献时由调用方使用默认值。</summary>
    public ChatHistoryProvider? ChatHistoryProvider { get; } = chatHistoryProvider;

    /// <summary>Agent 模式提供器选项；未贡献时由调用方使用默认值。</summary>
    public AgentModeProviderOptions? AgentModeProviderOptions { get; } = agentModeProviderOptions;
}
#pragma warning restore MAAI001
