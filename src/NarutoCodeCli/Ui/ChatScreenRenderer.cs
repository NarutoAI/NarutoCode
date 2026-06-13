using NarutoCode.Domain.Messages;
using NarutoCode.Domain.Workspaces;
using Spectre.Console;
using Spectre.Console.Rendering;

namespace NarutoCodeCli.Ui;

/// <summary>
/// 负责构建和刷新聊天 TUI 画布，避免交互流程类直接拼接 Spectre 组件。
/// </summary>
internal sealed class ChatScreenRenderer(
    IWorkspaceContextAccessor workspaceContextAccessor,
    PendingUserMessageQueue pendingUserMessageQueue)
{
    private const string MessageLinePrefix = "    ";
    private const string AgentEventLinePrefix = "      ";
    private static readonly TuiColorPalette Palette = TuiColorPalettes.Current;

    private readonly Dictionary<ChatMessage, CachedMessageRender> messageRenderCache = [];

    /// <summary>
    /// 清屏并渲染当前会话状态。
    /// </summary>
    /// <param name="sessionState">当前 CLI 会话视图状态。</param>
    public void Render(ChatSessionState sessionState)
    {
        ArgumentNullException.ThrowIfNull(sessionState);

        AnsiConsole.Clear();
        AnsiConsole.Write(BuildScreen(sessionState));
    }

    /// <summary>
    /// 在 Live 区域内刷新会话画布，确保流式输出从屏幕顶部重绘而不是追加在输入行之后。
    /// </summary>
    /// <param name="sessionState">当前 CLI 会话视图状态。</param>
    /// <param name="updateAsync">执行状态更新的回调，参数用于触发画布刷新。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    public async Task RenderLiveAsync(
        ChatSessionState sessionState,
        Func<Action, Task> updateAsync,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(sessionState);
        ArgumentNullException.ThrowIfNull(updateAsync);

        if (Console.IsOutputRedirected)
        {
            await updateAsync(() => { });
            Render(sessionState);
            return;
        }

        AnsiConsole.Clear();
        await AnsiConsole.Live(BuildScreen(sessionState))
            .AutoClear(false)
            .StartAsync(async context =>
            {
                // 先刷新首帧，避免模型返回首个片段前清屏后长时间白屏。
                context.UpdateTarget(BuildScreen(sessionState));
                context.Refresh();

                await updateAsync(() =>
                {
                    context.UpdateTarget(BuildScreen(sessionState));
                    context.Refresh();
                });
            });
    }

    private IRenderable BuildScreen(ChatSessionState sessionState)
    {
        PruneMessageRenderCache(sessionState.Messages);

        var rows = new List<IRenderable>
        {
            CreateBrandHeader(sessionState),
            new Text(string.Empty),
            CreateConversationStream(sessionState),
            new Text(string.Empty),
            CreateTokenUsageFooter(sessionState),
            new Text(string.Empty)
        };

        if (sessionState.IsOperationRunning && !sessionState.IsToolApprovalPending)
        {
            rows.Add(CreateQueuedInputPrompt());
        }

        return new Rows(rows);
    }

    private IRenderable CreateQueuedInputPrompt()
    {
        var rows = new List<IRenderable>();
        var queuedInputs = pendingUserMessageQueue.CreateSnapshot();
        if (queuedInputs.Count > 0)
        {
            rows.Add(new Markup($"[bold {Palette.Accent}]queued messages[/]"));
            for (var index = 0; index < queuedInputs.Count; index++)
            {
                rows.Add(new Markup($"  [{Palette.Subtle}]{index + 1}.[/] [{Palette.Ink}]{Markup.Escape(queuedInputs[index])}[/]"));
            }
        }

        var draft = pendingUserMessageQueue.Draft;
        rows.Add(new Markup(
            $"[{Palette.Subtle}]╰─[/] [bold {Palette.Accent}]ask[/] [{Palette.Subtle}]›[/] [{Palette.Ink}]{Markup.Escape(draft)}[/]"));
        return new Rows(rows);
    }

    private IRenderable CreateBrandHeader(ChatSessionState sessionState)
    {
        var cwd = workspaceContextAccessor.Current.WorkingDirectory;

        var operationStatus = sessionState.IsOperationRunning
            ? $"   [{Palette.Muted}]status[/] [bold {Palette.Accent}]running[/]   [{Palette.Muted}]press[/] [bold {Palette.Danger}]Ctrl+C[/] [{Palette.Muted}]to cancel[/]"
            : string.Empty;

        return new Rows(
            new Markup(
                $"[bold {Palette.Accent}]◆ NarutoCode[/] [{Palette.Muted}]agentic coding TUI[/] [{Palette.Subtle}]·[/] [bold {Palette.Secondary}]Command Canvas[/]"),
            new Markup(
                $"[{Palette.Muted}]cwd[/] [{Palette.Ink}]{Markup.Escape(cwd)}[/]   [{Palette.Muted}]mode[/] [{Palette.Accent}]chat[/]{operationStatus}"));
    }

    private static IRenderable CreateTokenUsageFooter(ChatSessionState sessionState)
    {
        return new Markup(
            $"[{Palette.Secondary}]◈[/] [bold {Palette.Accent}]context[/] [{Palette.Secondary}]{sessionState.EstimatedTokens}[/]");
    }

    private IRenderable CreateConversationStream(ChatSessionState sessionState)
    {
        var rows = new List<IRenderable>();

        if (sessionState.Messages.Count == 0)
        {
            rows.Add(CreateEmptyConversationCard());
        }
        else
        {
            for (var index = 0; index < sessionState.Messages.Count; index++)
            {
                var message = sessionState.Messages[index];
                var cacheMarkdown = index < sessionState.Messages.Count - 1;
                var isWaitingAssistantMessage = (sessionState.IsOperationRunning || sessionState.IsToolApprovalPending)
                                                && index == sessionState.Messages.Count - 1
                                                && message.Role == ChatRole.Assistant;
                rows.Add(CreateMessageCard(message, cacheMarkdown, isWaitingAssistantMessage));
            }
        }

        return new Rows(rows);
    }

    private static IRenderable CreateEmptyConversationCard()
    {
        return new Rows(
            new Markup($"[bold {Palette.Ink}]Start with intent, not commands.[/]"),
            new Markup($"[{Palette.Muted}]Ask NarutoCode to implement, debug, refactor, review, or explain code.[/]"),
            new Text(string.Empty),
            new Markup(
                $"[{Palette.Accent}]@file[/] [{Palette.Muted}]attach context[/]   [{Palette.Secondary}]/workflow[/] [{Palette.Muted}]guided actions[/]   [{Palette.Warning}]?[/] [{Palette.Muted}]shortcuts[/]"));
    }

    private IRenderable CreateMessageCard(ChatMessage message, bool cacheMarkdown, bool isWaitingAssistantMessage)
    {
        if (!isWaitingAssistantMessage
            && messageRenderCache.TryGetValue(message, out var cachedRender)
            && cachedRender.Version == message.RenderVersion)
        {
            return cachedRender.Renderable;
        }

        var renderable = CreateMessageCardCore(message, cacheMarkdown, isWaitingAssistantMessage);
        if (!isWaitingAssistantMessage)
        {
            messageRenderCache[message] = new CachedMessageRender(message.RenderVersion, renderable);
        }

        return renderable;
    }

    private static IRenderable CreateMessageCardCore(ChatMessage message, bool cacheMarkdown,
        bool isWaitingAssistantMessage)
    {
        var isUser = message.Role == ChatRole.User;
        var roleLabel = isUser ? "›" : "◇";
        var roleStyle = isUser ? Palette.Accent : Palette.Secondary;

        return new Rows(
            new Markup($"[bold {roleStyle}]{roleLabel}[/]"),
            isUser
                ? CreateUserMessageContent(message, cacheMarkdown)
                : CreateAssistantMessageContent(message, cacheMarkdown, isWaitingAssistantMessage));
    }

    private static IRenderable CreateUserMessageContent(ChatMessage message, bool cacheMarkdown)
    {
        return new Rows(MarkdownConsoleRenderer.Render(message.Content, MessageLinePrefix, cacheMarkdown).ToArray());
    }

    private static IRenderable CreateAssistantMessageContent(ChatMessage message, bool cacheMarkdown,
        bool isWaitingAssistantMessage)
    {
        if (message.AgentMessages.Count == 0)
        {
            return new Markup($"    [bold {Palette.Accent}]⏳ waiting for response...[/]");
        }

        var rows = new List<IRenderable>();
        foreach (var agentMessage in message.AgentMessages)
        {
            rows.Add(CreateAgentMessageContent(agentMessage, cacheMarkdown));
        }

        if (isWaitingAssistantMessage)
        {
            rows.Add(new Markup($"    [bold {Palette.Accent}]⏳ waiting for response...[/]"));
        }

        return new Rows(rows);
    }

    private static IRenderable CreateAgentMessageContent(AgentMessage message, bool cacheMarkdown)
    {
        return message.Type switch
        {
            AgentMessageType.Thinking => CreateAgentEventBlock("thinking", "✧", Palette.Thinking, message.Content, cacheMarkdown),
            AgentMessageType.ToolCall => CreateAgentEventBlock("tool", "✦", Palette.Secondary, message.Content, cacheMarkdown),
            AgentMessageType.ToolApprovalRequest => CreateToolApprovalBlock(message.Content, cacheMarkdown),
            AgentMessageType.Error => CreateErrorBlock(message.Content),
            _ => new Rows(MarkdownConsoleRenderer.Render(message.Content, MessageLinePrefix, cacheMarkdown).ToArray())
        };
    }

    private static IRenderable CreateAgentEventBlock(
        string label,
        string marker,
        string labelStyle,
        string content,
        bool cacheMarkdown)
    {
        return new Rows(
            new Markup($"    [{labelStyle}]{marker}[/] [{labelStyle}]{Markup.Escape(label)}[/]"),
            new Rows(MarkdownConsoleRenderer.Render(content, AgentEventLinePrefix, cacheMarkdown).ToArray()));
    }

    private static IRenderable CreateToolApprovalBlock(string content, bool cacheMarkdown)
    {
        return new Rows(
            new Markup($"    [{Palette.Subtle}]·[/] [{Palette.Secondary}]approval[/] [{Palette.Muted}]tool permission required[/]"),
            new Rows(MarkdownConsoleRenderer.Render(content, AgentEventLinePrefix, cacheMarkdown).ToArray()),
            new Markup($"    [{Palette.Muted}]reply[/] [bold {Palette.Accent}]1[/] [{Palette.Muted}]agree[/]   [bold {Palette.Danger}]0[/] [{Palette.Muted}]deny[/]"));
    }

    private static IRenderable CreateErrorBlock(string content)
    {
        var rows = new List<IRenderable>
        {
            new Markup($"    [{Palette.Subtle}]·[/] [bold {Palette.Danger}]error[/]")
        };

        foreach (var line in content.Replace("\r\n", "\n", StringComparison.Ordinal)
                     .Replace("\r", "\n", StringComparison.Ordinal).Split('\n'))
        {
            rows.Add(new Markup($"{AgentEventLinePrefix}[{Palette.Danger}]{Markup.Escape(line)}[/]"));
        }

        return new Rows(rows);
    }

    private void PruneMessageRenderCache(IReadOnlyList<ChatMessage> currentMessages)
    {
        if (messageRenderCache.Count <= currentMessages.Count + 8)
        {
            return;
        }

        var activeMessages = currentMessages.ToHashSet();
        foreach (var cachedMessage in messageRenderCache.Keys.ToArray())
        {
            if (!activeMessages.Contains(cachedMessage))
            {
                messageRenderCache.Remove(cachedMessage);
            }
        }
    }

    private readonly record struct CachedMessageRender(int Version, IRenderable Renderable);
}