namespace NarutoCode.Domain.Interactions;

/// <summary>
/// 选择题的一个选项：Id 返回给 LLM，Label 展示给用户。
/// </summary>
/// <param name="Id">选项标识，交互内唯一，返回给 LLM。</param>
/// <param name="Label">选项展示文本。</param>
public sealed record UserInteractionOption(string Id, string Label);
