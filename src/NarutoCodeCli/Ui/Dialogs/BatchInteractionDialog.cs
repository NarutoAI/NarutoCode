using NarutoCode.Domain.Interactions;
using NarutoCode.Infrastructure;
using NarutoCode.Infrastructure.JsonSerializerContexts;
using Terminal.Gui.App;
using Terminal.Gui.Drawing;
using Terminal.Gui.Drivers;
using Terminal.Gui.Input;
using Terminal.Gui.ViewBase;
using Terminal.Gui.Views;

namespace NarutoCodeCli.Ui;

/// <summary>
/// 批量选择问卷抽屉：在同一模态会话内切换多道题目，统一提交全部答案，避免 Agent 逐题等待。
/// </summary>
internal sealed class BatchInteractionDialog : Dialog
{
    private const int ChatInputPanelRows = 7;

    private readonly IApplication app;
    private readonly UserInteractionRequest request;
    private readonly Action? requestOperationCancel;
    private readonly List<Label> optionLabels = [];
    private readonly Label questionLabel;
    private readonly int[] selectedIndexes;
    private readonly bool[][] checkedStates;
    // 按题缓存补充说明文本：切题时保存当前题、恢复目标题，提交时写入对应题目的结果。
    private readonly string[] supplementTexts;
    private readonly InteractionSupplementField supplementField;
    private readonly Label hintLabel;
    private int questionIndex;
    private bool finished;
    private bool focusInitialized;

    /// <summary>
    /// 用户作答结果；弹窗停止后读取，取消时为取消结果。
    /// </summary>
    public UserInteractionResult? InteractionResult { get; private set; }

    /// <summary>
    /// 创建批量选择问卷抽屉。
    /// </summary>
    /// <param name="app">Terminal.Gui 应用实例。</param>
    /// <param name="request">包含批量题目的交互请求。</param>
    /// <param name="requestOperationCancel">Ctrl+C 时取消当前 Agent 运行的回调。</param>
    public BatchInteractionDialog(
        IApplication app,
        UserInteractionRequest request,
        Action? requestOperationCancel = null)
    {
        if (request.Questions.Count == 0)
        {
            throw new ArgumentException("批量问卷必须包含至少一道题。", nameof(request));
        }

        this.app = app;
        this.request = request;
        this.requestOperationCancel = requestOperationCancel;
        selectedIndexes = new int[request.Questions.Count];
        checkedStates = request.Questions.Select(question => new bool[question.Options.Count]).ToArray();
        supplementTexts = new string[request.Questions.Count];

        Title = string.IsNullOrWhiteSpace(request.Title) ? "❯ agent 批量提问" : $"❯ {request.Title}";
        BorderStyle = LineStyle.Rounded;
        SetScheme(TuiStyles.GetCanvasScheme());

        questionLabel = new Label
        {
            X = 2,
            Y = 0,
            Width = Dim.Fill(Dim.Absolute(4)),
            Height = 1
        };
        Add(questionLabel);

        for (var index = 0; index < request.Questions.Max(question => question.Options.Count); index++)
        {
            var optionLabel = new Label { X = 2, Y = 2 + index, Width = Dim.Fill(Dim.Absolute(4)), Height = 1 };
            optionLabel.SetScheme(TuiStyles.GetScheme(UiTextStyle.Normal));
            optionLabels.Add(optionLabel);
            Add(optionLabel);
        }

        var supplementTitle = new Label
        {
            Text = "┄ 补充说明（可选，对应当前题目）",
            X = 2,
            Y = Pos.AnchorEnd(6),
            Width = Dim.Fill(Dim.Absolute(4)),
            Height = 1
        };
        supplementTitle.SetScheme(TuiStyles.GetScheme(UiTextStyle.Subtle));
        Add(supplementTitle);

        supplementField = new InteractionSupplementField
        {
            X = 2,
            Y = Pos.AnchorEnd(5),
            Width = Dim.Fill(Dim.Absolute(4)),
            Height = 3
        };
        supplementField.SetScheme(TuiStyles.GetInputScheme());
        supplementField.SubmitPressed += TrySubmit;
        supplementField.TabRequested += HandleTab;
        supplementField.CancelRequested += CancelInteraction;
        // 输入框聚焦时方向键/空格仍路由回选项区，避免问卷操作被文本编辑吞掉。
        supplementField.NavigationKeyHandler = HandleOptionKey;
        Add(supplementField);

        var hint = new Label
        {
            Text = "Tab 下一题    Shift+Tab 上一题    ↑↓ 选择    Space 确认/勾选    Enter 提交    Esc 取消",
            X = 2,
            Y = Pos.AnchorEnd(1),
            Width = Dim.Fill(Dim.Absolute(4)),
            Height = 1
        };
        hint.SetScheme(TuiStyles.GetScheme(UiTextStyle.Subtle));
        Add(hint);
        hintLabel = hint;

        ApplyGeometry();
        SwitchToQuestion(0);
    }

