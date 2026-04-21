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

    public static double GetOperationalReadinessScore(PerformanceTestResult? performanceTesting)
    {
        if (performanceTesting == null)
        {
            return AdvisoryPerformanceScore;
        }

        if (performanceTesting.Status == TestStatus.Skipped)
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
}