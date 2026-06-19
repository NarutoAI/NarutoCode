using System.Collections.Concurrent;
using Microsoft.Agents.AI.Compaction;
using Microsoft.Extensions.AI;
using NarutoCode.Domain.Configurations.Settings;
using NarutoCode.Infrastructure.AIAgents.DelegatingChatClients;

namespace NarutoCode.Infrastructure.AIAgents;

/// <summary>
/// 压缩策略协调
/// </summary>
public class CompactionStrategyCoordinator(ILlmSettingsService llmSettingsService, DynamicChatClient dynamicChatClient)
{
    private static ConcurrentDictionary<string, IChatReducer> _datas = new();

    public IChatReducer Create()
    {
        return _datas.GetOrAdd(llmSettingsService.CurrentProvider, BuildChatReducer);
    }

    private IChatReducer BuildChatReducer(string provider)
    {
        //SummarizationCompactionStrategy 生成摘要压缩
        //ToolResultCompactionStrategy 工具结果 压缩，只是把工具的结果用yaml 拼接在一起，不会移除任何的用户消息
        //TruncationCompactionStrategy 简单粗暴，直接移除老的消息，只保留最新的MinimumPreservedGroups条消息
        //SlidingWindowCompactionStrategy 按照用户交互的轮次来移除老的轮次

        /**
         * ContextWindowCompactionStrategy 上下文窗口形式，
         * 结合了ToolResultCompactionStrategy和TruncationCompactionStrategy 两种模式
         * 但是因为流水线优先执行的是ToolResultCompactionStrategy方式，此方式不会移除任何的用户消息 对于压缩的效果不高
         *
         */

        //输入窗口的剩余最大token
        var inputBudgetTokens = llmSettingsService.CurrentLlm.MaxContextWindowTokens - AgentFactory.MaxOutputTokens;
        //工具结果的摘要
        var toolEvictionTokens = (int) (inputBudgetTokens * 0.6);
        //摘要的阀值
        var summarizationTokens = (int) (inputBudgetTokens * 0.8);
#pragma warning disable MAAI001
       var compactionStrategy= new PipelineCompactionStrategy([
           new ToolResultCompactionStrategy( trigger: CompactionTriggers.TokensExceed(toolEvictionTokens)),//首先不调用大模型，只将tool的结果拼接，然后移除思考过程
           //调用摘要的压缩，将历史的消息浓缩成摘要信息，+ 保留的最近的几条消息+上面的工具压缩的
            new SummarizationCompactionStrategy(dynamicChatClient,
                trigger: CompactionTriggers.TokensExceed(summarizationTokens), minimumPreservedGroups: 8)// minimumPreservedGroups 设置保留的最新的消息数量，用于上下文的连贯
        ]);
#pragma warning restore MAAI001
        //增加一个工具结果的本地存储
        return compactionStrategy.AsChatReducer();
    }
}