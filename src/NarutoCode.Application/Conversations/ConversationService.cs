using System.Runtime.CompilerServices;
using NarutoCode.Application.Agents;
using NarutoCode.Domain;
using NarutoCode.Domain.Conversations;
using NarutoCode.Domain.Entities;
using NarutoCode.Domain.Messages;

namespace NarutoCode.Application.Conversations;

/// <summary>
/// 对话应用服务，负责编排用户消息、工具审批响应、历史加载和 Agent 后续任务续跑。
/// </summary>
public class ConversationService(
    IAgentChatClient agentChatClient,
    IConversationRepository conversationRepository) : IConversationService
{
    /// <inheritdoc />
    public async Task<ConversationHistory> LoadWorkspaceHistoryAsync(
        string workDirectory,
        CancellationToken cancellationToken = default)
    {
        var conversation =
            await conversationRepository.GetOrCreateByWorkDirectoryAsync(workDirectory, cancellationToken);
        var messages = await conversationRepository.ListMessagesWithUIAsync(conversation.Id, cancellationToken);
        var historyMessages = messages.Select(ToHistoryMessage).ToArray();

        return new ConversationHistory(
            new ConversationSessionId(conversation.Id),
            historyMessages);
    }

    /// <inheritdoc />
    public async IAsyncEnumerable<AgentMessage> SendMessageAsync(
        ConversationSessionId sessionId,
        AgentMessage message,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        var nextMessage = message;
        var turnCount = 1;

        while (true)
        {
            var isAllowUserOps = false;
            var remainingTask = false;

            await foreach (var item in agentChatClient.SendMessageAsync(sessionId, nextMessage, cancellationToken))
            {
                yield return item;

                switch (item.Type)
                {
                    case AgentMessageType.ToolApprovalRequest:
                    case AgentMessageType.Plan:
                        isAllowUserOps = true;
                        break;
                    case AgentMessageType.RemainingTask:
                        isAllowUserOps = false;
                        remainingTask = true;
                        break;
                    case AgentMessageType.Error:
                    default:
                        isAllowUserOps = true;
                        break;
                }
            }

            if (isAllowUserOps)
            {
                break;
            }

            if (AppData.Config.MaxTurnCount <= turnCount)
            {
                break;
            }

            nextMessage = remainingTask
                ? new AgentMessage(
                    AgentMessageType.Content,
                    "<system-reminder> Reminder: Continue with existing tasks and use TaskUpdate to keep status current</system-reminder>", isAutoSend:true)
                : new AgentMessage(AgentMessageType.Content, "<system-reminder>continue</system-reminder>", isAutoSend:true);

            turnCount++;
        }
    }

    /// <inheritdoc />
    public Task ResetRuntimeSessionAsync(
        ConversationSessionId sessionId,
        CancellationToken cancellationToken = default)
    {
        return agentChatClient.ResetRuntimeSessionAsync(sessionId, cancellationToken);
    }

    private static ConversationHistoryMessage ToHistoryMessage(Message message)
    {
        var role = Enum.TryParse<ConversationMessageRole>(message.Role, out var parsedRole)
            ? parsedRole
            : ConversationMessageRole.assistant;

        return new ConversationHistoryMessage(
            role,
            new AgentMessage(
                message.MessageType,
                message.Content,
                message.ModelContent,
                new DateTimeOffset(message.CreatedAt)));
    }
}