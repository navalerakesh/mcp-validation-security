using System.Text;
using Mcp.Benchmark.CLI.Abstractions;
using Mcp.Benchmark.Core.Models;

namespace Mcp.Benchmark.CLI.Services;

public sealed class GitHubActionsReporter : IGitHubActionsReporter
{
    private const int MaxSummaryFindings = 10;
    private const int MaxAnnotations = 25;

    public void PublishValidationResult(ValidationResult validationResult, IEnumerable<string>? artifactPaths = null)
    {
        ArgumentNullException.ThrowIfNull(validationResult);

        if (ShouldWriteSummary())
        {
            AppendSummary(BuildValidationSummary(validationResult, artifactPaths));
        }

        if (ShouldWriteAnnotations())
        {
            foreach (var annotation in BuildAnnotations(validationResult))
            {
                WriteAnnotation(annotation);
            }
        }
    }

    public void PublishOfflineReport(ValidationResult validationResult, string format, string outputPath)
    {
        ArgumentNullException.ThrowIfNull(validationResult);

        if (ShouldWriteSummary())
        {
            AppendSummary(BuildOfflineReportSummary(validationResult, format, outputPath));
        }

        if (ShouldWriteAnnotations())
        {
            foreach (var annotation in BuildAnnotations(validationResult))
            {
                WriteAnnotation(annotation);
            }
        }
    }

    public string BuildValidationSummary(ValidationResult validationResult, IEnumerable<string>? artifactPaths = null)
    {
        ArgumentNullException.ThrowIfNull(validationResult);

        var findings = CollectFindings(validationResult);
        var builder = new StringBuilder();
        builder.AppendLine("## MCP Validator");
        builder.AppendLine();
        builder.AppendLine($"- Status: **{validationResult.OverallStatus}**");
        builder.AppendLine($"- Policy: **{FormatPolicy(validationResult.PolicyOutcome)}**");
        builder.AppendLine($"- Trust: **{FormatTrust(validationResult.TrustAssessment)}**");
        builder.AppendLine($"- Compliance Score: **{validationResult.ComplianceScore:F1}%**");
        builder.AppendLine($"- Endpoint: `{validationResult.ServerConfig.Endpoint ?? "unknown"}`");
        builder.AppendLine($"- Validation ID: `{validationResult.ValidationId}`");
        builder.AppendLine();

        if (validationResult.PolicyOutcome is { Passed: false, Reasons.Count: > 0 })
        {
            builder.AppendLine("### Blocking Reasons");
            builder.AppendLine();
            foreach (var reason in validationResult.PolicyOutcome.Reasons.Take(5))
            {
                builder.AppendLine($"- {reason}");
            }
            builder.AppendLine();
        }

        if (findings.Count > 0)
        {
            builder.AppendLine("### Top Findings");
            builder.AppendLine();
            builder.AppendLine("| Level | Source | Rule | Component | Finding |");
            builder.AppendLine("| --- | --- | --- | --- | --- |");
            foreach (var finding in findings.Take(MaxSummaryFindings))
            {
                builder.AppendLine($"| {finding.Level.ToUpperInvariant()} | {finding.Source} | {finding.RuleId} | {finding.Component} | {EscapePipe(finding.Message)} |");
            }
            builder.AppendLine();
        }

        if (validationResult.ClientCompatibility?.Assessments.Count > 0)
        {
            builder.AppendLine("### Client Profiles");
            builder.AppendLine();
            foreach (var assessment in validationResult.ClientCompatibility.Assessments)
            {
                builder.AppendLine($"- **{assessment.DisplayName}** (`{assessment.ProfileId}`): {assessment.StatusLabel} — {assessment.Summary}");
            }
            builder.AppendLine();
        }

        var materializedArtifacts = artifactPaths?.Where(path => !string.IsNullOrWhiteSpace(path)).ToList();
        if (materializedArtifacts is { Count: > 0 })
        {
            builder.AppendLine("### Artifacts");
            builder.AppendLine();
            foreach (var artifactPath in materializedArtifacts)
            {
                builder.AppendLine($"- `{artifactPath}`");
            }
            builder.AppendLine();
        }

        return builder.ToString().TrimEnd();
    }

    public string BuildOfflineReportSummary(ValidationResult validationResult, string format, string outputPath)
    {
        var builder = new StringBuilder();
        builder.AppendLine("## MCP Validator Offline Report");
        builder.AppendLine();
        builder.AppendLine($"- Generated Format: **{format.ToUpperInvariant()}**");
        builder.AppendLine($"- Output Path: `{outputPath}`");
        builder.AppendLine($"- Validation ID: `{validationResult.ValidationId}`");
        builder.AppendLine();
        builder.Append(BuildValidationSummary(validationResult, new[] { outputPath }));
        return builder.ToString().TrimEnd();
    }

