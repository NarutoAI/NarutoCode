using System.Collections.Concurrent;
using System.Runtime.CompilerServices;
using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;
using NarutoCode.Application.Agents;
using NarutoCode.Domain.Conversations;
using NarutoCode.Domain.Messages;
using NarutoCode.Infrastructure.JsonSerializerContexts;

namespace NarutoCode.Infrastructure.AIAgents;

/// <summary>
/// 基于 Microsoft Agent Framework 的 Agent 对话客户端实现。
/// </summary>
public class MafAgentChatClient : IAgentChatClient
{
    private readonly AIAgent _agent;

    private readonly ConcurrentDictionary<long, Lazy<Task<AgentSession>>> _agentSessions = new();


    private readonly IConversationRepository _conversationRepository;

    private readonly ILogger<MafAgentChatClient> _logger;

    /// <summary>
    /// 初始化 <see cref="MafAgentChatClient" /> 实例。
    /// </summary>
    /// <param name="agentFactory">Agent 工厂。</param>
    public MafAgentChatClient(IAgentFactory agentFactory,
        IConversationRepository conversationRepository, ILogger<MafAgentChatClient> logger)
    {
        ArgumentNullException.ThrowIfNull(agentFactory);

        _conversationRepository = conversationRepository;
        _logger = logger;
        _agent = agentFactory.Create();
    }

    private Task<AgentSession> GetAgentSessionAsync(ConversationSessionId sessionId)
    {
        var lazySession = _agentSessions.GetOrAdd(
            sessionId.Value,
            id => new Lazy<Task<AgentSession>>(
                () => CreateSessionAsync(new ConversationSessionId(id)),
                LazyThreadSafetyMode.ExecutionAndPublication));

        return lazySession.Value;
    }

    /// <summary>
    /// 重置会话信息，下一次重新读取 主要为了防止 取消之后，中途的工具调用没有结果导致报错
    /// </summary>
    /// <param name="sessionId"></param>
    /// <param name="cancellationToken"></param>
    /// <returns></returns>
    public Task ResetRuntimeSessionAsync(
        ConversationSessionId sessionId,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        _agentSessions.TryRemove(sessionId.Value, out _);
        return Task.CompletedTask;
    }

    private async Task<AgentSession> CreateSessionAsync(ConversationSessionId sessionId)
    {
        // 读取持久化历史时必须丢弃取消或崩溃留下的半截工具调用，否则模型会因缺少工具结果拒绝继续对话。
        var messages = await _conversationRepository.ListMessagesAsync(sessionId.Value);
        var session = await _agent.CreateSessionAsync();
        var chatMessages = new List<ChatMessage>(messages.Count);

        foreach (var item in messages.OrderBy(a => a.Id))
        {
            var itemChatMessage = new ChatMessage
            {
                Contents = AIContentJsonSerializerContext.DeserializeContents(item.ModelContent),
                Role = new ChatRole(item.Role),
            };

            NormalizeToolApprovalRequest(itemChatMessage);
            chatMessages.Add(itemChatMessage);
        }

        return session.CreateSession(sessionId, PruneIncompleteToolCalls(chatMessages));
    }

    /// <summary>
    /// 将持久化的审批请求恢复为框架期望的工具调用内容。
    /// </summary>
    /// <param name="message">需要恢复的聊天消息。</param>
    private static void NormalizeToolApprovalRequest(ChatMessage message)
    {
        if (message.Contents is not {Count: > 0})
        {
            return;
        }

        for (var i = 0; i < message.Contents.Count; i++)
        {
            // 审批请求历史在恢复 Agent 会话时必须转换回原始工具调用，否则框架会找不到对应工具输出。
            if (message.Contents[i] is ToolApprovalRequestContent toolApprovalRequestContent)
            {
                message.Contents[i] = toolApprovalRequestContent.ToolCall;
            }
        }
    }

