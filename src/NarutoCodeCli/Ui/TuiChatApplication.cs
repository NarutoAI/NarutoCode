using NarutoCode.Domain.Configurations;
using NarutoCode.Domain.Configurations.Settings;
using NarutoCode.Domain.Conversations;
using NarutoCode.Domain.Messages;
using NarutoCode.Domain.Workspaces;

namespace NarutoCodeCli.Ui;

/// <summary>
/// TUI 聊天应用入口，负责协调输入读取、会话状态和模型流式输出。
/// </summary>
internal sealed class TuiChatApplication(
    IConversationService conversationService,
    ChatPromptReader promptReader,
    ChatScreenRenderer screenRenderer,
    IClipboardImageStore clipboardImageStore,
    IWorkspaceContextAccessor workspaceContextAccessor,
    ChatCancellationCoordinator cancellationCoordinator,
    PendingUserMessageQueue pendingUserMessageQueue,
    QueuedChatInputReader queuedInputReader,
    SessionLauncherRenderer sessionLauncherRenderer,
    SessionLauncherPromptReader sessionLauncherPromptReader,
    ILlmSettingsService llmSettingsService)
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

    /// <summary>
    /// 运行 TUI 主循环，直到用户退出或收到取消请求。
    /// </summary>
    /// <param name="cancellationToken">取消令牌。</param>
    public async Task RunAsync(CancellationToken cancellationToken)
    {
        var launcherResult = await SelectConversationAsync(cancellationToken);
        if (launcherResult.ShouldExit)
        {
            return;
        }

        await LoadHistoryAsync(launcherResult, cancellationToken);
        screenRenderer.Render(sessionState);

        while (!cancellationToken.IsCancellationRequested)
        {
            var requiresToolApproval = sessionState.IsToolApprovalPending;
            var input = !requiresToolApproval && pendingUserMessageQueue.TryDrain(out var queuedInput)
                ? queuedInput
                : await promptReader.ReadAsync(requiresToolApproval);

            if (input is null)
            {
                break;
            }

            if (string.IsNullOrWhiteSpace(input))
            {
                screenRenderer.Render(sessionState);
                continue;
            }

            if (requiresToolApproval && !ChatPromptReader.IsToolApprovalResponse(input))
            {
                screenRenderer.Render(sessionState);
                continue;
            }

            if (!requiresToolApproval && IsExitCommand(input))
            {
                break;
            }
            //处理供应商切换
            if (!requiresToolApproval && IsProviderCommand(input))
            {
                HandleProviderCommand(input);
                screenRenderer.Render(sessionState);
                continue;
            }

            if (!TryCreateOutgoingMessage(input, requiresToolApproval, out var outgoingMessage, out var displayContent,
                    out var error))
            {
                var errorMessage = ChatMessage.CreateAssistant();
                errorMessage.Append(new AgentMessage(AgentMessageType.Error, error));
                sessionState.AddMessage(errorMessage);
                screenRenderer.Render(sessionState);
                continue;
            }

            sessionState.AddMessage(ChatMessage.CreateUser(displayContent));
            var assistantMessage = ChatMessage.CreateAssistant();
            sessionState.AddMessage(assistantMessage);

            using var operationCancellationTokenSource =
                CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            cancellationCoordinator.RegisterOperation(operationCancellationTokenSource);
            sessionState.MarkOperationRunning();

            try
            {
                var hasError = await StreamAssistantMessageAsync(
                    outgoingMessage,
                    assistantMessage,
                    operationCancellationTokenSource.Token);
                if (outgoingMessage.Type == AgentMessageType.ToolApprovalResponse && !hasError)
                {
                    sessionState.CompleteToolApproval(outgoingMessage.ToolApprovalContent);
                }
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                break;
            }
            catch (OperationCanceledException) when (operationCancellationTokenSource.IsCancellationRequested)
            {
                await conversationService.ResetRuntimeSessionAsync(sessionId, CancellationToken.None);
                assistantMessage.Append(new AgentMessage(AgentMessageType.Error, "当前操作已取消。"));
                sessionState.MarkOperationCompleted();
                pendingUserMessageQueue.UpdateDraft(string.Empty);
                screenRenderer.Render(sessionState);
            }
            finally
            {
                sessionState.MarkOperationCompleted();
                cancellationCoordinator.ClearOperation(operationCancellationTokenSource);
            }
        }
    }


    /// <summary>
    /// 
    /// </summary>
    /// <param name="input"></param>
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

    private async Task<SessionLauncherResult> SelectConversationAsync(CancellationToken cancellationToken)
    {
        var workDirectory = workspaceContextAccessor.Current.WorkingDirectory;
        var conversations = await conversationService.ListWorkspaceConversationsAsync(workDirectory, cancellationToken);
        var state = new SessionLauncherState(workDirectory, conversations);

        while (!cancellationToken.IsCancellationRequested)
        {
            sessionLauncherRenderer.Render(state);
            var key = await sessionLauncherPromptReader.ReadKeyAsync(cancellationToken);
            if (state.IsHistoryMode)
            {
                var historyResult = HandleHistoryKey(state, key);
                if (historyResult is not null)
                {
                    return historyResult;
                }

                continue;
            }

            var hubResult = HandleHubKey(state, key);
            if (hubResult is not null)
            {
                return hubResult;
            }
        }

        return SessionLauncherResult.Exit();
    }

    private static SessionLauncherResult? HandleHubKey(SessionLauncherState state, ConsoleKeyInfo key)
    {
        switch (key.Key)
        {
            case ConsoleKey.UpArrow:
                state.MoveHubSelection(-1);
                return null;
            case ConsoleKey.DownArrow:
                state.MoveHubSelection(1);
                return null;
            case ConsoleKey.Escape:
                return SessionLauncherResult.Exit();
            case ConsoleKey.Enter:
                return state.SelectedHubOption switch
                {
                    SessionLauncherOption.ContinueRecent => state.RecentConversation is null
                        ? SessionLauncherResult.NewConversation()
                        : SessionLauncherResult.Existing(new ConversationSessionId(state.RecentConversation.Id)),
                    SessionLauncherOption.ViewHistory => EnterHistoryOrCreate(state),
                    SessionLauncherOption.NewConversation => SessionLauncherResult.NewConversation(),
                    _ => SessionLauncherResult.Exit()
                };
            default:
                return null;
        }
    }

    private static SessionLauncherResult? HandleHistoryKey(SessionLauncherState state, ConsoleKeyInfo key)
    {
        switch (key.Key)
        {
            case ConsoleKey.UpArrow:
                state.MoveHistorySelection(-1);
                return null;
            case ConsoleKey.DownArrow:
                state.MoveHistorySelection(1);
                return null;
            case ConsoleKey.Escape:
                state.ReturnToHub();
                return null;
            case ConsoleKey.N:
                return SessionLauncherResult.NewConversation();
            case ConsoleKey.Enter when state.Conversations.Count > 0:
                return SessionLauncherResult.Existing(
                    new ConversationSessionId(state.Conversations[state.SelectedHistoryIndex].Id));
            default:
                return null;
        }
    }

    private static SessionLauncherResult? EnterHistoryOrCreate(SessionLauncherState state)
    {
        if (state.Conversations.Count == 0)
        {
            return SessionLauncherResult.NewConversation();
        }

        state.EnterHistoryMode();
        return null;
    }

    private async Task LoadHistoryAsync(SessionLauncherResult launcherResult, CancellationToken cancellationToken)
    {
        var history = launcherResult switch
        {
            { CreateNew: true } => await conversationService.CreateWorkspaceConversationAsync(
                workspaceContextAccessor.Current.WorkingDirectory,
                cancellationToken),
            { ConversationId: { } conversationId } => await conversationService.LoadConversationHistoryAsync(
                conversationId,
                cancellationToken),
            _ => await conversationService.LoadWorkspaceHistoryAsync(
                workspaceContextAccessor.Current.WorkingDirectory,
                cancellationToken)
        };

        sessionId = history.SessionId;
        sessionState.LoadHistory(history);
    }

    private async Task<bool> StreamAssistantMessageAsync(
        AgentMessage outgoingMessage,
        ChatMessage assistantMessage,
        CancellationToken cancellationToken)
    {
        var hasError = false;
        var canCaptureQueuedInput = outgoingMessage.Type != AgentMessageType.ToolApprovalResponse;
        using var captureCancellationTokenSource = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        Task? captureTask = null;

        await screenRenderer.RenderLiveAsync(
            sessionState,
            async refresh =>
            {
                if (canCaptureQueuedInput)
                {
                    captureTask = queuedInputReader.CaptureAsync(refresh, captureCancellationTokenSource.Token);
                }

                try
                {
                    await foreach (var chunk in conversationService.SendMessageAsync(sessionId, outgoingMessage,
                                       cancellationToken))
                    {
                        assistantMessage.Append(chunk);

                        if (chunk.Type == AgentMessageType.ToolApprovalRequest)
                        {
                            sessionState.MarkToolApprovalPending(chunk);
                            await captureCancellationTokenSource.CancelAsync();
                            pendingUserMessageQueue.UpdateDraft(string.Empty);
                        }

                        if (chunk.Type == AgentMessageType.Error)
                        {
                            hasError = true;
                        }

                        refresh();
                    }

                    refresh();
                }
                finally
                {
                    await captureCancellationTokenSource.CancelAsync();
                    if (captureTask is not null)
                    {
                        await captureTask.ConfigureAwait(false);
                    }

                    pendingUserMessageQueue.UpdateDraft(string.Empty);
                    sessionState.MarkOperationCompleted();
                    refresh();
                }
            },
            cancellationToken);

        return hasError;
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

            attachments.Add(new AgentMessageAttachment(imagePath, mediaType));
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
        var imageLines = attachments.Select((attachment, index) => $"image[{index + 1}]: {attachment.FilePath}");
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
