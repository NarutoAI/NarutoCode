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
    public override IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(IEnumerable<ChatMessage> messages,
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
                messageInjectingChatClient!.EnqueueMessages(AIAgent.CurrentRunContext!.Session!,
                    [new ChatMessage(ChatRole.User, [new TextContent(queueMessage)])]);
            }
        }
#pragma warning restore MAAI001
        return base.GetStreamingResponseAsync(messages, options, cancellationToken);
    }
}

public static class ListeningMessageQueueChatClientExtension
{
    public static ChatClientBuilder UseListeningMessageQueue(this ChatClientBuilder builder)
    {
        return builder.Use(innerClient => new ListeningMessageQueueChatClient(innerClient));
    }
}