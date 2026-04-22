using System.Collections.Generic;
using System.Linq;
using Mcp.Benchmark.CLI.Utilities;
using Mcp.Benchmark.Core.Models;
using Mcp.Benchmark.Core.Resources;
using Mcp.Benchmark.Core.Services;

namespace Mcp.Benchmark.CLI.Services.Formatters;

public static class ValidationFormatter
{
    public static void DisplayResults(ValidationResult result, bool showDetails, bool useColors, bool verbose)
    {
        Console.WriteLine();
        FormatterUtils.WriteLineWithColor("MCP SERVER VALIDATION RESULTS", ConsoleColor.Cyan, useColors);
        Console.WriteLine(new string('-', 40));

        DisplayServerInfo(result, useColors);
        DisplayOverallResults(result, useColors);
        DisplayCategoryBreakdown(result, useColors);

        if (result.CriticalErrors?.Any() == true)
        {
            DisplayCriticalErrors(result.CriticalErrors, useColors);
        }

        DisplayRecommendations(result.Recommendations, useColors);
        DisplayOutputInfo(result, verbose, useColors);

        // Always display Security Assessment breakdown if available (Critical for user visibility)
        if (result.SecurityTesting != null && !showDetails)
        {
            Console.WriteLine();
            FormatterUtils.WriteLineWithColor("SECURITY ASSESSMENT DETAILS", ConsoleColor.Cyan, useColors);
            Console.WriteLine(new string('-', 40));
            SecurityFormatter.DisplayAssessmentBreakdown(result, useColors);
        }

        if (showDetails)
        {
            DisplayDetailedResults(result, useColors, verbose);
        }

        Console.WriteLine();
    }

    public static void DisplayDetailedResults(ValidationResult result, bool useColors, bool verbose)
    {
        Console.WriteLine();
        FormatterUtils.WriteLineWithColor("DETAILED REPORT", ConsoleColor.Cyan, useColors);
        Console.WriteLine(new string('-', 40));

        DisplayDetailedOverallSummary(result, useColors);

        if (result.SecurityTesting != null)
        {
            SecurityFormatter.DisplayAssessmentBreakdown(result, useColors);
        }

        if (result.ProtocolCompliance != null)
        {
            ProtocolFormatter.DisplayBreakdown(result, useColors);
        }

        if (result.ToolValidation != null)
        {
            ToolFormatter.DisplayBreakdown(result, useColors);
        }

        if (result.PerformanceTesting != null)
        {
            PerformanceFormatter.DisplayBreakdown(result, useColors);
        }

        DisplayExpertAssessmentInsights(result, useColors);
    }

