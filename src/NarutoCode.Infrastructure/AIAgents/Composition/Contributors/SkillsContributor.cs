#pragma warning disable MAAI001
using Microsoft.Agents.AI;
using Microsoft.Extensions.Logging;
using NarutoCode.Domain;
using NarutoCode.Infrastructure.AIAgents.Skills;

namespace NarutoCode.Infrastructure.AIAgents.Composition.Contributors;

/// <summary>
/// 技能贡献者：挂载全局技能目录的 Agent 技能提供器。
/// </summary>
public sealed class SkillsContributor(ILoggerFactory loggerFactory) : IAgentContributor
{
    /// <inheritdoc />
    public string Name => "Skills";

    /// <inheritdoc />
    public void Contribute(AgentCompositionContext context, AgentCompositionBuilder builder)
    {
        builder.AddAIContextProvider(new AgentSkillsProvider(
            [Path.Combine(context.WorkingDirectory,".agents","skills"),ProjectConstant.SkillsDirectory],
            scriptRunner: SkillSubprocessScriptRunner.RunAsync,
            loggerFactory: loggerFactory));
    }
}
#pragma warning restore MAAI001
