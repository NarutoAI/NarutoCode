namespace NarutoCode.Domain.Interactions;

/// <summary>
/// 批量用户问卷的结构化应答：按题目 Id 保存每题的选择结果与补充说明。
/// </summary>
/// <param name="Answers">题目 Id 到应答的映射。</param>
/// <param name="Supplement">整份问卷的补充说明。</param>
public sealed record UserInteractionBatchAnswer(
    IReadOnlyDictionary<string, UserInteractionSelectionAnswer> Answers,
    string Supplement);