    private static void DisplayServerInfo(ValidationResult result, bool useColors)
    {
        FormatterUtils.WriteLineWithColor("SUMMARY", ConsoleColor.White, useColors);
        WriteKeyValue("Server", result.ServerConfig?.Endpoint ?? "(unknown)", useColors);
        WriteKeyValue("Transport", result.ServerConfig?.Transport?.ToUpperInvariant() ?? "(unspecified)", useColors);
        if (result.Duration.HasValue)
        {
            WriteKeyValue("Duration", $"{result.Duration.Value.TotalSeconds:F2}s", useColors);
        }

        var protocolVersion = result.ProtocolVersion ?? result.InitializationHandshake?.Payload?.ProtocolVersion;
        if (!string.IsNullOrWhiteSpace(protocolVersion))
        {
            WriteKeyValue("Protocol", protocolVersion!, useColors);
        }

        var serverInfo = result.InitializationHandshake?.Payload?.ServerInfo;
        if (!string.IsNullOrWhiteSpace(serverInfo?.Name))
        {
            var versionSuffix = string.IsNullOrWhiteSpace(serverInfo.Version) ? string.Empty : $" v{serverInfo.Version}";
            WriteKeyValue("Implementation", serverInfo.Name + versionSuffix, useColors);
        }

        var handshakeTransport = result.InitializationHandshake?.Transport;
        if (handshakeTransport != null)
        {
            var latency = handshakeTransport.Duration.TotalMilliseconds;
            var statusText = handshakeTransport.StatusCode?.ToString() ?? "n/a";
            if (latency > 0)
            {
                WriteKeyValue("Handshake", $"{latency:F1}ms · HTTP {statusText}", useColors);
            }
        }

        if (result.CapabilitySnapshot?.Payload is CapabilitySummary capabilities)
        {
            var toolCount = capabilities.DiscoveredToolsCount > 0
                ? capabilities.DiscoveredToolsCount
                : capabilities.Tools?.Count ?? 0;

            var toolListingSucceeded = capabilities.ToolListingSucceeded;
            bool? toolInvocationSucceeded = capabilities.ToolInvocationSucceeded;

            // If detailed tool validation ran and found no hard failures,
            // treat the high-level Tools summary as successful even if
            // the capability probe reported softer issues (e.g. schema-only warnings).
            if (result.ToolValidation is { } toolValidation)
            {
                var hasHardFailures = toolValidation.ToolsTestFailed > 0 ||
                                      toolValidation.Status == TestStatus.Failed ||
                                      toolValidation.Status == TestStatus.Error;

                if (!hasHardFailures)
                {
                    toolListingSucceeded = true;

                    if (toolValidation.ToolResults?.Count > 0)
                    {
                        toolInvocationSucceeded = true;
                    }
                }
            }

            WriteKeyValue(
                "Tools",
                BuildSummaryProbe(toolCount, toolListingSucceeded, capabilities.ToolListDurationMs, capabilities.ToolListResponse?.StatusCode, toolInvocationSucceeded),
                useColors);

            if (capabilities.DiscoveredResourcesCount > 0 || capabilities.ResourceListResponse != null)
            {
                WriteKeyValue(
                    "Resources",
                    BuildSummaryProbe(capabilities.DiscoveredResourcesCount, capabilities.ResourceListingSucceeded, capabilities.ResourceListDurationMs, capabilities.ResourceListResponse?.StatusCode, null),
                    useColors);
            }

            if (capabilities.DiscoveredPromptsCount > 0 || capabilities.PromptListResponse != null)
            {
                WriteKeyValue(
                    "Prompts",
                    BuildSummaryProbe(capabilities.DiscoveredPromptsCount, capabilities.PromptListingSucceeded, capabilities.PromptListDurationMs, capabilities.PromptListResponse?.StatusCode, null),
                    useColors);
            }

            if (!string.IsNullOrWhiteSpace(capabilities.FirstToolName))
            {
                var callStatus = capabilities.ToolInvocationSucceeded ? "✅" : "⚠️";
                WriteKeyValue("First Tool", $"{capabilities.FirstToolName} ({callStatus} invocation)", useColors);
            }
        }

        WriteKeyValue("Validation ID", result.ValidationId ?? "(unavailable)", useColors);
        Console.WriteLine();
    }

    private static void DisplayOverallResults(ValidationResult result, bool useColors)
    {
        var statusColor = result.OverallStatus switch
        {
            ValidationStatus.Passed => ConsoleColor.Green,
            ValidationStatus.Failed => ConsoleColor.Red,
            ValidationStatus.PartiallyCompleted => ConsoleColor.Yellow,
            _ => ConsoleColor.White
        };

        FormatterUtils.WriteWithColor("Status: ", ConsoleColor.Gray, useColors);
        FormatterUtils.WriteLineWithColor(result.OverallStatus.ToString().ToUpper(), statusColor, useColors);

        FormatterUtils.WriteWithColor("Score:  ", ConsoleColor.Gray, useColors);
        FormatterUtils.WriteLineWithColor($"{result.ComplianceScore:F1}%", FormatterUtils.GetScoreColor(result.ComplianceScore), useColors);
        Console.WriteLine();
    }

    private static void DisplayCategoryBreakdown(ValidationResult result, bool useColors)
    {
        FormatterUtils.WriteLineWithColor("CATEGORIES", ConsoleColor.White, useColors);

        var categories = new (string name, object? testResult)[]
        {
            ("Security", result.SecurityTesting),
            ("Protocol", result.ProtocolCompliance),
            ("Tools", result.ToolValidation),
            ("Performance", result.PerformanceTesting),
            ("Resources", result.ResourceTesting),
            ("Prompts", result.PromptTesting)
        };

        DisplayCategoriesTable(categories.Where(c => c.testResult != null).ToArray(), useColors);
        Console.WriteLine();
    }

