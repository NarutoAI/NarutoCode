using NarutoCode.Infrastructure.AIAgents.Composition;

namespace NarutoCode.Infrastructure.AIAgents;

/// <summary>
/// Agent 工厂行为开关：由宿主在依赖注入注册时决定，避免 CLI 专属能力泄漏到桌面端/网关。
/// </summary>
public sealed class AgentFactoryOptions
{
    /// <summary>
    /// 创建 Agent 工厂开关。
    /// </summary>
    /// <param name="enableUserInteractionTools">是否挂载 ask_user 用户交互工具。</param>
    public AgentFactoryOptions(bool enableUserInteractionTools)
    {
        EnableUserInteractionTools = enableUserInteractionTools;
    }

    /// <summary>
    /// 是否挂载 ask_user 用户交互工具：仅 CLI 宿主传入 true，桌面端/网关保持 false。
    /// </summary>
    public bool EnableUserInteractionTools { get; }

    /// <summary>
    /// 宿主手动附加的编排贡献者列表，与 DI 注册贡献者合并参与装配（DI 项在前、附加项在后）。
    /// </summary>
    public IReadOnlyList<IAgentContributor> AdditionalContributors { get; init; } = [];
}
