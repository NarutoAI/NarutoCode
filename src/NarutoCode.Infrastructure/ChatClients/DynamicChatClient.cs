// using Microsoft.Extensions.AI;
// using Microsoft.Extensions.DependencyInjection;
// using NarutoCode.Domain.Configurations.Settings;
//
// namespace NarutoCode.Infrastructure.ChatClients;
//
// /// <summary>
// /// 动态聊天客户端
// /// </summary>
// /// <param name="serviceProvider"></param>
// /// <param name="llmSettingsService"></param>
// public class DynamicChatClient(IServiceProvider serviceProvider, ILlmSettingsService llmSettingsService) : IChatClient
// {
//     public void Dispose()
//     {
//         Dispose(true);
//     }
//     
//     protected virtual void Dispose(bool disposing)
//     {
//         if (!disposing)
//             return;
//         foreach (var provider in llmSettingsService.GetAvailableProviders())
//         {
//             serviceProvider.GetRequiredKeyedService<IChatClient>(provider).Dispose();
//         }
//     }
//
//     public Task<ChatResponse> GetResponseAsync(IEnumerable<ChatMessage> messages, ChatOptions? options = null,
//         CancellationToken cancellationToken = new CancellationToken())
//     {
//         return serviceProvider.GetRequiredKeyedService<IChatClient>(llmSettingsService.CurrentProvider)
//             .GetResponseAsync(messages, options, cancellationToken);
//     }
//
//     public IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(IEnumerable<ChatMessage> messages,
//         ChatOptions? options = null,
//         CancellationToken cancellationToken = new CancellationToken())
//     {
//         return serviceProvider.GetRequiredKeyedService<IChatClient>(llmSettingsService.CurrentProvider)
//             .GetStreamingResponseAsync(messages, options, cancellationToken);
//     }
//
//     public object? GetService(Type serviceType, object? serviceKey = null)
//     {
//         return serviceProvider.GetRequiredKeyedService<IChatClient>(llmSettingsService.CurrentProvider)
//             .GetService(serviceType, serviceKey);
//     }
// }