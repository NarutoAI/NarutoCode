// Copyright (c) Microsoft. All rights reserved.

using Microsoft.Agents.AI;

namespace NarutoCode.Infrastructure.AIAgents.AIContextProviders.AgentMode;

/// <summary>
/// Options controlling the behavior of <see cref="AgentModeProvider"/>.
/// </summary>
public sealed class ModeProviderOptions
{
    /// <summary>
    /// Gets or sets custom instructions provided to the agent for using the mode tools.
    /// </summary>
    /// <remarks>
    /// The instructions must contain a <c>{available_modes}</c> placeholder for the provider to inject the
    /// currently available list of modes, and a <c>{current_mode}</c> placeholder to inject the currently
    /// active mode.
    /// </remarks>
    /// <value>
    /// When <see langword="null"/> (the default), the provider uses a default set of instructions.
    /// </value>
    public string? Instructions { get; set; }

    /// <summary>
    /// Gets or sets the list of available modes the agent can operate in.
    /// </summary>
    /// <value>
    /// When <see langword="null"/> (the default), the provider uses two built-in modes:
    /// <c>"plan"</c> (interactive planning) and <c>"execute"</c> (autonomous execution).
    /// </value>
    public IReadOnlyList<AgentMode>? Modes { get; set; }

    /// <summary>
    /// Gets or sets the initial mode for new sessions.
    /// </summary>
    /// <value>
    /// When <see langword="null"/> (the default), the first mode in the <see cref="Modes"/> list is used.
    /// Must match the <see cref="AgentMode.Name"/> of one of the configured modes.
    /// </value>
    public string? DefaultMode { get; set; }

    /// <summary>
    /// Represents an agent operating mode with a name and instructions.
    /// </summary>
    public sealed class AgentMode
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="AgentMode"/> class.
        /// </summary>
        /// <param name="name">The name of the mode.</param>
        /// <param name="instructions">Instructions for the agent describing when and how to operate in this mode.</param>
        /// <exception cref="ArgumentNullException"><paramref name="name"/> or <paramref name="instructions"/> is <see langword="null"/>.</exception>
        /// <exception cref="ArgumentException"><paramref name="name"/> or <paramref name="instructions"/> is empty or whitespace.</exception>
        public AgentMode(string name, string instructions)
        {
            this.Name = name;
            this.Instructions = instructions;
        }

        /// <summary>
        /// Gets the name of the mode.
        /// </summary>
        public string Name { get; }

        /// <summary>
        /// Gets the instructions for the agent describing when and how to operate in this mode.
        /// </summary>
        public string Instructions { get; }
    }
}
