using System.Runtime.CompilerServices;
using NarutoCode.Application.Agents;
using NarutoCode.Domain;
using NarutoCode.Domain.Conversations;
using NarutoCode.Domain.Entities;
using NarutoCode.Domain.Messages;
using NarutoCode.Domain.Workspaces;

namespace NarutoCode.Application.Conversations;

/// <summary>
/// 对话应用服务，负责编排用户消息、工具审批响应、历史加载和 Agent 后续任务续跑。
/// </summary>
public class ConversationService(
    IAgentChatClient agentChatClient,
    IConversationRepository conversationRepository) : IConversationService
{
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
            historyMessages,
            conversation.TokenCount);
    }


    public async Task<IReadOnlyList<ConversationSummary>> ListWorkspaceConversationsAsync(
        string workDirectory,
        CancellationToken cancellationToken = default)
    {
        return await conversationRepository.ListByWorkDirectoryAsync(workDirectory, cancellationToken);
    }


    public async Task<ConversationHistory> CreateWorkspaceConversationAsync(
        string workDirectory,
        CancellationToken cancellationToken = default)
    {
        var conversation = await conversationRepository.CreateForWorkDirectoryAsync(workDirectory, cancellationToken);
        return new ConversationHistory(
            new ConversationSessionId(conversation.Id),
            [],
            conversation.TokenCount);
    }

    /// <inheritdoc />
    public async Task<ConversationHistory> LoadConversationHistoryAsync(
        ConversationSessionId conversationId,
        CancellationToken cancellationToken = default)
    {
        var conversation = await conversationRepository.GetByIdAsync(conversationId.Value, cancellationToken)
                           ?? throw new InvalidOperationException($"会话不存在：{conversationId.Value}");
        var messages = await conversationRepository.ListMessagesWithUIAsync(conversation.Id, cancellationToken);
        var historyMessages = messages.Select(ToHistoryMessage).ToArray();

        return new ConversationHistory(
            new ConversationSessionId(conversation.Id),
            historyMessages,
            conversation.TokenCount);
    }

    /// <inheritdoc />
    public async IAsyncEnumerable<AgentMessage> SendMessageAsync(
        ConversationSessionId sessionId,
        AgentMessage message,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        await foreach (var item in agentChatClient.SendMessageAsync(sessionId, message, cancellationToken))
        {
            yield return item;
        }
    }

    /// <inheritdoc />
    public Task ResetRuntimeSessionAsync(
        ConversationSessionId sessionId,
        CancellationToken cancellationToken = default)
    {
        return agentChatClient.ResetRuntimeSessionAsync(sessionId, cancellationToken);
    }

    /// <inheritdoc />
    public Task<IReadOnlyList<WorkspaceSummary>> ListWorkspacesAsync(
        CancellationToken cancellationToken = default)
    {
        // 直接委托给仓储聚合查询，保持服务层薄透传
        return conversationRepository.ListWorkspacesAsync(cancellationToken);
    }

    /// <inheritdoc />
    public async Task<OpenWorkspaceResult> OpenWorkspaceAsync(
        string workDirectory,
        CancellationToken cancellationToken = default)
    {
        // 规范化为绝对路径，确保跨宿主一致
        var normalizedPath = WorkspacePath.Normalize(workDirectory);
        // 查询该工作区下已有会话，取最近一条
        var existing = (await conversationRepository.ListByWorkDirectoryAsync(normalizedPath, cancellationToken))
            .FirstOrDefault();

        // 存在会话则加载历史，不存在则创建首个会话
        var history = existing is null
            ? await CreateWorkspaceConversationAsync(normalizedPath, cancellationToken)
            : await LoadConversationHistoryAsync(new ConversationSessionId(existing.Id), cancellationToken);

        return new OpenWorkspaceResult(history, existing is null);
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