    public IReadOnlyList<GitHubActionsAnnotation> BuildAnnotations(ValidationResult validationResult)
    {
        ArgumentNullException.ThrowIfNull(validationResult);

        var annotations = new List<GitHubActionsAnnotation>();
        if (validationResult.PolicyOutcome is { Passed: false } policyOutcome)
        {
            var reasonText = policyOutcome.Reasons.Count == 0
                ? policyOutcome.Summary
                : $"{policyOutcome.Summary} Reasons: {string.Join(" | ", policyOutcome.Reasons.Take(3))}";
            annotations.Add(new GitHubActionsAnnotation("error", "MCP Validator Policy", reasonText));
        }

        annotations.AddRange(validationResult.CriticalErrors.Select(error =>
            new GitHubActionsAnnotation("error", "MCP Validator Execution", error)));

        annotations.AddRange(CollectFindings(validationResult)
            .Take(MaxAnnotations)
            .Select(finding => new GitHubActionsAnnotation(
                finding.Level,
                BuildTitle(finding),
                BuildMessage(finding))));

        return annotations
            .GroupBy(annotation => $"{annotation.Level}|{annotation.Title}|{annotation.Message}", StringComparer.Ordinal)
            .Select(group => group.First())
            .Take(MaxAnnotations)
            .ToList();
    }

    private static List<GitHubFinding> CollectFindings(ValidationResult validationResult)
    {
        var findings = new List<GitHubFinding>();

        AddStructuredFindings(findings, validationResult.ProtocolCompliance?.Findings, "protocol");
        AddStructuredFindings(findings, validationResult.ToolValidation?.Findings, "tools");
        AddStructuredFindings(findings, validationResult.ToolValidation?.AiReadinessFindings, "tools/ai-readiness");
        AddStructuredFindings(findings, validationResult.ToolValidation?.AuthenticationSecurity?.StructuredFindings, "auth");
        AddStructuredFindings(findings, validationResult.ResourceTesting?.Findings, "resources");
        AddStructuredFindings(findings, validationResult.PromptTesting?.Findings, "prompts");
        AddStructuredFindings(findings, validationResult.SecurityTesting?.Findings, "security");
        AddStructuredFindings(findings, validationResult.PerformanceTesting?.Findings, "performance");
        AddStructuredFindings(findings, validationResult.ErrorHandling?.Findings, "error-handling");

        if (validationResult.ToolValidation?.ToolResults is { Count: > 0 } toolResults)
        {
            foreach (var tool in toolResults)
            {
                AddStructuredFindings(findings, tool.Findings, tool.ToolName);
            }
        }

        if (validationResult.ResourceTesting?.ResourceResults is { Count: > 0 } resourceResults)
        {
            foreach (var resource in resourceResults)
            {
                AddStructuredFindings(findings, resource.Findings, string.IsNullOrWhiteSpace(resource.ResourceUri) ? resource.ResourceName : resource.ResourceUri);
            }
        }

        if (validationResult.PromptTesting?.PromptResults is { Count: > 0 } promptResults)
        {
            foreach (var prompt in promptResults)
            {
                AddStructuredFindings(findings, prompt.Findings, prompt.PromptName);
            }
        }

        if (validationResult.ProtocolCompliance?.Violations is { Count: > 0 } violations)
        {
            findings.AddRange(violations.Select(violation => new GitHubFinding(
                Level: MapLevel(violation.Severity),
                Source: ValidationRuleSourceClassifier.GetLabel(violation),
                RuleId: string.IsNullOrWhiteSpace(violation.CheckId) ? "MCP.PROTOCOL" : violation.CheckId,
                Component: string.IsNullOrWhiteSpace(violation.Category) ? "protocol" : violation.Category,
                Message: violation.Description,
                Fingerprint: $"protocol|{violation.CheckId}|{violation.Category}|{violation.Description}")));
        }

        if (validationResult.SecurityTesting?.Vulnerabilities is { Count: > 0 } vulnerabilities)
        {
            findings.AddRange(vulnerabilities.Select(vulnerability => new GitHubFinding(
                Level: MapLevel(vulnerability.Severity),
                Source: ValidationRuleSourceClassifier.GetLabel(vulnerability),
                RuleId: string.IsNullOrWhiteSpace(vulnerability.Id) ? "MCP.SECURITY" : vulnerability.Id,
                Component: string.IsNullOrWhiteSpace(vulnerability.AffectedComponent) ? "security" : vulnerability.AffectedComponent,
                Message: vulnerability.Description,
                Fingerprint: $"vulnerability|{vulnerability.Id}|{vulnerability.AffectedComponent}|{vulnerability.Description}")));
        }

        return findings
            .GroupBy(finding => finding.Fingerprint, StringComparer.Ordinal)
            .Select(group => group.First())
            .OrderByDescending(finding => LevelWeight(finding.Level))
            .ThenBy(finding => finding.RuleId, StringComparer.Ordinal)
            .ThenBy(finding => finding.Component, StringComparer.Ordinal)
            .ToList();
    }

