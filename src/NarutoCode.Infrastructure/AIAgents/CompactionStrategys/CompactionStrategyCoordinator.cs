using Microsoft.Agents.AI.Compaction;
using Microsoft.Extensions.AI;
using NarutoCode.Domain;
using NarutoCode.Domain.Configurations.Settings;
using NarutoCode.Infrastructure.AIAgents.DelegatingChatClients;

namespace NarutoCode.Infrastructure.AIAgents.CompactionStrategys;

/// <summary>
/// 压缩策略协调器，根据配置的阈值依次执行图片、工具结果、摘要和兜底截断策略。
/// </summary>
public class CompactionStrategyCoordinator(ILlmSettingsService llmSettingsService, DynamicChatClient dynamicChatClient)
{
    private IChatReducer BuildChatReducer(long? lastUsageTokenCount)
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
        var inputBudgetTokens = llmSettingsService.CurrentLlm.MaxContextWindowTokens - llmSettingsService.CurrentLlm.MaxOutputTokens;

        // 从配置获取压缩阈值
        var thresholds = AppData.Config.System.CompactionThresholds;

        // 图片压缩阈值：图片占用空间大，优先处理
        var imageCompactionTokens = (int)(inputBudgetTokens * thresholds.ImageCompaction);
        // 工具结果压缩阈值：不调用LLM，轻量级压缩
        var toolEvictionTokens = (int)(inputBudgetTokens * thresholds.ToolEviction);
        // 摘要压缩阈值：调用LLM生成摘要，代价最高
        var summarizationTokens = (int)(inputBudgetTokens * thresholds.Summarization);
        // 兜底截断阈值：摘要后仍接近窗口上限时，直接保留最近消息，避免请求超过窗口。
        var fallbackTruncationTokens = (int)(inputBudgetTokens * thresholds.FallbackTruncation);
        var minimumPreservedGroups = Math.Max(1, thresholds.MinimumPreservedGroups);

        // 构建自适应触发器：优先使用最近一次 LLM 调用的真实 input token 用量，
        // 没有真实用量（如首次调用）时回退到框架的 IncludedTokenCount 估算值。
#pragma warning disable MAAI001
        CompactionTrigger CreateTokenTrigger(int threshold) =>
            lastUsageTokenCount > 0
                ? index => lastUsageTokenCount.Value > threshold
                : CompactionTriggers.TokensExceed(threshold);

        // todo 设置摘要提取的推理强度为none
        var compactionStrategy = new PipelineCompactionStrategy([
            new ImageCompactionStrategy(trigger: CreateTokenTrigger(imageCompactionTokens)),
            new ToolResultCompactionStrategy(
                trigger: CreateTokenTrigger(toolEvictionTokens)), // 大于 60% 时先压工具结果，减少无需 LLM 的上下文成本。
            new SummarizationCompactionStrategy(dynamicChatClient,
                trigger: CreateTokenTrigger(summarizationTokens),
                minimumPreservedGroups: minimumPreservedGroups), // 大于 80% 时生成摘要，保留最近消息保证连续性。
            new TruncationCompactionStrategy(
                trigger: CreateTokenTrigger(fallbackTruncationTokens),
                minimumPreservedGroups: minimumPreservedGroups) // 大于 90% 时兜底截断，避免上下文超过模型窗口。
        ]);
#pragma warning restore MAAI001
        // 增加一个工具结果的本地存储
        return compactionStrategy.AsChatReducer();
    }

    /// <summary>
    /// 裁剪消息列表。
    /// </summary>
    /// <param name="messages">待裁剪的消息列表。</param>
    /// <param name="lastUsageTokenCount">最近一次 LLM 调用的真实 input token 用量，用于更精确地判断是否触发压缩；为 null 时回退到框架估算。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>裁剪后的消息列表。</returns>
    public async Task<IEnumerable<ChatMessage>> ReduceAsync(IEnumerable<ChatMessage> messages,
        long? lastUsageTokenCount = null,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var chatReducer = BuildChatReducer(lastUsageTokenCount);
        return await chatReducer.ReduceAsync(messages, cancellationToken);
    }
}