    private static void DisplayCategoriesTable((string name, object? testResult)[] categories, bool useColors)
    {
        if (!categories.Any()) return;

        // Simple aligned table, no borders
        var fmt = "{0,-15} {1,-12} {2,-10} {3}";

        FormatterUtils.WriteLineWithColor(string.Format(fmt, "CATEGORY", "STATUS", "SCORE", "ISSUES"), ConsoleColor.DarkGray, useColors);
        Console.WriteLine(new string('-', 60));

        foreach (var (name, testResult) in categories)
        {
            if (testResult == null) continue;

            var status = GetTestStatus(testResult);
            var score = GetTestScore(testResult);
            var issues = GetIssueCount(testResult);

            var statusColor = status switch
            {
                TestStatus.Passed => ConsoleColor.Green,
                TestStatus.Failed => ConsoleColor.Red,
                TestStatus.Error => ConsoleColor.Red,
                TestStatus.Skipped => ConsoleColor.DarkGray,
                _ => ConsoleColor.Gray
            };

            var scoreText = FormatScoreText(testResult, status, score);
            var issuesText = issues > 0 ? $"{issues}" : "-";

            FormatterUtils.WriteWithColor(name.PadRight(16), ConsoleColor.White, useColors);
            FormatterUtils.WriteWithColor(status.ToString().ToUpper().PadRight(13), statusColor, useColors);
            FormatterUtils.WriteWithColor(scoreText.PadRight(11), ConsoleColor.Gray, useColors);
            FormatterUtils.WriteLineWithColor(issuesText, issues > 0 ? ConsoleColor.Red : ConsoleColor.Gray, useColors);
        }
    }

    private static void DisplayCriticalErrors(IEnumerable<string> errors, bool useColors)
    {
        Console.WriteLine();
        FormatterUtils.WriteLineWithColor("CRITICAL ISSUES", ConsoleColor.Red, useColors);
        foreach (var error in errors)
        {
            Console.WriteLine($"! {error}");
        }
        Console.WriteLine();
    }

    private static void DisplayRecommendations(IEnumerable<string>? recommendations, bool useColors)
    {
        if (recommendations?.Any() != true) return;

        FormatterUtils.WriteLineWithColor("RECOMMENDATIONS", ConsoleColor.Yellow, useColors);
        foreach (var recommendation in recommendations)
        {
            Console.WriteLine($"- {recommendation}");
        }
        Console.WriteLine();
    }

    private static void DisplayOutputInfo(ValidationResult result, bool verbose, bool useColors)
    {
        var skippedTests = GetAuthenticationLimitedSkippedCategories(result);

        if (skippedTests.Any())
        {
            Console.WriteLine();
            FormatterUtils.WriteLineWithColor(Strings.Auth_Limitations_Title, ConsoleColor.Cyan, useColors);
            Console.WriteLine(Strings.Auth_Limitations_Desc);
            Console.WriteLine(string.Format(Strings.Auth_Limitations_Skipped, string.Join(", ", skippedTests)));
            Console.WriteLine(Strings.Auth_Limitations_Scoring);
            FormatterUtils.WriteLineWithColor(Strings.Auth_Limitations_Important, ConsoleColor.Yellow, useColors);
            Console.WriteLine(Strings.Auth_Limitations_CommandHint);
            Console.WriteLine();
        }

    }

    private static List<string> GetAuthenticationLimitedSkippedCategories(ValidationResult result)
    {
        var skippedTests = new List<string>();

        if (IsAuthenticationLimitedSkip(result.ToolValidation, result.ToolValidation?.AuthenticationSecurity?.AuthenticationRequired == true))
        {
            skippedTests.Add("Tools");
        }

        if (IsAuthenticationLimitedSkip(result.ResourceTesting))
        {
            skippedTests.Add("Resources");
        }

        if (IsAuthenticationLimitedSkip(result.PromptTesting))
        {
            skippedTests.Add("Prompts");
        }

        if (IsAuthenticationLimitedSkip(result.PerformanceTesting))
        {
            skippedTests.Add("Performance");
        }

        return skippedTests;
    }

