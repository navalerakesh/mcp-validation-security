using System.Globalization;
using Mcp.Benchmark.Core.Models;

namespace Mcp.Benchmark.Core.Services;

/// <summary>
/// Evaluates a completed validation result against a host-level enforcement policy.
/// Suppressions are applied only to policy signals and never mutate raw findings.
/// </summary>
public static class ValidationPolicyEvaluator
{
    public static ValidationPolicyOutcome Evaluate(ValidationResult result, string? requestedMode)
    {
        return Evaluate(result, new ValidationPolicyConfig { Mode = requestedMode ?? ValidationPolicyModes.Balanced });
    }

    public static ValidationPolicyOutcome Evaluate(ValidationResult result, ValidationPolicyConfig? policy)
    {
        ArgumentNullException.ThrowIfNull(result);

        policy ??= new ValidationPolicyConfig();
        var mode = NormalizeMode(policy.Mode);
        var ignoredSuppressions = new List<IgnoredPolicySuppression>();
        var activeSuppressions = GetActiveSuppressions(policy.Suppressions, ignoredSuppressions);
        var signals = BuildSignals(result, mode);
        var appliedSuppressions = new Dictionary<string, AppliedPolicySuppression>(StringComparer.OrdinalIgnoreCase);
        var unsuppressedSignals = new List<PolicySignal>();
        var suppressedSignalCount = 0;

        foreach (var signal in signals)
        {
            if (!signal.Suppressible)
            {
                unsuppressedSignals.Add(signal);
                continue;
            }

            var matchingSuppression = activeSuppressions.FirstOrDefault(suppression => Matches(suppression, signal));
            if (matchingSuppression == null)
            {
                unsuppressedSignals.Add(signal);
                continue;
            }

            suppressedSignalCount++;
            var suppressionId = GetSuppressionId(matchingSuppression);
            if (!appliedSuppressions.TryGetValue(suppressionId, out var applied))
            {
                applied = new AppliedPolicySuppression
                {
                    Id = suppressionId,
                    Owner = matchingSuppression.Owner!.Trim(),
                    Reason = matchingSuppression.Reason!.Trim(),
                    ExpiresOn = matchingSuppression.ExpiresOn
                };
                appliedSuppressions[suppressionId] = applied;
            }

            applied.MatchedSignalCount++;
            applied.MatchedSignals.Add(signal.Description);
        }

        var blockingSignals = unsuppressedSignals.Where(signal => Blocks(mode, signal)).ToList();
        return new ValidationPolicyOutcome
        {
            Mode = mode,
            Passed = blockingSignals.Count == 0,
            RecommendedExitCode = blockingSignals.Count == 0 ? 0 : 1,
            Summary = BuildSummary(mode, blockingSignals.Count == 0, blockingSignals.Count, suppressedSignalCount),
            TotalSignalCount = signals.Count,
            UnsuppressedSignalCount = unsuppressedSignals.Count,
            BlockingSignalCount = blockingSignals.Count,
            Reasons = blockingSignals.Select(signal => signal.Description).Distinct(StringComparer.Ordinal).ToList(),
            SuppressedSignalCount = suppressedSignalCount,
            AppliedSuppressions = appliedSuppressions.Values.OrderBy(value => value.Id, StringComparer.OrdinalIgnoreCase).ToList(),
            IgnoredSuppressions = ignoredSuppressions
        };
    }

    public static string NormalizeMode(string? requestedMode)
    {
        if (string.IsNullOrWhiteSpace(requestedMode))
        {
            return ValidationPolicyModes.Balanced;
        }

        return requestedMode.Trim().ToLowerInvariant() switch
        {
            ValidationPolicyModes.Advisory => ValidationPolicyModes.Advisory,
            ValidationPolicyModes.Strict => ValidationPolicyModes.Strict,
            _ => ValidationPolicyModes.Balanced
        };
    }

