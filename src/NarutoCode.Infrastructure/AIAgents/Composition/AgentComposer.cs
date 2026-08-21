using Microsoft.Extensions.Logging;

namespace NarutoCode.Infrastructure.AIAgents.Composition;

/// <summary>
/// Agent 装配协调器：合并 DI 注册贡献者与 AgentFactoryOptions.AdditionalContributors 手动贡献者，
/// 按注册顺序执行 ShouldContribute 过滤与 Contribute 聚合，产出最终装配结果。
/// </summary>
public sealed class AgentComposer
{
    private readonly IReadOnlyList<IAgentContributor> _contributors;
    private readonly ILogger<AgentComposer> _logger;

    /// <summary>
    /// 创建装配协调器，合并双通道贡献者。
    /// </summary>
    /// <param name="contributors">DI 注册的贡献者集合，解析顺序即注册顺序。</param>
    /// <param name="agentFactoryOptions">Agent 工厂选项，携带宿主手动追加的贡献者。</param>
    /// <param name="logger">日志器。</param>
    public AgentComposer(
        IEnumerable<IAgentContributor> contributors,
        AgentFactoryOptions agentFactoryOptions,
        ILogger<AgentComposer> logger)
    {
        ArgumentNullException.ThrowIfNull(contributors);
        ArgumentNullException.ThrowIfNull(agentFactoryOptions);

        // DI 注册顺序即贡献顺序（.NET DI IEnumerable<T> 按注册顺序解析），手动贡献者统一追加在末尾
        _contributors = [.. contributors, .. agentFactoryOptions.AdditionalContributors];
        _logger = logger;
    }

    /// <summary>
    /// 按上下文执行全部适用贡献者并产出装配结果。
    /// </summary>
    /// <param name="context">装配上下文。</param>
    /// <returns>最终装配结果。</returns>
    public AgentComposition Compose(AgentCompositionContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        var builder = new AgentCompositionBuilder();
        foreach (var contributor in _contributors)
        {
            // 先按身份档案/宿主开关过滤，再聚合要素，保证顺序稳定
            if (contributor.ShouldContribute(context))
            {
                contributor.Contribute(context, builder);
            }
            else
            {
                Log.ContributorSkipped(_logger, contributor.Name, context.Profile);
            }
        }

        return builder.Build();
    }
}
