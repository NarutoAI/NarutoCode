namespace NarutoCode.Domain.Messages;

/// <summary>
/// 会话的强类型标识，避免在领域层直接传递裸字符串。
/// </summary>
public readonly record struct ConversationSessionId
{
    /// <summary>
    /// 创建会话标识。
    /// </summary>
    /// <param name="value">标识值。</param>
    /// <exception cref="ArgumentException">当标识值为空时抛出。</exception>
    public ConversationSessionId(long value)
    {
        Value = value;
    }

    /// <summary>
    /// 标识值。
    /// </summary>
    public long Value { get; }

    /// <summary>
    /// 创建新的会话标识。
    /// </summary>
    /// <returns>新的会话标识。</returns>
    public static ConversationSessionId New()
    {
        return new ConversationSessionId(SnowflakeIdHelper.Instance.NextId());
    }
}
