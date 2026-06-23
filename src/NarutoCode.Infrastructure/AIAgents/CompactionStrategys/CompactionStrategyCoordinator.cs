using System.Collections.Concurrent;
using Microsoft.Agents.AI.Compaction;
using Microsoft.Extensions.AI;
using NarutoCode.Domain;
using NarutoCode.Domain.Configurations.Settings;
using NarutoCode.Infrastructure.AIAgents.DelegatingChatClients;

namespace NarutoCode.Infrastructure.AIAgents.CompactionStrategys;

/// <summary>
/// 压缩策略协调器，根据配置的阈值依次执行图片、工具结果、摘要三种压缩策略。
/// </summary>
public class CompactionStrategyCoordinator(ILlmSettingsService llmSettingsService, DynamicChatClient dynamicChatClient)
{
    private static ConcurrentDictionary<string, IChatReducer> _datas = new();

    private IChatReducer Create()
    {
        return _datas.GetOrAdd(llmSettingsService.CurrentProvider, BuildChatReducer());
    }

    private IChatReducer BuildChatReducer()
    {
        // SummarizationCompactionStrategy 生成摘要压缩
        // ToolResultCompactionStrategy 工具结果压缩，只是把工具的结果用yaml拼接在一起，不会移除任何的用户消息
        // TruncationCompactionStrategy 简单粗暴，直接移除老的消息，只保留最新的MinimumPreservedGroups条消息
        // SlidingWindowCompactionStrategy 按照用户交互的轮次来移除老的轮次

        /**
         * ContextWindowCompactionStrategy 上下文窗口形式，
         * 结合了ToolResultCompactionStrategy和TruncationCompactionStrategy两种模式
         * 但是因为流水线优先执行的是ToolResultCompactionStrategy方式，此方式不会移除任何的用户消息，对于压缩的效果不高
         */

        // 输入窗口的剩余最大token
        var inputBudgetTokens = llmSettingsService.CurrentLlm.MaxContextWindowTokens - AgentFactory.MaxOutputTokens;

        // 从配置获取压缩阈值
        var thresholds = AppData.Config.System.CompactionThresholds;

        // 图片压缩阈值：图片占用空间大，优先处理
        var imageCompactionTokens = (int)(inputBudgetTokens * thresholds.ImageCompaction);
        // 工具结果压缩阈值：不调用LLM，轻量级压缩
        var toolEvictionTokens = (int)(inputBudgetTokens * thresholds.ToolEviction);
        // 摘要压缩阈值：调用LLM生成摘要，代价最高
        var summarizationTokens = (int)(inputBudgetTokens * thresholds.Summarization);

#pragma warning disable MAAI001
        // todo 设置推理强度为none
        var compactionStrategy = new PipelineCompactionStrategy([
            new ImageCompactionStrategy(trigger: CompactionTriggers.TokensExceed(imageCompactionTokens)),
            new ToolResultCompactionStrategy(
                trigger: CompactionTriggers.TokensExceed(toolEvictionTokens)), // 首先不调用大模型，只将tool的结果拼接，然后移除思考过程
            // 调用摘要的压缩，将历史的消息浓缩成摘要信息，+保留的最近的几条消息+上面的工具压缩的
            new SummarizationCompactionStrategy(dynamicChatClient,
                trigger: CompactionTriggers.TokensExceed(summarizationTokens),
                minimumPreservedGroups: 8) // minimumPreservedGroups 设置保留的最新的消息数量，用于上下文的连贯
        ]);
#pragma warning restore MAAI001
        // 增加一个工具结果的本地存储
        return compactionStrategy.AsChatReducer();
    }

    /// <summary>
    /// 裁剪消息列表。
    /// </summary>
    /// <param name="messages">待裁剪的消息列表。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>裁剪后的消息列表。</returns>
    public async Task<IEnumerable<ChatMessage>> ReduceAsync(IEnumerable<ChatMessage> messages,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var chatReducer = Create();
        return await chatReducer.ReduceAsync(messages, cancellationToken);
    }
}