    /// <summary>
    /// 裁剪取消或异常中断后遗留的未完成工具调用，避免下一轮恢复会话时报缺少工具输出。
    /// </summary>
    /// <param name="messages">按历史顺序排列的聊天消息。</param>
    /// <returns>可安全恢复给 Agent Framework 的历史消息。</returns>
    private static List<ChatMessage> PruneIncompleteToolCalls(List<ChatMessage> messages)
    {
        if (messages.Count == 0)
        {
            return messages;
        }

        var unresolvedCallIds = new HashSet<string>(StringComparer.Ordinal);
        var firstUnresolvedIndex = -1;

        for (var index = 0; index < messages.Count; index++)
        {
            var message = messages[index];
            if (message.Contents is not {Count: > 0})
            {
                continue;
            }

            foreach (var content in message.Contents)
            {
                if (content is FunctionCallContent functionCallContent)
                {
                    // 记录未完成工具调用的起点，后续没有匹配结果时需要整体裁剪。
                    unresolvedCallIds.Add(functionCallContent.CallId);
                    if (firstUnresolvedIndex < 0)
                    {
                        firstUnresolvedIndex = index;
                    }

                    continue;
                }

                if (content is FunctionResultContent functionResultContent)
                {
                    unresolvedCallIds.Remove(functionResultContent.CallId);
                    if (unresolvedCallIds.Count == 0)
                    {
                        firstUnresolvedIndex = -1;
                    }
                }
            }
        }

        if (firstUnresolvedIndex < 0 || unresolvedCallIds.Count == 0)
        {
            return messages;
        }

        //从起点处移除
        return messages.Take(firstUnresolvedIndex).ToList();
    }

