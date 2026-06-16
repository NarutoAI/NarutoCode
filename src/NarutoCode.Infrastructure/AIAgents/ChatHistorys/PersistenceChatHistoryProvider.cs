using System.Text.Json.Serialization;
using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;
using NarutoCode.Domain;
using NarutoCode.Infrastructure.AIAgents.AIContextProviders;

namespace NarutoCode.Infrastructure.AIAgents.ChatHistorys;

/// <summary>
/// 聊天记录持久化提供者
/// </summary>
public class PersistenceChatHistoryProvider(
    IChatHistoryPersistenceHandler? persistenceHandler,
    IChatReducer chatReducer)
    : ChatHistoryProvider
{
    //设置状态信息
    private readonly ProviderSessionState<State> _sessionState =
        new((_) => new State(), nameof(PersistenceChatHistoryProvider));

    /// <summary>
    /// 提供当前 Agent 会话的聊天历史，并在返回前执行上下文窗口裁剪。
    /// </summary>
    /// <param name="context">Agent 调用前上下文。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>裁剪后的聊天消息集合。</returns>
    protected override async ValueTask<IEnumerable<ChatMessage>> ProvideChatHistoryAsync(InvokingContext context,
        CancellationToken cancellationToken = new CancellationToken())
    {
        var state = this._sessionState.GetOrInitializeState(context.Session);
        //消息裁剪
        state.Messages = (await chatReducer.ReduceAsync(state.Messages, cancellationToken)).ToList();
        return state.Messages;
    }

    protected override async ValueTask StoreChatHistoryAsync(InvokedContext context,
        CancellationToken cancellationToken = new CancellationToken())
    {
        var state = this._sessionState.GetOrInitializeState(context.Session);
        var responseMessages = new List<ChatMessage>(context.ResponseMessages?.Count() ?? 0);
        if (context.ResponseMessages != null)
        {
            var collectApprovalToolAiContextProvider = context.Agent.GetService<CollectApprovalToolAiContextProvider>();
            foreach (var item in context.ResponseMessages)
            {
                //重新构建上下文，用于解决审批工具审批的问题
                var newItem = item.Clone();
                newItem.Contents = [];
                for (var i = 0; i < item.Contents.Count; i++)
                {
                    var itemContent = item.Contents[i];
                    //判断是否需要审批
                    if (itemContent is FunctionCallContent {InformationalOnly: false} functionCallContent &&
                        collectApprovalToolAiContextProvider?.IsApprovalToolsNames(functionCallContent.Name) == true)
                    {
                        newItem.Contents.Add(
                            new ToolApprovalRequestContent(ComposeApprovalRequestId(functionCallContent.CallId),
                                functionCallContent));
                    }
                    else
                    {
                        newItem.Contents.Add(itemContent);
                    }
                }

                responseMessages.Add(newItem);
            }
        }
        //过滤请求消息
        var requestMessages =FilteringMessage(context.RequestMessages);
        //维护内存中的消息 这里不用 responseMessages 是因为responseMessages里面的ToolApprovalRequestContent无法被OpenAIClient识别成正确的工具调用
        var newMessages = requestMessages
            .Concat(context.ResponseMessages??[])
            .ToList();
        state.Messages.AddRange(newMessages);
        //调用持久化处理器保存新增消息
        if (persistenceHandler is not null)
        {
            await persistenceHandler.PersistAsync(
                new ChatHistoryPersistenceContext(state.SessionId,context.RequestMessages 
                    .Concat(responseMessages)
                    .ToList(), state.TotalUsage),
                cancellationToken);
        }
    }

    /// <summary>
    /// 过滤消息
    /// </summary>
    /// <param name="messages"></param>
    /// <returns></returns>
    private static List<ChatMessage> FilteringMessage(IEnumerable<ChatMessage> messages)
    {
        var chatMessages = new List<ChatMessage>();
        foreach (var message in messages)
        {
            //过滤掉从AgentRequestMessageSourceType.AIContextProvider的输入消息，这些消息不需要进行存储，因为每轮对话都会读取 同时减少历史消息的token成本
            if (message.AdditionalProperties != null
                && message.AdditionalProperties.TryGetValue(
                    AgentRequestMessageSourceAttribution.AdditionalPropertiesKey, out var messageSourceAttribution)
                && messageSourceAttribution is AgentRequestMessageSourceAttribution typedMessageSourceAttribution
                && typedMessageSourceAttribution.SourceType == AgentRequestMessageSourceType.AIContextProvider)
            {
                continue;
            }

            chatMessages.Add(message);
        }

        return chatMessages;
    }
    /// <summary>Composes an approval request ID from a function call ID.</summary>
    private static string ComposeApprovalRequestId(string callId) => $"ficc_{callId}";

    public sealed class State
    {
        public long SessionId { get; set; }

        public State()
        {
            SessionId = 0;
            Messages = new List<ChatMessage>();
        }

        /// <summary>
        /// 创建指定会话标识和历史消息的聊天历史状态。
        /// </summary>
        /// <param name="sessionId">当前对话会话标识。</param>
        /// <param name="messages">已加载的历史消息。</param>
        public State(long sessionId, List<ChatMessage> messages)
        {
            SessionId = sessionId;
            Messages = messages;
        }

        /// <summary>
        /// 消息记录
        /// </summary>
        public List<ChatMessage> Messages { get; set; }


        [JsonIgnore] public long? TotalUsage { get; set; }
    }
}