    private static bool IsAuthenticationLimitedSkip(TestResultBase? testResult, bool explicitAuthEvidence = false)
    {
        if (testResult?.Status != TestStatus.Skipped)
        {
            return false;
        }

        if (explicitAuthEvidence)
        {
            return true;
        }

        if (testResult.Findings.Any(f => string.Equals(f.RuleId, ValidationFindingRuleIds.PerformanceAuthRequiredAdvisory, StringComparison.OrdinalIgnoreCase)))
        {
            return true;
        }

        var message = testResult.Message;
        if (string.IsNullOrWhiteSpace(message))
        {
            return false;
        }

        return message.Contains("authentication", StringComparison.OrdinalIgnoreCase)
            || message.Contains("unauthorized", StringComparison.OrdinalIgnoreCase)
            || message.Contains("invalid token", StringComparison.OrdinalIgnoreCase);
    }

    private static void DisplayDetailedOverallSummary(ValidationResult result, bool useColors)
    {
        if (result.InitializationHandshake?.Payload is { } init)
        {
            FormatterUtils.WriteLineWithColor("HANDSHAKE INSIGHTS", ConsoleColor.White, useColors);
            WriteKeyValue("Server Name", init.ServerInfo?.Name ?? "Unknown", useColors);
            WriteKeyValue("Server Version", init.ServerInfo?.Version ?? "Unknown", useColors);
            WriteKeyValue("Protocol", init.ProtocolVersion ?? "Unknown", useColors);
            WriteKeyValue("Duration", $"{result.InitializationHandshake.Transport.Duration.TotalMilliseconds:F1}ms", useColors);
            if (!string.IsNullOrWhiteSpace(init.Instructions))
            {
                WriteKeyValue("Instructions", init.Instructions.Trim(), useColors);
            }

            var capabilities = init.Capabilities;
            if (capabilities != null)
            {
                var capabilityFlags = new List<string>();
                if (capabilities.Tools != null) capabilityFlags.Add("Tools");
                if (capabilities.Resources != null) capabilityFlags.Add("Resources");
                if (capabilities.Prompts != null) capabilityFlags.Add("Prompts");
                if (capabilities.Logging != null) capabilityFlags.Add("Logging");
                if (capabilities.Completions != null) capabilityFlags.Add("Completions");
                if (capabilityFlags.Count > 0)
                {
                    WriteKeyValue("Capabilities", string.Join(", ", capabilityFlags), useColors);
                }
            }

            Console.WriteLine();
        }

        if (result.CapabilitySnapshot?.Payload is { } snapshot)
        {
            if (snapshot.Tools?.Any() == true)
            {
                FormatterUtils.WriteLineWithColor("TOOL SNAPSHOT", ConsoleColor.White, useColors);
                var preview = snapshot.Tools!.Take(5);
                foreach (var tool in preview)
                {
                    Console.WriteLine($"- {tool.Name}: {tool.Description ?? "(no description)"}");
                }

                if (snapshot.Tools.Count > 5)
                {
                    Console.WriteLine($"  … +{snapshot.Tools.Count - 5} more tools");
                }

                Console.WriteLine();
            }

            FormatterUtils.WriteLineWithColor("CAPABILITY PROBES", ConsoleColor.White, useColors);
            Console.WriteLine(BuildProbeDescription("Tools/list", snapshot.DiscoveredToolsCount, snapshot.ToolListingSucceeded, snapshot.ToolListDurationMs, snapshot.ToolListResponse?.StatusCode, snapshot.ToolInvocationSucceeded));
            Console.WriteLine(BuildProbeDescription("Resources/list", snapshot.DiscoveredResourcesCount, snapshot.ResourceListingSucceeded, snapshot.ResourceListDurationMs, snapshot.ResourceListResponse?.StatusCode, null));
            Console.WriteLine(BuildProbeDescription("Prompts/list", snapshot.DiscoveredPromptsCount, snapshot.PromptListingSucceeded, snapshot.PromptListDurationMs, snapshot.PromptListResponse?.StatusCode, null));
            Console.WriteLine();
        }
    }

