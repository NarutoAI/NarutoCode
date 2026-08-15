using System.ComponentModel;
using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;
using NarutoCode.Application.Interactions;
using NarutoCode.Domain.Interactions;
using NarutoCode.Infrastructure.JsonSerializerContexts;

namespace NarutoCode.Infrastructure.AIAgents.AIContextProviders;

/// <summary>
/// 为会话级 Agent 提供向用户发起结构化交互（提问/选择/输入）的工具。
/// 工具体在 MAF 工具执行线程运行，经 IUserInteractionManager 挂起等待用户应答，
/// 对 Terminal.Gui 等前端实现零依赖；仅 CLI 宿主挂载本 Provider。
/// </summary>
internal sealed class AskUserInteractionProvider(IUserInteractionManager interactionManager) : AIContextProvider
{
    // 构造时一次性构建不可变上下文：使用规范指令 + 两个交互工具
    private readonly AIContext context = BuildContext(interactionManager);

    /// <inheritdoc />
    protected override ValueTask<AIContext> ProvideAIContextAsync(InvokingContext context,
        CancellationToken cancellationToken = default)
    {
        return ValueTask.FromResult(this.context);
    }

    /// <summary>
    /// 构建用户交互上下文：使用规范指令 + narutocode_ask_user_question / narutocode_ask_user_input 工具。
    /// </summary>
    private static AIContext BuildContext(IUserInteractionManager interactionManager)
    {
        var instructions =
            """
            # 用户交互

            你可以通过 `narutocode_ask_user_question` 与 `narutocode_ask_user_input` 工具向用户发起提问，用户会在界面弹窗中作答，工具返回其回答后你再继续。

            ## 何时使用
            - 存在多种有效实现方案，且取舍会显著影响结果时。
            - 缺少关键参数（路径、名称、版本、格式等）无法安全继续时。
            - 用户明确要求先确认再执行时。

            ## 使用约束
            - 能从上下文、代码或用户消息推断的信息，不要提问。
            - 需要确认多个相互独立的关键决策时，优先一次调用 `narutocode_ask_user_question` 并传入 `questions`；界面会在一张问卷中统一收集答案，避免逐题等待。
            - `questions` 一次提供 2-4 道选择题，每题提供 2-4 个选项；题目 id 与选项 id 使用简短英文，展示文本使用中文；界面固定提供可选的补充说明输入区。
            - 只有一个关键问题时使用单题 `question`；无选项的自由文本仍使用 `narutocode_ask_user_input`。
            - 用户取消回答时，基于已有信息继续或说明无法继续的原因，不要立即重复追问。
            """;

        // 工具委托：参数经源生成上下文反序列化，保证 AOT 安全
        var askQuestion = AIFunctionFactory.Create(AskQuestionHandler,
            name: "narutocode_ask_user_question",
            serializerOptions: UserInteractionJsonSerializerContext.Default.AskUserQuestionRequest.Options);
        var askInput = AIFunctionFactory.Create(AskInputHandler,
            name: "narutocode_ask_user_input",
            serializerOptions: UserInteractionJsonSerializerContext.Default.AskUserInputRequest.Options);

        return new AIContext { Instructions = instructions, Tools = [askQuestion, askInput] };

        // 绑定管理器的工具委托
        Task<string> AskQuestionHandler(AskUserQuestionRequest request, CancellationToken cancellationToken) =>
            AskQuestionCoreAsync(interactionManager, request, cancellationToken);

        Task<string> AskInputHandler(AskUserInputRequest request, CancellationToken cancellationToken) =>
            AskInputCoreAsync(interactionManager, request, cancellationToken);
    }

    /// <summary>
    /// ask_user_question 工具体：校验参数 → 构造交互请求 → 挂起等待用户应答 → 返回给模型的应答文本。
    /// </summary>
    [Description("向用户提出问题：提供 options 时渲染为带可选补充说明的选择问卷，省略时为开放式提问。适用于存在多种有效方案或缺少关键参数时。")]
    private static async Task<string> AskQuestionCoreAsync(
        IUserInteractionManager interactionManager,
        AskUserQuestionRequest request,
        CancellationToken cancellationToken)
    {
        // 会话上下文缺失（AsyncLocal 未流动到工具线程）时防御性拒绝，避免落库外键失败
        var sessionId = interactionManager.CurrentSessionId;
        if (sessionId == 0)
        {
            return "当前会话上下文不可用，无法使用交互式提问，请直接使用常规提问。";
        }

        // 批量选择问卷：一次创建一条交互记录，由前端统一展示和提交。
        if (request.Questions is { Count: > 0 })
        {
            var validationError = TryCreateBatchQuestions(request.Questions, out var questions);
            if (validationError is not null)
            {
                return validationError;
            }

            var interaction = new UserInteractionRequest(
                sessionId,
                UserInteractionType.Selection,
                request.Title ?? string.Empty,
                "请完成以下问题。",
                questions: questions);
            var result = await interactionManager.RequestAsync(interaction, cancellationToken);
            return FormatResult(interaction, result);
        }

        if (string.IsNullOrWhiteSpace(request.Question))
        {
            return "question 或 questions 不能为空。";
        }

        var optionsError = TryCreateOptions(request.Options, out var options);
        if (optionsError is not null)
        {
            return optionsError;
        }

        // 有选项为选择题，无选项为开放式提问
        var type = options.Count > 0 ? UserInteractionType.Selection : UserInteractionType.Question;
        var singleInteraction = new UserInteractionRequest(
            sessionId, type, request.Title ?? string.Empty, request.Question, request.Multiple, options);
        var singleResult = await interactionManager.RequestAsync(singleInteraction, cancellationToken);
        return FormatResult(singleInteraction, singleResult);
    }

