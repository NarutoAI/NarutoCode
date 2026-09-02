using System.Runtime.CompilerServices;
using System.Text;
using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;
using NarutoCode.Infrastructure.AIAgents.AIContextProviders.AgentMode;
using NarutoCode.Infrastructure.JsonSerializerContexts;

namespace NarutoCode.Infrastructure.AIAgents.AIContextProviders;

public class ModeAIContextProvider:AIContextProvider
{
    private const string DefaultInstructions =
        """
        ## Agent Mode

        - You can operate in different modes. Depending on the mode you are in, you will be required to follow different processes.

        Use the mode_get tool to check your current operating mode.
        Use the mode_set tool to switch between modes as your work progresses. Only use mode_set if the user explicitly instructs/allows you to change modes.

        You are currently operating in the {current_mode} mode.

        ### Mandatory Mode based Workflow

        For every new substantive user request, including short factual questions, your behavior is determined by the mode you are in.

        {available_modes}
        """;

    private static readonly IReadOnlyList<ModeProviderOptions.AgentMode> s_defaultModes =
    [
        new(
            "plan",
            """
            Use this mode when analyzing requirements, breaking down tasks, and creating plans. This is the interactive mode — ask clarifying questions, discuss options, and get user approval before proceeding.

            Process to follow when in plan mode:
            1. Analyze the request with the purpose of building a research plan.
            2. Create a list of todo items.
            3. If needed, use the provided tools to do some exploratory checks to help build a plan and determine what clarifying questions you may need from the user.
            4. Ask for clarifications from the user where needed.
              1. Ask each clarification one by one.
              2. When asking for clarification and you have specific options in mind, present them to the user, so they can choose the option instead of having to retype the entire response.
              3. Do not proceed until you have received all the needed clarifications.
              4. Do short exploratory research if it helps with being able to ask sensible clarifications from the user.
            5. Write the plan to a memory file, so that it is retained even if compaction happens. Make sure to update the plan file if the user requests changes.
            6. Present the plan to the user and ask for approval to switch to execute mode and process the plan.
            7. When approval is granted, always switch to execute mode (using the `mode_set` tool), and follow the steps for *Execute mode*.
            """),
        new(
            "execute",
            """
            Determine the type of ask:
            1. Simple question that doesn't require any further work to answer.
            2. Any other work, including complex user request that requires a multi-step process to satisfy.

            If 1. just answer the question directly.
            If 2. Work autonomously using your best judgment — do not ask the user questions or wait for feedback and follow the following process:
            1. If you don't have a plan or tasks yet, analyze the user request and create tasks and a plan. (**Skip this step if you came from plan mode**)
            2. Work autonomously — use your best judgment to make decisions and keep progressing without asking the user questions. The goal is to have a complete, useful result ready when the user returns.
            3. If you encounter ambiguity or an unexpected situation during execution, choose the most reasonable option, note your choice, and keep going.
            4. Mark tasks as completed as you finish them.
            5. Continue working, thinking and calling tools until you have the research result for the user.
            """),
    ];

    private readonly ProviderSessionState<ModeState> _sessionState;
    private readonly IReadOnlyList<ModeProviderOptions.AgentMode> _modes;
    private readonly string _defaultMode;
    private readonly string? _instructions;
    private readonly HashSet<string> _validModeNames;
    private readonly string _modeNamesDisplay;
    private readonly ConditionalWeakTable<AgentSession, SemaphoreSlim> _sessionLocks = new();
    private readonly SemaphoreSlim _nullSessionLock = new(1, 1);
    private IReadOnlyList<string>? _stateKeys;

