using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;

#pragma warning disable MAAI001

namespace NarutoCode.Infrastructure.AIAgents.Composition;

/// <summary>
/// Agent 装配构建器：按贡献顺序聚合编排要素，最终产出 <see cref="AgentComposition"/>。
/// </summary>
public sealed class AgentCompositionBuilder
{
    private readonly List<AIContextProvider> _aiContextProviders = [];
    private readonly List<string> _instructionSections = [];
    private readonly List<LoopEvaluator> _loopEvaluators = [];
    private readonly List<AITool> _tools = [];
    private ChatHistoryProvider? _chatHistoryProvider;
    private AgentModeProviderOptions? _agentModeProviderOptions;

    /// <summary>
    /// 追加 AI 上下文提供器，保持贡献顺序。
    /// </summary>
    /// <param name="provider">AI 上下文提供器。</param>
    /// <returns>当前构建器。</returns>
    public AgentCompositionBuilder AddAIContextProvider(AIContextProvider provider)
    {
        ArgumentNullException.ThrowIfNull(provider);
        _aiContextProviders.Add(provider);
        return this;
    }

    /// <summary>
    /// 追加 Instruction 片段，构建时按贡献顺序以空行拼接。
    /// </summary>
    /// <param name="section">指令片段；空白片段忽略。</param>
    /// <returns>当前构建器。</returns>
    public AgentCompositionBuilder AddInstruction(string section)
    {
        // 空片段直接忽略，避免拼接出多余空行
        if (!string.IsNullOrWhiteSpace(section))
        {
            _instructionSections.Add(section);
        }

        return this;
    }

    /// <summary>
    /// 追加循环评估器，保持贡献顺序。
    /// </summary>
    /// <param name="evaluator">循环评估器。</param>
    /// <returns>当前构建器。</returns>
    public AgentCompositionBuilder AddLoopEvaluator(LoopEvaluator evaluator)
    {
        ArgumentNullException.ThrowIfNull(evaluator);
        _loopEvaluators.Add(evaluator);
        return this;
    }

    /// <summary>
    /// 追加 ChatOptions 工具（如持久 Shell 函数）。
    /// </summary>
    /// <param name="tool">工具实例。</param>
    /// <returns>当前构建器。</returns>
    public AgentCompositionBuilder AddTool(AITool tool)
    {
        ArgumentNullException.ThrowIfNull(tool);
        _tools.Add(tool);
        return this;
    }

    /// <summary>
    /// 设置聊天历史提供器；多个贡献者设置时后写覆盖。
    /// </summary>
    /// <param name="provider">聊天历史提供器。</param>
    /// <returns>当前构建器。</returns>
    public AgentCompositionBuilder AddChatHistoryProvider(ChatHistoryProvider provider)
    {
        _chatHistoryProvider = provider;
        return this;
    }

    /// <summary>
    /// 设置 Agent 模式提供器选项；多个贡献者设置时后写覆盖。
    /// </summary>
    /// <param name="options">Agent 模式提供器选项。</param>
    /// <returns>当前构建器。</returns>
    public AgentCompositionBuilder AddAgentModeProviderOptions(AgentModeProviderOptions options)
    {
        _agentModeProviderOptions = options;
        return this;
    }

    /// <summary>
    /// 产出装配结果；Instruction 片段按贡献顺序以空行拼接。
    /// </summary>
    /// <returns>最终装配结果。</returns>
    public AgentComposition Build() => new(
        _aiContextProviders.AsReadOnly(),
        string.Join("\n\n", _instructionSections),
        _loopEvaluators.AsReadOnly(),
        _tools.AsReadOnly(),
        _chatHistoryProvider,
        _agentModeProviderOptions);
}
#pragma warning restore MAAI001
