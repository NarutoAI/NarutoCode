#pragma warning disable MAAI001
using Microsoft.Agents.AI;
using NarutoCode.Domain;
using NarutoCode.Domain.Configurations;
using NarutoCode.Domain.Configurations.Settings;
using NarutoCode.Infrastructure.AIAgents.AIContextProviders;
using NarutoCode.Infrastructure.Images;
using NarutoCode.Infrastructure.Vision;

namespace NarutoCode.Infrastructure.AIAgents.Composition.Contributors;

/// <summary>
/// 图片识别贡献者：注册图片识别提供器，由提供器按当前主模型能力与视觉配置决定是否注入工具。
/// </summary>
public sealed class VisionContributor(
    IImageUrlLoader imageUrlLoader,
    ILlmSettingsService llmSettingsService) : IAgentContributor
{
    /// <inheritdoc />
    public string Name => "Vision";

    /// <inheritdoc />
    public void Contribute(AgentCompositionContext context, AgentCompositionBuilder builder)
    {
        // Provider 在每次组装上下文时根据当前主模型和最新视觉配置决定是否注入工具。
        builder.AddAIContextProvider(new VisionAIContextProvider(
            imageUrlLoader,
            new VisionChatClient(AppData.Config.Vision ?? new VisionConfiguration()),
            llmSettingsService));
    }
}
#pragma warning restore MAAI001