    /// <inheritdoc />
    protected override bool OnKeyDown(Key key)
    {
        if (finished)
        {
            return true;
        }

        if (key == Key.Esc)
        {
            CloseWithCancellation();
            return true;
        }

        if (key.IsCtrl && (key.KeyCode & KeyCode.CharMask) == KeyCode.C)
        {
            CancelInteraction(true);
            return true;
        }

        if (key == Key.Tab)
        {
            HandleTab(forward: true);
            return true;
        }

        if (key == Key.Tab.WithShift)
        {
            HandleTab(forward: false);
            return true;
        }

        if (key == Key.Enter)
        {
            TrySubmit();
            return true;
        }

        if (HandleOptionKey(key))
        {
            return true;
        }

        return base.OnKeyDown(key);
    }

    /// <inheritdoc />
    protected override void OnSubViewsLaidOut(LayoutEventArgs e)
    {
        base.OnSubViewsLaidOut(e);
        // 焦点固定在补充说明输入框：用户可直接打字填写补充说明，
        // 而 ↑↓/Space/Tab/Enter 等导航键经输入框拦截后仍路由回问卷逻辑，行为一致。
        if (!focusInitialized)
        {
            focusInitialized = true;
            supplementField.SetFocus();
        }
    }

    /// <summary>
    /// 处理选项区导航按键：↑↓ 移动高亮；Space 对单选确认当前项并进入下一题、对多选切换勾选。
    /// 同时供补充说明输入框在聚焦时回调，保证按键行为一致。
    /// </summary>
    /// <param name="key">按键。</param>
    /// <returns>按键被选项区消费时返回 <see langword="true" />。</returns>
    private bool HandleOptionKey(Key key)
    {
        var question = request.Questions[questionIndex];
        if (key == Key.CursorUp)
        {
            selectedIndexes[questionIndex] = Math.Max(0, selectedIndexes[questionIndex] - 1);
            RefreshQuestion();
            ClearValidationHint();
            return true;
        }

        if (key == Key.CursorDown)
        {
            selectedIndexes[questionIndex] = Math.Min(question.Options.Count - 1, selectedIndexes[questionIndex] + 1);
            RefreshQuestion();
            ClearValidationHint();
            return true;
        }

        var isSpace = (key.KeyCode & KeyCode.CharMask) == KeyCode.Space;
        if (isSpace)
        {
            if (question.Multiple)
            {
                // 多选：切换当前高亮项的勾选状态，停留在本题继续选择。
                var selectedIndex = selectedIndexes[questionIndex];
                checkedStates[questionIndex][selectedIndex] = !checkedStates[questionIndex][selectedIndex];
                RefreshQuestion();
            }
            else if (questionIndex < request.Questions.Count - 1)
            {
                // 单选：高亮项即选择，Space 确认并进入下一题。
                SwitchToQuestion(questionIndex + 1);
            }
            // 最后一题单选：高亮项即选择，Space 仅确认，停留在本题（Enter 提交）。

            ClearValidationHint();
            return true;
        }

        return false;
    }

    /// <summary>
    /// 处理 Tab/Shift+Tab：Tab 循环切下一题（最后一题后回到第一题），Shift+Tab 反向循环，
    /// 保证任意位置都能来回切换题目；切题时同步保存/恢复各题的补充说明。
    /// </summary>
    /// <param name="forward">是否前移。</param>
    private void HandleTab(bool forward)
    {
        var questionCount = request.Questions.Count;
        var newIndex = forward
            ? (questionIndex + 1) % questionCount
            : (questionIndex - 1 + questionCount) % questionCount;
        SwitchToQuestion(newIndex);
        ClearValidationHint();
    }

    /// <summary>
    /// 切换到指定题目：先保存当前题的补充说明到缓存，再刷新题目并恢复目标题的补充说明。
    /// </summary>
    /// <param name="newIndex">目标题目索引。</param>
    private void SwitchToQuestion(int newIndex)
    {
        // 切题前把输入框当前内容保存到该题缓存，避免切换丢失。
        supplementTexts[questionIndex] = supplementField.Text ?? string.Empty;
        questionIndex = newIndex;
        RefreshQuestion();
        // 切题后恢复目标题的补充说明；首次展示第 0 题时为空。
        supplementField.Text = supplementTexts[questionIndex] ?? string.Empty;
    }