    private static List<ValidationPolicySuppression> GetActiveSuppressions(
        IEnumerable<ValidationPolicySuppression>? suppressions,
        List<IgnoredPolicySuppression> ignoredSuppressions)
    {
        var active = new List<ValidationPolicySuppression>();
        if (suppressions == null)
        {
            return active;
        }

        foreach (var suppression in suppressions)
        {
            var id = GetSuppressionId(suppression);

            if (string.IsNullOrWhiteSpace(suppression.Owner))
            {
                ignoredSuppressions.Add(new IgnoredPolicySuppression { Id = id, Reason = "Missing owner metadata." });
                continue;
            }

            if (string.IsNullOrWhiteSpace(suppression.Reason))
            {
                ignoredSuppressions.Add(new IgnoredPolicySuppression { Id = id, Reason = "Missing reason metadata." });
                continue;
            }

            if (!suppression.ExpiresOn.HasValue)
            {
                ignoredSuppressions.Add(new IgnoredPolicySuppression { Id = id, Reason = "Missing expiresOn metadata." });
                continue;
            }

            if (suppression.ExpiresOn.Value <= DateTimeOffset.UtcNow)
            {
                ignoredSuppressions.Add(new IgnoredPolicySuppression { Id = id, Reason = "Suppression has expired." });
                continue;
            }

            if (string.IsNullOrWhiteSpace(suppression.SignalId) &&
                string.IsNullOrWhiteSpace(suppression.RuleId) &&
                string.IsNullOrWhiteSpace(suppression.Component) &&
                string.IsNullOrWhiteSpace(suppression.Source) &&
                string.IsNullOrWhiteSpace(suppression.Category))
            {
                ignoredSuppressions.Add(new IgnoredPolicySuppression { Id = id, Reason = "Suppression has no selectors." });
                continue;
            }

            active.Add(suppression);
        }

        return active;
    }

