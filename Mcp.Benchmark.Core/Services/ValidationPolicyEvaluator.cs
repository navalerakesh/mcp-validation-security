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
        var activeSuppressions = GetActiveSuppressions(policy.Suppressions, ignoredSuppressions, DateTimeOffset.UtcNow);
        var signals = policy.RegressionOnly
            ? BuildRegressionSignals(result)
            : BuildAssuranceSignals(result);
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
            RegressionOnly = policy.RegressionOnly,
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

    private static List<PolicySignal> BuildRegressionSignals(ValidationResult result)
    {
        var signals = new List<PolicySignal>();
        if (IsExecutionIntegrityFailure(result.OverallStatus))
        {
            signals.Add(new PolicySignal(
                PolicySignalKind.ExecutionIntegrity,
                "POLICY.RUN.INTEGRITY",
                null,
                ValidationRuleSourceClassifier.GetLabel(ValidationRuleSource.Heuristic),
                "Execution",
                "validation-run",
                SeverityBand.Critical,
                $"Validation run did not complete cleanly: {result.OverallStatus}.",
                false));
        }

        if (result.BaselineComparison == null)
        {
            signals.Add(new PolicySignal(
                PolicySignalKind.BaselineUnavailable,
                "POLICY.BASELINE.REQUIRED",
                null,
                ValidationRuleSourceClassifier.GetLabel(ValidationRuleSource.Heuristic),
                "Baseline",
                "validation-run",
                SeverityBand.Critical,
                "Regression-only policy requires a compatible baseline validation result.",
                false));
            return signals;
        }

        foreach (var change in result.BaselineComparison.NewNonSuccessClaims)
        {
            signals.Add(new PolicySignal(
                PolicySignalKind.BaselineRegression,
            "POLICY.BASELINE.NEW_CLAIM",
            change.ClaimId,
            change.Authority.ToString(),
            change.Dimension.ToString(),
            change.Subject,
            change.Outcome is AssuranceClaimOutcome.Violated or AssuranceClaimOutcome.Error ? SeverityBand.Critical : SeverityBand.High,
            $"New non-success assurance claim relative to baseline: {change.ClaimId} ({change.Outcome}).",
                true));
        }

        if (result.BaselineComparison.IsRegression && result.BaselineComparison.NewNonSuccessClaims.Count == 0)
        {
            signals.Add(new PolicySignal(
                PolicySignalKind.BaselineRegression,
            "POLICY.BASELINE.ASSESSMENT_DEGRADED",
                null,
                ValidationRuleSourceClassifier.GetLabel(ValidationRuleSource.Heuristic),
                "Baseline",
            "assurance-assessments",
                SeverityBand.Critical,
            "One or more independent assurance assessments degraded relative to baseline.",
                true));
        }

        return signals;
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
        List<IgnoredPolicySuppression> ignoredSuppressions,
        DateTimeOffset now)
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

            if (suppression.ExpiresOn.Value <= now)
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

    public static PolicySuppressionAssessment AssessSuppressions(
        ValidationPolicyConfig? policy,
        DateTimeOffset now)
    {
        var ignored = new List<IgnoredPolicySuppression>();
        var active = GetActiveSuppressions(policy?.Suppressions, ignored, now);
        return new PolicySuppressionAssessment
        {
            ActiveCount = active.Count,
            InvalidSuppressions = ignored
        };
    }

    private static List<PolicySignal> BuildAssuranceSignals(ValidationResult result)
    {
        var signals = new List<PolicySignal>();
        if (IsExecutionIntegrityFailure(result.OverallStatus))
        {
            signals.Add(new PolicySignal(
                PolicySignalKind.ExecutionIntegrity,
                "POLICY.RUN.INTEGRITY",
                null,
                AssuranceAuthority.ProviderPolicy.ToString(),
                AssuranceDimension.ExecutionIntegrity.ToString(),
                "validation-run",
                SeverityBand.Critical,
                $"Validation run did not complete cleanly: {result.OverallStatus}.",
                false));
        }

        foreach (var criticalError in result.CriticalErrors)
        {
            signals.Add(new PolicySignal(
                PolicySignalKind.CriticalError,
                "POLICY.CRITICAL_ERROR",
                null,
                AssuranceAuthority.ProviderPolicy.ToString(),
                AssuranceDimension.ExecutionIntegrity.ToString(),
                "validation-run",
                SeverityBand.Critical,
                $"Critical error recorded: {criticalError}",
                false));
        }

        var nonSuccessClaims = result.Assurance.Claims.Where(claim =>
            claim.Applicability == AssuranceApplicability.Applicable &&
            claim.Outcome is not (AssuranceClaimOutcome.Satisfied or AssuranceClaimOutcome.NotApplicable)).ToArray();
        if (!nonSuccessClaims.Any(claim => claim.Dimension is AssuranceDimension.CoreProtocol or AssuranceDimension.CapabilityContract or AssuranceDimension.ExternalConformance)) AddAssessmentSignal(
            signals,
            "POLICY.ASSURANCE.CORE",
            AssuranceDimension.CoreProtocol,
            result.Assurance.Assessments.CoreConformance.Status == CoreConformanceStatus.Nonconformant,
            result.Assurance.Assessments.CoreConformance.Status == CoreConformanceStatus.Incomplete,
            $"Core conformance is {result.Assurance.Assessments.CoreConformance.Status}.");
        if (!nonSuccessClaims.Any(claim => claim.Dimension == AssuranceDimension.Security)) AddAssessmentSignal(
            signals,
            "POLICY.ASSURANCE.SECURITY",
            AssuranceDimension.Security,
            result.Assurance.Assessments.SecurityAssurance.Status == SecurityAssuranceStatus.IssuesFound,
            result.Assurance.Assessments.SecurityAssurance.Status == SecurityAssuranceStatus.Incomplete,
            $"Security assurance is {result.Assurance.Assessments.SecurityAssurance.Status}.");
        if (!nonSuccessClaims.Any(claim => claim.Dimension == AssuranceDimension.Operations)) AddAssessmentSignal(
            signals,
            "POLICY.ASSURANCE.OPERATIONS",
            AssuranceDimension.Operations,
            result.Assurance.Assessments.OperationalReadiness.Status == OperationalReadinessStatus.NotReady,
            result.Assurance.Assessments.OperationalReadiness.Status == OperationalReadinessStatus.Incomplete,
            $"Operational readiness is {result.Assurance.Assessments.OperationalReadiness.Status}.");

        foreach (var claim in nonSuccessClaims)
        {
            var advisory = claim.Authority is AssuranceAuthority.Heuristic or AssuranceAuthority.ModelAdvisory;
            var kind = claim.Outcome switch
            {
                AssuranceClaimOutcome.Violated or AssuranceClaimOutcome.Error => PolicySignalKind.GateReject,
                AssuranceClaimOutcome.Incomplete => PolicySignalKind.CoverageDebt,
                AssuranceClaimOutcome.ReviewRequired when !advisory => PolicySignalKind.GateReviewRequired,
                _ => PolicySignalKind.StructuredFinding
            };
            var severity = claim.Outcome switch
            {
                AssuranceClaimOutcome.Error => SeverityBand.Critical,
                AssuranceClaimOutcome.Violated when claim.RequirementLevel is AssuranceRequirementLevel.Must or AssuranceRequirementLevel.SecurityControl => SeverityBand.Critical,
                AssuranceClaimOutcome.Violated => SeverityBand.High,
                AssuranceClaimOutcome.Incomplete => SeverityBand.Medium,
                _ => SeverityBand.Medium
            };
            claim.Metadata.TryGetValue("ruleId", out var ruleId);
            signals.Add(new PolicySignal(
                kind,
                $"POLICY.CLAIM.{NormalizeToken(claim.ClaimId)}",
                string.IsNullOrWhiteSpace(ruleId) ? claim.ClaimId : ruleId,
                claim.Authority.ToString(),
                claim.Dimension.ToString(),
                claim.Subject,
                severity,
                claim.Summary,
                true));
        }

        return signals
            .GroupBy(signal => $"{signal.SignalId}|{signal.Component}|{signal.Description}", StringComparer.Ordinal)
            .Select(group => group.First())
            .ToList();
    }

    private static void AddAssessmentSignal(
        ICollection<PolicySignal> signals,
        string signalId,
        AssuranceDimension dimension,
        bool failed,
        bool incomplete,
        string description)
    {
        if (!failed && !incomplete) return;
        signals.Add(new PolicySignal(
            failed ? PolicySignalKind.GateReject : PolicySignalKind.CoverageDebt,
            signalId,
            null,
            AssuranceAuthority.ProviderPolicy.ToString(),
            dimension.ToString(),
            "assurance-assessment",
            failed ? SeverityBand.Critical : SeverityBand.Medium,
            description,
            false));
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
        if (signal.Kind is PolicySignalKind.ExecutionIntegrity or PolicySignalKind.CriticalError or PolicySignalKind.BaselineUnavailable)
        {
            return true;
        }

        return mode switch
        {
            ValidationPolicyModes.Advisory => false,
            ValidationPolicyModes.Strict => signal.Kind is PolicySignalKind.GateReject
                or PolicySignalKind.GateReviewRequired
                or PolicySignalKind.CoverageDebt
                || signal.Severity >= SeverityBand.High,
            _ => signal.Kind is PolicySignalKind.GateReject
                || signal.Severity >= SeverityBand.Critical
        };
    }

    private static bool IsExecutionIntegrityFailure(ValidationStatus status)
    {
        return status is ValidationStatus.Error or ValidationStatus.Cancelled or ValidationStatus.InProgress;
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
            ? $"{modeLabel} policy blocked the validation result with {blockingSignalCount} blocking signal(s) after suppressing {suppressedSignalCount}."
            : $"{modeLabel} policy blocked the validation result with {blockingSignalCount} blocking signal(s).";
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

    private static string NormalizeToken(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return "UNKNOWN";
        }

        return new string(value.Trim().ToUpperInvariant().Select(ch => char.IsLetterOrDigit(ch) ? ch : '_').ToArray()).Trim('_');
    }

    private enum SeverityBand
    {
        Info = 1,
        Low = 2,
        Medium = 3,
        High = 4,
        Critical = 5
    }

    private enum PolicySignalKind
    {
        ExecutionIntegrity,
        CriticalError,
        GateReject,
        GateReviewRequired,
        CoverageDebt,
        StructuredFinding,
        BaselineRegression,
        BaselineUnavailable
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
