namespace NarutoCode.Domain.Interactions;

/// <summary>
/// 用户交互类型：Agent 通过结构化工具向用户发起的交互形态。
/// 预留扩展 Approval/PlanReview 等类型，MVP 只实现前三类。
/// </summary>
public enum UserInteractionType
{
    /// <summary>
    /// 开放提问：用户以自由文本回答。
    /// </summary>
    Question = 0,

    /// <summary>
    /// 选择题：用户从选项集合中选择（支持单选与多选）。
    /// </summary>
    Selection = 1,

    /// <summary>
    /// 参数输入：用户输入文本，可带默认值。
    /// </summary>
    Input = 2
}
