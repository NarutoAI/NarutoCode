using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;
using NarutoCode.Domain.Enums;
using NarutoCode.Domain.Messages;
using NarutoCode.Domain.Models;
using NarutoCode.Infrastructure.AIAgents.AIContextProviders;
using NarutoCode.Infrastructure.AIAgents.ChatHistorys;

namespace NarutoCode.Infrastructure.AIAgents;

public static class AgentSessionExtension
{
    extension(AgentSession agentSession)
    {
        /// <summary>
        /// 是否开启计划模式
        /// </summary>
        /// <returns></returns>
        public bool IsOpenPlan(AIAgent agent)
        {
#pragma warning disable MAAI001
            var agentModeProvider = agent.GetService<AgentModeProvider>();
            //
            var mode = agentModeProvider?.GetMode(agentSession);

            return string.Equals(mode, "plan", StringComparison.CurrentCultureIgnoreCase);
#pragma warning restore MAAI001
        }

        /// <summary>
        /// 是否开启待办任务
        /// </summary>
        /// <returns></returns>
        public async Task<bool> IsOpenTodoAsync(AIAgent agent)
        {
#pragma warning disable MAAI001
            var todoProvider = agent.GetService<TodoProvider>();
            //获取剩余的待办
            var remainingTodos = await todoProvider.GetRemainingTodosAsync(agentSession);
            if (remainingTodos is {Count: > 0})
            {
                return true;
            }
#pragma warning restore MAAI001
            return false;
        }

        /// <summary>
        /// 是否存在执行中的任务
        /// </summary>
        /// <returns></returns>
        public bool IsExistsInProgressTask(AIAgent agent)
        {
#pragma warning disable MAAI001
            var todoProvider = agent.GetService<TaskProvider>();
            //获取所有的任务
            var remainings = todoProvider.GetList(agentSession);
            if (remainings.Any(a => a.Status == TaskAgentTaskStatus.InProgress))
            {
                return true;
            }
#pragma warning restore MAAI001
            return false;
        }

        /// <summary>
        /// 根据会话id 创建会话
        /// </summary>
        /// <param name="sessionId"></param>
        /// <param name="messages">消息记录</param>
        /// <param name="lastInputTokenCount">数据库记录的最近一次输入 Token 用量，用于恢复压缩判断依据。</param>
        /// <param name="cancellationToken"></param>
        /// <returns></returns>
        public AgentSession CreateSession(ConversationSessionId sessionId, List<ChatMessage> messages,
            long? lastInputTokenCount = null,
            CancellationToken cancellationToken = default)
        {
            agentSession.StateBag.SetValue(nameof(PersistenceChatHistoryProvider),
                new PersistenceChatHistoryProvider.State(sessionId.Value, messages: messages),
                AgentAbstractionsJsonUtilities.DefaultOptions);

            // 恢复会话时，将数据库记录的最近一次输入 token 用量写入状态，供压缩策略使用
            if (lastInputTokenCount is > 0
                && agentSession.StateBag.TryGetValue(nameof(PersistenceChatHistoryProvider),
                    out PersistenceChatHistoryProvider.State? state) && state != null)
            {
                state.LastInputTokenCount = lastInputTokenCount;
            }

            return agentSession;
        }

        public void SetSessionUsage(UsageContent usage)
        {
            if (agentSession.StateBag.TryGetValue(nameof(PersistenceChatHistoryProvider),
                    out PersistenceChatHistoryProvider.State? state) && state != null)
            {
                state.TotalUsage = usage.Details.TotalTokenCount;
                // 记录最近一次调用的输入 token，供压缩策略判断上下文窗口占用
                state.LastInputTokenCount = usage.Details.InputTokenCount;
            }
        }
    }
}