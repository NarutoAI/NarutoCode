using NarutoCode.Domain.Configurations.Settings;
using NarutoCode.Domain.Messages;
using NarutoCode.Domain.Workspaces;
using Terminal.Gui.App;
using Terminal.Gui.Drawing;
using Terminal.Gui.Drivers;
using Terminal.Gui.Input;
using Terminal.Gui.ViewBase;
using Terminal.Gui.Views;

namespace NarutoCodeCli.Ui;

/// <summary>
/// 聊天主窗口：品牌头 + 可滚动消息区 + 状态栏 + 固定输入框。
/// 输入框提交事件通过 TCS 桥接给业务线程；业务线程通过 UpdateState（经 app.Invoke）刷新界面。
/// Ctrl+C：有运行中任务时取消任务，否则请求退出应用。
/// </summary>
internal sealed class ChatTuiWindow : Window
{
    private readonly IApplication app;
    private readonly ChatMessageListView messageList = new();
    private readonly Label brandLabel = new();
    private readonly Label dividerLabel = new();
    private readonly Label cwdLabel = new();
    private readonly Label statusLabel = new();
    private readonly Label inputPromptLabel = new();
    private readonly Label pendingImagesLabel = new();
    private readonly Label hintLabel = new();
    private readonly ChatInputField inputField;
    private readonly PendingUserMessageQueue pendingUserMessageQueue;
    private readonly ChatCancellationCoordinator cancellationCoordinator;
    private readonly IWorkspaceContextAccessor workspaceContextAccessor;
    private readonly IClipboardImageStore clipboardImageStore;
    private readonly ILlmSettingsService llmSettingsService;
    private readonly List<string> pendingImagePaths = [];

    private readonly Lock inputGate = new();
    private TaskCompletionSource<string?>? pendingInput;
    private CancellationTokenRegistration inputCancellation;

    private volatile bool isOperationRunning;
    private volatile bool isToolApprovalPending;
    private volatile bool stopRequested;
    private long contextTokenUsage;
    private int queuedMessageCount;
    private bool inputFocusInitialized;
    private int renderedDividerWidth = -1;
    private const int InputAreaHeightRows = 4;

    /// <summary>
    /// 用户请求退出（Ctrl+C 且无运行中任务）时触发。
    /// </summary>
    public event Action? ExitRequested;

