using Microsoft.Agents.AI;

namespace NarutoCode.Infrastructure.AIAgents;

/// <summary>
/// 表示一次会话 Agent 执行期间持有的独占租约。
/// </summary>
public interface IConversationAgentLease : IAsyncDisposable
{
    /// <summary>
    /// 当前会话专属的 Agent。
    /// </summary>
    AIAgent Agent { get; }

    /// <summary>
    /// 当前会话的运行时状态。
    /// </summary>
    AgentSession? Session { get; set; }

    /// <summary>
    /// 标记当前会话运行时失效；租约释放后将销毁对应 Shell，并在下一次使用时重建。
    /// </summary>
    void Invalidate();
}
