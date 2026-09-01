using System.ComponentModel;

namespace NarutoCode.Infrastructure.AIAgents.AIContextProviders;

/// <summary>
/// ask_user_question 工具参数：单题可使用 <see cref="Question" /> 与 <see cref="Options" />；
/// 多题使用 <see cref="Questions" />，前端在同一张问卷内统一收集并一次返回所有答案。
/// </summary>
/// <param name="Question">单题问题正文，展示给用户；批量提问时可省略。</param>
/// <param name="Title">弹窗标题，简短概括问题主题。</param>
/// <param name="Options">单题选项集合；提供时为选择题，省略时为开放式提问。</param>
/// <param name="Multiple">单题选择题是否允许多选。</param>
/// <param name="Questions">批量选择题集合（2-4 题，每题 2-4 个选项）；提供后忽略单题参数。</param>
internal sealed record AskUserQuestionRequest(
    [Description("单题问题正文，批量提问时可省略")] string Question = "",
    [Description("弹窗标题，简短概括问题主题")] string Title = "",
    [Description("单题选项集合；提供时渲染为选择问卷，用户可额外填写补充说明；省略时为开放式提问")] List<AskUserOptionRequest>? Options = null,
    [Description("单题选择题是否允许多选")] bool Multiple = false,
    [Description("批量选择题集合（2-4 题，每题 2-4 个选项）；前端一次展示、统一提交全部答案")] List<AskUserBatchQuestionRequest>? Questions = null);

/// <summary>
/// ask_user_question 的批量问卷题目参数。
/// </summary>
/// <param name="Id">题目唯一标识，简短英文，作为答案键返回给 Agent。</param>
/// <param name="Question">问题正文。</param>
/// <param name="Options">该题的 2-4 个可选项。</param>
/// <param name="Multiple">该题是否允许多选。</param>
internal sealed record AskUserBatchQuestionRequest(
    [Description("题目唯一标识，简短英文，作为答案键返回给 Agent")] string Id,
    [Description("问题正文")] string Question,
    [Description("该题的 2-4 个可选项")] List<AskUserOptionRequest> Options,
    [Description("该题是否允许多选")] bool Multiple = false);

/// <summary>
/// ask_user_question 的选项参数。
/// </summary>
/// <param name="Id">选项标识，简短英文，交互内唯一，返回给模型。</param>
/// <param name="Label">选项展示文本。</param>
internal sealed record AskUserOptionRequest(
    [Description("选项标识，简短英文，交互内唯一")] string Id,
    [Description("选项展示文本")] string Label);
