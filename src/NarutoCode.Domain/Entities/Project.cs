namespace NarutoCode.Domain.Entities;

/// <summary>
/// 项目实体，表示用户在 NarutoCode 中打开和管理的工作目录。
/// </summary>
public class Project
{
    /// <summary>
    /// 项目主键，由 SQLite 在插入时生成。
    /// </summary>
    public long Id { get; set; }

    /// <summary>
    /// 项目显示名称。
    /// </summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// 项目对应的规范化绝对工作目录。
    /// </summary>
    public string WorkDirectory { get; set; } = string.Empty;

    /// <summary>
    /// 项目自定义排序值，值越小越靠前。
    /// </summary>
    public int SortOrder { get; set; }

    /// <summary>
    /// 项目创建时间。
    /// </summary>
    public DateTime CreatedAt { get; set; } = DateTime.Now;

    /// <summary>
    /// 项目最近更新时间。
    /// </summary>
    public DateTime UpdatedAt { get; set; } = DateTime.Now;
}
