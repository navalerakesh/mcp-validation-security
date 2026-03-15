using System.Collections.Generic;

namespace Mcp.Benchmark.CLI.Abstractions;

/// <summary>
/// Provides contextual "next step" guidance after CLI commands complete.
/// Commands register suggestions when they detect common issues (authentication failures, missing config, etc.)
/// and the advisor renders a consistent helper block for the user.
/// </summary>
public interface INextStepAdvisor
{
    /// <summary>
    /// Clears any pending suggestions. Commands should invoke this at the beginning of execution.
    /// </summary>
    void Reset();

    /// <summary>
    /// Adds a custom suggestion block with a title and one or more action lines.
    /// </summary>
    /// <param name="title">Short descriptive title.</param>
    /// <param name="actions">Actionable guidance lines.</param>
    void AddSuggestion(string title, IEnumerable<string> actions);

    /// <summary>
    /// Adds a standard authentication guidance block for commands that require credentials.
    /// </summary>
    /// <param name="commandName">Command name (e.g. "health-check").</param>
    /// <param name="endpointHint">Optional endpoint hint to show in the sample command.</param>
    void SuggestAuthenticationFlow(string commandName, string? endpointHint = null);

    /// <summary>
    /// Adds a standardized suggestion prompting the user to open the detailed session log
    /// captured on disk for the current CLI run.
    /// </summary>
    /// <param name="context">Optional context label (e.g. "Validation log").</param>
    void SuggestSessionLogReview(string? context = null);

    /// <summary>
    /// Adds contextual guidance about supported MCP spec profiles so users can target
    /// the appropriate version (e.g. --mcpspec latest).
    /// </summary>
    /// <param name="configuredProfile">Profile explicitly requested in the run.</param>
    /// <param name="negotiatedVersion">Protocol version negotiated with the server.</param>
    void SuggestSpecProfiles(string? configuredProfile, string? negotiatedVersion);

    /// <summary>
    /// Renders any accumulated suggestions to the console.
    /// Commands should invoke this in their <c>finally</c> blocks so hints always display.
    /// </summary>
    void Render();
}
