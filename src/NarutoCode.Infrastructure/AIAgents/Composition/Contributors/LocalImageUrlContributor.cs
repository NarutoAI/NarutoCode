#pragma warning disable MAAI001
using Microsoft.Agents.AI;
using NarutoCode.Domain.Configurations.Settings;
using NarutoCode.Infrastructure.AIAgents.AIContextProviders;
using NarutoCode.Infrastructure.Images;

namespace NarutoCode.Infrastructure.AIAgents.Composition.Contributors;

/// <summary>
/// 本地 URL 图片贡献者：当前模型支持视觉时挂载 URL 图片加载提供器。
/// </summary>
public sealed class LocalImageUrlContributor(
    ILlmSettingsService llmSettingsService,
    IImageUrlLoader imageUrlLoader) : IAgentContributor
{
    /// <inheritdoc />
    public string Name => "LocalImageUrl";

    /// <inheritdoc />
    public void Contribute(AgentCompositionContext context, AgentCompositionBuilder builder)
    {
        builder.AddAIContextProvider(new LocalImageUrlAIContextProvider(imageUrlLoader,llmSettingsService));
    }
}
#pragma warning restore MAAI001