    private static List<PolicySignal> BuildSignals(ValidationResult result, string mode)
    {
        var signals = new List<PolicySignal>();

        if (IsExecutionIntegrityFailure(result.OverallStatus))
        {
            signals.Add(new PolicySignal(
                Kind: PolicySignalKind.ExecutionIntegrity,
                SignalId: "POLICY.RUN.INTEGRITY",
                RuleId: null,
                Source: ValidationRuleSourceClassifier.GetLabel(ValidationRuleSource.Heuristic),
                Category: "Execution",
                Component: "validation-run",
                Severity: SeverityBand.Critical,
                Description: $"Validation run did not complete cleanly: {result.OverallStatus}.",
                Suppressible: false));
        }

        foreach (var criticalError in result.CriticalErrors)
        {
            signals.Add(new PolicySignal(
                Kind: PolicySignalKind.CriticalError,
                SignalId: "POLICY.CRITICAL_ERROR",
                RuleId: null,
                Source: ValidationRuleSourceClassifier.GetLabel(ValidationRuleSource.Heuristic),
                Category: "Execution",
                Component: "validation-run",
                Severity: SeverityBand.Critical,
                Description: $"Critical error recorded: {criticalError}",
                Suppressible: false));
        }

            if (result.VerdictAssessment is { } verdictAssessment)
            {
                AddVerdictSignals(signals, verdictAssessment);
                return signals;
            }

        if (result.TrustAssessment?.TierChecks is { Count: > 0 } tierChecks)
        {
            foreach (var check in tierChecks.Where(check => !check.Passed && string.Equals(check.Tier, "MUST", StringComparison.OrdinalIgnoreCase)))
            {
                signals.Add(new PolicySignal(
                    Kind: PolicySignalKind.MustFailure,
                    SignalId: "POLICY.TIER.MUST_FAILURE",
                    RuleId: null,
                    Source: ValidationRuleSourceClassifier.GetLabel(ValidationRuleSource.Spec),
                    Category: "TierCheck",
                    Component: string.IsNullOrWhiteSpace(check.Component) ? "tier-check" : check.Component,
                    Severity: SeverityBand.Critical,
                    Description: $"MUST requirement failed: {check.Requirement}{FormatDetail(check.Detail)}",
                    Suppressible: true));
            }

            foreach (var check in tierChecks.Where(check => !check.Passed && string.Equals(check.Tier, "SHOULD", StringComparison.OrdinalIgnoreCase)))
            {
                signals.Add(new PolicySignal(
                    Kind: PolicySignalKind.ShouldFailure,
                    SignalId: "POLICY.TIER.SHOULD_FAILURE",
                    RuleId: null,
                    Source: ValidationRuleSourceClassifier.GetLabel(ValidationRuleSource.Spec),
                    Category: "TierCheck",
                    Component: string.IsNullOrWhiteSpace(check.Component) ? "tier-check" : check.Component,
                    Severity: SeverityBand.High,
                    Description: $"SHOULD requirement failed: {check.Requirement}{FormatDetail(check.Detail)}",
                    Suppressible: true));
            }
        }

        AddFindingSignals(signals, result.ProtocolCompliance?.Findings, "protocol");
        AddFindingSignals(signals, result.ToolValidation?.AuthenticationSecurity?.StructuredFindings, "auth");
        AddFindingSignals(signals, result.SecurityTesting?.Findings, "security");
        AddFindingSignals(signals, result.PerformanceTesting?.Findings, "performance");
        AddFindingSignals(signals, result.ErrorHandling?.Findings, "error-handling");

        AddCatalogFindingSignals(
            signals,
            CollectCatalogFindings(
                result.ToolValidation?.Findings,
                result.ToolValidation?.AiReadinessFindings,
                result.ToolValidation?.ToolResults.SelectMany(tool => tool.Findings)),
            ValidationFindingAggregator.GetToolCatalogSize(result.ToolValidation),
            "tools");

        AddCatalogFindingSignals(
            signals,
            CollectCatalogFindings(
                result.ResourceTesting?.Findings,
                result.ResourceTesting?.ResourceResults.SelectMany(resource => resource.Findings)),
            result.ResourceTesting?.ResourcesDiscovered ?? result.ResourceTesting?.ResourceResults.Count ?? 0,
            "resources");

        AddCatalogFindingSignals(
            signals,
            CollectCatalogFindings(
                result.PromptTesting?.Findings,
                result.PromptTesting?.PromptResults.SelectMany(prompt => prompt.Findings)),
            result.PromptTesting?.PromptsDiscovered ?? result.PromptTesting?.PromptResults.Count ?? 0,
            "prompts");

        if (result.ProtocolCompliance?.Violations is { Count: > 0 } violations)
        {
            foreach (var violation in violations)
            {
                signals.Add(new PolicySignal(
                    Kind: PolicySignalKind.StructuredFinding,
                    SignalId: "POLICY.SPEC.VIOLATION",
                    RuleId: string.IsNullOrWhiteSpace(violation.CheckId) ? null : violation.CheckId,
                    Source: ValidationRuleSourceClassifier.GetLabel(violation),
                    Category: string.IsNullOrWhiteSpace(violation.Category) ? "ProtocolCompliance" : violation.Category,
                    Component: string.IsNullOrWhiteSpace(violation.Category) ? "protocol" : violation.Category,
                    Severity: MapSeverity(violation.Severity),
                    Description: violation.Description,
                    Suppressible: true));
            }
        }

        if (result.SecurityTesting?.Vulnerabilities is { Count: > 0 } vulnerabilities)
        {
            foreach (var vulnerability in vulnerabilities)
            {
                signals.Add(new PolicySignal(
                    Kind: PolicySignalKind.StructuredFinding,
                    SignalId: "POLICY.SECURITY.VULNERABILITY",
                    RuleId: string.IsNullOrWhiteSpace(vulnerability.Id) ? null : vulnerability.Id,
                    Source: ValidationRuleSourceClassifier.GetLabel(vulnerability),
                    Category: string.IsNullOrWhiteSpace(vulnerability.Category) ? "SecurityTesting" : vulnerability.Category,
                    Component: string.IsNullOrWhiteSpace(vulnerability.AffectedComponent) ? "security" : vulnerability.AffectedComponent,
                    Severity: MapSeverity(vulnerability.Severity),
                    Description: vulnerability.Description,
                    Suppressible: true));
            }
        }

        if (result.TrustAssessment?.BoundaryFindings is { Count: > 0 } boundaryFindings)
        {
            foreach (var finding in boundaryFindings)
            {
                signals.Add(new PolicySignal(
                    Kind: PolicySignalKind.BoundaryFinding,
                    SignalId: $"POLICY.BOUNDARY.{NormalizeToken(finding.Category)}",
                    RuleId: null,
                    Source: ValidationRuleSourceClassifier.GetLabel(ValidationRuleSource.Heuristic),
                    Category: string.IsNullOrWhiteSpace(finding.Category) ? "Boundary" : finding.Category,
                    Component: string.IsNullOrWhiteSpace(finding.Component) ? "boundary" : finding.Component,
                    Severity: MapSeverity(finding.Severity),
                    Description: finding.Description,
                    Suppressible: true));
            }
        }

        var trustLevel = result.TrustAssessment?.TrustLevel ?? McpTrustLevel.Unknown;
        if (mode == ValidationPolicyModes.Balanced && trustLevel != McpTrustLevel.Unknown && trustLevel < McpTrustLevel.L3_Acceptable)
        {
            signals.Add(new PolicySignal(
                Kind: PolicySignalKind.TrustThreshold,
                SignalId: "POLICY.TRUST.L3_MINIMUM",
                RuleId: null,
                Source: ValidationRuleSourceClassifier.GetLabel(ValidationRuleSource.Heuristic),
                Category: "Policy",
                Component: "trust-assessment",
                Severity: SeverityBand.Critical,
                Description: $"Trust level {trustLevel} is below the balanced minimum of L3_Acceptable.",
                Suppressible: true));
        }

        if (mode == ValidationPolicyModes.Strict)
        {
            if (trustLevel == McpTrustLevel.Unknown)
            {
                signals.Add(new PolicySignal(
                    Kind: PolicySignalKind.TrustAssessmentMissing,
                    SignalId: "POLICY.TRUST.ASSESSMENT_REQUIRED",
                    RuleId: null,
                    Source: ValidationRuleSourceClassifier.GetLabel(ValidationRuleSource.Heuristic),
                    Category: "Policy",
                    Component: "trust-assessment",
                    Severity: SeverityBand.High,
                    Description: "Trust assessment is unavailable, which is not sufficient for strict mode.",
                    Suppressible: true));
            }
            else if (trustLevel < McpTrustLevel.L4_Trusted)
            {
                signals.Add(new PolicySignal(
                    Kind: PolicySignalKind.TrustThreshold,
                    SignalId: "POLICY.TRUST.L4_MINIMUM",
                    RuleId: null,
                    Source: ValidationRuleSourceClassifier.GetLabel(ValidationRuleSource.Heuristic),
                    Category: "Policy",
                    Component: "trust-assessment",
                    Severity: SeverityBand.High,
                    Description: $"Trust level {trustLevel} is below the strict minimum of L4_Trusted.",
                    Suppressible: true));
            }
        }

        return signals;
    }

