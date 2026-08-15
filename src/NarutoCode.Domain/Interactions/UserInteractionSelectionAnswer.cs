namespace NarutoCode.Domain.Interactions;

/// <summary>
/// 选择型用户交互的结构化应答：保留选中选项与用户填写的可选补充说明。
/// </summary>
/// <param name="SelectedIds">选中的选项标识集合。</param>
/// <param name="Supplement">用户填写的补充说明；未填写时为空字符串。</param>
public sealed record UserInteractionSelectionAnswer(
    IReadOnlyList<string> SelectedIds,
    string Supplement);
