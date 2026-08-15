namespace NarutoCode.Domain.Interactions;

/// <summary>
/// 用户交互请求：Agent 工具构造的完整交互意图，包含前端渲染弹窗所需的全部数据。
/// </summary>
public sealed record UserInteractionRequest
{
    /// <summary>
    /// 创建交互请求。
    /// </summary>
    /// <param name="sessionId">发起交互的会话标识（ConversationSessionId.Value）。</param>
    /// <param name="type">交互类型。</param>
    /// <param name="title">弹窗标题。</param>
    /// <param name="question">问题正文。</param>
    /// <param name="multiple">选择题是否允许多选。</param>
    /// <param name="options">选择题选项集合，非选择题传空集合。</param>
    /// <param name="defaultValue">输入类型的默认值。</param>
    /// <param name="questions">批量选择问卷的题目集合；为空时使用 <paramref name="question" /> 与 <paramref name="options" /> 表示单题。</param>
    public UserInteractionRequest(
        long sessionId,
        UserInteractionType type,
        string title,
        string question,
        bool multiple = false,
        IReadOnlyList<UserInteractionOption>? options = null,
        string? defaultValue = null,
        IReadOnlyList<UserInteractionQuestion>? questions = null)
    {
        // 雪花 ID：与 Message/Conversation 实体惯例一致，构造时生成
        Id = SnowflakeIdHelper.Instance.NextId();
        SessionId = sessionId;
        Type = type;
        Title = title;
        Question = question;
        Multiple = multiple;
        Options = options ?? [];
        DefaultValue = defaultValue;
        Questions = questions ?? [];
        CreatedAt = DateTimeOffset.Now;
    }

    /// <summary>
    /// 交互标识（雪花 ID）。
    /// </summary>
    public long Id { get; init; }

    /// <summary>
    /// 发起交互的会话标识。
    /// </summary>
    public long SessionId { get; init; }

    /// <summary>
    /// 交互类型。
    /// </summary>
    public UserInteractionType Type { get; init; }

    /// <summary>
    /// 弹窗标题。
    /// </summary>
    public string Title { get; init; }

    /// <summary>
    /// 问题正文。
    /// </summary>
    public string Question { get; init; }

    /// <summary>
    /// 选择题是否允许多选。
    /// </summary>
    public bool Multiple { get; init; }

    /// <summary>
    /// 选择题选项集合。
    /// </summary>
    public IReadOnlyList<UserInteractionOption> Options { get; init; }

    /// <summary>
    /// 输入类型的默认值。
    /// </summary>
    public string? DefaultValue { get; init; }

    /// <summary>
    /// 批量选择问卷的题目集合；非空时前端一次展示并统一提交所有题目的答案。
    /// </summary>
    public IReadOnlyList<UserInteractionQuestion> Questions { get; init; }

    /// <summary>
    /// 请求创建时间。
    /// </summary>
    public DateTimeOffset CreatedAt { get; init; }
}