    /// <summary>
    /// 创建聊天主窗口。
    /// </summary>
    /// <param name="app">Terminal.Gui 应用实例（用于停止当前会话）。</param>
    /// <param name="workspaceContextAccessor">工作目录访问器。</param>
    /// <param name="pendingUserMessageQueue">运行期间排队消息队列。</param>
    /// <param name="cancellationCoordinator">Ctrl+C 取消协调器。</param>
    /// <param name="clipboardImageStore">剪贴板图片存储（用于 Ctrl+V 粘贴图片）。</param>
    /// <param name="llmSettingsService">LLM 配置服务（用于判断当前模型是否支持视觉）。</param>
    public ChatTuiWindow(
        IApplication app,
        IWorkspaceContextAccessor workspaceContextAccessor,
        PendingUserMessageQueue pendingUserMessageQueue,
        ChatCancellationCoordinator cancellationCoordinator,
        IClipboardImageStore clipboardImageStore,
        ILlmSettingsService llmSettingsService)
    {
        this.app = app;
        this.workspaceContextAccessor = workspaceContextAccessor;
        this.pendingUserMessageQueue = pendingUserMessageQueue;
        this.cancellationCoordinator = cancellationCoordinator;
        this.clipboardImageStore = clipboardImageStore;
        this.llmSettingsService = llmSettingsService;

        BorderStyle = LineStyle.None;
        SetScheme(TuiStyles.GetCanvasScheme());

        // 顶层窗口必须显式填满屏幕，否则默认 Dim.Auto 尺寸为 0，子视图全部被裁剪（表现为控制台空白）
        X = 0;
        Y = 0;
        Width = Dim.Fill();
        Height = Dim.Fill();

        brandLabel.Text = "◆ NarutoCode";
        brandLabel.X = 0;
        brandLabel.Y = 0;
        brandLabel.Width = Dim.Fill();
        brandLabel.SetScheme(TuiStyles.GetBrandScheme());

        // 品牌头下方的装饰分隔线，分隔标题区与消息区，避免内容堆叠。
        // 文本长度在布局时按窗口宽度重新生成，保证缩放后分隔线始终铺满。
        dividerLabel.Text = new string('─', 96);
        dividerLabel.X = 0;
        dividerLabel.Y = 1;
        dividerLabel.Width = Dim.Fill();
        dividerLabel.SetScheme(TuiStyles.GetDividerScheme());

        cwdLabel.X = 0;
        cwdLabel.Y = 2;
        cwdLabel.Width = Dim.Fill();
        cwdLabel.SetScheme(TuiStyles.GetScheme(UiTextStyle.Muted));

        // 消息区直接使用全宽，避免窄终端中左侧装饰占用正文列数。
        // 底部固定区域：待发图片栏(1) + 输入面板(4) + 状态(1) + 快捷键(1) + 1 空行 = 8 行
        messageList.X = 0;
        messageList.Y = 4;
        messageList.Width = Dim.Fill();
        messageList.Height = Dim.Fill(Dim.Absolute(8));
        messageList.StateChanged += RefreshStatus;

        // 底部固定区域：待发图片栏 + 四行输入面板 + 状态 + 快捷键栏，直接贴齐底部。
        // 输入面板固定 4 行高，长文本在框内折行显示，超过 4 行内部滚动。
        pendingImagesLabel.X = 0;
        pendingImagesLabel.Y = Pos.AnchorEnd(7);
        pendingImagesLabel.Width = Dim.Fill();
        pendingImagesLabel.SetScheme(TuiStyles.GetScheme(UiTextStyle.Warning));
        pendingImagesLabel.Text = string.Empty;

        inputPromptLabel.Text = "❯ ";
        inputPromptLabel.X = 0;
        inputPromptLabel.Y = Pos.AnchorEnd(6);
        inputPromptLabel.Width = 2;
        inputPromptLabel.SetScheme(TuiStyles.GetScheme(UiTextStyle.AccentStrong));

        // 多行输入框：Multiline/WordWrap 已在 ChatInputField 构造函数中开启，
        // 长行在框内自动折行显示；固定 4 行高，与提示符首行对齐
        inputField = new ChatInputField { X = 2, Y = Pos.AnchorEnd(6), Width = Dim.Fill(), Height = Dim.Absolute(InputAreaHeightRows) };
        inputField.SetScheme(TuiStyles.GetInputScheme());
        inputField.SubmitPressed += OnInputAccepted;
        inputField.PasteImageRequested += OnPasteImageRequested;
        inputField.CancelRequested += HandleCancelRequested;
        // TextView 的 TextChanged 仅在 Text 整体赋值时触发；ContentsChanged 覆盖键入、粘贴与删除
        inputField.TextChanged += (_, _) => RefreshHint();
        inputField.ContentsChanged += (_, _) => RefreshHint();

        statusLabel.X = 0;
        statusLabel.Y = Pos.AnchorEnd(2);
        statusLabel.Width = Dim.Fill();
        statusLabel.SetScheme(TuiStyles.GetInputPanelScheme());

        hintLabel.X = 0;
        hintLabel.Y = Pos.AnchorEnd(1);
        hintLabel.Width = Dim.Fill();
        hintLabel.SetScheme(TuiStyles.GetInputPanelScheme());

        Add(brandLabel, dividerLabel, cwdLabel, messageList, statusLabel, pendingImagesLabel, inputPromptLabel, inputField, hintLabel);

        RefreshHeader();
        RefreshStatus();
        RefreshHint();
    }

    /// <summary>
    /// 由业务线程（经 app.Invoke）调用，刷新整个窗口状态。
    /// </summary>
    /// <param name="sessionState">当前 CLI 会话视图状态。</param>
    public void UpdateState(ChatSessionState sessionState)
    {
        isOperationRunning = sessionState.IsOperationRunning;
        isToolApprovalPending = sessionState.IsToolApprovalPending;
        contextTokenUsage = sessionState.ContextTokenUsage;
        queuedMessageCount = pendingUserMessageQueue.CreateSnapshot().Count;
        messageList.UpdateMessages(sessionState.Messages);
        RefreshHeader();
        RefreshStatus();
    }

    /// <summary>
    /// 等待一条用户输入；用户退出或取消时返回 <see langword="null" />。
    /// </summary>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>用户输入内容，或结束标记。</returns>
    public Task<string?> ReadInputAsync(CancellationToken cancellationToken)
    {
        lock (inputGate)
        {
            if (stopRequested)
            {
                return Task.FromResult<string?>(null);
            }

            if (pendingInput is not null)
            {
                throw new InvalidOperationException("上一次输入尚未被业务循环消费。");
            }

            var tcs = new TaskCompletionSource<string?>(TaskCreationOptions.RunContinuationsAsynchronously);
            pendingInput = tcs;
            inputCancellation = cancellationToken.Register(static state => ((ChatTuiWindow)state!).CancelPendingInput(), this);
            return tcs.Task;
        }
    }

