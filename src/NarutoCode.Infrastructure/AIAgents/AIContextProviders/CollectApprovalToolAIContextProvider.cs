using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;

namespace NarutoCode.Infrastructure.AIAgents.AIContextProviders;

/// <summary>
/// 收集审批工具 上下文
/// </summary>
public sealed class CollectApprovalToolAiContextProvider : AIContextProvider
{
    private  readonly HashSet<string> approvalToolsByName = new(StringComparer.Ordinal);

    /// <summary>
    /// is approvar tool 
    /// </summary>
    public bool IsApprovalToolsNames(string name) => approvalToolsByName?.Contains(name) ?? false;

    /// <inheritdoc />
    protected override ValueTask<AIContext> ProvideAIContextAsync(InvokingContext context,
        CancellationToken cancellationToken = new CancellationToken())
    {
        if (context.AIContext.Tools != null)
        {
            foreach (var item in context.AIContext.Tools)
            {
                // 判断是否为需要审批的工具，并按名称去重后维护到集合中。
                if (item.GetService<ApprovalRequiredAIFunction>() is not null)
                {
                    approvalToolsByName.Add(item.Name);
                }
            }
        }

        return base.ProvideAIContextAsync(context, cancellationToken);
    }
    
}