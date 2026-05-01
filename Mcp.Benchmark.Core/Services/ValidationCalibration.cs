using Mcp.Benchmark.Core.Models;

namespace Mcp.Benchmark.Core.Services;

/// <summary>
/// Central policy helpers for translating raw validation observations into calibrated
/// security, compliance, trust, and readiness decisions.
/// </summary>
public static class ValidationCalibration
{
    public const double StandardsAlignedScenarioScore = 100.0;
    public const double SecureCompatibleScenarioScore = 75.0;
    public const double AdvisoryPerformanceScore = 70.0;

    public static bool RequiresStrictAuthentication(McpServerProfile profile)
    {
        return profile is McpServerProfile.Authenticated or McpServerProfile.Enterprise;
    }

    public static bool IsPublicProfile(McpServerProfile profile)
    {
        return profile == McpServerProfile.Public;
    }

    public static int GetFunctionalProbeConcurrency(McpServerConfig serverConfig, int requestedConcurrency)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(requestedConcurrency);

        var requiresRemoteCalibration = serverConfig.Profile != McpServerProfile.Unspecified || serverConfig.Authentication != null;
        if (!requiresRemoteCalibration)
        {
            return requestedConcurrency;
        }

        return 1;
    }

    public static int GetInitialPerformanceProbeConcurrency(McpServerConfig serverConfig, int targetConcurrency)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(targetConcurrency);

        if (!IsPublicProfile(serverConfig.Profile) || targetConcurrency < 10)
        {
            return targetConcurrency;
        }

        return Math.Max(1, Math.Min(targetConcurrency, (int)Math.Ceiling(targetConcurrency / 5.0)));
    }

    public static int GetPerformanceProbeRequestCount(McpServerConfig serverConfig, int currentConcurrency, int targetConcurrency)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(currentConcurrency);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(targetConcurrency);

        if (IsPublicProfile(serverConfig.Profile) && currentConcurrency < targetConcurrency)
        {
            return Math.Max(20, currentConcurrency * 5);
        }

        return Math.Max(20, targetConcurrency * 5);
    }

    public static bool ShouldEscalatePerformanceProbe(
        McpServerConfig serverConfig,
        int currentConcurrency,
        int targetConcurrency,
        int failedRequests,
        int rateLimitedRequests,
        int transientFailures)
    {
        if (!IsPublicProfile(serverConfig.Profile) || targetConcurrency < 10 || currentConcurrency >= targetConcurrency)
        {
            return false;
        }

        return failedRequests == 0
            && rateLimitedRequests == 0
            && transientFailures == 0;
    }

    public static bool IsDiscoveryMethod(string? method)
    {
        return method is "initialize" or "tools/list" or "resources/list" or "prompts/list";
    }

    public static bool IsSensitiveMethod(string? method)
    {
        return !string.IsNullOrWhiteSpace(method) && !IsDiscoveryMethod(method);
    }

    public static bool IsBlockingAuthenticationFailure(AuthenticationScenario scenario, McpServerProfile profile)
    {
        return RequiresStrictAuthentication(profile)
            && scenario.AssessmentDisposition == AuthenticationAssessmentDisposition.Insecure
            && IsSensitiveMethod(scenario.Method)
            && !string.Equals(scenario.TestType, "Valid Token", StringComparison.OrdinalIgnoreCase);
    }

    public static VulnerabilitySeverity GetAuthenticationVulnerabilitySeverity(AuthenticationScenario scenario, McpServerProfile profile)
    {
        if (IsBlockingAuthenticationFailure(scenario, profile))
        {
            return VulnerabilitySeverity.Critical;
        }

        if (scenario.AssessmentDisposition == AuthenticationAssessmentDisposition.Insecure)
        {
            return VulnerabilitySeverity.High;
        }

        return VulnerabilitySeverity.Low;
    }

    public static bool ShouldTreatPerformanceAsAdvisory(McpServerConfig serverConfig, PerformanceTestResult? result)
    {
        if (result?.LoadTesting == null)
        {
            return false;
        }

        if (serverConfig.Profile is not (McpServerProfile.Public or McpServerProfile.Authenticated or McpServerProfile.Enterprise))
        {
            return false;
        }

        if (result.LoadTesting.TotalRequests <= 0)
        {
            return false;
        }

        var successRatio = (double)result.LoadTesting.SuccessfulRequests / result.LoadTesting.TotalRequests;
        return result.LoadTesting.FailedRequests > 0
            && successRatio >= 0.85
            && result.LoadTesting.AverageResponseTimeMs <= 500
            && result.LoadTesting.ConnectionErrors.Count == 0;
    }

    public static void ApplyPerformanceOutcomeCalibration(McpServerConfig serverConfig, PerformanceTestResult? result)
    {
        if (result == null)
        {
            return;
        }

        if (ShouldTreatUnavailablePublicPerformanceAsAdvisory(serverConfig, result))
        {
            ApplyAdvisoryPerformanceOutcome(
                serverConfig,
                result,
                ValidationFindingRuleIds.PerformancePublicRemoteTimeoutAdvisory,
                ValidationFindingSeverity.Critical,
                "Public remote synthetic load probe did not capture any measurements before timing out or being cancelled, so the performance result is treated as advisory rather than a readiness failure.",
                "Use endpoint-specific benchmarks or production telemetry for final capacity judgments.",
                "Public remote synthetic load probe did not capture measurements before timeout/cancellation; results are reported as advisory and excluded from pass/fail decisions.");
            return;
        }

        if (!ShouldTreatPerformanceAsAdvisory(serverConfig, result))
        {
            return;
        }

        ApplyAdvisoryPerformanceOutcome(
            serverConfig,
            result,
            ValidationFindingRuleIds.PerformancePublicRemoteAdvisory,
            ValidationFindingSeverity.Medium,
            "Remote public endpoint showed partial failures under synthetic pressure, so the performance result is treated as advisory rather than a readiness failure.",
            "Use endpoint-specific benchmarks or production telemetry for final capacity judgments.",
            "Synthetic load probe hit remote capacity limits or edge protections; results are reported as advisory and excluded from pass/fail decisions.");
    }

    public static double GetOperationalReadinessScore(McpServerConfig serverConfig, PerformanceTestResult? performanceTesting)
    {
        if (performanceTesting == null)
        {
            return AdvisoryPerformanceScore;
        }

        if (performanceTesting.Status == TestStatus.Skipped)
        {
            return Math.Max(performanceTesting.Score, AdvisoryPerformanceScore);
        }

        if (ShouldTreatUnavailablePublicPerformanceAsAdvisory(serverConfig, performanceTesting)
            || ShouldTreatPerformanceAsAdvisory(serverConfig, performanceTesting))
        {
            return Math.Max(performanceTesting.Score, AdvisoryPerformanceScore);
        }

        return performanceTesting.Score;
    }

    public static double CalculateRelativeExposurePenalty(int affectedComponents, int totalComponents, double maxPenalty, double minimumPenaltyIfAny = 0.0)
    {
        if (affectedComponents <= 0 || maxPenalty <= 0)
        {
            return 0.0;
        }

        var coverageRatio = ValidationFindingAggregator.CalculateCoverageRatio(affectedComponents, totalComponents);
        var penalty = minimumPenaltyIfAny + ((maxPenalty - minimumPenaltyIfAny) * coverageRatio);
        return Math.Round(Math.Min(maxPenalty, penalty), 1);
    }

    public static bool HasBlockingSecurityFailure(ValidationResult result)
    {
        if (result.SecurityTesting?.AuthenticationTestResult?.TestScenarios != null)
        {
            if (result.SecurityTesting.AuthenticationTestResult.TestScenarios.Any(s => IsBlockingAuthenticationFailure(s, result.ServerProfile)))
            {
                return true;
            }
        }

        return result.SecurityTesting?.Vulnerabilities.Any(v => v.Severity >= VulnerabilitySeverity.Critical) == true;
    }

    private static bool ShouldTreatUnavailablePublicPerformanceAsAdvisory(McpServerConfig serverConfig, PerformanceTestResult result)
    {
        if (!IsPublicProfile(serverConfig.Profile))
        {
            return false;
        }

        if (PerformanceMeasurementEvaluator.HasObservedMetrics(result))
        {
            return false;
        }

        return IsTimeoutOrCancellationReason(PerformanceMeasurementEvaluator.GetUnavailableReason(result, string.Empty));
    }

    private static bool IsTimeoutOrCancellationReason(string? reason)
    {
        if (string.IsNullOrWhiteSpace(reason))
        {
            return false;
        }

        return reason.Contains("timed out", StringComparison.OrdinalIgnoreCase)
            || reason.Contains("cancelled", StringComparison.OrdinalIgnoreCase)
            || reason.Contains("canceled", StringComparison.OrdinalIgnoreCase);
    }

    private static void ApplyAdvisoryPerformanceOutcome(
        McpServerConfig serverConfig,
        PerformanceTestResult result,
        string ruleId,
        ValidationFindingSeverity beforeSeverity,
        string findingSummary,
        string recommendation,
        string message)
    {
        var beforeStatus = result.Status;
        var beforeScore = result.Score;
        var unavailableReason = PerformanceMeasurementEvaluator.GetUnavailableReason(result, string.Empty);

        result.Status = TestStatus.Skipped;
        result.Score = Math.Max(result.Score, AdvisoryPerformanceScore);
        result.Message = message;

        AddPerformanceCalibrationOverride(
            serverConfig,
            result,
            ruleId,
            message,
            recommendation,
            beforeStatus,
            result.Status,
            beforeScore,
            result.Score,
            beforeSeverity,
            ValidationFindingSeverity.Info,
            unavailableReason);

        if (!result.PerformanceBottlenecks.Contains(message, StringComparer.Ordinal))
        {
            result.PerformanceBottlenecks.Add(message);
        }

        if (result.Findings.Any(f => string.Equals(f.RuleId, ruleId, StringComparison.OrdinalIgnoreCase)))
        {
            return;
        }

        result.Findings.Add(new ValidationFinding
        {
            RuleId = ruleId,
            Category = "Performance",
            Component = "load-testing",
            Severity = ValidationFindingSeverity.Info,
            Summary = findingSummary,
            Recommendation = recommendation,
            Source = ValidationRuleSource.Guideline,
            Metadata =
            {
                ["calibrationOverride"] = "true",
                ["beforeSeverity"] = beforeSeverity.ToString(),
                ["afterSeverity"] = ValidationFindingSeverity.Info.ToString(),
                ["changedDeterministicVerdict"] = "false"
            }
        });
    }

    private static void AddPerformanceCalibrationOverride(
        McpServerConfig serverConfig,
        PerformanceTestResult result,
        string ruleId,
        string reason,
        string recommendation,
        TestStatus beforeStatus,
        TestStatus afterStatus,
        double beforeScore,
        double afterScore,
        ValidationFindingSeverity beforeSeverity,
        ValidationFindingSeverity afterSeverity,
        string unavailableReason)
    {
        if (result.CalibrationOverrides.Any(overrideRecord => string.Equals(overrideRecord.RuleId, ruleId, StringComparison.OrdinalIgnoreCase)))
        {
            return;
        }

        var totalRequests = result.LoadTesting.TotalRequests;
        var successRatio = totalRequests > 0
            ? (double)result.LoadTesting.SuccessfulRequests / totalRequests
            : 0.0;
        result.CalibrationOverrides.Add(new PerformanceCalibrationOverride
        {
            RuleId = ruleId,
            Reason = reason,
            Recommendation = recommendation,
            AffectedTests = ["performance/load-testing"],
            Inputs = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["serverProfile"] = serverConfig.Profile.ToString(),
                ["metricsCaptured"] = PerformanceMeasurementEvaluator.HasObservedMetrics(result).ToString(),
                ["totalRequests"] = totalRequests.ToString(System.Globalization.CultureInfo.InvariantCulture),
                ["successfulRequests"] = result.LoadTesting.SuccessfulRequests.ToString(System.Globalization.CultureInfo.InvariantCulture),
                ["failedRequests"] = result.LoadTesting.FailedRequests.ToString(System.Globalization.CultureInfo.InvariantCulture),
                ["successRatio"] = successRatio.ToString("F3", System.Globalization.CultureInfo.InvariantCulture),
                ["averageResponseTimeMs"] = result.LoadTesting.AverageResponseTimeMs.ToString("F1", System.Globalization.CultureInfo.InvariantCulture),
                ["connectionErrors"] = result.LoadTesting.ConnectionErrors.Count.ToString(System.Globalization.CultureInfo.InvariantCulture),
                ["unavailableReason"] = string.IsNullOrWhiteSpace(unavailableReason) ? "-" : unavailableReason,
                ["advisoryScoreFloor"] = AdvisoryPerformanceScore.ToString("F1", System.Globalization.CultureInfo.InvariantCulture)
            },
            BeforeStatus = beforeStatus,
            AfterStatus = afterStatus,
            BeforeScore = beforeScore,
            AfterScore = afterScore,
            BeforeSeverity = beforeSeverity,
            AfterSeverity = afterSeverity,
            ChangedComponentStatus = beforeStatus != afterStatus,
            ChangedDeterministicVerdict = false
        });
    }
}