    /// <inheritdoc />
    public async IAsyncEnumerable<AgentMessage> SendMessageAsync(
        ConversationSessionId sessionId,
        AgentMessage message,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        ChatMessage? chatMessage = null;
        AgentSession? agentSession = null;
        Exception? initializationException = null;

        try
        {
            chatMessage = await CreateChatMessageAsync(message);
            agentSession = await GetAgentSessionAsync(sessionId).WaitAsync(cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            _agentSessions.TryRemove(sessionId.Value, out _);
            throw;
        }
        catch (Exception exception)
        {
            _agentSessions.TryRemove(sessionId.Value, out _);
            initializationException = exception;
        }

        if (initializationException is not null)
        {
            _logger.LogError(initializationException,"Agent 会话初始化失败");
            yield return new AgentMessage(
                AgentMessageType.Error,
                $"Agent 会话初始化失败：{initializationException.Message}");
            yield break;
        }

        var currentChatMessage = chatMessage!;
        var currentAgentSession = agentSession!;
        await using var enumerator = _agent.RunStreamingAsync(
                currentChatMessage,
                currentAgentSession,
                cancellationToken: cancellationToken)
            .GetAsyncEnumerator(cancellationToken);

        while (true)
        {
            AgentResponseUpdate? item = null;
            Exception? streamingException = null;
            var hasNext = false;

            try
            {
                hasNext = await enumerator.MoveNextAsync();
                if (hasNext)
                {
                    item = enumerator.Current;
                }
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                _agentSessions.TryRemove(sessionId.Value, out _);
                throw;
            }
            catch (Exception exception)
            {
                streamingException = exception;
            }

            if (streamingException is not null)
            {
                _logger.LogError(exception:streamingException,"Agent 执行失败");
                yield return new AgentMessage(
                    AgentMessageType.Error,
                    $"Agent 执行失败：{streamingException.Message}");
                yield break;
            }

            if (!hasNext)
            {
                break;
            }

            var reasoningContent = item!.Contents?.OfType<TextReasoningContent>().FirstOrDefault();
            if (reasoningContent is not null && !string.IsNullOrWhiteSpace(reasoningContent.Text))
            {
                yield return new(AgentMessageType.Thinking, reasoningContent.Text);
                continue;
            }

            var functionCallContent = item.Contents?.OfType<FunctionCallContent>().FirstOrDefault();
            if (functionCallContent is not null)
            {
                yield return new(AgentMessageType.ToolCall, $"{functionCallContent.Name}");
                continue;
            }

            var toolApprovalRequestContent = item.Contents?.OfType<ToolApprovalRequestContent>().FirstOrDefault();
            if (toolApprovalRequestContent != null)
            {
                if (toolApprovalRequestContent.ToolCall is FunctionCallContent functionCallContentApproval)
                {
                    yield return new(AgentMessageType.ToolApprovalRequest,
                        $"{functionCallContentApproval.Name}({string.Join(',', functionCallContentApproval.Arguments ?? new Dictionary<string, object?>())})",
                        toolApprovalContent: AIContentJsonSerializerContext.SerializeToolApprovalRequestContent(
                            toolApprovalRequestContent));
                }

                yield break;
            }

            var errorContent = item.Contents?.OfType<ErrorContent>().FirstOrDefault();
            if (errorContent is not null)
            {
                yield return new(AgentMessageType.Error, errorContent.Message);
                continue;
            }

            //更新当前会话的使用量
            var usageContent = item.Contents?.OfType<UsageContent>().FirstOrDefault();
            if (usageContent != null)
            {
                agentSession!.SetSessionUsage(usageContent);
                yield return new(AgentMessageType.Usage,
                    usageContent.Details.TotalTokenCount.GetValueOrDefault().ToString());
            }
            else if (!string.IsNullOrEmpty(item.Text))
            {
                yield return new(AgentMessageType.Content, item.Text);
            }
        }
    }

    private async Task<ChatMessage> CreateChatMessageAsync(AgentMessage message)
    {
        return message.Type switch
        {
            AgentMessageType.Content => await CreateUserInputMessageAsync(message),
            AgentMessageType.ToolApprovalResponse => CreateToolApprovalResponseMessage(message),
            _ => throw new InvalidOperationException($"消息类型 {message.Type} 不能作为用户输入发送给 Agent。")
        };
    }

    /// <summary>
    /// 创建真实用户输入消息，并通过扩展属性与框架内部补充的 user 消息区分。
    /// </summary>
    /// <param name="message">用户输入消息。</param>
    /// <returns>带有用户输入标记的聊天消息。</returns>
    private static async Task<ChatMessage> CreateUserInputMessageAsync(AgentMessage message)
    {
        var chatMessage = message.Attachments.Count == 0
            ? new ChatMessage(ChatRole.User, message.Content)
            : await CreateUserInputMessageWithAttachmentsAsync(message);
        
        chatMessage.AdditionalProperties = new AdditionalPropertiesDictionary
        {
            [ChatMessageAdditionalPropertyNames.IsUserInput] = true
        };
        return chatMessage;
    }

    /// <summary>
    /// 创建用户图片附件信息
    /// </summary>
    /// <param name="message"></param>
    /// <returns></returns>
    private static async Task<ChatMessage> CreateUserInputMessageWithAttachmentsAsync(AgentMessage message)
    {
        var contents = new List<AIContent>();
        if (!string.IsNullOrWhiteSpace(message.Content))
        {
            contents.Add(new TextContent(message.Content));
        }

        foreach (var attachment in message.Attachments)
        {
            await using (FileStream fileStream = new FileStream(attachment.FilePath, FileMode.Open, FileAccess.Read))
            {
                contents.Add(await DataContent.LoadFromAsync(fileStream));
            }
        }

        return new ChatMessage(ChatRole.User, contents);
    }

    private ChatMessage CreateToolApprovalResponseMessage(AgentMessage message)
    {
        if (string.IsNullOrWhiteSpace(message.ToolApprovalContent))
        {
            throw new InvalidOperationException("工具审批响应的 CallId 无效。");
        }

        var toolApprovalRequest =
            AIContentJsonSerializerContext.DeserializeToolApprovalRequestContent(message.ToolApprovalContent);
        if (toolApprovalRequest is null)
        {
            throw new InvalidOperationException($"未找到工具审批上下文：{message.ToolApprovalContent}。");
        }

        var response = toolApprovalRequest.CreateResponse(message.Content.Trim() == "1");
        return new ChatMessage(ChatRole.User, [response])
        {
            AdditionalProperties = new AdditionalPropertiesDictionary
            {
                [ChatMessageAdditionalPropertyNames.IsUserInput] = true
            }
        };
    }
}