    protected override bool OnKeyDown(Key key)
    {
        if (key.IsCtrl && (key.KeyCode & KeyCode.CharMask) == KeyCode.C)
        {
            // 焦点在输入框时由输入框自行处理（有选中文字则复制）；此处覆盖焦点在其它区域的场景
            HandleCancelRequested();
            return true;
        }

        // 聊天界面只有输入框需要键盘输入：Tab/Shift+Tab 不再在视图间切换焦点，
        // 避免焦点落入消息列表后字母输入被视图吞掉导致"无法输入"；
        // 同时把焦点交还输入框，兼作焦点丢失时的恢复手段。
        if (key == Key.Tab || key == Key.Tab.WithShift)
        {
            inputField.SetFocus();
            return true;
        }

        // 焦点不在输入框时按 Esc（如误点消息列表后）：把焦点交还输入框并停止传播。
        // 框架默认把 Esc 绑定到 Command.Quit（Application.cs 默认键绑定），
        // 若放任其冒泡，界面会进入失效/退出路径，表现为"按 Esc 后无法操作"。
        if (key == Key.Esc && !inputField.HasFocus)
        {
            inputField.SetFocus();
            return true;
        }

        return base.OnKeyDown(key);
    }

    /// <summary>
    /// 首次布局完成或终端尺寸变化后触发：把焦点交给输入框（只执行一次），
    /// 并按当前窗口尺寸补齐左侧导轨与分隔线，保证缩放后无空白。
    /// </summary>
    /// <param name="e">布局事件参数。</param>
    protected override void OnSubViewsLaidOut(LayoutEventArgs e)
    {
        base.OnSubViewsLaidOut(e);
        RefreshDivider();

        if (!inputFocusInitialized)
        {
            inputFocusInitialized = true;
            inputField.SetFocus();
        }
    }

    /// <summary>
    /// 输入框提交：审批模式校验 1/0；有待发图片时合并图片路径与输入文字构造 /image 消息；
    /// 运行期间进入排队队列；空闲时桥接给业务线程。
    /// </summary>
    private void OnInputAccepted()
    {
        var text = ChatPromptReader.NormalizeLineEndings(inputField.Text).Trim();
        ClearInputDraft();

        if (isToolApprovalPending)
        {
            // 审批阶段只发送 1/0 纯文本：若输入合法则发送并清空暂存图片，
            // 避免把图片拼进审批回复；输入非法则提示并保留图片。
            if (text is not ("1" or "0"))
            {
                statusLabel.Text = "工具审批只能输入 1（同意）或 0（拒绝）";
                SetNeedsDraw();
                return;
            }

            pendingImagePaths.Clear();
            RefreshPendingImages();
            CompleteInput(text);
            return;
        }

        // 有待发图片时：图片路径 + 输入文字合并为 /image 消息，一次性发送
        var hasPendingImages = pendingImagePaths.Count > 0;
        var message = hasPendingImages ? BuildImageInput(text) : text;
        if (hasPendingImages)
        {
            // 图片已随消息发送，清空暂存区并刷新待发提示
            pendingImagePaths.Clear();
            RefreshPendingImages();
        }

        if (isOperationRunning)
        {
            // Agent 运行期间输入进入排队队列，下一轮统一发送
            pendingUserMessageQueue.Enqueue(message);
            queuedMessageCount = pendingUserMessageQueue.CreateSnapshot().Count;
            RefreshStatus();
            return;
        }

        CompleteInput(message);
    }

    /// <summary>
    /// Ctrl+V 图片粘贴：剪贴板含图片时保存图片并加入待发送暂存区（不立即发送，
    /// 用户可继续输入文字，Enter 时图片与文字一起发送，支持多张累积）；
    /// 剪贴板无图片时返回 <see langword="false" /> 放行默认文本粘贴。
    /// </summary>
    /// <returns>图片粘贴已消费按键时返回 <see langword="true" />。</returns>
    private bool OnPasteImageRequested()
    {
        // 工具审批阶段只接受 1/0，不接受图片消息
        if (isToolApprovalPending)
        {
            statusLabel.Text = "工具审批中，图片粘贴不可用";
            SetNeedsDraw();
            return true;
        }

        // 当前模型不支持视觉时直接拦截，避免白保存剪贴板图片；
        // 提示后消费按键，防止图片二进制字节被当成文本塞进输入框
        if (!llmSettingsService.CurrentLlm.SupportsVision)
        {
            statusLabel.Text = "当前模型不支持图片输入，请切换支持视觉的模型";
            SetNeedsDraw();
            return true;
        }

        if (!clipboardImageStore.TrySaveClipboardImages(out var relativePaths) || relativePaths.Count == 0)
        {
            // 剪贴板无图片，放行基类默认文本粘贴
            return false;
        }

        // 多张图片累积加入待发送暂存区，等待用户输入文字后随 Enter 一并发送
        pendingImagePaths.AddRange(relativePaths);
        RefreshPendingImages();
        RefreshStatus();
        return true;
    }

