using System.Collections.Concurrent;
using Microsoft.Agents.AI.Compaction;
using Microsoft.Extensions.AI;
using NarutoCode.Domain.Configurations.Settings;

namespace NarutoCode.Infrastructure.AIAgents;

/// <summary>
/// 压缩策略协调
/// </summary>
public class CompactionStrategyCoordinator(ILlmSettingsService llmSettingsService)
{
    private static ConcurrentDictionary<string, IChatReducer> _datas = new();

    public IChatReducer Create()
    {
        return _datas.GetOrAdd(llmSettingsService.CurrentProvider, BuildChatReducer);
    }

    private  IChatReducer BuildChatReducer(string provider)
    {
        //上下文窗口裁剪
#pragma warning disable MAAI001
        var compactionStrategy = new ContextWindowCompactionStrategy(
#pragma warning restore MAAI001
            maxContextWindowTokens: llmSettingsService.CurrentLlm.MaxContextWindowTokens,
            maxOutputTokens: AgentFactory.MaxOutputTokens);

        return compactionStrategy.AsChatReducer();
    }
}