#pragma warning disable MAAI001
using Microsoft.Agents.AI;
using NarutoCode.Infrastructure.AIAgents.AIContextProviders;

namespace NarutoCode.Infrastructure.AIAgents.Composition.Contributors;

/// <summary>
/// SVG 渲染贡献者：挂载将 SVG 写入工作区并生成安全预览的渲染提供器。
/// </summary>
public sealed class SvgRenderContributor : IAgentContributor
{
    /// <inheritdoc />
    public string Name => "SvgRender";

    /// <inheritdoc />
    public void Contribute(AgentCompositionContext context, AgentCompositionBuilder builder)
    {
        // 预览输出目录绑定当前工作目录，须按上下文实例化
        builder.AddAIContextProvider(new SvgRenderProvider(context.WorkingDirectory));
    }
}
#pragma warning restore MAAI001