    private static void AddVerdictSignals(List<PolicySignal> signals, VerdictAssessment verdictAssessment)
    {
        foreach (var decision in verdictAssessment.TriggeredDecisions)
        {
            if (decision.Gate == GateOutcome.Note)
            {
                continue;
            }

            signals.Add(new PolicySignal(
                Kind: decision.Gate switch
                {
                    GateOutcome.Reject => PolicySignalKind.GateReject,
                    GateOutcome.ReviewRequired => PolicySignalKind.GateReviewRequired,
                    GateOutcome.CoverageDebt => PolicySignalKind.CoverageDebt,
                    _ => PolicySignalKind.StructuredFinding
                },
                SignalId: $"POLICY.VERDICT.{decision.Gate.ToString().ToUpperInvariant()}",
                RuleId: decision.RuleId,
                Source: ValidationRuleSourceClassifier.GetLabel(decision.Authority),
                Category: decision.Category,
                Component: decision.Component,
                Severity: MapSeverity(decision.Severity),
                Description: decision.Summary,
                Suppressible: true));
        }

        foreach (var decision in verdictAssessment.CoverageDecisions)
        {
            signals.Add(new PolicySignal(
                Kind: PolicySignalKind.CoverageDebt,
                SignalId: "POLICY.VERDICT.COVERAGE_DEBT",
                RuleId: decision.RuleId,
                Source: ValidationRuleSourceClassifier.GetLabel(decision.Authority),
                Category: decision.Category,
                Component: decision.Component,
                Severity: MapSeverity(decision.Severity),
                Description: decision.Summary,
                Suppressible: true));
        }

        if (verdictAssessment.ProtocolVerdict == ValidationVerdict.Unknown)
        {
            signals.Add(new PolicySignal(
                Kind: PolicySignalKind.VerdictUnavailable,
                SignalId: "POLICY.VERDICT.PROTOCOL_UNKNOWN",
                RuleId: null,
                Source: ValidationRuleSourceClassifier.GetLabel(ValidationRuleSource.Heuristic),
                Category: "Verdict",
                Component: "protocol",
                Severity: SeverityBand.High,
                Description: "Protocol verdict is unknown, so policy cannot treat the run as authoritative.",
                Suppressible: true));
        }

        if (verdictAssessment.CoverageVerdict == ValidationVerdict.Unknown)
        {
            signals.Add(new PolicySignal(
                Kind: PolicySignalKind.VerdictUnavailable,
                SignalId: "POLICY.VERDICT.COVERAGE_UNKNOWN",
                RuleId: null,
                Source: ValidationRuleSourceClassifier.GetLabel(ValidationRuleSource.Heuristic),
                Category: "Verdict",
                Component: "coverage",
                Severity: SeverityBand.High,
                Description: "Coverage verdict is unknown, so the run cannot be treated as fully authoritative.",
                Suppressible: true));
        }
    }

