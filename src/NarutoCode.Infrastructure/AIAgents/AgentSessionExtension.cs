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
        public async Task<bool> IsExistsInProgressTask(AIAgent agent)
        {
#pragma warning disable MAAI001
            var todoProvider = agent.GetService<TaskProvider>();
            //获取所有的任务
            var remainings = todoProvider.GetList(agentSession);
            if (remainings.Any(a=>a.Status== TaskAgentTaskStatus.InProgress))
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
        /// <param name="cancellationToken"></param>
        /// <returns></returns>
        public AgentSession CreateSession(ConversationSessionId sessionId, List<ChatMessage> messages,
            CancellationToken cancellationToken = default)
        {
            agentSession.StateBag.SetValue(nameof(PersistenceChatHistoryProvider),
                new PersistenceChatHistoryProvider.State(sessionId.Value, messages: messages),
                AgentAbstractionsJsonUtilities.DefaultOptions);
            return agentSession;
        }
    }
}