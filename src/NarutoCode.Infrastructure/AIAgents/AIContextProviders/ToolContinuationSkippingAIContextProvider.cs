using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;

namespace NarutoCode.Infrastructure.AIAgents.AIContextProviders;

/// <summary>
/// 在工具审批响应或工具结果回合跳过上下文注入，避免破坏模型工具调用协议要求的消息相邻性。
/// 为了解决 https://platform.claude.com/docs/en/agents-and-tools/tool-use/handle-tool-calls 问题
/// </summary>
public sealed class ToolContinuationSkippingAiContextProvider : AIContextProvider
{
    private readonly AIContextProvider _innerProvider;

    /// <summary>
    /// 创建工具延续回合跳过包装器。
    /// </summary>
    /// <param name="innerProvider">需要按条件执行的内部上下文提供器。</param>
    private ToolContinuationSkippingAiContextProvider(AIContextProvider innerProvider)
    {
        ArgumentNullException.ThrowIfNull(innerProvider);
        _innerProvider = innerProvider;
    }

    public override IReadOnlyList<string> StateKeys => _innerProvider.StateKeys;

    /// <summary>
    /// 包装指定上下文提供器，使其在工具延续回合不追加额外消息、工具或指令。
    /// </summary>
    /// <param name="provider">需要包装的上下文提供器。</param>
    /// <returns>具备工具延续回合保护的上下文提供器。</returns>
    public static AIContextProvider Wrap(AIContextProvider provider)
    {
        return new ToolContinuationSkippingAiContextProvider(provider);
    }

    protected override async ValueTask<AIContext> InvokingCoreAsync(
        InvokingContext context,
        CancellationToken cancellationToken = default)
    {
        var aiContext=await _innerProvider.InvokingAsync(context, cancellationToken);
        //如果存在工具审批的话就不允许拼接 message
        if (ContainsToolContinuationContent(context.AIContext.Messages))
        {
            aiContext.Messages = [];
        }

        return aiContext;
    }

    protected override ValueTask InvokedCoreAsync(
        InvokedContext context,
        CancellationToken cancellationToken = default)
    {
        return _innerProvider.InvokedAsync(context, cancellationToken);
    }

    public override object? GetService(Type serviceType, object? serviceKey = null)
    {
        return _innerProvider.GetService(serviceType, serviceKey);
    }

    /// <summary>
    /// 判断是否为 工具审批的的结果消息
    /// </summary>
    /// <param name="messages"></param>
    /// <returns></returns>
    private static bool ContainsToolContinuationContent(IEnumerable<ChatMessage>? messages)
    {
        if (messages is null)
        {
            return false;
        }

        foreach (var message in messages)
        {
            // 工具审批响应会在 FunctionInvokingChatClient 内部触发工具执行并生成 FunctionResultContent。
            // 该回合不能再注入 todo、memory 等普通上下文，否则工具结果会被追加到这些消息之后。
            if (message.Contents.OfType<ToolApprovalResponseContent>().Any())
            {
                return true;
            }

            // 已经包含工具结果的回合同样必须保持纯净，避免破坏 assistant tool_use 与 user tool_result 的相邻关系。
            if (message.Contents.OfType<FunctionResultContent>().Any())
            {
                return true;
            }
        }

        return false;
    }
}
