using System.Collections.Concurrent;
using System.Runtime.CompilerServices;
using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;
using NarutoCode.Application.Agents;
using NarutoCode.Domain;
using NarutoCode.Domain.Configurations;
using NarutoCode.Domain.Configurations.Settings;
using NarutoCode.Domain.Conversations;
using NarutoCode.Domain.Messages;
using NarutoCode.Infrastructure.JsonSerializerContexts;
using NarutoCode.Infrastructure.Vision;

namespace NarutoCode.Infrastructure.AIAgents;

/// <summary>
/// 基于 Microsoft Agent Framework 的 Agent 对话客户端实现。
/// </summary>
public class MafAgentChatClient : IAgentChatClient
{
    private readonly IAgentFactory _agentFactory;

    private readonly IConversationRepository _conversationRepository;

    private readonly ILogger<MafAgentChatClient> _logger;

    private readonly ILlmSettingsService _llmSettingsService;

    /// <summary>
    /// 初始化 <see cref="MafAgentChatClient" /> 实例。
    /// </summary>
    /// <param name="agentFactory">Agent 工厂。</param>
    /// <param name="llmSettingsService">当前主模型设置服务，用于判断主模型是否支持视觉。</param>
    public MafAgentChatClient(IAgentFactory agentFactory,
        IConversationRepository conversationRepository,
        ILlmSettingsService llmSettingsService,
        ILogger<MafAgentChatClient> logger)
    {
        ArgumentNullException.ThrowIfNull(agentFactory);
        ArgumentNullException.ThrowIfNull(llmSettingsService);

        _agentFactory = agentFactory;
        _conversationRepository = conversationRepository;
        _llmSettingsService = llmSettingsService;
        _logger = logger;
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
        _agentFactory.ResetCurrentConversation(sessionId);
        return Task.CompletedTask;
    }

    private async Task<AgentSession> CreateSessionAsync(
        AIAgent agent,
        ConversationSessionId sessionId,
        ChatMessage pendingMessage,
        CancellationToken cancellationToken)
    {
        var messages = await LoadSessionHistoryMessagesAsync(_conversationRepository, sessionId, cancellationToken);

        // 读取会话实体，获取数据库记录的最近一次输入 token 用量
        var conversation = await _conversationRepository.GetByIdAsync(sessionId.Value, cancellationToken);

        var session = await agent.CreateSessionAsync(cancellationToken);
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

        return session.CreateSession(sessionId,
            PruneIncompleteToolCalls(chatMessages, pendingMessage),
            conversation?.LastInputTokenCount);
    }

    /// <summary>
    /// 读取恢复 Agent 会话所需的历史消息，优先使用已裁剪的 LLM 运行时上下文。
    /// </summary>
    /// <param name="conversationRepository">对话仓储。</param>
    /// <param name="sessionId">会话标识。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>用于恢复 Agent 会话的历史消息。</returns>
    internal static async Task<IReadOnlyList<Domain.Entities.Message>> LoadSessionHistoryMessagesAsync(
        IConversationRepository conversationRepository,
        ConversationSessionId sessionId,
        CancellationToken cancellationToken = default)
    {
        // 读取持久化历史时优先使用已裁剪的 LLM 运行时上下文，避免重启后从 UI 完整历史重复裁剪。
        var messages = await conversationRepository.ListRuntimeMessagesAsync(sessionId.Value, cancellationToken);
        if (messages.Count > 0)
        {
            return messages;
        }

        // 兼容旧版本数据库：首次升级后 runtime 表为空时，回退读取原历史，后续持久化会写入 runtime 表。
        return await conversationRepository.ListMessagesAsync(sessionId.Value, cancellationToken);
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
    /// <param name="pendingMessage">当前待发送的消息，用于识别尚未写入历史的审批响应。</param>
    /// <returns>可安全恢复给 Agent Framework 的历史消息。</returns>
    internal static List<ChatMessage> PruneIncompleteToolCalls(
        List<ChatMessage> messages,
        ChatMessage? pendingMessage = null)
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

                    continue;
                }

