#pragma warning disable MAAI001
using Microsoft.Agents.AI.LocalCodeAct;
using Microsoft.Extensions.Logging;

namespace NarutoCode.Infrastructure.AIAgents.Composition.Contributors;

/// <summary>
/// 本地代码执行贡献者：挂载 LocalCodeAct 提供器，Python 解释器按优先级解析。
/// </summary>
public sealed class LocalCodeActContributor(ILogger<LocalCodeActContributor> logger) : IAgentContributor
{
    /// <inheritdoc />
    public string Name => "LocalCodeAct";

    /// <inheritdoc />
    public void Contribute(AgentCompositionContext context, AgentCompositionBuilder builder)
    {
        builder.AddAIContextProvider(new LocalCodeActProvider(PythonExecutableResolver.Resolve(logger)));
    }
}
#pragma warning restore MAAI001
