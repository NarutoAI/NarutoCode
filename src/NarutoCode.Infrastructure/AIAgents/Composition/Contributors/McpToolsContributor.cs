#pragma warning disable MAAI001
using Microsoft.Agents.AI;
using NarutoCode.Infrastructure.AIAgents.AIContextProviders;
using NarutoCode.Infrastructure.AIAgents.Mcp;

namespace NarutoCode.Infrastructure.AIAgents.Composition.Contributors;

/// <summary>
/// MCP 工具贡献者：将配置中的 MCP 服务工具注入当前 Agent 上下文。
/// </summary>
public sealed class McpToolsContributor(McpClientManager mcpClientManager) : IAgentContributor
{
    /// <inheritdoc />
    public string Name => "McpTools";

    /// <inheritdoc />
    public void Contribute(AgentCompositionContext context, AgentCompositionBuilder builder)
    {
        builder.AddAIContextProvider(new McpToolsAIContextProvider(mcpClientManager));
    }
}
#pragma warning restore MAAI001
