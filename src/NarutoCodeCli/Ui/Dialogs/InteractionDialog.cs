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
/// 交互弹窗几何样式：同一内容的三种布局变体，切换 <see cref="InteractionDialog.CurrentStyle" /> 即可对比效果。
/// </summary>
internal enum InteractionDialogStyle
{
    /// <summary>样式 A：经典居中模态框（默认）。</summary>
    Centered = 0,

    /// <summary>样式 B：底部弹出面板，贴主窗口输入区上沿。</summary>
    BottomDrawer = 1,

    /// <summary>样式 C：全屏接管式，窄终端友好。</summary>
    FullScreen = 2
}

/// <summary>
/// Agent 用户交互模态弹窗：选择题渲染为带补充说明的底部问卷抽屉，文本题使用输入弹窗。
/// 键盘交互：↑↓ 移动高亮、Space 切换多选、Tab 进入补充说明、Enter 提交、Esc 取消、Ctrl+C 取消并请求中止当前任务；
/// 弹窗停止后经 <see cref="InteractionResult" /> 输出结果（与 SessionLauncherWindow.SelectionResult 同模式）。
/// </summary>
internal sealed class InteractionDialog : Dialog
{
    /// <summary>当前默认弹窗样式；修改此值即可在三种布局间切换对比。</summary>
    public static InteractionDialogStyle CurrentStyle { get; set; } = InteractionDialogStyle.Centered;

    // 主窗口底部固定区域共 7 行：抽屉贴在待发图片栏与输入面板上沿。
    private const int ChatInputPanelRows = 7;

    private readonly IApplication app;
    private readonly UserInteractionRequest request;
    private readonly Action? requestOperationCancel;
    private readonly List<Label> optionLabels = [];
    private readonly bool[] checkedStates;
    private readonly List<string> questionLines;
    private TextField? inputField;
    private InteractionSupplementField? supplementField;
    private int selectedIndex;
    private bool finished;
    private bool inputFocusInitialized;

    /// <summary>
    /// 用户作答结果；弹窗停止后读取，取消时为取消结果。
    /// 命名为 InteractionResult 以避免隐藏基类 Dialog.Result。
    /// </summary>
    public UserInteractionResult? InteractionResult { get; private set; }

    /// <summary>
    /// 创建交互弹窗。
    /// </summary>
    /// <param name="app">Terminal.Gui 应用实例（用于停止模态会话）。</param>
    /// <param name="request">交互请求。</param>
    /// <param name="style">弹窗几何样式；省略时使用 <see cref="CurrentStyle" />。</param>
    /// <param name="requestOperationCancel">Ctrl+C 时请求中止当前运行任务的回调（走现有取消链路）。</param>
    public InteractionDialog(
        IApplication app,
        UserInteractionRequest request,
        InteractionDialogStyle? style = null,
        Action? requestOperationCancel = null)
    {
        this.app = app;
        this.request = request;
        this.requestOperationCancel = requestOperationCancel;
        checkedStates = new bool[request.Options.Count];

        Title = string.IsNullOrWhiteSpace(request.Title) ? "❯ agent 提问" : $"❯ {request.Title}";
        BorderStyle = LineStyle.Rounded;
        SetScheme(TuiStyles.GetCanvasScheme());

        // 问题文本按内容宽度折行（CJK 按字符折行），再构建内容与几何布局
        // 选择题统一使用问卷抽屉；纯文本问题保留既有居中输入体验。
        var effectiveStyle = request.Type == UserInteractionType.Selection
            ? InteractionDialogStyle.BottomDrawer
            : style ?? CurrentStyle;
        questionLines = WrapText(request.Question, Math.Max(24, GetContentWidth(effectiveStyle) - 4));
        BuildContent();
        ApplyGeometry(effectiveStyle);
    }

