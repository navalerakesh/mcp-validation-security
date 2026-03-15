using System.Collections.Generic;
using System.Linq;
using Mcp.Benchmark.CLI.Abstractions;
using Mcp.Benchmark.CLI.Services.Formatters;
using Mcp.Benchmark.CLI.Utilities;
using Mcp.Compliance.Spec;

namespace Mcp.Benchmark.CLI.Services;

internal sealed class NextStepAdvisor : INextStepAdvisor
{
    private readonly List<NextStepSuggestion> _suggestions = new();
    private readonly bool _useColors = !Console.IsOutputRedirected;
    private readonly CliSessionContext _sessionContext;
    private readonly IReadOnlyList<SpecProfileInfo> _specProfiles;
    private bool _sessionLogSuggestionAdded;
    private bool _specSuggestionAdded;

    public NextStepAdvisor(CliSessionContext sessionContext, ISchemaRegistry schemaRegistry)
    {
        _sessionContext = sessionContext ?? throw new ArgumentNullException(nameof(sessionContext));
        _specProfiles = SpecProfileCatalog.GetProfiles(schemaRegistry ?? throw new ArgumentNullException(nameof(schemaRegistry)));
    }

    public void Reset()
    {
        _suggestions.Clear();
        _sessionLogSuggestionAdded = false;
        _specSuggestionAdded = false;
    }

    public void AddSuggestion(string title, IEnumerable<string> actions)
    {
        if (string.IsNullOrWhiteSpace(title))
        {
            return;
        }

        var normalizedActions = actions?.Where(a => !string.IsNullOrWhiteSpace(a)).ToList() ?? new List<string>();
        if (normalizedActions.Count == 0)
        {
            return;
        }

        _suggestions.Add(new NextStepSuggestion(title, normalizedActions));
    }

    public void SuggestAuthenticationFlow(string commandName, string? endpointHint = null)
    {
        var endpoint = string.IsNullOrWhiteSpace(endpointHint) ? "<endpoint>" : endpointHint;
        AddSuggestion(
            "Authentication required",
            new[]
            {
                "This server rejected unauthenticated requests.",
                "Try rerunning with credentials:",
                $"  mcpval {commandName} -s {endpoint} --access authenticated -t <token>",
                $"  mcpval {commandName} -s {endpoint} --access authenticated -i"
            });
    }

    public void SuggestSessionLogReview(string? context = null)
    {
        if (_sessionLogSuggestionAdded)
        {
            return;
        }

        var title = string.IsNullOrWhiteSpace(context)
            ? "Review session log"
            : context.Trim();

        var logPath = _sessionContext.LogFilePath;
        var openCommand = BuildOpenCommand(logPath);
        var actions = new List<string>
        {
            "Detailed diagnostics were saved for this run.",
            $"Open the log file: {logPath}"
        };

        if (!string.IsNullOrWhiteSpace(openCommand))
        {
            actions.Add($"Quick open command: {openCommand}");
        }

        AddSuggestion(title, actions);
        _sessionLogSuggestionAdded = true;
    }

    public void SuggestSpecProfiles(string? configuredProfile, string? negotiatedVersion)
    {
        if (_specSuggestionAdded || _specProfiles.Count == 0)
        {
            return;
        }

        var activeProfile = string.IsNullOrWhiteSpace(configuredProfile)
            ? "latest (default)"
            : configuredProfile;
        var negotiated = string.IsNullOrWhiteSpace(negotiatedVersion)
            ? "server default"
            : negotiatedVersion;

        var actions = new List<string>
        {
            $"Active profile: {activeProfile}",
            $"Negotiated with server: {negotiated}",
            "Available spec profiles:"
        };

        foreach (var profile in _specProfiles)
        {
            var aliasSuffix = profile.IsAlias && !string.IsNullOrWhiteSpace(profile.AliasOf)
                ? $" → {profile.AliasOf}"
                : string.Empty;
            actions.Add($"  - {profile.Name}{aliasSuffix}");
        }

        actions.Add("Use --mcpspec <profile> or run `mcpval --list-spec-profiles` for details.");

        AddSuggestion("Choose an MCP spec profile", actions);
        _specSuggestionAdded = true;
    }

    public void Render()
    {
        if (_suggestions.Count == 0)
        {
            return;
        }

        Console.WriteLine();
        FormatterUtils.WriteLineWithColor("NEXT ACTIONS", ConsoleColor.Cyan, _useColors);
        foreach (var suggestion in _suggestions)
        {
            FormatterUtils.WriteLineWithColor($"• {suggestion.Title}", ConsoleColor.White, _useColors);
            foreach (var action in suggestion.Actions)
            {
                Console.WriteLine(action);
            }
            Console.WriteLine();
        }

        _suggestions.Clear();
    }

    private sealed record NextStepSuggestion(string Title, IReadOnlyList<string> Actions);

    private static string BuildOpenCommand(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return string.Empty;
        }

        if (OperatingSystem.IsWindows())
        {
            return $"start \"\" \"{path}\"";
        }

        if (OperatingSystem.IsMacOS())
        {
            return $"open \"{path}\"";
        }

        if (OperatingSystem.IsLinux())
        {
            return $"xdg-open \"{path}\"";
        }

        return string.Empty;
    }
}