    /// <summary>
    /// 构造 /image 消息输入：图片路径集合 + 可选文字说明。
    /// </summary>
    /// <param name="text">用户在输入框已输入的文字。</param>
    /// <returns>可交给 /image 解析链路的完整输入。</returns>
    private string BuildImageInput(string text)
    {
        var imagePaths = string.Join(' ', pendingImagePaths);
        return string.IsNullOrWhiteSpace(text)
            ? $"/image {imagePaths}"
            : $"/image {imagePaths} {text}";
    }

    /// <summary>
    /// 刷新待发图片提示栏：显示已粘贴待发送的图片数量。
    /// </summary>
    private void RefreshPendingImages()
    {
        pendingImagesLabel.Text = pendingImagePaths.Count > 0
            ? $"  📎 已粘贴 {pendingImagePaths.Count} 张图片待发送，输入文字后 Enter 一并发送"
            : string.Empty;
        SetNeedsDraw();
    }

    /// <summary>
    /// 清空输入框草稿并复位光标，供消息提交或 Esc 清除后调用。
    /// 输入区高度固定，无需重新调整布局。
    /// </summary>
    private void ClearInputDraft()
    {
        inputField.Text = string.Empty;
        inputField.InvokeCommand(Command.Start);
    }

    /// <summary>
    /// Ctrl+C 取消处理：有运行中任务时取消任务，空闲时请求退出应用。
    /// </summary>
    private void HandleCancelRequested()
    {
        if (cancellationCoordinator.TryCancelCurrentOperation())
        {
            statusLabel.Text = "正在取消当前任务...";
            SetNeedsDraw();
        }
        else
        {
            RequestExit();
        }
    }

    /// <summary>
    /// 请求退出应用：完成挂起的输入读取并停止当前 runnable 会话。
    /// </summary>
    private void RequestExit()
    {
        if (stopRequested)
        {
            return;
        }

        stopRequested = true;
        CompleteInput(null);
        ExitRequested?.Invoke();
        app.RequestStop(this);
    }

    private void CompleteInput(string? text)
    {
        lock (inputGate)
        {
            if (pendingInput is null)
            {
                return;
            }

            pendingInput.TrySetResult(text);
            pendingInput = null;
            inputCancellation.Dispose();
        }
    }

    private void CancelPendingInput()
    {
        lock (inputGate)
        {
            if (pendingInput is null)
            {
                return;
            }

            pendingInput.TrySetResult(null);
            pendingInput = null;
            inputCancellation.Dispose();
        }
    }

    /// <summary>
    /// 按窗口当前宽度重新生成品牌分隔线，保证终端缩放后分隔线始终铺满且无空白。
    /// </summary>
    private void RefreshDivider()
    {
        // 分隔线占满窗口宽度，文本长度取当前可用列数。
        var width = Math.Max(1, Viewport.Width);
        if (width == renderedDividerWidth)
        {
            return;
        }

        renderedDividerWidth = width;
        dividerLabel.Text = new string('─', width);
    }

    /// <summary>
    /// 刷新顶部状态行：显示工作目录、当前 provider、模型 ID、推理强度与运行状态。
    /// </summary>
    private void RefreshHeader()
    {
        var cwd = workspaceContextAccessor.Current.WorkingDirectory;
        var provider = llmSettingsService.CurrentProvider;
        var model = llmSettingsService.CurrentLlm.Model;
        var effort = llmSettingsService.CurrentEffort.ToString().ToLowerInvariant();
        var activity = isOperationRunning ? "● running" : "✓ ready";

        cwdLabel.Text = $"{cwd}  ·  provider {provider}  ·  model {model}  ·  effort {effort}  ·  {activity}";
        cwdLabel.SetScheme(isOperationRunning
            ? TuiStyles.GetRunningScheme()
            : TuiStyles.GetReadyScheme());
    }

    /// <summary>
    /// 刷新输入框下方提示：首字符为斜杠时展示当前支持的命令，否则展示常规快捷键。
    /// </summary>
    private void RefreshHint()
    {
        hintLabel.Text = ChatPromptReader.GetInputHint(inputField.Text);
        SetNeedsDraw();
    }

    private void RefreshStatus()
    {
        var parts = new List<string> { $"context {contextTokenUsage:N0}" };
        if (isToolApprovalPending)
        {
            parts.Add("approval: 1 allow / 0 deny");
        }

        if (queuedMessageCount > 0)
        {
            parts.Add($"queued {queuedMessageCount}");
        }

        if (messageList.HasUnread)
        {
            parts.Add("↓ unread");
        }

        statusLabel.Text = "  " + string.Join("  ·  ", parts);
    }
}