    /// <inheritdoc />
    protected override bool OnKeyDown(Key key)
    {
        if (finished)
        {
            return true;
        }

        // Esc 取消本次交互；Ctrl+C 取消交互并请求中止当前运行任务
        if (key == Key.Esc)
        {
            CloseWithCancellation();
            return true;
        }

        if (key.IsCtrl && (key.KeyCode & KeyCode.CharMask) == KeyCode.C)
        {
            CancelInteraction(shouldCancelOperation: true);
            return true;
        }

        if (request.Type == UserInteractionType.Selection && key == Key.Tab)
        {
            supplementField?.SetFocus();
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

    /// <summary>
    /// 处理选择题的选项导航按键：↑↓ 移动高亮，多选时 Space 切换勾选。
    /// 同时供补充说明输入框在聚焦时回调，保证按键行为一致。
    /// </summary>
    /// <param name="key">按键。</param>
    /// <returns>按键被选项区消费时返回 <see langword="true" />。</returns>
    private bool HandleOptionKey(Key key)
    {
        if (request.Type != UserInteractionType.Selection)
        {
            return false;
        }

        if (key == Key.CursorUp)
        {
            selectedIndex = Math.Max(0, selectedIndex - 1);
            RefreshOptions();
            return true;
        }

        if (key == Key.CursorDown)
        {
            selectedIndex = Math.Min(request.Options.Count - 1, selectedIndex + 1);
            RefreshOptions();
            return true;
        }

        var isSpace = (key.KeyCode & KeyCode.CharMask) == KeyCode.Space;
        if (isSpace)
        {
            // 多选时空格切换当前高亮项的勾选状态；单选时空格仅消费不动作（避免向补充说明框输入空格），提交以 Enter 为准。
            if (request.Multiple)
            {
                checkedStates[selectedIndex] = !checkedStates[selectedIndex];
                RefreshOptions();
            }

            return true;
        }

        return false;
    }

    /// <summary>
    /// 首次布局完成后把焦点交给输入框（输入类交互），或保持选项区焦点（选择题），保证可直接操作。
    /// </summary>
    /// <param name="e">布局事件参数。</param>
    protected override void OnSubViewsLaidOut(LayoutEventArgs e)
    {
        base.OnSubViewsLaidOut(e);

        if (inputFocusInitialized)
        {
            return;
        }

        inputFocusInitialized = true;
        if (inputField is not null)
        {
            inputField.SetFocus();
        }
        else
        {
            // 选择题：焦点保持在问卷选项区，↑↓/Space/Tab 直接操控问卷。
            SetFocus();
        }
    }

    /// <summary>
    /// 构建弹窗内容：问题行 + 选项/输入区 + 选择题补充说明 + 快捷键提示。
    /// </summary>
    private void BuildContent()
    {
        var y = 0;
        foreach (var line in questionLines)
        {
            AddContentLabel(line, y, UiTextStyle.Normal);
            y++;
        }

        // 问题与作答区之间空一行
        y++;

        if (request.Type == UserInteractionType.Selection)
        {
            // 选项行：文本由 RefreshOptions 按选中/勾选状态重建
            for (var index = 0; index < request.Options.Count; index++)
            {
                var label = new Label { X = 2, Y = y, Width = Dim.Fill(Dim.Absolute(4)), Height = 1 };
                label.SetScheme(TuiStyles.GetScheme(UiTextStyle.Normal));
                optionLabels.Add(label);
                Add(label);
                y++;
            }

            RefreshOptions();

            // 选择问卷固定提供可选补充说明，输入区独立接管多行编辑与提交快捷键。
            y++;
            AddContentLabel("┄ 补充说明（可选）", y, UiTextStyle.Subtle);
            y++;
            supplementField = new InteractionSupplementField
            {
                X = 2,
                Y = y,
                Width = Dim.Fill(Dim.Absolute(4)),
                Height = Dim.Absolute(3)
            };
            supplementField.SetScheme(TuiStyles.GetInputScheme());
            supplementField.SubmitPressed += TrySubmit;
            supplementField.TabRequested += _ => supplementField.SetFocus();
            supplementField.CancelRequested += CancelInteraction;
            // 输入框聚焦时方向键/空格仍路由回选项区，避免问卷操作被文本编辑吞掉。
            supplementField.NavigationKeyHandler = HandleOptionKey;
            Add(supplementField);
            y += 3;
        }
        else
        {
            // 开放提问与参数输入：单行文本输入框，预填默认值
            inputField = new TextField
            {
                X = 2,
                Y = y,
                Width = Dim.Fill(Dim.Absolute(4)),
                Text = request.DefaultValue ?? string.Empty
            };
            inputField.SetScheme(TuiStyles.GetInputScheme());
            Add(inputField);
            y++;
        }

        // 底部空行 + 快捷键提示
        y++;
        var hint = request.Type == UserInteractionType.Selection
            ? (request.Multiple
                ? "↑↓ 移动    Space 勾选    Tab 补充说明    Enter 提交    Esc 取消"
                : "↑↓ 选择    Tab 补充说明    Enter 提交    Esc 取消")
            : "输入内容后 Enter 确认    Esc 取消";
        AddContentLabel(hint, y, UiTextStyle.Subtle);
    }

    /// <summary>
    /// 添加一行静态内容标签。
    /// </summary>
    private void AddContentLabel(string text, int y, UiTextStyle style)
    {
        var label = new Label { Text = text, X = 2, Y = y, Width = Dim.Fill(Dim.Absolute(4)), Height = 1 };
        label.SetScheme(TuiStyles.GetScheme(style));
        Add(label);
    }

    /// <summary>
    /// 按选中/勾选状态重建选项行文本与样式。
    /// </summary>
    private void RefreshOptions()
    {
        for (var index = 0; index < optionLabels.Count; index++)
        {
            var selected = index == selectedIndex;
            // 多选用勾选框标记，单选用高亮 + 圆点标记
            var marker = request.Multiple
                ? (checkedStates[index] ? "☑" : "☐")
                : (selected ? "◉" : "○");
            var cursor = selected ? "❯ " : "  ";
            optionLabels[index].Text = $" {cursor}{marker} {request.Options[index].Label}";
            optionLabels[index].SetScheme(TuiStyles.GetScheme(selected ? UiTextStyle.AccentStrong : UiTextStyle.Normal));
        }

        SetNeedsDraw();
    }

    /// <summary>
    /// 按样式应用几何布局：居中模态 / 底部抽屉 / 全屏。
    /// </summary>
    private void ApplyGeometry(InteractionDialogStyle style)
    {
        // 选择问卷额外包括说明标题与三行多行输入框；最小高度保证核心问答区域可见。
        var contentRows = request.Type == UserInteractionType.Selection
            ? questionLines.Count + 1 + request.Options.Count + 1 + 1 + 3 + 1 + 1
            : questionLines.Count + 1 + 1 + 1 + 1;
        var minimumHeight = request.Type == UserInteractionType.Selection ? 12 : 7;
        var availableHeight = style == InteractionDialogStyle.BottomDrawer
            ? Math.Max(7, SafeWindowHeight() - ChatInputPanelRows)
            : Math.Max(7, SafeWindowHeight() - 2);
        var height = Math.Min(Math.Max(contentRows + 2, minimumHeight), availableHeight);

        switch (style)
        {
            case InteractionDialogStyle.BottomDrawer:
                // 底部抽屉：底边锚定在主窗口输入面板上沿
                X = 0;
                Y = Pos.AnchorEnd(height + ChatInputPanelRows);
                Width = Dim.Fill();
                Height = height;
                break;

            case InteractionDialogStyle.FullScreen:
                X = 0;
                Y = 0;
                Width = Dim.Fill();
                Height = Dim.Fill();
                break;

            default:
                // 居中模态框：宽度限制在 40-80 列
                X = Pos.Center();
                Y = Pos.Center();
                Width = GetContentWidth(InteractionDialogStyle.Centered);
                Height = height;
                break;
        }
    }

    /// <summary>
    /// 计算指定样式的内容宽度。
    /// </summary>
    private static int GetContentWidth(InteractionDialogStyle style)
    {
        var screenWidth = SafeWindowWidth();
        return style == InteractionDialogStyle.Centered
            ? Math.Clamp(screenWidth - 8, 40, 80)
            : Math.Max(40, screenWidth - 4);
    }

    /// <summary>
    /// 尝试提交：选择题校验至少一项选中，输入类校验非空；通过后写入结果并停止模态会话。
    /// </summary>
    private void TrySubmit()
    {
        if (request.Type == UserInteractionType.Selection)
        {
            // 单选提交当前高亮项；多选提交全部勾选项
            var selectedIds = request.Multiple
                ? request.Options.Where((_, index) => checkedStates[index]).Select(option => option.Id).ToArray()
                : [request.Options[selectedIndex].Id];
            if (selectedIds.Length == 0)
            {
                return;
            }

            InteractionResult = new UserInteractionResult(
                request.Id,
                UserInteractionStatus.Completed,
                UserInteractionJson.SerializeSelectionAnswer(
                    new UserInteractionSelectionAnswer(selectedIds, (supplementField?.Text ?? string.Empty).Trim())));
        }
        else
        {
            // 开放提问与参数输入：空输入不提交
            var text = (inputField?.Text ?? string.Empty).Trim();
            if (text.Length == 0)
            {
                return;
            }

            InteractionResult = new UserInteractionResult(request.Id, UserInteractionStatus.Completed, text);
        }

        finished = true;
        app.RequestStop(this);
    }

    /// <summary>
    /// 取消本次交互并停止模态会话。
    /// </summary>
    private void CloseWithCancellation()
    {
        InteractionResult = new UserInteractionResult(request.Id, UserInteractionStatus.Cancelled, string.Empty);
        finished = true;
        app.RequestStop(this);
    }

    /// <summary>
    /// 处理所有取消入口：Esc 只取消当前问卷，Ctrl+C 同时取消当前 Agent 运行。
    /// </summary>
    /// <param name="shouldCancelOperation">是否请求中止当前运行任务。</param>
    private void CancelInteraction(bool shouldCancelOperation)
    {
        if (shouldCancelOperation)
        {
            requestOperationCancel?.Invoke();
        }

        CloseWithCancellation();
    }

    /// <summary>
    /// 按显示宽度折行：优先在空格处断行，超长单词（连续 CJK/长标识符）按字符硬切。
    /// </summary>
    /// <param name="text">原始文本。</param>
    /// <param name="width">目标显示宽度。</param>
    /// <returns>折行后的文本行集合。</returns>
    private static List<string> WrapText(string text, int width)
    {
        var lines = new List<string>();
        foreach (var rawLine in text.Replace("\r\n", "\n", StringComparison.Ordinal).Split('\n'))
        {
            var current = string.Empty;
            foreach (var word in rawLine.Split(' '))
            {
                // 超长单词按宽度硬切
                var remainder = word;
                while (remainder.Length > width)
                {
                    lines.Add(remainder[..width]);
                    remainder = remainder[width..];
                }

                // 空格拼接后超宽则当前行收尾、换行续写
                var candidate = current.Length == 0 ? remainder : current + " " + remainder;
                if (candidate.Length <= width)
                {
                    current = candidate;
                }
                else
                {
                    if (current.Length > 0)
                    {
                        lines.Add(current);
                    }

                    current = remainder;
                }
            }

            lines.Add(current);
        }

        return lines;
    }

    /// <summary>
    /// 安全读取终端宽度；重定向等异常场景回退 80。
    /// </summary>
    private static int SafeWindowWidth()
    {
        try
        {
            return Console.WindowWidth;
        }
        catch (IOException)
        {
            return 80;
        }
    }

    /// <summary>
    /// 安全读取终端高度；重定向等异常场景回退 24。
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
