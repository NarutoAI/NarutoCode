using System.Collections.Concurrent;
using NarutoCode.Application.Interactions;
using NarutoCode.Domain.Configurations;
using NarutoCode.Domain.Configurations.Settings;
using NarutoCode.Domain.Conversations;
using NarutoCode.Domain.Enums;
using NarutoCode.Domain.Interactions;
using NarutoCode.Domain.Messages;
using NarutoCode.Domain.Workspaces;
using NarutoCode.Infrastructure;
using Terminal.Gui.App;

namespace NarutoCodeCli.Ui;

/// <summary>
/// TUI 聊天应用入口：负责启动 Terminal.Gui、协调会话入口、会话状态与模型流式输出。
/// 业务逻辑运行在后台线程，所有界面更新通过 app.Invoke 调度到 UI 线程；
/// 消息区滚动位置由消息视图自身维护，任务完成不再整屏重绘。
/// </summary>
internal sealed class TuiChatApplication(
    IConversationService conversationService,
    IClipboardImageStore clipboardImageStore,
    IWorkspaceContextAccessor workspaceContextAccessor,
    ChatCancellationCoordinator cancellationCoordinator,
    PendingUserMessageQueue pendingUserMessageQueue,
    ILlmSettingsService llmSettingsService,
    IUserInteractionManager userInteractionManager)
{
    private static readonly HashSet<string> SupportedImageExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".png",
        ".jpg",
        ".jpeg",
        ".webp",
        ".gif"
    };

    private readonly ChatSessionState sessionState = new();
    private ConversationSessionId sessionId = ConversationSessionId.New();
    private long projectId;

    // 活跃交互登记：交互 Id → 等待态卡片；终态删除卡片并以 TryRemove 去重。
    private readonly ConcurrentDictionary<long, ChatMessage> activeInteractions = new();

    /// <summary>
    /// 运行 TUI 主流程（同步阻塞；调用线程承担 Terminal.Gui 的 Init/Run/Shutdown 生命周期）。
    /// 标准输入或输出被重定向时自动降级为非交互模式。
    /// </summary>
    /// <param name="cancellationToken">取消令牌。</param>
    public void Run(CancellationToken cancellationToken)
    {
        if (Console.IsInputRedirected || Console.IsOutputRedirected)
        {
            RunRedirectedAsync(cancellationToken).GetAwaiter().GetResult();
            return;
        }

        // 会话入口阶段：独立 IApplication 实例。
        // 注意：Terminal.Gui 2.4 同一实例第二次 Run 时新窗口只设置标题、不绘制内容（实测确认），
        // 因此 launcher 与 chat 必须各用一个 Application.Create().Init() 实例。
        var selection = RunLauncherStage(cancellationToken);
        if (selection is null || selection.ShouldExit)
        {
            return;
        }

        // 加载历史会话到会话状态（终端处于普通模式，不遮挡加载过程）
        var history = LoadHistory(selection, cancellationToken);
        sessionId = history.SessionId;
        sessionState.LoadHistory(history);

        // 清理上次进程遗留的等待中交互：当前无 Run 级恢复能力，标记取消并保留审计记录
        userInteractionManager.CancelPendingAsync(sessionId.Value, cancellationToken).GetAwaiter().GetResult();

        // 聊天阶段：独立 IApplication 实例
        RunChatStage(cancellationToken);
    }

    /// <summary>
    /// 运行会话入口窗口（独立 TUI 会话），返回用户选择；取消时返回 <see langword="null" />。
    /// </summary>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>会话入口选择结果，或取消标记。</returns>
    private SessionLauncherResult? RunLauncherStage(CancellationToken cancellationToken)
    {
        using var app = Application.Create();
        app.AppModel = AppModel.FullScreen;
        app.Init();
        // Terminal.Gui 默认用 CSI 18t 查询终端尺寸；macOS Terminal.app 等终端不响应该查询，
        // 框架会认为尺寸为 0 导致整屏空白。这里用 .NET 原生尺寸（ioctl）兜底。
        EnsureDriverScreenSize(app);

        var launcherData = LoadLauncherData(cancellationToken);
        if (launcherData is null)
        {
            return null;
        }

        var launcherWindow = new SessionLauncherWindow(app, launcherData.Value.WorkDirectory, launcherData.Value.Conversations);
        var screenSizeMonitor = StartScreenSizeMonitor(app);
        try
        {
            app.Run(launcherWindow);
            return launcherWindow.SelectionResult;
        }
        finally
        {
            app.RemoveTimeout(screenSizeMonitor);
            launcherWindow.Dispose();
        }
    }

    /// <summary>
    /// 运行聊天主窗口（独立 TUI 会话）：后台线程执行业务循环，当前线程承担 UI 泵。
    /// </summary>
    /// <param name="cancellationToken">取消令牌。</param>
    private void RunChatStage(CancellationToken cancellationToken)
    {
        using var app = Application.Create();
        app.AppModel = AppModel.FullScreen;
        app.Init();
        EnsureDriverScreenSize(app);
        var screenSizeMonitor = StartScreenSizeMonitor(app);

        var chatWindow = new ChatTuiWindow(app, workspaceContextAccessor, pendingUserMessageQueue, cancellationCoordinator, clipboardImageStore, llmSettingsService);
        // 订阅用户交互事件：Agent 工具发起提问时在 UI 线程弹出模态 Dialog
        AttachUserInteraction(app, chatWindow);
        var businessTask = Task.Run(() => RunChatLoopAsync(app, chatWindow, cancellationToken));
        try
        {
            app.Run(chatWindow);
        }
        finally
        {
            app.RemoveTimeout(screenSizeMonitor);
            chatWindow.Dispose();
        }

        businessTask.GetAwaiter().GetResult();
    }

    /// <summary>
    /// 订阅用户交互事件：Agent 工具发起提问时在 UI 线程弹出模态 Dialog，作答后回写终态并在对话流留痕。
    /// </summary>
    /// <param name="app">Terminal.Gui 应用实例（用于 UI 线程调度）。</param>
    /// <param name="chatWindow">聊天主窗口。</param>
    private void AttachUserInteraction(IApplication app, ChatTuiWindow chatWindow)
    {
        // 交互请求：在 UI 线程先创建等待态卡片，再弹出抽屉/输入弹窗；工具线程继续等待 TCS。
        userInteractionManager.InteractionRequested += (request, cancellationToken) =>
        {
            app.Invoke(() =>
            {
                AddPendingInteractionTraceMessage(request);
                chatWindow.UpdateState(sessionState);
                RunInteractionDialog(app, request, chatWindow, cancellationToken);
            });
            return Task.CompletedTask;
        };

        // 交互终态：对话流留痕并刷新界面（含 Esc 取消与 Ctrl+C 运行取消两条路径）
        userInteractionManager.InteractionCompleted += result =>
        {
            app.Invoke(() =>
            {
                AddInteractionTraceMessage(result);
                chatWindow.UpdateState(sessionState);
            });
        };
    }

    /// <summary>
    /// 在 UI 线程运行交互模态弹窗：用户作答/取消后回写终态唤醒工具线程；
    /// 运行取消令牌触发时同步关闭弹窗，避免孤儿 Dialog。
    /// </summary>
    /// <param name="app">Terminal.Gui 应用实例。</param>
    /// <param name="request">交互请求。</param>
    /// <param name="chatWindow">聊天主窗口；弹窗模态期间转发滚动按键翻看历史消息。</param>
    /// <param name="cancellationToken">运行取消令牌（Ctrl+C 链路）。</param>
    private void RunInteractionDialog(IApplication app, UserInteractionRequest request, ChatTuiWindow chatWindow, CancellationToken cancellationToken)
    {
        try
        {
            // Ctrl+C 时请求中止当前运行任务（走现有取消协调器），Esc 仅取消本次交互。
            // 批量题目在同一张抽屉中切换并统一提交，单题继续使用既有弹窗。
            UserInteractionResult? result;
            if (request.Questions.Count > 0)
            {
                var dialog = new BatchInteractionDialog(
                    app, request, chatWindow,
                    requestOperationCancel: () => cancellationCoordinator.TryCancelCurrentOperation());
                using var cancellationRegistration = cancellationToken.Register(
                    () => app.Invoke(() => app.RequestStop(dialog)));
                // 抽屉打开期间收缩消息区到抽屉上方，最新输出不被遮挡
                chatWindow.ReserveBottomRows(dialog.DrawerHeight);
                app.Run(dialog);
                result = dialog.InteractionResult;
            }
            else
            {
                var dialog = new InteractionDialog(
                    app, request, chatWindow: chatWindow,
                    requestOperationCancel: () => cancellationCoordinator.TryCancelCurrentOperation());
                using var cancellationRegistration = cancellationToken.Register(
                    () => app.Invoke(() => app.RequestStop(dialog)));
                // 底部抽屉样式才预留空间；居中输入弹窗不改变消息区布局
                chatWindow.ReserveBottomRows(dialog.DrawerHeight);
                app.Run(dialog);
                result = dialog.InteractionResult;
            }

            // Dialog 停止后读取结果；异常关闭时按取消处理，保证工具线程必然被唤醒
            _ = userInteractionManager.CompleteAsync(
                request.Id,
                result ?? new UserInteractionResult(request.Id, UserInteractionStatus.Cancelled, string.Empty),
                CancellationToken.None);
        }
        catch (Exception)
        {
            // 弹窗异常视为交互不可用：回写取消，避免工具线程永久挂起
            _ = userInteractionManager.CompleteAsync(
                request.Id,
                new UserInteractionResult(request.Id, UserInteractionStatus.Cancelled, string.Empty),
                CancellationToken.None);
        }
        finally
        {
            // 弹窗关闭后恢复消息区完整高度
            chatWindow.ReserveBottomRows(0);
        }
    }

    /// <summary>
    /// 在聊天流创建待回答卡片，并登记为可原地更新的活跃交互。
    /// </summary>
    /// <param name="request">用户交互请求。</param>
    private void AddPendingInteractionTraceMessage(UserInteractionRequest request)
    {
        var trace = ChatMessage.CreateAssistant();
        trace.Append(new AgentMessage(AgentMessageType.Content, FormatInteractionTrace(request, "⏳ 等待你的回答…")));
        sessionState.AddMessage(trace);
        activeInteractions[request.Id] = trace;
    }

    /// <summary>
    /// 交互终态处理：用户提交或取消后均移除临时等待卡片；重复终态只处理一次。
    /// </summary>
    /// <param name="result">交互结果。</param>
    private void AddInteractionTraceMessage(UserInteractionResult result)
    {
        // 首次终态才会命中缓存移除；重复事件直接跳过，避免重复移除。
        if (!activeInteractions.TryRemove(result.InteractionId, out var traceMessage))
        {
            return;
        }

        sessionState.RemoveMessage(traceMessage);
    }

    /// <summary>
    /// 格式化交互卡片：标题、问题、选择题只读选项摘要与当前状态行。
    /// </summary>
    /// <param name="request">交互请求。</param>
    /// <param name="status">等待态或终态摘要。</param>
    /// <returns>聊天流卡片文本。</returns>
    private static string FormatInteractionTrace(UserInteractionRequest request, string status)
    {
        var lines = new List<string>();
        if (!string.IsNullOrWhiteSpace(request.Title))
        {
            lines.Add($"❓ {request.Title}");
        }

        lines.Add(string.IsNullOrWhiteSpace(request.Title) ? $"❓ {request.Question}" : $"   {request.Question}");
        if (request.Type == UserInteractionType.Selection)
        {
            var marker = request.Multiple ? "☐" : "○";
            lines.AddRange(request.Options.Select(option => $"   {marker} {option.Label}"));
        }

        lines.Add($"   ↳ {status}");
        return string.Join(Environment.NewLine, lines);
    }


    /// <summary>
    /// 聊天业务主循环：读取输入（窗口 TCS 桥接）→ 处理 → 流式输出，直到退出。
    /// </summary>
    /// <param name="app">Terminal.Gui 应用实例（用于 UI 线程调度）。</param>
    /// <param name="chatWindow">聊天窗口。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    private async Task RunChatLoopAsync(IApplication app, ChatTuiWindow chatWindow, CancellationToken cancellationToken)
    {
        var exitRequested = false;
        chatWindow.ExitRequested += () => exitRequested = true;

        try
        {
            Action refreshUi = () => InvokeUi(app, () => chatWindow.UpdateState(sessionState));
            refreshUi();

            while (!cancellationToken.IsCancellationRequested && !exitRequested)
            {
                var requiresToolApproval = sessionState.IsToolApprovalPending;
                var input = !requiresToolApproval && pendingUserMessageQueue.TryDrain(out var queuedInput)
                    ? queuedInput
                    : await chatWindow.ReadInputAsync(cancellationToken);

                if (input is null)
                {
                    break;
                }

                var shouldContinue = await HandleInputAsync(input, requiresToolApproval, refreshUi, cancellationToken);
                if (!shouldContinue)
                {
                    break;
                }
            }
        }
        finally
        {
            // 业务结束（含异常）时请求 UI 泵退出；用户主动退出时窗口已自行停止
            if (!exitRequested)
            {
                app.Invoke(() => app.RequestStop(chatWindow));
            }
        }
    }

    /// <summary>
    /// 处理一条用户输入（命令解析、消息发送与流式输出）；返回 <see langword="false" /> 表示需要退出。
    /// </summary>
    /// <param name="input">用户输入。</param>
    /// <param name="requiresToolApproval">是否正在等待工具审批。</param>
    /// <param name="refreshUi">状态变化后的界面刷新回调。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>继续运行时返回 <see langword="true" />。</returns>
    private async Task<bool> HandleInputAsync(
        string input,
        bool requiresToolApproval,
        Action refreshUi,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(input))
        {
            refreshUi();
            return true;
        }

        if (requiresToolApproval && !ChatPromptReader.IsToolApprovalResponse(input))
        {
            refreshUi();
            return true;
        }

        if (!requiresToolApproval && IsExitCommand(input))
        {
            return false;
        }

        if (!requiresToolApproval && IsProviderCommand(input))
        {
            HandleProviderCommand(input);
            refreshUi();
            return true;
        }

        if (!requiresToolApproval && IsEffortCommand(input))
        {
            HandleEffortCommand(input);
            refreshUi();
            return true;
        }

        if (!TryCreateOutgoingMessage(input, requiresToolApproval, out var outgoingMessage, out var displayContent,
                out var error))
        {
            var errorMessage = ChatMessage.CreateAssistant();
            errorMessage.Append(new AgentMessage(AgentMessageType.Error, error));
            sessionState.AddMessage(errorMessage);
            refreshUi();
            return true;
        }

        sessionState.AddMessage(ChatMessage.CreateUser(displayContent));
        var assistantMessage = ChatMessage.CreateAssistant();
        sessionState.AddMessage(assistantMessage);

        using var operationCancellationTokenSource = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        cancellationCoordinator.RegisterOperation(operationCancellationTokenSource);
        sessionState.MarkOperationRunning();
        refreshUi();

        try
        {
            var hasError = await StreamAssistantMessageAsync(
                outgoingMessage,
                assistantMessage,
                refreshUi,
                operationCancellationTokenSource.Token);
            if (outgoingMessage.Type == AgentMessageType.ToolApprovalResponse && !hasError)
            {
                sessionState.CompleteToolApproval(outgoingMessage.ToolApprovalContent);
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            return false;
        }
        catch (OperationCanceledException) when (operationCancellationTokenSource.IsCancellationRequested)
        {
            await conversationService.ResetRuntimeSessionAsync(sessionId, CancellationToken.None);
            assistantMessage.Append(new AgentMessage(AgentMessageType.Error, "当前操作已取消。"));
        }
        finally
        {
            sessionState.MarkOperationCompleted();
            cancellationCoordinator.ClearOperation(operationCancellationTokenSource);
            refreshUi();
        }

        return true;
    }

    /// <summary>
    /// 流式接收模型输出并逐段刷新界面。
    /// </summary>
    /// <param name="outgoingMessage">发送给 Agent 的消息。</param>
    /// <param name="assistantMessage">助手消息视图模型。</param>
    /// <param name="refreshUi">界面刷新回调。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>是否产生过错误消息。</returns>
    private async Task<bool> StreamAssistantMessageAsync(
        AgentMessage outgoingMessage,
        ChatMessage assistantMessage,
        Action refreshUi,
        CancellationToken cancellationToken)
    {
        var hasError = false;

        // 会话作用域：AsyncLocal 随异步调用链向 MAF 工具执行线程流动当前会话标识（ask_user 工具读取）
        using var sessionScope = userInteractionManager.BeginSessionScope(sessionId.Value);

        await foreach (var chunk in conversationService.SendMessageAsync(sessionId, outgoingMessage, cancellationToken))
        {
            assistantMessage.Append(chunk);

            if (chunk.Type == AgentMessageType.ToolApprovalRequest)
            {
                sessionState.MarkToolApprovalPending(chunk);
            }

            if (chunk.Type == AgentMessageType.Error)
            {
                hasError = true;
            }

            refreshUi();
        }

        return hasError;
    }

    /// <summary>
    /// 将界面更新调度到 UI 线程并等待其完成，避免 UI 线程读取会话状态时与业务线程写入并发。
    /// </summary>
    /// <param name="app">Terminal.Gui 应用实例。</param>
    /// <param name="action">需要在 UI 线程执行的动作。</param>
    private static void InvokeUi(IApplication app, Action action)
    {
        using var completed = new ManualResetEventSlim();
        app.Invoke(() =>
        {
            try
            {
                action();
            }
            finally
            {
                completed.Set();
            }
        });
        completed.Wait();
    }

    /// <summary>
    /// 启动运行期间的终端尺寸同步：部分编辑器内嵌终端不会把面板 resize 及时通知 Terminal.Gui，
    /// 因此在 UI 线程定期比对 .NET 终端尺寸与 Driver 尺寸，仅在发生差异时更新绘制画布。
    /// </summary>
    /// <param name="app">Terminal.Gui 应用实例。</param>
    /// <returns>可用于取消定时器的令牌。</returns>
    private static object StartScreenSizeMonitor(IApplication app)
    {
        return app.AddTimeout(TimeSpan.FromMilliseconds(100), () =>
        {
            SynchronizeDriverScreenSize(app, fallbackToDefault: false);
            return true;
        }) ?? throw new InvalidOperationException("无法创建终端尺寸同步定时器。");
    }

    /// <summary>
    /// 兜底设置终端尺寸：当 Terminal.Gui 的 CSI 18t 尺寸查询未被终端响应（如 macOS Terminal.app）
    /// 导致 Driver.Cols/Rows 为 0 时，用 .NET 原生终端尺寸（基于 ioctl）恢复屏幕大小，避免整屏空白。
    /// </summary>
    /// <param name="app">Terminal.Gui 应用实例。</param>
    private static void EnsureDriverScreenSize(IApplication app)
    {
        SynchronizeDriverScreenSize(app, fallbackToDefault: true);
    }

    /// <summary>
    /// 将 .NET 终端尺寸同步到 Terminal.Gui Driver。Driver 尺寸已匹配时不执行反射调用，
    /// 避免普通绘制帧产生额外布局和输出缓冲重建。
    /// </summary>
    /// <param name="app">Terminal.Gui 应用实例。</param>
    /// <param name="fallbackToDefault">读取终端尺寸失败时是否回退到 80x25。</param>
    private static void SynchronizeDriverScreenSize(IApplication app, bool fallbackToDefault)
    {
        var driver = app.Driver;
        if (driver is null)
        {
            return;
        }

        int width;
        int height;
        try
        {
            width = Console.WindowWidth;
            height = Console.WindowHeight;
        }
        catch (IOException) when (fallbackToDefault)
        {
            // 极少数终端不支持查询窗口尺寸时，启动阶段回退到可用的默认画布。
            width = 80;
            height = 25;
        }
        catch (IOException)
        {
            // 运行期间查询失败时保留既有 Driver 尺寸，避免无意义地抖动画布。
            return;
        }

        if (width <= 0 || height <= 0 || (driver.Cols == width && driver.Rows == height))
        {
            return;
        }

        // SetScreenSize 定义在 internal DriverImpl 上（公开 virtual 方法），反射调用以重建输出缓冲并广播尺寸变化。
        var setScreenSize = driver.GetType().GetMethod("SetScreenSize", [typeof(int), typeof(int)]);
        setScreenSize?.Invoke(driver, [width, height]);
    }

    /// <summary>
    /// 非交互降级模式：标准输入逐行读取，输出以纯文本打印。
    /// </summary>
    /// <param name="cancellationToken">取消令牌。</param>
    private async Task RunRedirectedAsync(CancellationToken cancellationToken)
    {
        var launcherData = LoadLauncherData(cancellationToken);
        if (launcherData is null)
        {
            return;
        }

        // 重定向场景无法交互选择，默认继续最近会话；没有历史则新建
        var selection = launcherData.Value.Conversations.Count == 0
            ? SessionLauncherResult.NewConversation()
            : SessionLauncherResult.Existing(new ConversationSessionId(launcherData.Value.Conversations[0].Id));
        var history = LoadHistory(selection, cancellationToken);
        sessionId = history.SessionId;
        sessionState.LoadHistory(history);

        var printedMessageCount = 0;
        void PrintNewAssistantMessages()
        {
            var messages = sessionState.Messages;
            for (var index = printedMessageCount; index < messages.Count; index++)
            {
                if (messages[index].Role == ChatRole.Assistant)
                {
                    Console.WriteLine(messages[index].Content);
                }
            }

            printedMessageCount = messages.Count;
        }

        while (!cancellationToken.IsCancellationRequested)
        {
            var input = Console.ReadLine();
            if (input is null)
            {
                break;
            }

            var requiresToolApproval = sessionState.IsToolApprovalPending;
            var shouldContinue = await HandleInputAsync(input, requiresToolApproval, PrintNewAssistantMessages, cancellationToken);
            if (!shouldContinue)
            {
                break;
            }
        }
    }

    /// <summary>
    /// 加载会话入口数据（工作区与会话摘要）。
    /// </summary>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>入口页数据；取消时返回 <see langword="null" />。</returns>
    private (string WorkDirectory, IReadOnlyList<ConversationSummary> Conversations)? LoadLauncherData(
        CancellationToken cancellationToken)
    {
        try
        {
            var workDirectory = workspaceContextAccessor.Current.WorkingDirectory;
            var workspace = RunSync(() => conversationService.GetOrCreateWorkspaceAsync(workDirectory, cancellationToken));
            projectId = workspace.Id;
            var conversations = RunSync(() => conversationService.ListProjectConversationsAsync(projectId, cancellationToken));
            return (workspace.WorkDirectory, conversations);
        }
        catch (OperationCanceledException)
        {
            return null;
        }
    }

    /// <summary>
    /// 按入口选择加载会话历史。
    /// </summary>
    /// <param name="selection">会话入口选择结果。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>会话历史。</returns>
    private ConversationHistory LoadHistory(SessionLauncherResult selection, CancellationToken cancellationToken)
    {
        return RunSync(() => selection switch
        {
            { CreateNew: true } => conversationService.CreateProjectConversationAsync(projectId, cancellationToken),
            { ConversationId: { } conversationId } => conversationService.LoadConversationHistoryAsync(
                conversationId,
                cancellationToken),
            _ => conversationService.LoadWorkspaceHistoryAsync(
                workspaceContextAccessor.Current.WorkingDirectory,
                cancellationToken)
        });
    }

    /// <summary>
    /// 同步等待异步任务完成（无同步上下文时直接解包结果）。
    /// </summary>
    private static T RunSync<T>(Func<Task<T>> factory)
    {
        return factory().GetAwaiter().GetResult();
    }

    private void HandleProviderCommand(string input)
    {
        var arguments = ChatPromptReader.SplitArguments(input);
        var assistantMessage = ChatMessage.CreateAssistant();

        if (arguments.Count == 1)
        {
            assistantMessage.Append(new AgentMessage(
                AgentMessageType.Content,
                CreateProviderStatusContent()));
            sessionState.AddMessage(assistantMessage);
            return;
        }

        var provider = arguments[1];
        try
        {
            llmSettingsService.SwitchProvider(provider);
            assistantMessage.Append(new AgentMessage(
                AgentMessageType.Content,
                $"已切换当前 provider：{llmSettingsService.CurrentProvider}"));
        }
        catch (InvalidOperationException exception)
        {
            assistantMessage.Append(new AgentMessage(
                AgentMessageType.Error,
                $"切换 provider 失败：{exception.Message}\n\n{CreateProviderStatusContent()}"));
        }

        sessionState.AddMessage(assistantMessage);
    }

    private string CreateProviderStatusContent()
    {
        var providers = llmSettingsService.GetAvailableProviders();
        var providerLines = providers.Select(provider =>
            string.Equals(provider, llmSettingsService.CurrentProvider, StringComparison.OrdinalIgnoreCase)
                ? $"- {provider}（当前）"
                : $"- {provider}");

        return $"当前 provider：{llmSettingsService.CurrentProvider}\n\n可用 provider：\n{string.Join(Environment.NewLine, providerLines)}\n\n使用 /provider <provider> 切换。";
    }

    private static bool IsProviderCommand(string input)
    {
        return input.Equals("/provider", StringComparison.OrdinalIgnoreCase)
               || input.StartsWith("/provider ", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// 处理推理强度切换命令。
    /// </summary>
    /// <param name="input">用户输入。</param>
    private void HandleEffortCommand(string input)
    {
        var arguments = ChatPromptReader.SplitArguments(input);
        var assistantMessage = ChatMessage.CreateAssistant();

        if (arguments.Count == 1)
        {
            assistantMessage.Append(new AgentMessage(
                AgentMessageType.Content,
                CreateEffortStatusContent()));
            sessionState.AddMessage(assistantMessage);
            return;
        }

        if (!TryParseEffort(arguments[1], out var effort))
        {
            assistantMessage.Append(new AgentMessage(
                AgentMessageType.Error,
                $"切换 effort 失败：不支持的推理强度 {arguments[1]}。\n\n{CreateEffortStatusContent()}"));
            sessionState.AddMessage(assistantMessage);
            return;
        }

        llmSettingsService.SwitchEffort(effort);
        assistantMessage.Append(new AgentMessage(
            AgentMessageType.Content,
            $"已切换当前 effort：{FormatEffort(llmSettingsService.CurrentEffort)}"));
        sessionState.AddMessage(assistantMessage);
    }

    private string CreateEffortStatusContent()
    {
        var effortLines = llmSettingsService.GetAvailableEfforts().Select(effort =>
            effort == llmSettingsService.CurrentEffort
                ? $"- {FormatEffort(effort)}（当前）"
                : $"- {FormatEffort(effort)}");

        return $"当前 effort：{FormatEffort(llmSettingsService.CurrentEffort)}\n\n可用 effort：\n{string.Join(Environment.NewLine, effortLines)}\n\n使用 /effort <low|medium|high|xhigh> 切换。";
    }

    private static bool TryParseEffort(string input, out LlmEffort effort)
    {
        return Enum.TryParse(input, ignoreCase: true, out effort);
    }

    private static string FormatEffort(LlmEffort effort)
    {
        return effort.ToString().ToLowerInvariant();
    }

    private static bool IsEffortCommand(string input)
    {
        return input.Equals("/effort", StringComparison.OrdinalIgnoreCase)
               || input.StartsWith("/effort ", StringComparison.OrdinalIgnoreCase);
    }

    private bool TryCreateOutgoingMessage(
        string input,
        bool requiresToolApproval,
        out AgentMessage message,
        out string displayContent,
        out string error)
    {
        if (requiresToolApproval)
        {
            message = sessionState.CreateOutgoingMessage(input);
            displayContent = input;
            error = string.Empty;
            return true;
        }

        var imageInput = NormalizeImageInput(input);
        if (imageInput is null)
        {
            message = sessionState.CreateOutgoingMessage(input);
            displayContent = input;
            error = string.Empty;
            return true;
        }

        // 当前模型不支持视觉时拒绝图片消息：图片会被后端过滤，发送后模型也看不到，
        // 直接在此拦截并提示用户切换支持视觉的模型，避免白保存/白发送。
        if (!llmSettingsService.CurrentLlm.SupportsVision)
        {
            message = default;
            displayContent = string.Empty;
            error = "当前模型不支持图片输入（SupportsVision=false），请使用 /provider 切换支持视觉的模型。";
            return false;
        }

        var arguments = ChatPromptReader.SplitArguments(imageInput);
        if (arguments.Count < 2)
        {
            message = default;
            displayContent = string.Empty;
            error = "图片消息格式：/image <图片路径1> <图片路径2> ... <文字>。";
            return false;
        }

        var attachments = new List<AgentMessageAttachment>();
        var textStartIndex = arguments.Count;
        for (var index = 1; index < arguments.Count; index++)
        {
            var mediaType = ResolveImageMediaType(arguments[index]);
            if (mediaType is null)
            {
                textStartIndex = index;
                break;
            }

            var imagePath = ResolveWorkspacePath(arguments[index]);
            if (!File.Exists(imagePath))
            {
                message = default;
                displayContent = string.Empty;
                error = $"图片文件不存在：{imagePath}";
                return false;
            }

            var imageData = File.ReadAllBytes(imagePath);
            attachments.Add(new AgentMessageAttachment(imageData, mediaType));
        }

        if (attachments.Count == 0)
        {
            message = default;
            displayContent = string.Empty;
            error = "至少需要提供一张图片，支持 png、jpg、jpeg、webp、gif。";
            return false;
        }

        var content = textStartIndex < arguments.Count
            ? string.Join(' ', arguments.Skip(textStartIndex))
            : "请分析图片内容。";
        message = new AgentMessage(AgentMessageType.Content, content, attachments: attachments);
        displayContent = CreateImageDisplayContent(content, attachments);
        error = string.Empty;
        return true;
    }

    /// <summary>
    /// 将图片相关斜杠命令规范化为 /image 输入；非图片命令返回 <see langword="null" />。
    /// </summary>
    /// <param name="input">用户原始输入。</param>
    /// <returns>可继续按 /image 解析的输入，或非图片命令标记。</returns>
    private string? NormalizeImageInput(string input)
    {
        if (input.StartsWith("/image ", StringComparison.OrdinalIgnoreCase))
        {
            return input;
        }

        if (!input.Equals("/pi", StringComparison.OrdinalIgnoreCase)
            && !input.StartsWith("/pi ", StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        var content = input.Length > 3 ? input[3..].TrimStart() : string.Empty;
        if (!clipboardImageStore.TrySaveClipboardImages(out var relativePaths) || relativePaths.Count == 0)
        {
            return string.IsNullOrWhiteSpace(content) ? null : content;
        }

        var imagePaths = string.Join(' ', relativePaths);
        return string.IsNullOrWhiteSpace(content)
            ? $"/image {imagePaths}"
            : $"/image {imagePaths} {content}";
    }

    private string ResolveWorkspacePath(string inputPath)
    {
        return Path.IsPathRooted(inputPath)
            ? Path.GetFullPath(inputPath)
            : Path.GetFullPath(Path.Combine(workspaceContextAccessor.Current.WorkingDirectory, inputPath));
    }

    private static string CreateImageDisplayContent(
        string content,
        IReadOnlyList<AgentMessageAttachment> attachments)
    {
        var imageLines = attachments.Select((attachment, index) => $"image[{index + 1}]: {attachment.MediaType} ({attachment.Data.Length} bytes)");
        return string.Join(Environment.NewLine, imageLines) + Environment.NewLine + content;
    }

    private static string? ResolveImageMediaType(string filePath)
    {
        var extension = Path.GetExtension(filePath);
        if (!SupportedImageExtensions.Contains(extension))
        {
            return null;
        }

        return extension.ToLowerInvariant() switch
        {
            ".png" => "image/png",
            ".jpg" or ".jpeg" => "image/jpeg",
            ".webp" => "image/webp",
            ".gif" => "image/gif",
            _ => null
        };
    }

    private static bool IsExitCommand(string input)
    {
        return input.Equals("/exit", StringComparison.OrdinalIgnoreCase)
               || input.Equals("exit", StringComparison.OrdinalIgnoreCase)
               || input.Equals("quit", StringComparison.OrdinalIgnoreCase);
    }
}
