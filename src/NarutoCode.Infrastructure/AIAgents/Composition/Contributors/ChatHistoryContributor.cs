#pragma warning disable MAAI001
using Microsoft.Agents.AI;
using NarutoCode.Domain.Configurations.Settings;
using NarutoCode.Infrastructure.AIAgents.ChatHistorys;
using NarutoCode.Infrastructure.AIAgents.CompactionStrategys;

namespace NarutoCode.Infrastructure.AIAgents.Composition.Contributors;

/// <summary>
/// 聊天历史贡献者：会话级 Agent 使用持久化历史（含上下文裁剪），子 Agent 使用内存历史。
/// </summary>
public sealed class ChatHistoryContributor(
    IChatHistoryPersistenceHandler chatHistoryPersistenceHandler,
    CompactionStrategyCoordinator compactionStrategyCoordinator,
    ILlmSettingsService llmSettingsService) : IAgentContributor
{
    /// <inheritdoc />
    public string Name => "ChatHistory";

    /// <inheritdoc />
    public void Contribute(AgentCompositionContext context, AgentCompositionBuilder builder)
    {
        // 会话级 Agent 持久化历史；子 Agent 内存历史，任务结束即丢弃
        builder.AddChatHistoryProvider(context.Profile == AgentProfile.Session
            ? new PersistenceChatHistoryProvider(
                chatHistoryPersistenceHandler,
                compactionStrategyCoordinator,
                llmSettingsService)
            : new InMemoryChatHistoryProvider());
    }
}
#pragma warning restore MAAI001
