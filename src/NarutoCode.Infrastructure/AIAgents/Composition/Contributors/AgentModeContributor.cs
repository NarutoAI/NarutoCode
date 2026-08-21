#pragma warning disable MAAI001
using Microsoft.Agents.AI;

namespace NarutoCode.Infrastructure.AIAgents.Composition.Contributors;

/// <summary>
/// Agent 模式贡献者：贡献 mode_get/mode_set 模式管理指令与默认 execute 模式。
/// </summary>
public sealed class AgentModeContributor : IAgentContributor
{
    /// <inheritdoc />
    public string Name => "AgentMode";

    /// <inheritdoc />
    public void Contribute(AgentCompositionContext context, AgentCompositionBuilder builder)
    {
        builder.AddAgentModeProviderOptions(new AgentModeProviderOptions
        {
            // {current_mode} 与 {available_modes} 由框架运行时替换，此处不做字符串插值
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
        });
    }
}
#pragma warning restore MAAI001