    /// <summary>
    /// 刷新当前题目与其选项状态；补充说明输入框由 <see cref="SwitchToQuestion" /> 负责保存/恢复，此处不触碰。
    /// </summary>
    private void RefreshQuestion()
    {
        var question = request.Questions[questionIndex];
        questionLabel.Text = $"{questionIndex + 1}/{request.Questions.Count}  {question.Question}";

        for (var index = 0; index < optionLabels.Count; index++)
        {
            if (index >= question.Options.Count)
            {
                optionLabels[index].Text = string.Empty;
                continue;
            }

            var selected = index == selectedIndexes[questionIndex];
            var marker = question.Multiple
                ? (checkedStates[questionIndex][index] ? "☑" : "☐")
                : (selected ? "◉" : "○");
            optionLabels[index].Text = $" {(selected ? "❯" : " ")} {marker} {question.Options[index].Label}";
            optionLabels[index].SetScheme(TuiStyles.GetScheme(selected ? UiTextStyle.AccentStrong : UiTextStyle.Normal));
        }

        SetNeedsDraw();
    }

    /// <summary>
    /// 汇总每题选择结果并一次提交；多选题目未勾选任何选项时提示并阻止提交。
    /// </summary>
    private void TrySubmit()
    {
        // 提交前先把当前题输入框内容存入缓存，避免未切题直接提交时丢失最新补充说明。
        supplementTexts[questionIndex] = supplementField.Text ?? string.Empty;

        // 提交前校验：每题必须至少有一个有效选择（多选需勾选至少一项，单选高亮项即选择）。
        for (var index = 0; index < request.Questions.Count; index++)
        {
            var question = request.Questions[index];
            if (question.Multiple && checkedStates[index].All(state => !state))
            {
                ShowValidationHint($"第 {index + 1} 题未勾选任何选项，请完成所有问题后再提交");
                return;
            }
        }

        var answers = new Dictionary<string, UserInteractionSelectionAnswer>(StringComparer.Ordinal);
        for (var index = 0; index < request.Questions.Count; index++)
        {
            var question = request.Questions[index];
            var selectedIds = question.Multiple
                ? question.Options.Where((_, optionIndex) => checkedStates[index][optionIndex]).Select(option => option.Id).ToArray()
                : [question.Options[selectedIndexes[index]].Id];
            // 补充说明按题绑定：取该题缓存文本，切题已保存、提交时统一写入对应题目。
            answers[question.Id] = new UserInteractionSelectionAnswer(selectedIds, (supplementTexts[index] ?? string.Empty).Trim());
        }

        InteractionResult = new UserInteractionResult(
            request.Id,
            UserInteractionStatus.Completed,
            UserInteractionJson.SerializeBatchAnswer(
                new UserInteractionBatchAnswer(answers, (supplementField.Text ?? string.Empty).Trim())));
        finished = true;
        app.RequestStop(this);
    }

    /// <summary>
    /// 在底部快捷键行显示校验失败提示。
    /// </summary>
    /// <param name="message">提示文本。</param>
    private void ShowValidationHint(string message)
    {
        hintLabel.Text = $"⚠ {message}";
        hintLabel.SetScheme(TuiStyles.GetScheme(UiTextStyle.AccentStrong));
        SetNeedsDraw();
    }

    /// <summary>
    /// 用户继续操作时清除校验失败提示，恢复默认快捷键行。
    /// </summary>
    private void ClearValidationHint()
    {
        hintLabel.Text = "Tab 下一题    Shift+Tab 上一题    ↑↓ 选择    Space 确认/勾选    Enter 提交    Esc 取消";
        hintLabel.SetScheme(TuiStyles.GetScheme(UiTextStyle.Subtle));
    }

    /// <summary>
    /// 取消当前批量问卷。
    /// </summary>
    private void CloseWithCancellation()
    {
        InteractionResult = new UserInteractionResult(request.Id, UserInteractionStatus.Cancelled, string.Empty);
        finished = true;
        app.RequestStop(this);
    }

    /// <summary>
    /// 处理取消入口；Ctrl+C 同时中止当前 Agent 运行。
    /// </summary>
    /// <param name="shouldCancelOperation">是否中止当前 Agent 运行。</param>
    private void CancelInteraction(bool shouldCancelOperation)
    {
        if (shouldCancelOperation)
        {
            requestOperationCancel?.Invoke();
        }

        CloseWithCancellation();
    }

    /// <summary>
    /// 应用底部抽屉几何布局。
    /// </summary>
    private void ApplyGeometry()
    {
        var height = Math.Min(15, Math.Max(12, SafeWindowHeight() - ChatInputPanelRows));
        X = 0;
        Y = Pos.AnchorEnd(height + ChatInputPanelRows);
        Width = Dim.Fill();
        Height = height;
    }

    /// <summary>
    /// 安全读取终端高度；不可用时使用默认高度。
    /// </summary>
    private static int SafeWindowHeight()
    {
        try
        {
            return Console.WindowHeight;
        }
        catch (IOException)
        {
            return 24;
        }
    }
}
