namespace NarutoCode.Domain.Entities;

/// <summary>
/// 对话实体
/// 表示一个完整的对话会话
/// </summary>
public class Conversation
{
    public Conversation()
    {
        Id = SnowflakeIdHelper.Instance.NextId();
    }

    /// <summary>
    /// 对话ID（主键）
    /// </summary>
    public long Id { get; set; }

    /// <summary>
    /// 对话标题
    /// </summary>
    public string Title { get; set; } = string.Empty;

    /// <summary>
    /// 对话创建时间
    /// </summary>
    public DateTime CreatedAt { get; set; } = DateTime.Now;

    /// <summary>
    /// 对话更新时间
    /// </summary>
    public DateTime UpdatedAt { get; set; } = DateTime.Now;

    /// <summary>
    /// 工作目录
    /// </summary>
    public string WorkDirectory { get; set; } = string.Empty;

    /// <summary>
    /// 对话累计 Token 数量。
    /// </summary>
    public long TokenCount { get; set; }

    /// <summary>
    /// 最近一次对话 Token 消耗数量。
    /// </summary>
    public long LastUsageTokenCount { get; set; }
}