    private static void DisplayExpertAssessmentInsights(ValidationResult result, bool useColors)
    {
        if (result.ComplianceScore >= 95)
        {
            FormatterUtils.WriteLineWithColor("VERDICT: EXEMPLARY IMPLEMENTATION", ConsoleColor.Green, useColors);
        }
        else if (result.ComplianceScore >= 80)
        {
            FormatterUtils.WriteLineWithColor("VERDICT: PRODUCTION READY", ConsoleColor.Green, useColors);
        }
        else
        {
            FormatterUtils.WriteLineWithColor("VERDICT: IMPROVEMENTS REQUIRED", ConsoleColor.Yellow, useColors);
        }
    }

    // Helpers
    private static string BuildSummaryProbe(int count, bool succeeded, double durationMs, int? statusCode, bool? invocationSucceeded)
    {
        var icon = succeeded ? "✅" : "⚠️";
        var durationText = durationMs > 0 ? $"{durationMs:F1}ms" : "n/a";
        var statusText = statusCode.HasValue ? $"HTTP {statusCode}" : "HTTP n/a";
        var invocationText = invocationSucceeded is bool callSucceeded
            ? $" · call {(callSucceeded ? "✅" : "⚠️")}"
            : string.Empty;

        return $"{icon} {count} · {durationText} · {statusText}{invocationText}";
    }

    private static string BuildProbeDescription(string label, int count, bool succeeded, double durationMs, int? statusCode, bool? invocationSucceeded)
    {
        var statusIcon = succeeded ? "✅" : "⚠️";
        var durationText = durationMs > 0 ? $"{durationMs:F1}ms" : "n/a";
        var statusText = statusCode.HasValue ? $"HTTP {statusCode}" : "HTTP n/a";
        var countText = count >= 0 ? count.ToString() : "-";
        var invocationText = invocationSucceeded is bool callSucceeded
            ? $"call {(callSucceeded ? "✅" : "⚠️")}"
            : string.Empty;

        return $"{label}: {statusIcon} {countText} · {durationText} · {statusText}{(string.IsNullOrWhiteSpace(invocationText) ? string.Empty : $" · {invocationText}")}";
    }

    private static void WriteKeyValue(string label, string value, bool useColors)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return;
        }

        const int labelWidth = 16;
        FormatterUtils.WriteWithColor($"  {label.PadRight(labelWidth)}", ConsoleColor.DarkGray, useColors);
        FormatterUtils.WriteLineWithColor(value, ConsoleColor.White, useColors);
    }

    private static TestStatus GetTestStatus(object testResult)
    {
        return testResult switch
        {
            ComplianceTestResult compliance => compliance.Status,
            ToolTestResult tool => tool.Status,
            SecurityTestResult security => security.Status,
            PerformanceTestResult performance => performance.Status,
            ResourceTestResult resource => resource.Status,
            PromptTestResult prompt => prompt.Status,
            _ => TestStatus.Skipped
        };
    }

    private static double? GetTestScore(object testResult)
    {
        return testResult switch
        {
            ComplianceTestResult compliance => compliance.ComplianceScore,
            SecurityTestResult security => security.SecurityScore,
            PerformanceTestResult performance => performance.Score,
            ToolTestResult tool => tool.Score,
            ResourceTestResult resource => resource.Score,
            PromptTestResult prompt => prompt.Score,
            _ => null
        };
    }

    private static int GetIssueCount(object testResult)
    {
        return testResult switch
        {
            ComplianceTestResult compliance => compliance.Violations?.Count ?? 0,
            SecurityTestResult security => security.Vulnerabilities?.Count ?? 0,
            ToolTestResult tool => tool.ToolsTestFailed,
            PerformanceTestResult performance => performance.PerformanceBottlenecks?.Count ?? 0,
            ResourceTestResult resource => resource.ResourcesTestFailed,
            PromptTestResult prompt => prompt.PromptsTestFailed,
            _ => 0
        };
    }

    private static string FormatScoreText(object testResult, TestStatus status, double? score)
    {
        if (status == TestStatus.Skipped)
        {
            return "-";
        }

        if (testResult is PerformanceTestResult performance && !PerformanceMeasurementEvaluator.HasObservedMetrics(performance))
        {
            return "Unavailable";
        }

        return score.HasValue ? $"{score.Value:F0}%" : "-";
    }

}
