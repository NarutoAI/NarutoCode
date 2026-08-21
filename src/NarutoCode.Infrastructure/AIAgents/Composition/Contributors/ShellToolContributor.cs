#pragma warning disable MAAI001
using Microsoft.Agents.AI;
using Microsoft.Agents.AI.Tools.Shell;
using Microsoft.Extensions.AI;
using NarutoCode.Infrastructure.AIAgents.AIContextProviders;

namespace NarutoCode.Infrastructure.AIAgents.Composition.Contributors;

/// <summary>
/// Shell 贡献者：挂载持久 Shell 环境提供器，并将持久 Shell 注册为 ChatOptions 工具。
/// </summary>
public sealed class ShellToolContributor : IAgentContributor
{
    /// <inheritdoc />
    public string Name => "ShellTool";

    /// <inheritdoc />
    public void Contribute(AgentCompositionContext context, AgentCompositionBuilder builder)
    {
        builder.AddAIContextProvider(new ShellEnvironmentProvider(context.PersistentShell));
        builder.AddTool(context.PersistentShell.AsAIFunction());
    }
}
#pragma warning restore MAAI001
