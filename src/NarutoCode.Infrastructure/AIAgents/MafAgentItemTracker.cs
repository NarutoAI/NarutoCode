using System.Text;
using Microsoft.Extensions.Logging;
using NarutoCode.Domain.Conversations;
using NarutoCode.Domain.Messages;
using NarutoCode.Infrastructure.JsonSerializerContexts;
using NarutoCode.Infrastructure.Stores;

namespace NarutoCode.Infrastructure.AIAgents;

/// <summary>
/// MAF Agent 流式内容到 UI Item（agent_session_items）的聚合器。
/// 参照 turn 过程把流式增量聚合为独立的结构化 Item：
/// reasoning 与 agentMessage 段互斥（新段开始先关闭活跃段）、工具调用按 CallId 关联参数与结果。
/// 完成态 Item 构造后经 <see cref="SqliteSessionItemWriter" /> 落库；
/// 面向 UI 的独立通道，与面向模型的 PersistenceChatHistoryProvider / ConversationRepositoryCoordinator 无耦合。
/// </summary>
/// <param name="sessionId">会话 ID。</param>
/// <param name="itemWriter">Item 写入器。</param>
/// <param name="logger">日志器。</param>
internal sealed class MafAgentItemTracker(
    long sessionId,
    SqliteSessionItemWriter itemWriter,
    ILogger logger)
{
    /// <summary>活跃思考（推理）段的聚合文本；null 表示当前无活跃段。</summary>
    private StringBuilder? _reasoningText;

    /// <summary>活跃助手正文段的聚合文本；null 表示当前无活跃段。</summary>
    private StringBuilder? _messageText;

    /// <summary>未闭合的工具调用：CallId → (工具名, 参数文本)。</summary>
    private readonly Dictionary<string, (string ToolName, string Arguments)> _openToolCalls = new();

    /// <summary>
    /// 落库用户真实输入（kind=userMessage，完整态即时写入）。
    /// </summary>
    /// <param name="text">用户输入文本。</param>
    /// <param name="attachments">图片附件集合。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    public async Task RecordUserInputAsync(
        string text,
        IReadOnlyList<AgentMessageAttachment> attachments,
        CancellationToken cancellationToken)
    {
        // 附件以原始字节持久化，历史重载后图片可直接还原渲染
        var payloadAttachments = attachments.Count == 0
            ? null
            : attachments.Select(a => new AgentMessageAttachment(a.Data, a.MediaType)).ToArray();
        await InsertAsync(
            ConversationItemKinds.UserMessage,
            ConversationItemStatuses.Succeeded,
            new UserMessageItemPayload(text, payloadAttachments),
            cancellationToken);
    }

    /// <summary>
    /// 聚合一段思考（推理）增量；无活跃段时开启新段（先关闭活跃正文段保持互斥）。
    /// </summary>
    /// <param name="text">增量文本。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    public async Task OnReasoningDeltaAsync(string text, CancellationToken cancellationToken)
    {
        // 思考与正文互斥：切段时先关闭对方，保持段独立
        if (_messageText is not null)
        {
            await CloseMessageSegmentAsync(cancellationToken);
        }

        _reasoningText ??= new StringBuilder();
        _reasoningText.Append(text);
    }

    /// <summary>
    /// 聚合一段助手正文增量；无活跃段时开启新段（先关闭活跃思考段保持互斥）。
    /// </summary>
    /// <param name="text">增量文本。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    public async Task OnMessageDeltaAsync(string text, CancellationToken cancellationToken)
    {
        if (_reasoningText is not null)
        {
            await CloseReasoningSegmentAsync(cancellationToken);
        }

        _messageText ??= new StringBuilder();
        _messageText.Append(text);
    }

    /// <summary>
    /// 记录一次工具调用开始（登记工具名与参数，等待结果回填后落库）。
    /// 先关闭活跃思考/正文段，保证 Item 落库顺序与真实时间线一致（正文段先于工具调用）。
    /// ask_user 交互工具由调用方跳过（userInteraction Item 承载）。
    /// </summary>
    /// <param name="callId">工具调用标识（FunctionCallContent.CallId）。</param>
    /// <param name="toolName">工具名称。</param>
    /// <param name="arguments">原始调用参数字典。</param>
    public async Task OnToolCallAsync(string callId, string toolName, IDictionary<string, object?>? arguments)
    {
        // 工具调用开始即切段：未闭合的思考/正文段按成功态先行落库
        await CloseReasoningSegmentAsync(CancellationToken.None);
        await CloseMessageSegmentAsync(CancellationToken.None);

        // 同一 CallId 因流式更新重复到达时只登记一次
        _openToolCalls.TryAdd(callId, (toolName, FormatArguments(arguments)));
    }

    /// <summary>
    /// 回填工具执行结果并落库对应 toolCall Item；无关联的未闭合调用时忽略。
    /// </summary>
    /// <param name="callId">工具调用标识。</param>
    /// <param name="result">FunctionResultContent.Result 原始对象。</param>
    /// <param name="exception">工具执行异常；成功时为 <see langword="null" />。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    public async Task OnToolResultAsync(
        string callId,
        object? result,
        Exception? exception,
        CancellationToken cancellationToken)
    {
        if (!_openToolCalls.Remove(callId, out var call))
        {
            return;
        }

        // 工具执行失败以 failed 状态落库，便于历史渲染区分成功/失败卡片
        var status = exception is null
            ? ConversationItemStatuses.Succeeded
            : ConversationItemStatuses.Failed;
        await InsertAsync(
            ConversationItemKinds.ToolCall,
            status,
            new ToolCallItemPayload(call.ToolName, call.Arguments, FormatValue(result)),
            cancellationToken);
    }

    /// <summary>
    /// 查询未闭合工具调用对应的工具名；无登记记录返回 <see langword="null" />。
    /// 供调用方在 FunctionResult 到达时判断是否为 ask_user 交互工具的结果。
    /// </summary>
    /// <param name="callId">工具调用标识。</param>
    /// <returns>工具名称；未登记返回 <see langword="null" />。</returns>
    internal string? ResolveToolName(string callId)
    {
        return _openToolCalls.TryGetValue(callId, out var call) ? call.ToolName : null;
    }

    /// <summary>
    /// 落库工具审批请求卡（kind=toolApprovalRequest），并先按成功关闭活跃段（审批后对话暂停）。
    /// </summary>
    /// <param name="toolName">被审批的工具名称。</param>
    /// <param name="approvalContent">审批请求 JSON（响应审批时回传）。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    public async Task OnToolApprovalRequestAsync(
        string toolName,
        string approvalContent,
        CancellationToken cancellationToken)
    {
        await CloseActiveAsync(failed: false, cancellationToken);
        await InsertAsync(
            ConversationItemKinds.ToolApprovalRequest,
            ConversationItemStatuses.Succeeded,
            new ToolApprovalRequestItemPayload(toolName, approvalContent),
            cancellationToken);
    }

    /// <summary>
    /// 落库错误 Item（kind=error），并按失败关闭所有活跃段与未闭合工具调用。
    /// </summary>
    /// <param name="message">错误描述文本。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    public async Task OnErrorAsync(string message, CancellationToken cancellationToken)
    {
        await CloseActiveAsync(failed: true, cancellationToken);
        await InsertAsync(
            ConversationItemKinds.Error,
            ConversationItemStatuses.Failed,
            new ErrorItemPayload(message),
            cancellationToken);
    }

    /// <summary>
    /// 流结束或中断时关闭所有活跃状态并落库：
    /// 聚合中的思考/正文段按成功态写入；未收到结果的工具调用按 <paramref name="failed" /> 决定状态（result 为空）。
    /// </summary>
    /// <param name="failed">流是否因异常/取消中断（未闭合工具调用将标记为已取消）。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    public async Task CloseActiveAsync(bool failed, CancellationToken cancellationToken)
    {
        await CloseReasoningSegmentAsync(cancellationToken);
        await CloseMessageSegmentAsync(cancellationToken);

        // 未闭合工具调用：流中断导致没有 FunctionResult，result 允许为空（与 eWorld 行为一致）
        var toolStatus = failed ? ConversationItemStatuses.Cancelled : ConversationItemStatuses.Succeeded;
        foreach (var call in _openToolCalls.Values)
        {
            await InsertAsync(
                ConversationItemKinds.ToolCall,
                toolStatus,
                new ToolCallItemPayload(call.ToolName, call.Arguments, Result: null),
                cancellationToken);
        }

        _openToolCalls.Clear();
    }

    /// <summary>关闭活跃思考段并落库。</summary>
    private async Task CloseReasoningSegmentAsync(CancellationToken cancellationToken)
    {
        if (_reasoningText is not { } text)
        {
            return;
        }

        _reasoningText = null;
        await InsertAsync(
            ConversationItemKinds.Reasoning,
            ConversationItemStatuses.Succeeded,
            new ReasoningItemPayload(text.ToString()),
            cancellationToken);
    }

    /// <summary>关闭活跃正文段并落库。</summary>
    private async Task CloseMessageSegmentAsync(CancellationToken cancellationToken)
    {
        if (_messageText is not { } text)
        {
            return;
        }

        _messageText = null;
        await InsertAsync(
            ConversationItemKinds.AgentMessage,
            ConversationItemStatuses.Succeeded,
            new AgentMessageItemPayload(text.ToString()),
            cancellationToken);
    }

    /// <summary>
    /// 写入一条 Item；落库异常吞掉并记日志（UI 历史缺失不应中断对话主链路）。
    /// </summary>
    /// <param name="kind">Item 类型。</param>
    /// <param name="status">Item 状态。</param>
    /// <param name="payload">强类型载荷。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    private async Task InsertAsync(string kind, string status, object payload, CancellationToken cancellationToken)
    {
        try
        {
            await itemWriter.InsertAsync(sessionId, kind, status, payload, cancellationToken);
        }
        catch (Exception exception)
        {
            // UI 投影写入失败只记日志：对话继续，历史加载时该 Item 缺失可接受
            logger.LogError(exception, "写入会话 Item 失败：{Kind}（会话 {SessionId}）", kind, sessionId);
        }
    }
    
    /// <summary>
    /// 格式化工具调用参数为展示文本：key=value 逗号拼接（手写拼接避免 AOT 下多态 JSON 问题）。
    /// </summary>
    /// <param name="arguments">原始参数字典。</param>
    /// <returns>参数文本；空参数返回空字符串。</returns>
    private static string FormatArguments(IDictionary<string, object?>? arguments)
    {
        if (arguments is null || arguments.Count == 0)
        {
            return string.Empty;
        }

        return string.Join(", ", arguments.Select(pair => $"{pair.Key}={FormatValue(pair.Value) ?? "null"}"));
    }

    /// <summary>
    /// 把工具结果/参数值转为展示文本。
    /// </summary>
    /// <param name="value">原始值。</param>
    /// <returns>展示文本；null 返回 <see langword="null" />。</returns>
    private static string? FormatValue(object? value) => value switch
    {
        null => null,
        string text => text,
        bool flag => flag ? "true" : "false",
        _ => value.ToString()
    };
}