    private static void AddStructuredFindings(List<GitHubFinding> findings, IEnumerable<ValidationFinding>? validationFindings, string defaultComponent)
    {
        if (validationFindings == null)
        {
            return;
        }

        findings.AddRange(validationFindings.Select(finding => new GitHubFinding(
            Level: MapLevel(finding.Severity),
            Source: finding.EffectiveSourceLabel,
            RuleId: string.IsNullOrWhiteSpace(finding.RuleId) ? "MCP.VALIDATION" : finding.RuleId,
            Component: string.IsNullOrWhiteSpace(finding.Component) ? defaultComponent : finding.Component,
            Message: finding.Summary,
            Fingerprint: $"finding|{finding.RuleId}|{finding.Component}|{finding.Summary}")));
    }

    private static string BuildTitle(GitHubFinding finding)
    {
        return $"MCP Validator {finding.RuleId}";
    }

    private static string BuildMessage(GitHubFinding finding)
    {
        return $"[{finding.Source}] {finding.Component}: {finding.Message}";
    }

    private static int LevelWeight(string level)
    {
        return level switch
        {
            "error" => 3,
            "warning" => 2,
            _ => 1
        };
    }

    private static string MapLevel(ValidationFindingSeverity severity)
    {
        return severity switch
        {
            ValidationFindingSeverity.Critical => "error",
            ValidationFindingSeverity.High => "error",
            ValidationFindingSeverity.Medium => "warning",
            _ => "notice"
        };
    }

    private static string MapLevel(ViolationSeverity severity)
    {
        return severity switch
        {
            ViolationSeverity.Critical => "error",
            ViolationSeverity.High => "error",
            ViolationSeverity.Medium => "warning",
            _ => "notice"
        };
    }

    private static string MapLevel(VulnerabilitySeverity severity)
    {
        return severity switch
        {
            VulnerabilitySeverity.Critical => "error",
            VulnerabilitySeverity.High => "error",
            VulnerabilitySeverity.Medium => "warning",
            _ => "notice"
        };
    }

    private static string FormatPolicy(ValidationPolicyOutcome? policyOutcome)
    {
        if (policyOutcome == null)
        {
            return "not evaluated";
        }

        return policyOutcome.Passed
            ? $"{policyOutcome.Mode} passed"
            : $"{policyOutcome.Mode} blocked";
    }

    private static string FormatTrust(McpTrustAssessment? trustAssessment)
    {
        return trustAssessment == null ? "unknown" : trustAssessment.TrustLabel;
    }

    private static string EscapePipe(string value)
    {
        return value.Replace("|", "\\|");
    }

    private static bool ShouldWriteSummary()
    {
        return IsExplicitlyEnabled("MCPVAL_GITHUB_SUMMARY") ||
               (IsGitHubActions() && !IsExplicitlyDisabled("MCPVAL_GITHUB_SUMMARY") &&
                !string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("GITHUB_STEP_SUMMARY")));
    }

    private static bool ShouldWriteAnnotations()
    {
        return IsExplicitlyEnabled("MCPVAL_GITHUB_ANNOTATIONS") ||
               (IsGitHubActions() && !IsExplicitlyDisabled("MCPVAL_GITHUB_ANNOTATIONS"));
    }

    private static bool IsGitHubActions()
    {
        return string.Equals(Environment.GetEnvironmentVariable("GITHUB_ACTIONS"), "true", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsExplicitlyEnabled(string variableName)
    {
        return string.Equals(Environment.GetEnvironmentVariable(variableName), "true", StringComparison.OrdinalIgnoreCase) ||
               string.Equals(Environment.GetEnvironmentVariable(variableName), "1", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsExplicitlyDisabled(string variableName)
    {
        return string.Equals(Environment.GetEnvironmentVariable(variableName), "false", StringComparison.OrdinalIgnoreCase) ||
               string.Equals(Environment.GetEnvironmentVariable(variableName), "0", StringComparison.OrdinalIgnoreCase);
    }

    private static void AppendSummary(string summary)
    {
        var summaryPath = Environment.GetEnvironmentVariable("GITHUB_STEP_SUMMARY");
        if (string.IsNullOrWhiteSpace(summaryPath))
        {
            return;
        }

        File.AppendAllText(summaryPath, summary + Environment.NewLine + Environment.NewLine);
    }

    private static void WriteAnnotation(GitHubActionsAnnotation annotation)
    {
        Console.WriteLine($"::{annotation.Level} title={EscapeProperty(annotation.Title)}::{EscapeMessage(annotation.Message)}");
    }

    private static string EscapeProperty(string value)
    {
        return value
            .Replace("%", "%25", StringComparison.Ordinal)
            .Replace("\r", "%0D", StringComparison.Ordinal)
            .Replace("\n", "%0A", StringComparison.Ordinal)
            .Replace(":", "%3A", StringComparison.Ordinal)
            .Replace(",", "%2C", StringComparison.Ordinal);
    }

    private static string EscapeMessage(string value)
    {
        return value
            .Replace("%", "%25", StringComparison.Ordinal)
            .Replace("\r", "%0D", StringComparison.Ordinal)
            .Replace("\n", "%0A", StringComparison.Ordinal);
    }

    private sealed record GitHubFinding(
        string Level,
        string Source,
        string RuleId,
        string Component,
        string Message,
        string Fingerprint);
}

public sealed record GitHubActionsAnnotation(string Level, string Title, string Message);