    /// <summary>
    /// Initializes a new instance of the <see cref="AgentModeProvider"/> class.
    /// </summary>
    /// <param name="options">Optional settings that control provider behavior. When <see langword="null"/>, defaults are used.</param>
    public ModeAIContextProvider(ModeProviderOptions? options = null)
    {
        this._modes = options?.Modes ?? s_defaultModes;

        if (this._modes.Count == 0)
        {
            throw new ArgumentException("At least one mode must be configured.", nameof(options));
        }

        this._instructions = options?.Instructions ?? DefaultInstructions;

        this._validModeNames = new HashSet<string>(StringComparer.Ordinal);
        var modeNamesList = new List<string>(this._modes.Count);
        for (int i = 0; i < this._modes.Count; i++)
        {
            var mode = this._modes[i];
            if (mode is null)
            {
                throw new ArgumentException($"Configured mode at index {i} must not be null.", nameof(options));
            }

            if (string.IsNullOrEmpty(mode.Name))
            {
                throw new ArgumentException($"Configured mode at index {i} must have a non-empty name.", nameof(options));
            }

            if (!this._validModeNames.Add(mode.Name))
            {
                throw new ArgumentException($"Configured modes contain a duplicate mode name \"{mode.Name}\".", nameof(options));
            }

            modeNamesList.Add(mode.Name);
        }

        this._modeNamesDisplay = string.Join("\", \"", modeNamesList);
        this._defaultMode = options?.DefaultMode ?? modeNamesList[0];

        if (!this._validModeNames.Contains(this._defaultMode))
        {
            throw new ArgumentException($"Default mode \"{this._defaultMode}\" is not in the configured modes list.", nameof(options));
        }

        this._sessionState = new ProviderSessionState<ModeState>(
            _ => new ModeState {CurrentMode = this._defaultMode},
            this.GetType().Name,
            AIContentJsonSerializerContext.Default.ModeState.Options);
    }

    /// <inheritdoc />
    public override IReadOnlyList<string> StateKeys => this._stateKeys ??= [this._sessionState.StateKey];

    /// <inheritdoc />
    public void Dispose()
    {
        this._nullSessionLock.Dispose();
    }

