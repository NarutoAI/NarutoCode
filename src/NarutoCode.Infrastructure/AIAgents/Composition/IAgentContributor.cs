namespace NarutoCode.Infrastructure.AIAgents.Composition;

/// <summary>
/// Agent 编排贡献者：向装配构建器贡献任意编排要素（Instructions、AIContextProvider、LoopEvaluator、Tool 等），
/// 一个实现可同时贡献多类要素；后续新增 Agent 能力只需新增实现并注册 DI，无需修改 AgentFactory。
/// </summary>
public interface IAgentContributor
{
    /// <summary>
    /// 贡献者标识，用于日志与诊断。
    /// </summary>
    string Name { get; }

    /// <summary>
    /// 是否参与当前装配；默认参与，实现可按身份档案或宿主开关细化（如仅 Session 档案挂载）。
    /// </summary>
    /// <param name="context">装配上下文。</param>
    /// <returns>参与装配返回 <see langword="true"/>。</returns>
    bool ShouldContribute(AgentCompositionContext context) => true;

    /// <summary>
    /// 向构建器贡献编排要素；Provider 实例化在本地按上下文完成，贡献者自身可注册为单例。
    /// </summary>
    /// <param name="context">装配上下文。</param>
    /// <param name="builder">装配构建器。</param>
    void Contribute(AgentCompositionContext context, AgentCompositionBuilder builder);
}
