using Microsoft.Agents.AI;

namespace NarutoCode.Infrastructure.AIAgents;

/// <summary>
/// Agent 工厂
/// </summary>
public interface IAgentFactory
{
    AIAgent Create();
}