                if (content is ToolApprovalResponseContent toolApprovalResponseContent)
                {
                    // 审批响应同样代表对应工具调用已完成，不能将审批前的 FunctionCallContent 裁剪掉。
                    unresolvedCallIds.Remove(toolApprovalResponseContent.ToolCall.CallId);
                    if (unresolvedCallIds.Count == 0)
                    {
                        firstUnresolvedIndex = -1;
                    }
                }
            }
        }

        if (pendingMessage?.Contents is {Count: > 0})
        {
            foreach (var content in pendingMessage.Contents.OfType<ToolApprovalResponseContent>())
            {
                // 当前审批响应尚未进入历史，但它会闭合对应的工具调用，需参与裁剪判断。
                unresolvedCallIds.Remove(content.ToolCall.CallId);
            }

            if (unresolvedCallIds.Count == 0)
            {
                firstUnresolvedIndex = -1;
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
        IConversationAgentLease? lease = null;
        Exception? initializationException = null;

        try
        {
            chatMessage = await CreateChatMessageAsync(message, cancellationToken);
            lease = await _agentFactory.AcquireCurrentConversationAsync(sessionId, cancellationToken);
            lease.Session ??= await CreateSessionAsync(
                lease.Agent,
                sessionId,
                chatMessage,
                cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            lease?.Invalidate();
            throw;
        }
        catch (Exception exception)
        {
            lease?.Invalidate();
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

        await using var currentLease = lease!;
        var currentChatMessage = chatMessage!;
        var currentAgentSession = currentLease.Session!;
        await using var enumerator = currentLease.Agent.RunStreamingAsync(
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
                currentLease.Invalidate();
                throw;
            }
            catch (Exception exception)
            {
                streamingException = exception;
            }

            if (streamingException is not null)
            {
                currentLease.Invalidate();
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
                // ask_user 工具由 TUI 的结构化问卷卡片负责展示，不能再泄露内部工具名。
                if (!IsUserInteractionFunction(functionCallContent.Name))
                {
                    yield return new AgentMessage(AgentMessageType.ToolCall, functionCallContent.Name);
                }

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
                currentAgentSession.SetSessionUsage(usageContent);
                yield return new(AgentMessageType.Usage,
                    usageContent.Details.TotalTokenCount.GetValueOrDefault().ToString());
            }
            else if (!string.IsNullOrEmpty(item.Text))
            {
                yield return new(AgentMessageType.Content, item.Text);
            }
        }
    }

    /// <summary>
    /// 判断函数调用是否为需要以问答卡片展示的用户交互工具。
    /// </summary>
    /// <param name="functionName">函数名称。</param>
    /// <returns>属于 ask_user 工具时返回 <see langword="true" />。</returns>
    private static bool IsUserInteractionFunction(string functionName)
    {
        return functionName is "narutocode_ask_user_question";
    }

    private async Task<ChatMessage> CreateChatMessageAsync(
        AgentMessage message,
        CancellationToken cancellationToken)
    {
        return message.Type switch
        {
            AgentMessageType.Content => await CreateUserInputMessageAsync(message, cancellationToken),
            AgentMessageType.ToolApprovalResponse => CreateToolApprovalResponseMessage(message),
            _ => throw new InvalidOperationException($"消息类型 {message.Type} 不能作为用户输入发送给 Agent。")
        };
    }

    /// <summary>
    /// 创建真实用户输入消息，并通过扩展属性与框架内部补充的 user 消息区分。
    /// 主模型不支持视觉且独立视觉配置有效时，附件图片先经小视觉模型解析为文本再发送。
    /// </summary>
    /// <param name="message">用户输入消息。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>带有用户输入标记的聊天消息。</returns>
    private async Task<ChatMessage> CreateUserInputMessageAsync(
        AgentMessage message,
        CancellationToken cancellationToken)
    {
        var chatMessage = message.Attachments.Count == 0
            ? new ChatMessage(ChatRole.User, message.Content)
            : await CreateUserInputMessageWithAttachmentsAsync(message, cancellationToken);

        chatMessage.AdditionalProperties = new AdditionalPropertiesDictionary
        {
            [ChatMessageAdditionalPropertyNames.IsUserInput] = true
        };
        return chatMessage;
    }

    /// <summary>
    /// 创建用户图片附件消息：视觉主模型直接携带图片内容；
    /// 纯文本主模型在独立视觉配置有效时，先把附件解析为文本再发送给主模型。
    /// </summary>
    /// <param name="message">用户输入消息。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>携带图片或图片解析文本的用户消息。</returns>
    private async Task<ChatMessage> CreateUserInputMessageWithAttachmentsAsync(
        AgentMessage message,
        CancellationToken cancellationToken)
    {
        // 纯文本主模型 + 有效独立视觉配置：附件先经小视觉模型转成文本，主模型只接收文本
        if (NeedsVisionPreprocessing(_llmSettingsService.CurrentLlm.SupportsVision, AppData.Config.Vision))
        {
            var content = await RecognizeAttachmentsAsTextAsync(
                message.Content,
                message.Attachments,
                new VisionChatClient(AppData.Config.Vision!),
                cancellationToken).ConfigureAwait(false);
            return new ChatMessage(ChatRole.User, content);
        }

        var contents = new List<AIContent>();
        if (!string.IsNullOrWhiteSpace(message.Content))
        {
            contents.Add(new TextContent(message.Content));
        }

        foreach (var attachment in message.Attachments)
        {
            // 附件以内存字节承载，直接构造 DataContent，无需文件路径
            contents.Add(new DataContent(attachment.Data, attachment.MediaType));
        }

        return new ChatMessage(ChatRole.User, contents);
    }

    /// <summary>
    /// 判断用户图片附件是否需要独立视觉模型预处理：主模型不支持视觉且视觉配置有效。
    /// </summary>
    /// <param name="supportsVision">当前主模型是否支持视觉。</param>
    /// <param name="vision">独立视觉模型配置。</param>
    /// <returns>需要预处理时返回 <see langword="true" />。</returns>
    internal static bool NeedsVisionPreprocessing(bool supportsVision, VisionConfiguration? vision)
    {
        return !supportsVision && vision is { IsValid: true };
    }

    /// <summary>
    /// 逐张调用独立视觉模型解析图片附件，拼接为纯文本主模型可消费的用户消息文本；
    /// 单张识别失败降级为占位文本，不影响其余图片与原用户输入。
    /// </summary>
    /// <param name="content">用户原始文本输入，可为空。</param>
    /// <param name="attachments">图片附件集合。</param>
    /// <param name="visionClient">独立视觉模型客户端。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>原文本与各图片解析结果拼接后的消息文本。</returns>
    internal static async Task<string> RecognizeAttachmentsAsTextAsync(
        string content,
        IReadOnlyList<AgentMessageAttachment> attachments,
        IVisionChatClient visionClient,
        CancellationToken cancellationToken)
    {
        var sections = new List<string>();
        var userText = content.Trim();
        if (userText.Length > 0)
        {
            sections.Add(userText);
        }

        // 串行识别：保证多图顺序稳定，并避免视觉端点并发限流
        for (var index = 0; index < attachments.Count; index++)
        {
            var attachment = attachments[index];
            // 用户文本作为识别上下文，帮助视觉模型聚焦与问题相关的内容
            var prompt = userText.Length == 0
                ? "请准确描述图片中的关键信息、可读文字（OCR）、界面元素和与用户问题相关的内容。"
                : $"用户消息：{userText}\n请结合上述用户消息，准确描述图片中的关键信息、可读文字（OCR）、界面元素和相关内容。";

            try
            {
                var description = await visionClient
                    .RecognizeAsync(attachment.Data, attachment.MediaType, prompt, cancellationToken)
                    .ConfigureAwait(false);
                sections.Add($"[图片 {index + 1} 解析]\n{description.Trim()}");
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex)
            {
                // 单张失败保留占位文本，原用户输入与其余图片继续发送
                sections.Add($"[图片 {index + 1} 解析失败]\n{ex.Message}");
            }
        }

        return string.Join("\n\n", sections);
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