    /// <summary>
    /// ask_user_input 工具体：向用户收集一段文本参数。
    /// </summary>
    [Description("向用户请求输入一段文本参数（可带默认值）。适用于缺少路径、名称等关键参数时。")]
    private static async Task<string> AskInputCoreAsync(
        IUserInteractionManager interactionManager,
        AskUserInputRequest request,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.Question))
        {
            return "question 不能为空。";
        }

        var sessionId = interactionManager.CurrentSessionId;
        if (sessionId == 0)
        {
            return "当前会话上下文不可用，无法向用户发起输入，请使用常规输入方式。";
        }

        var interaction = new UserInteractionRequest(
            sessionId, UserInteractionType.Input, request.Title ?? string.Empty, request.Question,
            defaultValue: request.DefaultValue);
        var result = await interactionManager.RequestAsync(interaction, cancellationToken);
        return FormatResult(interaction, result);
    }

    /// <summary>
    /// 校验并创建批量选择问卷题目，保证单次问卷在终端中可完整操作。
    /// </summary>
    /// <param name="requests">工具调用传入的题目集合。</param>
    /// <param name="questions">校验成功后创建的领域题目集合。</param>
    /// <returns>校验失败文本；成功时返回 <see langword="null" />。</returns>
    private static string? TryCreateBatchQuestions(
        IReadOnlyList<AskUserBatchQuestionRequest> requests,
        out IReadOnlyList<UserInteractionQuestion> questions)
    {
        questions = [];
        if (requests.Count is < 2 or > 4)
        {
            return "questions 必须包含 2-4 道题。";
        }

        var seenQuestionIds = new HashSet<string>(StringComparer.Ordinal);
        var values = new List<UserInteractionQuestion>(requests.Count);
        foreach (var request in requests)
        {
            var id = request.Id?.Trim() ?? string.Empty;
            var question = request.Question?.Trim() ?? string.Empty;
            if (id.Length == 0 || question.Length == 0 || !seenQuestionIds.Add(id))
            {
                return "questions 中每题的 id 与 question 不能为空，且题目 id 不能重复。";
            }

            var optionsError = TryCreateOptions(request.Options, out var options);
            if (optionsError is not null || options.Count is < 2 or > 4)
            {
                return optionsError ?? "questions 中每题必须提供 2-4 个选项。";
            }

            values.Add(new UserInteractionQuestion(id, question, options, request.Multiple));
        }

        questions = values;
        return null;
    }

    /// <summary>
    /// 校验并映射工具选项为领域选项。
    /// </summary>
    /// <param name="requests">工具调用传入的选项集合。</param>
    /// <param name="options">校验成功后创建的领域选项集合。</param>
    /// <returns>校验失败文本；成功时返回 <see langword="null" />。</returns>
    private static string? TryCreateOptions(
        IReadOnlyList<AskUserOptionRequest>? requests,
        out IReadOnlyList<UserInteractionOption> options)
    {
        options = [];
        if (requests is not { Count: > 0 })
        {
            return null;
        }

        var seenIds = new HashSet<string>(StringComparer.Ordinal);
        var values = new List<UserInteractionOption>(requests.Count);
        foreach (var request in requests)
        {
            var id = request.Id?.Trim() ?? string.Empty;
            var label = request.Label?.Trim() ?? string.Empty;
            if (id.Length == 0 || label.Length == 0 || !seenIds.Add(id))
            {
                return "options 的 id 与 label 不能为空，且 id 不能重复。";
            }

            values.Add(new UserInteractionOption(id, label));
        }

        options = values;
        return null;
    }

    /// <summary>
    /// 将交互结果格式化为返回给模型的文本：选择题映射为"展示文本(id)"，取消时给出后续行为指引。
    /// </summary>
    private static string FormatResult(UserInteractionRequest request, UserInteractionResult result)
    {
        // 取消：明确告知模型不要立即重复追问
        if (result.Status != UserInteractionStatus.Completed)
        {
            return "用户取消了本次提问，未提供回答。请基于已有信息继续，或说明缺少该信息无法继续的原因，不要立即重复追问。";
        }

        if (request.Questions.Count > 0)
        {
            var answer = UserInteractionJsonSerializerContext.DeserializeBatchAnswer(result.Value);
            var lines = request.Questions.Select(question =>
            {
                var labelById = question.Options.ToDictionary(option => option.Id, StringComparer.Ordinal);
                answer.Answers.TryGetValue(question.Id, out var item);
                var selections = (item?.SelectedIds ?? [])
                    .Select(id => labelById.TryGetValue(id, out var option) ? $"{option.Label}({id})" : id);
                return $"{question.Id}：{string.Join("；", selections)}";
            });
            var formatted = $"用户逐题回答：\n{string.Join("\n", lines)}";
            return string.IsNullOrWhiteSpace(answer.Supplement)
                ? formatted
                : $"{formatted}\n补充说明：{answer.Supplement}";
        }

        // 选择题：结果值包含选中 Id 与可选补充说明，映射回展示文本便于模型理解
        if (request.Type == UserInteractionType.Selection)
        {
            var labelById = request.Options.ToDictionary(option => option.Id, StringComparer.Ordinal);
            var answer = UserInteractionJsonSerializerContext.DeserializeSelectionAnswer(result.Value);
            var parts = answer.SelectedIds
                .Select(id => labelById.TryGetValue(id, out var option) ? $"{option.Label}({id})" : id);
            var selection = $"用户选择了：{string.Join("；", parts)}";
            return string.IsNullOrWhiteSpace(answer.Supplement)
                ? selection
                : $"{selection}\n补充说明：{answer.Supplement}";
        }

        return $"用户的回答：{result.Value}";
    }
}
