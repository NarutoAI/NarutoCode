using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;
using NarutoCode.Domain;
using NarutoCode.Domain.Messages;
using NarutoCode.Infrastructure.ChatClients;

namespace NarutoCode.Infrastructure.AIAgents.DelegatingChatClients;

/// <summary>
/// 用于监听用户的队列消息 然后将消息提交到 MessageInjectingChatClient的队列
/// </summary>
public class ListeningMessageQueueChatClient(IChatClient innerClient) : DelegatingChatClient(innerClient)
{
    public override async IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(IEnumerable<ChatMessage> messages,
        ChatOptions? options = null,
        CancellationToken cancellationToken = new CancellationToken())
    {
#pragma warning disable MAAI001

        //插入消息
        // messageInjectingChatClient.EnqueueMessages();
        var pendingUserMessageQueue =
            (PendingUserMessageQueue) RootServiceProviderLocator.ServiceProvider.GetService(
                typeof(PendingUserMessageQueue));
        if (pendingUserMessageQueue != null && pendingUserMessageQueue.HasMessages)
        {
            //从agent中获取消息注入上下文
            var messageInjectingChatClient = AIAgent.CurrentRunContext!.Agent.GetService<MessageInjectingChatClient>();
            //读取消息
            if (pendingUserMessageQueue.TryDrain(out var queueMessage))
            {
               // 运行中插队的消息同样是真实用户输入，包裹 user-input 边界标签保持语义一致
               await messageInjectingChatClient!.EnqueueMessagesAsync(AIAgent.CurrentRunContext!.Session!,
                    [new ChatMessage(ChatRole.User, [new TextContent(PromptTags.WrapUserInput(queueMessage))])], cancellationToken);
            }
        }
#pragma warning restore MAAI001
        await foreach (var item in base.GetStreamingResponseAsync(messages, options, cancellationToken))
        {
            yield return item;
        }
    }
}

public static class ListeningMessageQueueChatClientExtension
{
    public static ChatClientBuilder UseListeningMessageQueue(this ChatClientBuilder builder)
    {
        return builder.Use(innerClient => new ListeningMessageQueueChatClient(innerClient));
    }
}