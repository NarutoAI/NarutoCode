#pragma warning disable MAAI001
using System.Runtime.CompilerServices;
using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;

namespace NarutoCode.Infrastructure.AIAgents.DelegatingAiAgent;

/// <summary>
/// 工具延续检查包装器：在 <see cref="RunCoreStreamingAsync" /> 入口扫描会话历史最后一条 Assistant 消息，
/// 当其含有未闭合的 <see cref="FunctionCallContent" /> 或 <see cref="ToolApprovalRequestContent" />
/// 而本次输入的 <c>messages</c> 中缺失对应的 <see cref="FunctionResultContent" />
/// 或 <see cref="ToolApprovalResponseContent" /> 时，自动补全工具中断/审批拒绝的结果，
/// 避免下游因 tool_call 缺少相邻 tool_result 消息而抛错。
/// </summary>
/// <param name="innerAgent">被包装的内层 Agent。</param>
public class ToolCheckAiAgent(AIAgent innerAgent) : DelegatingAIAgent(innerAgent)
{
    /// <inheritdoc />
    protected override async IAsyncEnumerable<AgentResponseUpdate> RunCoreStreamingAsync(
        IEnumerable<ChatMessage> messages,
        AgentSession? session = null,
        AgentRunOptions? options = null,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        var user = new List<ChatMessage>();

        // 读取会话内存历史，定位最新一条消息：未闭合调用只会出现在历史尾部
        var chatHistory = CurrentRunContext!.Session!.GetMessages(CurrentRunContext.Agent);
        if (chatHistory is { Count: > 0 })
        {
            var completion = BuildToolContinuationMessage(chatHistory.Last(), messages);
            if (completion is not null)
            {
                // 占位结果必须置于用户新输入之前，保证 tool_call 与 tool_result 的相邻关系
                user.Add(completion);
            }
        }

        user.AddRange(messages);
        await foreach (var item in base.RunCoreStreamingAsync(user, session, options, cancellationToken))
        {
            yield return item;
        }
    }

    /// <summary>
    /// 计算工具延续占位消息：当历史最后一条助手消息含有未闭合的工具调用或审批请求，
    /// 且本次输入缺失对应结果/响应时，返回需要前置到输入之前的占位 Tool 消息；无需补全时返回 <see langword="null" />。
    /// </summary>
    /// <param name="last">历史最后一条消息。</param>
    /// <param name="messages">本次运行的用户输入消息。</param>
    /// <returns>需要前置补全的占位消息；无需补全时为 <see langword="null" />。</returns>
    internal static ChatMessage? BuildToolContinuationMessage(ChatMessage last, IEnumerable<ChatMessage> messages)
    {
        // 只有助手消息才会发出工具调用或审批请求；同时确认该消息确实含有未闭合的工具调用或审批请求，
        // 避免为纯文本回复创建一个空的 ChatRole.Tool 消息。
        if (last.Role != ChatRole.Assistant
            || last.Contents is not { Count: > 0 }
            || !last.Contents.Any(c => c is FunctionCallContent or ToolApprovalRequestContent))
        {
            return null;
        }

        var chatmessage = new ChatMessage
        {
            Role = ChatRole.Tool,
            Contents = new List<AIContent>()
        };

        // 收集历史最后一条助手消息里发出的工具调用与审批请求
        // 工具调用以 CallId 标识，审批请求以 RequestId 标识
        var pendingCalls = new List<string>(); // 待补全的函数调用 CallId
        var pendingApprovals =
            new Dictionary<string, ToolCallContent>(); // 待补全的审批请求 RequestId -> 对应的工具调用
        foreach (var item in last.Contents)
        {
            if (item is FunctionCallContent fc)
            {
                pendingCalls.Add(fc.CallId);
            }
            else if (item is ToolApprovalRequestContent approval)
            {
                pendingApprovals[approval.RequestId] = approval.ToolCall;
            }
        }

        // 收集用户输入中已有的函数调用结果与审批响应
        var existingResults = new HashSet<string>(); // 已有的 FunctionResultContent CallId
        var existingResponses = new HashSet<string>(); // 已有的 ToolApprovalResponseContent RequestId
        foreach (var msg in messages)
        {
            foreach (var content in msg.Contents)
            {
                if (content is FunctionResultContent fr)
                {
                    existingResults.Add(fr.CallId);
                }
                else if (content is ToolApprovalResponseContent approvalResponse)
                {
                    existingResponses.Add(approvalResponse.RequestId);
                }
            }
        }

        // 用户未提供对应结果的函数调用，补全“工具执行被中断”的结果
        foreach (var callId in pendingCalls)
        {
            if (!existingResults.Contains(callId))
            {
                chatmessage.Contents.Add(new FunctionResultContent(callId, ToolAutoAdditionMessage));
            }
        }

        // 用户未提供对应响应的审批请求，补全“未获批准”的拒绝响应
        foreach (var (requestId, toolCall) in pendingApprovals)
        {
            if (!existingResponses.Contains(requestId))
            {
                chatmessage.Contents.Add(new ToolApprovalResponseContent(requestId, false, toolCall)
                {
                    Reason = "未收到审批响应，视为拒绝"
                });
            }
        }

        return chatmessage.Contents is { Count: > 0 } ? chatmessage : null;
    }

    /// <summary>
    /// 工具自动补全结果的消息内容：当历史助手消息中的工具调用未被回应时，
    /// 用此占位结果告知模型该工具执行已被中断，避免后续轮次假设工具已执行成功。
    /// </summary>
    private const string ToolAutoAdditionMessage =
        "<system-remind>\nTool execution interrupted: the run stopped before tool exec finished. Do not assume it completed; re-run it only if its work is still needed。\n</system-remind>";
}
#pragma warning restore MAAI001
