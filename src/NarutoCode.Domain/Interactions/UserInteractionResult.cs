namespace NarutoCode.Domain.Interactions;

/// <summary>
/// 用户交互结果：用户应答或取消的终态数据。
/// </summary>
public sealed record UserInteractionResult
{
    /// <summary>
    /// 创建交互结果。
    /// </summary>
    /// <param name="interactionId">交互标识。</param>
    /// <param name="status">终态。</param>
    /// <param name="value">应答值：选择题为包含选项 Id 与补充说明的 JSON 对象（兼容历史 JSON 数组），输入/问答为文本；取消时为空。</param>
    public UserInteractionResult(long interactionId, UserInteractionStatus status, string value)
    {
        InteractionId = interactionId;
        Status = status;
        Value = value;
        CompletedAt = DateTimeOffset.Now;
    }

    /// <summary>
    /// 交互标识（雪花 ID）。
    /// </summary>
    public long InteractionId { get; init; }

    /// <summary>
    /// 交互终态。
    /// </summary>
    public UserInteractionStatus Status { get; init; }

    /// <summary>
    /// 应答值：选择题为包含选项 Id 与补充说明的 JSON 对象（兼容历史 JSON 数组），输入/问答为文本；取消时为空字符串。
    /// </summary>
    public string Value { get; init; }

    /// <summary>
    /// 完成时间。
    /// </summary>
    public DateTimeOffset CompletedAt { get; init; }
}