    private static void AddFindingSignals(List<PolicySignal> signals, IEnumerable<ValidationFinding>? findings, string defaultComponent)
    {
        if (findings == null)
        {
            return;
        }

        foreach (var finding in findings)
        {
            var component = string.IsNullOrWhiteSpace(finding.Component) ? defaultComponent : finding.Component;
            signals.Add(new PolicySignal(
                Kind: PolicySignalKind.StructuredFinding,
                SignalId: "POLICY.STRUCTURED_FINDING",
                RuleId: string.IsNullOrWhiteSpace(finding.RuleId) ? null : finding.RuleId,
                Source: finding.EffectiveSourceLabel,
                Category: string.IsNullOrWhiteSpace(finding.Category) ? "Validation" : finding.Category,
                Component: component,
                Severity: MapSeverity(finding.Severity),
                Description: finding.Summary,
                Suppressible: true));
        }
    }

    private static void AddCatalogFindingSignals(List<PolicySignal> signals, IReadOnlyCollection<ValidationFinding> findings, int totalComponents, string defaultComponent)
    {
        if (findings.Count == 0)
        {
            return;
        }

        foreach (var rollup in ValidationFindingAggregator.SummarizeFindingsByRule(findings, totalComponents))
        {
            var coverageText = rollup.TotalComponents > 0
                ? $"affected {rollup.AffectedComponents}/{rollup.TotalComponents} component(s) ({FormatPercent(rollup.CoverageRatio, "F0")})"
                : $"affected {rollup.AffectedComponents} component(s)";
            var sampleComponents = rollup.ExampleComponents.Count > 0
                ? $" Examples: {string.Join(", ", rollup.ExampleComponents)}."
                : string.Empty;

            signals.Add(new PolicySignal(
                Kind: PolicySignalKind.StructuredFinding,
                SignalId: "POLICY.STRUCTURED_FINDING",
                RuleId: string.IsNullOrWhiteSpace(rollup.RuleId) ? null : rollup.RuleId,
                Source: rollup.SourceLabel,
                Category: string.IsNullOrWhiteSpace(rollup.Category) ? "Validation" : rollup.Category,
                Component: defaultComponent,
                Severity: MapSeverity(rollup.Severity),
                Description: $"{rollup.RuleId}: {coverageText}. {rollup.Summary}{sampleComponents}",
                Suppressible: true));
        }
    }

