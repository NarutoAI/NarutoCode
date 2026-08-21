namespace NarutoCode.Infrastructure.AIAgents.Composition;

/// <summary>
/// Agent 身份档案：决定装配管道中各贡献者的参与规则，同一管道按档案组装出不同能力的 Agent Runtime。
/// </summary>
public enum AgentProfile
{
    /// <summary>
    /// 会话级主 Agent：完整能力（历史持久化、用户交互工具、子 Agent 委派）。
    /// </summary>
    Session,

    /// <summary>
    /// 子 Agent：受限能力（内存历史、无用户交互、无嵌套委派）。
    /// </summary>
    SubAgent
}
