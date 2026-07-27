using NarutoCode.Domain.Entities;
using NarutoCode.Domain.Enums;

namespace NarutoCode.Domain.Conversations;

/// <summary>
/// 对话持久化仓储抽象，负责按工作目录加载会话和追加消息。
/// </summary>
public interface IConversationRepository
{
    /// <summary>
    /// 获取当前工作目录最近的对话；如果不存在则创建一个新的对话。
    /// </summary>
    /// <param name="workDirectory">当前工作目录。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>当前工作目录对应的对话实体。</returns>
    Task<Conversation> GetOrCreateByWorkDirectoryAsync(
        string workDirectory,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// 按最近更新时间倒序列出当前工作目录下的会话摘要。
    /// </summary>
    /// <param name="workDirectory">当前工作目录。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>会话摘要集合。</returns>
    Task<IReadOnlyList<ConversationSummary>> ListByWorkDirectoryAsync(
        string workDirectory,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// 按工作目录获取或创建项目，但不创建会话。
    /// </summary>
    /// <param name="workDirectory">项目工作目录。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>项目摘要。</returns>
    Task<WorkspaceSummary> GetOrCreateWorkspaceAsync(
        string workDirectory,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// 按最近更新时间倒序列出指定项目下的会话摘要。
    /// </summary>
    /// <param name="projectId">项目主键。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>会话摘要集合。</returns>
    Task<IReadOnlyList<ConversationSummary>> ListByProjectIdAsync(
        long projectId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// 按最近更新时间倒序列出包含历史会话的工作区。
    /// </summary>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>按工作目录聚合的工作区摘要集合。</returns>
    Task<IReadOnlyList<WorkspaceSummary>> ListWorkspacesAsync(
        CancellationToken cancellationToken = default);

    /// <summary>
    /// 为当前工作目录显式创建一个新会话。
    /// </summary>
    /// <param name="workDirectory">当前工作目录。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>新创建的会话实体。</returns>
    Task<Conversation> CreateForWorkDirectoryAsync(
        string workDirectory,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// 获取或创建指定项目下指定来源类型的最近会话。
    /// 网关按通道类型（如 WeCom）查询对应会话，不会出现在 TUI 和桌面端的会话列表中。
    /// </summary>
    /// <param name="projectId">项目主键。</param>
    /// <param name="source">会话来源类型。</param>
    /// <param name="sourceId">会话来源标识，用于区分同一来源类型下的不同通道会话（如不同群聊/单聊）。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>匹配来源类型与来源标识的会话实体。</returns>
    Task<Conversation> GetOrCreateBySourceAsync(
        long projectId,
        ConversationSource source,
        string sourceId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// 为指定项目创建一个新会话。
    /// </summary>
    /// <param name="projectId">项目主键。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>新创建的会话实体。</returns>
    Task<Conversation> CreateForProjectIdAsync(
        long projectId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// 为指定项目创建一个新会话，并指定会话来源类型。
    /// </summary>
    /// <param name="projectId">项目主键。</param>
    /// <param name="source">会话来源类型。</param>
    /// <param name="sourceId">会话来源标识，本地会话为空字符串。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>新创建的会话实体。</returns>
    Task<Conversation> CreateForProjectIdAsync(
        long projectId,
        ConversationSource source,
        string sourceId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// 按会话标识获取会话实体。
    /// </summary>
    /// <param name="conversationId">会话标识。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>找到时返回会话实体，否则返回 <see langword="null" />。</returns>
    Task<Conversation?> GetByIdAsync(
        long conversationId,
        CancellationToken cancellationToken = default);


    /// <summary>
    /// 
    /// </summary>
    /// <param name="conversationId"></param>
    /// <param name="cancellationToken"></param>
    /// <returns></returns>
    Task<IReadOnlyList<Message>> ListMessagesWithUIAsync(
        long conversationId,
        CancellationToken cancellationToken = default);
    /// <summary>
    /// 按创建顺序获取指定对话的历史消息。
    /// </summary>
    /// <param name="conversationId">对话标识。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>历史消息集合。</returns>
    Task<IReadOnlyList<Message>> ListMessagesAsync(
        long conversationId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// 按运行时顺序获取发送给 LLM 的已裁剪历史消息。
    /// </summary>
    /// <param name="conversationId">对话标识。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>LLM 运行时历史消息集合。</returns>
    Task<IReadOnlyList<Message>> ListRuntimeMessagesAsync(
        long conversationId,
        CancellationToken cancellationToken = default);
}