    private static IReadOnlyCollection<ValidationFinding> CollectCatalogFindings(params IEnumerable<ValidationFinding>?[] sources)
    {
        return sources
            .Where(source => source != null)
            .SelectMany(source => source!)
            .GroupBy(
                finding => $"{finding.RuleId}|{finding.Category}|{finding.Component}|{finding.Summary}",
                StringComparer.OrdinalIgnoreCase)
            .Select(group => group.First())
            .ToList();
    }

    private static bool Matches(ValidationPolicySuppression suppression, PolicySignal signal)
    {
        if (!string.IsNullOrWhiteSpace(suppression.SignalId) &&
            !string.Equals(suppression.SignalId, signal.SignalId, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        if (!string.IsNullOrWhiteSpace(suppression.RuleId) &&
            !string.Equals(suppression.RuleId, signal.RuleId, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        if (!string.IsNullOrWhiteSpace(suppression.Component) &&
            !string.Equals(suppression.Component, signal.Component, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        if (!string.IsNullOrWhiteSpace(suppression.Source) &&
            !string.Equals(suppression.Source, signal.Source, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        if (!string.IsNullOrWhiteSpace(suppression.Category) &&
            !string.Equals(suppression.Category, signal.Category, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        return true;
    }

    private static bool Blocks(string mode, PolicySignal signal)
    {
        if (signal.Kind is PolicySignalKind.ExecutionIntegrity or PolicySignalKind.CriticalError)
        {
            return true;
        }

        return mode switch
        {
            ValidationPolicyModes.Advisory => false,
            ValidationPolicyModes.Strict => signal.Kind is PolicySignalKind.MustFailure
                or PolicySignalKind.ShouldFailure
                or PolicySignalKind.TrustThreshold
                or PolicySignalKind.TrustAssessmentMissing
                or PolicySignalKind.GateReject
                or PolicySignalKind.GateReviewRequired
                or PolicySignalKind.CoverageDebt
                or PolicySignalKind.VerdictUnavailable
                || signal.Severity >= SeverityBand.High,
            _ => signal.Kind is PolicySignalKind.MustFailure
                or PolicySignalKind.TrustThreshold
                or PolicySignalKind.GateReject
                or PolicySignalKind.GateReviewRequired
                or PolicySignalKind.CoverageDebt
                or PolicySignalKind.VerdictUnavailable
                || signal.Severity >= SeverityBand.Critical
        };
    }

    private static bool IsExecutionIntegrityFailure(ValidationStatus status)
    {
        return status is ValidationStatus.Error or ValidationStatus.Cancelled or ValidationStatus.InProgress or ValidationStatus.PartiallyCompleted;
    }

    private static string BuildSummary(string mode, bool passed, int blockingSignalCount, int suppressedSignalCount)
    {
        var modeLabel = char.ToUpperInvariant(mode[0]) + mode[1..];
        if (passed)
        {
            return suppressedSignalCount > 0
                ? $"{modeLabel} policy requirements satisfied after suppressing {suppressedSignalCount} signal(s)."
                : $"{modeLabel} policy requirements satisfied.";
        }

        return suppressedSignalCount > 0
            ? $"{modeLabel} policy blocked the validation result with {blockingSignalCount} unsuppressed signal(s) after suppressing {suppressedSignalCount}."
            : $"{modeLabel} policy blocked the validation result with {blockingSignalCount} unsuppressed signal(s).";
    }

    private static string GetSuppressionId(ValidationPolicySuppression suppression)
    {
        if (!string.IsNullOrWhiteSpace(suppression.Id))
        {
            return suppression.Id.Trim();
        }

        return string.Join("|", new[]
        {
            suppression.SignalId,
            suppression.RuleId,
            suppression.Component,
            suppression.Source,
            suppression.Category
        }.Where(value => !string.IsNullOrWhiteSpace(value)));
    }

    private static string FormatDetail(string? detail)
    {
        return string.IsNullOrWhiteSpace(detail) ? string.Empty : $" — {detail}";
    }

    private static string NormalizeToken(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return "UNKNOWN";
        }

        return new string(value.Trim().ToUpperInvariant().Select(ch => char.IsLetterOrDigit(ch) ? ch : '_').ToArray()).Trim('_');
    }

    private static SeverityBand MapSeverity(ValidationFindingSeverity severity)
    {
        return severity switch
        {
            ValidationFindingSeverity.Critical => SeverityBand.Critical,
            ValidationFindingSeverity.High => SeverityBand.High,
            ValidationFindingSeverity.Medium => SeverityBand.Medium,
            ValidationFindingSeverity.Low => SeverityBand.Low,
            _ => SeverityBand.Info
        };
    }

    private static SeverityBand MapSeverity(GateOutcome gate)
    {
        return gate switch
        {
            GateOutcome.Reject => SeverityBand.Critical,
            GateOutcome.ReviewRequired => SeverityBand.High,
            GateOutcome.CoverageDebt => SeverityBand.High,
            _ => SeverityBand.Info
        };
    }

    private static SeverityBand MapSeverity(ViolationSeverity severity)
    {
        return severity switch
        {
            ViolationSeverity.Critical => SeverityBand.Critical,
            ViolationSeverity.High => SeverityBand.High,
            ViolationSeverity.Medium => SeverityBand.Medium,
            ViolationSeverity.Low => SeverityBand.Low,
            _ => SeverityBand.Low
        };
    }

    private static SeverityBand MapSeverity(VulnerabilitySeverity severity)
    {
        return severity switch
        {
            VulnerabilitySeverity.Critical => SeverityBand.Critical,
            VulnerabilitySeverity.High => SeverityBand.High,
            VulnerabilitySeverity.Medium => SeverityBand.Medium,
            VulnerabilitySeverity.Low => SeverityBand.Low,
            VulnerabilitySeverity.Informational => SeverityBand.Info,
            _ => SeverityBand.Info
        };
    }

    private static SeverityBand MapSeverity(string? severity)
    {
        return severity?.Trim().ToLowerInvariant() switch
        {
            "critical" => SeverityBand.Critical,
            "high" => SeverityBand.High,
            "medium" => SeverityBand.Medium,
            "low" => SeverityBand.Low,
            _ => SeverityBand.Info
        };
    }

    private enum SeverityBand
    {
        Info = 1,
        Low = 2,
        Medium = 3,
        High = 4,
        Critical = 5
    }

    private static string FormatPercent(double ratio, string format)
    {
        return $"{(ratio * 100).ToString(format, CultureInfo.InvariantCulture)}%";
    }

    private enum PolicySignalKind
    {
        ExecutionIntegrity,
        CriticalError,
        MustFailure,
        ShouldFailure,
        GateReject,
        GateReviewRequired,
        CoverageDebt,
        StructuredFinding,
        BoundaryFinding,
        TrustThreshold,
        TrustAssessmentMissing,
        VerdictUnavailable
    }

    private sealed record PolicySignal(
        PolicySignalKind Kind,
        string SignalId,
        string? RuleId,
        string Source,
        string Category,
        string Component,
        SeverityBand Severity,
        string Description,
        bool Suppressible);
}
