using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;
using NarutoCode.Domain.Configurations.Settings;
using NarutoCode.Infrastructure.AIAgents;
using NarutoCode.Infrastructure.Extensions;

namespace NarutoCode.Infrastructure.AIAgents.DelegatingChatClients;

/// <summary>
/// 动态聊天客户端
/// </summary>
/// <param name="serviceProvider"></param>
/// <param name="llmSettingsService"></param>
public class DynamicChatClient(IServiceProvider serviceProvider, ILlmSettingsService llmSettingsService) : IChatClient
{
    public void Dispose()
    {
        Dispose(true);
    }
    
    protected virtual void Dispose(bool disposing)
    {
        if (!disposing)
            return;
        foreach (var provider in llmSettingsService.GetAvailableProviders())
        {
            serviceProvider.GetRequiredKeyedService<IChatClient>(provider).Dispose();
        }
    }

    public Task<ChatResponse> GetResponseAsync(IEnumerable<ChatMessage> messages, ChatOptions? options = null,
        CancellationToken cancellationToken = new CancellationToken())
    {
        return serviceProvider.GetRequiredKeyedService<IChatClient>(llmSettingsService.CurrentProvider)
            .GetResponseAsync(messages, ApplyEffort(options), cancellationToken);
    }

    public IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(IEnumerable<ChatMessage> messages,
        ChatOptions? options = null,
        CancellationToken cancellationToken = new CancellationToken())
    {
        return serviceProvider.GetRequiredKeyedService<IChatClient>(llmSettingsService.CurrentProvider)
            .GetStreamingResponseAsync(messages, ApplyEffort(options), cancellationToken);
    }

    
    /// <summary>
    /// 更新推理强度
    /// </summary>
    /// <param name="options"></param>
    /// <returns></returns>
    private ChatOptions ApplyEffort(ChatOptions? options)
    {
        var configuredOptions = options ?? new ChatOptions();
        configuredOptions.Reasoning ??= new ReasoningOptions();
        configuredOptions.Reasoning.Effort = llmSettingsService.CurrentEffort.ToReasoningEffort();
        return configuredOptions;
    }

    public object? GetService(Type serviceType, object? serviceKey = null)
    {
        return serviceProvider.GetRequiredKeyedService<IChatClient>(llmSettingsService.CurrentProvider)
            .GetService(serviceType, serviceKey);
    }
}