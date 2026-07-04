using Microsoft.Agents.AI;
using NarutoCode.Domain;
using NarutoCode.Infrastructure.AIAgents.Mcp;

namespace NarutoCode.Infrastructure.AIAgents.AIContextProviders;

/// <summary>
/// 将配置中的 MCP 服务工具注入当前 Agent 上下文。
/// </summary>
public sealed class McpToolsAIContextProvider(McpClientManager mcpClientManager) : AIContextProvider
{
    /// <inheritdoc />
    protected override async ValueTask<AIContext> ProvideAIContextAsync(
        InvokingContext context,
        CancellationToken cancellationToken = default)
    {
        if (AppData.Config.McpServers.Count == 0)
        {
            return new AIContext();
        }

        var tools = await mcpClientManager.GetToolsAsync(AppData.Config.McpServers, cancellationToken);
        return new AIContext
        {
            Tools = tools.ToArray()
        };
    }
}
