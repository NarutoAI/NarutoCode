using Microsoft.Extensions.AI;

namespace NarutoCode.Infrastructure.AIAgents.ChatHistorys;

/// <summary>
/// 聊天历史持久化上下
/// </summary>
/// <param name="SessionId">当前对话会话标识。</param>
/// <param name="Messages">本次新增的请求消息和响应消息。</param>
/// <param name="TotalUsage">本次调用关联的 token 用量；没有统计信息时为 <see langword="null" />。</param>
public sealed record ChatHistoryPersistenceContext(
    long SessionId,
    IReadOnlyList<ChatMessage> Messages,
    long? TotalUsage);
