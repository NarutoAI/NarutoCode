namespace NarutoCode.Domain.Interactions;

/// <summary>
/// 批量用户问卷中的一道选择题：标识用于在结果中定位答案，选项供前端渲染。
/// </summary>
/// <param name="Id">题目唯一标识，返回给 Agent。</param>
/// <param name="Question">题目正文。</param>
/// <param name="Options">可选项集合。</param>
/// <param name="Multiple">是否允许多选。</param>
public sealed record UserInteractionQuestion(
    string Id,
    string Question,
    IReadOnlyList<UserInteractionOption> Options,
    bool Multiple = false);
