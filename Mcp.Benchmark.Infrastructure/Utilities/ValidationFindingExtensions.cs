using Mcp.Benchmark.Core.Models;

namespace Mcp.Benchmark.Infrastructure.Utilities;

internal static class ValidationFindingExtensions
{
    public static void AddFinding(this TestResultBase result, ValidationFinding finding, string? legacyIssue = null)
    {
        result.Findings.Add(finding);
        if (!string.IsNullOrWhiteSpace(legacyIssue) && result is ToolTestResult toolResult)
        {
            toolResult.Issues.Add(legacyIssue);
        }
        else if (!string.IsNullOrWhiteSpace(legacyIssue) && result is ResourceTestResult resourceResult)
        {
            resourceResult.Issues.Add(legacyIssue);
        }
        else if (!string.IsNullOrWhiteSpace(legacyIssue) && result is PromptTestResult promptResult)
        {
            promptResult.Issues.Add(legacyIssue);
        }
    }

    public static void AddAiReadinessFinding(this ToolTestResult result, ValidationFinding finding, string legacyIssue)
    {
        result.AiReadinessFindings.Add(finding);
        result.Findings.Add(finding);
        result.AiReadinessIssues.Add(legacyIssue);
    }

    public static void AddFinding(this IndividualToolResult result, ValidationFinding finding, string legacyIssue)
    {
        result.Findings.Add(finding);
        result.Issues.Add(legacyIssue);
    }

    public static void AddFinding(this IndividualResourceResult result, ValidationFinding finding, string legacyIssue)
    {
        result.Findings.Add(finding);
        result.Issues.Add(legacyIssue);
    }

    public static void AddFinding(this IndividualPromptResult result, ValidationFinding finding, string legacyIssue)
    {
        result.Findings.Add(finding);
        result.Issues.Add(legacyIssue);
    }
}