    /// <summary>
    /// Gets the current operating mode from the session state.
    /// </summary>
    /// <param name="session">The agent session to read the mode from.</param>
    /// <param name="cancellationToken">The <see cref="CancellationToken"/> to monitor for cancellation requests.</param>
    /// <returns>The current mode string.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="session"/> is <see langword="null"/>.</exception>
    public async Task<string> GetModeAsync(AgentSession session, CancellationToken cancellationToken = default)
    {
        SemaphoreSlim sessionLock = this.GetSessionLock(session);
        await sessionLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            return this._sessionState.GetOrInitializeState(session).CurrentMode;
        }
        finally
        {
            sessionLock.Release();
        }
    }

    /// <summary>
    /// Sets the operating mode in the session state.
    /// </summary>
    /// <param name="session">The agent session to update the mode in.</param>
    /// <param name="mode">The new mode to set.</param>
    /// <param name="cancellationToken">The <see cref="CancellationToken"/> to monitor for cancellation requests.</param>
    /// <returns>A <see cref="Task"/> that represents the asynchronous operation.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="session"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentException"><paramref name="mode"/> is not a configured mode.</exception>
    public async Task SetModeAsync(AgentSession session, string mode, CancellationToken cancellationToken = default)
    {
        this.ValidateMode(mode);

        SemaphoreSlim sessionLock = this.GetSessionLock(session);
        await sessionLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            ModeState state = this._sessionState.GetOrInitializeState(session);
            string previousMode = state.CurrentMode;
            state.CurrentMode = mode;

            if (!string.Equals(previousMode, mode, StringComparison.Ordinal))
            {
                state.PreviousModeForNotification = previousMode;
            }

            this._sessionState.SaveState(session, state);
        }
        finally
        {
            sessionLock.Release();
        }
    }

    /// <inheritdoc />
    protected override async ValueTask<AIContext> ProvideAIContextAsync(InvokingContext context, CancellationToken cancellationToken = default)
    {


        string currentMode;
        string? previousModeForNotification;

        SemaphoreSlim sessionLock = this.GetSessionLock(context.Session);
        await sessionLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            ModeState state = this._sessionState.GetOrInitializeState(context.Session);
            currentMode = state.CurrentMode;
            previousModeForNotification = state.PreviousModeForNotification;

            // If the mode was changed externally (e.g., via /mode command), clear the pending
            // notification flag now that we are about to surface it to the agent.
            if (previousModeForNotification != null)
            {
                state.PreviousModeForNotification = null;
                this._sessionState.SaveState(context.Session, state);
            }
        }
        finally
        {
            sessionLock.Release();
        }

        var aiContext = new AIContext
        {
            Instructions = this.BuildInstructions(currentMode),
            Tools = this.CreateTools(context.Session),
        };

        // If the mode was changed externally (e.g., via /mode command), inject a notification message
        // so the agent clearly sees the change rather than relying solely on the system instructions.
        if (previousModeForNotification != null)
        {
            aiContext.Messages =
            [
                new ChatMessage(ChatRole.User, $"[Mode changed: The operating mode has been switched from \"{previousModeForNotification}\" to \"{currentMode}\". You must now adjust your behavior to match the \"{currentMode}\" mode.]"),
            ];
        }

        return aiContext;
    }

    private string BuildInstructions(string currentMode)
    {
        var modesListBuilder = new StringBuilder();
        foreach (var mode in this._modes)
        {
            modesListBuilder.AppendLine($"#### {mode.Name}");
            modesListBuilder.AppendLine();
            modesListBuilder.AppendLine(mode.Instructions.TrimEnd());
            modesListBuilder.AppendLine();
        }

        var modesListText = modesListBuilder.ToString();

        return new StringBuilder(this._instructions)
            .Replace("{available_modes}", modesListText)
            .Replace("{current_mode}", currentMode)
            .ToString();
    }

    private void ValidateMode(string mode)
    {
        if (!this._validModeNames.Contains(mode))
        {
            throw new ArgumentException($"Invalid mode: \"{mode}\". Supported modes are: \"{this._modeNamesDisplay}\".", nameof(mode));
        }
    }

    /// <summary>
    /// Returns the per-session semaphore used to serialize all mode operations.
    /// </summary>
    private SemaphoreSlim GetSessionLock(AgentSession? session)
    {
        if (session is null)
        {
            return this._nullSessionLock;
        }

        return this._sessionLocks.GetValue(session, _ => new SemaphoreSlim(1, 1));
    }

    private AITool[] CreateTools(AgentSession? session)
    {

        return
        [
            AIFunctionFactory.Create(
                async (string mode) =>
                {
                    this.ValidateMode(mode);

                    SemaphoreSlim sessionLock = this.GetSessionLock(session);
                    await sessionLock.WaitAsync().ConfigureAwait(false);
                    try
                    {
                        ModeState state = this._sessionState.GetOrInitializeState(session);
                        state.CurrentMode = mode;
                        this._sessionState.SaveState(session, state);
                    }
                    finally
                    {
                        sessionLock.Release();
                    }

                    return $"Mode changed to \"{mode}\".";
                },
                new AIFunctionFactoryOptions
                {
                    Name = "mode_set",
                    Description = $"Switch the agent's operating mode. Supported modes: \"{this._modeNamesDisplay}\".",
                    SerializerOptions =   AIContentJsonSerializerContext.Default.ModeState.Options
                }),

            AIFunctionFactory.Create(
                async () =>
                {
                    SemaphoreSlim sessionLock = this.GetSessionLock(session);
                    await sessionLock.WaitAsync().ConfigureAwait(false);
                    try
                    {
                        return this._sessionState.GetOrInitializeState(session).CurrentMode;
                    }
                    finally
                    {
                        sessionLock.Release();
                    }
                },
                new AIFunctionFactoryOptions
                {
                    Name = "mode_get",
                    Description = "Get the agent's current operating mode.",
                    SerializerOptions =   AIContentJsonSerializerContext.Default.ModeState.Options
                }),
        ];
    }
}