using Mcp.Benchmark.Core.Models;
using Mcp.Benchmark.Core.Constants;
using Mcp.Benchmark.Infrastructure.Scoring;
using FluentAssertions;
using Xunit;

namespace Mcp.Benchmark.Tests.Unit.Scoring;

/// <summary>
/// Tests for McpTrustCalculator — the centralized trust level computation.
/// Verifies the 4-dimensional scoring, MUST failure gates, and boundary analysis.
/// </summary>
public class McpTrustCalculatorTests
{
    [Fact]
    public void Calculate_WithAllPerfectScores_ShouldReturnL4OrL5()
    {
        var result = BuildResult(protocolScore: 100, securityScore: 100, toolsPassed: 2, toolsDiscovered: 2);
        result.PerformanceTesting = new PerformanceTestResult { Status = TestStatus.Passed, Score = 100 };

        var trust = McpTrustCalculator.Calculate(result);

        // L4 or L5 depending on AI readiness score from tool schema quality
        trust.TrustLevel.Should().BeOneOf(McpTrustLevel.L4_Trusted, McpTrustLevel.L5_CertifiedSecure);
        trust.ProtocolCompliance.Should().Be(100);
        trust.SecurityPosture.Should().Be(100);
    }

    [Fact]
    public void Calculate_WithLowProtocol_ShouldDragToLowestDimension()
    {
        var result = BuildResult(protocolScore: 40, securityScore: 100, toolsPassed: 2, toolsDiscovered: 2);

        var trust = McpTrustCalculator.Calculate(result);

        // Protocol at 40% drags to L2
        trust.TrustLevel.Should().Be(McpTrustLevel.L2_Caution);
    }

    [Fact]
    public void Calculate_WithMustFailure_ShouldCapAtL2()
    {
        // Simulate a MUST failure by having protocol violations with capabilities missing
        var result = BuildResult(protocolScore: 100, securityScore: 100, toolsPassed: 2, toolsDiscovered: 2);
        result.ProtocolCompliance!.Violations = new List<ComplianceViolation>
        {
            new ComplianceViolation
            {
                CheckId = ValidationConstants.CheckIds.ProtocolInitializeMissingCapabilities,
                Description = "Server did not return capabilities in initialize response (MUST per spec)",
                Severity = ViolationSeverity.High,
                Category = "Protocol Lifecycle"
            }
        };

        var trust = McpTrustCalculator.Calculate(result);

        trust.MustFailCount.Should().BeGreaterThan(0);
        trust.TrustLevel.Should().Be(McpTrustLevel.L2_Caution);
    }

    [Fact]
    public void Calculate_WithErrorHandlingFindings_ShouldFailStandardErrorCodesMustCheck()
    {
        var result = BuildResult(protocolScore: 100, securityScore: 100, toolsPassed: 2, toolsDiscovered: 2);
        result.ErrorHandling = new ErrorHandlingTestResult
        {
            Status = TestStatus.Failed,
            Findings = new List<ValidationFinding>
            {
                new ValidationFinding
                {
                    RuleId = "MCP.ERROR_HANDLING.NON_STANDARD_ERROR_RESPONSE",
                    Category = "Protocol",
                    Component = "malformed-json",
                    Severity = ValidationFindingSeverity.High,
                    Summary = "Malformed JSON did not return -32700."
                }
            }
        };

        var trust = McpTrustCalculator.Calculate(result);

        trust.TierChecks.Should().Contain(check =>
            check.Requirement == McpComplianceTiers.Must.StandardErrorCodes &&
            !check.Passed &&
            check.Detail != null &&
            check.Detail.Contains("malformed-json", StringComparison.Ordinal));
        trust.MustFailCount.Should().BeGreaterThan(0);
    }

    [Fact]
    public void Calculate_WithGenericLifecycleDescriptionOnly_ShouldNotTreatProseAsStructuredMustFailure()
    {
        var result = BuildResult(protocolScore: 100, securityScore: 100, toolsPassed: 2, toolsDiscovered: 2);
        result.PerformanceTesting = new PerformanceTestResult { Status = TestStatus.Passed, Score = 100 };
        result.ToolValidation!.AiReadinessScore = 100;
        result.ToolValidation.ToolResults = new List<IndividualToolResult>();
        result.ProtocolCompliance!.Violations = new List<ComplianceViolation>
        {
            new ComplianceViolation
            {
                CheckId = ValidationConstants.CheckIds.ProtocolLifecycle,
                Description = "Server did not return capabilities in initialize response (MUST per spec)",
                Severity = ViolationSeverity.High,
                Category = "Protocol Lifecycle"
            }
        };

        var trust = McpTrustCalculator.Calculate(result);

        trust.MustFailCount.Should().Be(0);
        trust.TrustLevel.Should().BeOneOf(McpTrustLevel.L4_Trusted, McpTrustLevel.L5_CertifiedSecure);
    }

    [Fact]
    public void Calculate_WithInjectionReflection_ShouldPenalizeAiSafety()
    {
        var result = BuildResult(protocolScore: 100, securityScore: 100, toolsPassed: 2, toolsDiscovered: 2);
        result.SecurityTesting!.AttackSimulations = new List<AttackSimulationResult>
        {
            new AttackSimulationResult { AttackVector = "SQLi", Outcome = ValidationOutcome.Failed, AttackSuccessful = true, DefenseSuccessful = false }
        };

        var trust = McpTrustCalculator.Calculate(result);

        trust.AiSafety.Should().BeLessThan(100);
        trust.BoundaryFindings.Should().Contain(f => f.Category == "Injection");
        trust.TrustLevel.Should().Be(McpTrustLevel.L1_Untrusted);
    }

    [Fact]
    public void Calculate_WithRejectGatedSecurityFinding_ShouldForceL1()
    {
        var result = BuildResult(protocolScore: 100, securityScore: 95, toolsPassed: 2, toolsDiscovered: 2);
        result.SecurityTesting!.Findings.Add(new ValidationFinding
        {
            RuleId = "SECURITY.REJECT",
            Category = "Security",
            Component = "tools/call",
            Severity = ValidationFindingSeverity.High,
            GateOverride = GateOutcome.Reject,
            Summary = "Active security probe was accepted."
        });

        McpTrustCalculator.Calculate(result).TrustLevel.Should().Be(McpTrustLevel.L1_Untrusted);
    }

    [Fact]
    public void ApplyAuthoritativeVerdictCap_WithReject_ShouldCapAtL2AndPreserveDimensions()
    {
        var trust = new McpTrustAssessment
        {
            TrustLevel = McpTrustLevel.L5_CertifiedSecure,
            ProtocolCompliance = 98,
            SecurityPosture = 96,
            AiSafety = 94,
            OperationalReadiness = 92
        };
        var verdict = new VerdictAssessment
        {
            BaselineVerdict = ValidationVerdict.Reject,
            ProtocolVerdict = ValidationVerdict.Reject,
            CoverageVerdict = ValidationVerdict.Trusted
        };

        McpTrustCalculator.ApplyAuthoritativeVerdictCap(trust, verdict);

        trust.TrustLevel.Should().Be(McpTrustLevel.L2_Caution);
        trust.ProtocolCompliance.Should().Be(98);
        trust.SecurityPosture.Should().Be(96);
        trust.AiSafety.Should().Be(94);
        trust.OperationalReadiness.Should().Be(92);
    }

    [Fact]
    public void Calculate_WithSkippedPerformance_ShouldGiveNeutralOperational()
    {
        var result = BuildResult(protocolScore: 100, securityScore: 100, toolsPassed: 1, toolsDiscovered: 1);
        result.PerformanceTesting = new PerformanceTestResult { Status = TestStatus.Skipped };

        var trust = McpTrustCalculator.Calculate(result);

        trust.OperationalReadiness.Should().Be(70.0);
    }

    [Fact]
    public void Calculate_WithPublicTimeoutAndNoMeasurements_ShouldUseAdvisoryOperationalReadiness()
    {
        var result = BuildResult(protocolScore: 100, securityScore: 100, toolsPassed: 1, toolsDiscovered: 1);
        result.ServerConfig = new McpServerConfig { Profile = McpServerProfile.Public };
        result.PerformanceTesting = new PerformanceTestResult
        {
            Status = TestStatus.Failed,
            MeasurementDisposition = PerformanceMeasurementDisposition.TimedOut,
            Message = "Operation timed out or was cancelled",
            CriticalErrors = new List<string> { "Operation timed out or was cancelled" }
        };

        var trust = McpTrustCalculator.Calculate(result);

        trust.OperationalReadiness.Should().Be(70.0);
    }

    [Fact]
    public void Calculate_WithObservedCalibrationPressureButHealthyFinalRound_ShouldPreserveFinalOperationalReadiness()
    {
        var result = BuildResult(protocolScore: 100, securityScore: 100, toolsPassed: 1, toolsDiscovered: 1);
        result.ServerConfig = new McpServerConfig { Profile = McpServerProfile.Public };
        result.PerformanceTesting = new PerformanceTestResult
        {
            Status = TestStatus.Passed,
            Score = 88,
            LoadTesting = new LoadTestResult
            {
                TotalRequests = 20,
                SuccessfulRequests = 20,
                FailedRequests = 0,
                AverageResponseTimeMs = 180,
                ProbeRoundsExecuted = 2,
                ObservedRateLimitedRequests = 3,
                ObservedTransientFailures = 1
            }
        };

        var trust = McpTrustCalculator.Calculate(result);

        trust.OperationalReadiness.Should().Be(88.0);
    }

    [Fact]
    public void Calculate_ShouldDetectDestructiveTools()
    {
        var result = BuildResult(protocolScore: 100, securityScore: 100, toolsPassed: 1, toolsDiscovered: 1);
        result.ToolValidation!.ToolResults = new List<IndividualToolResult>
        {
            new IndividualToolResult { ToolName = "delete_all_data", Status = TestStatus.Passed }
        };

        var trust = McpTrustCalculator.Calculate(result);

        trust.DestructiveToolCount.Should().BeGreaterThan(0);
        trust.BoundaryFindings.Should().Contain(f => f.Category == "Destructive");
    }

    [Fact]
    public void Calculate_TierChecks_ShouldClassifyCorrectly()
    {
        var result = BuildResult(protocolScore: 100, securityScore: 100, toolsPassed: 2, toolsDiscovered: 2);

        var trust = McpTrustCalculator.Calculate(result);

        trust.MustTotalCount.Should().BeGreaterThan(0);
        trust.TierChecks.Should().Contain(c => c.Tier == "MUST");
        trust.TierChecks.Should().Contain(c => c.Tier == "MAY");
    }

    private static ValidationResult BuildResult(double protocolScore, double securityScore, int toolsPassed, int toolsDiscovered)
    {
        return new ValidationResult
        {
            ProtocolCompliance = new ComplianceTestResult
            {
                Status = TestStatus.Passed,
                Score = protocolScore,
                Violations = new List<ComplianceViolation>(),
                JsonRpcCompliance = new JsonRpcComplianceResult
                {
                    RequestFormatCompliant = true,
                    ResponseFormatCompliant = true,
                    ErrorHandlingCompliant = true
                }
            },
            SecurityTesting = new SecurityTestResult
            {
                Status = TestStatus.Passed,
                SecurityScore = securityScore,
                AttackSimulations = new List<AttackSimulationResult>()
            },
            ToolValidation = new ToolTestResult
            {
                Status = TestStatus.Passed,
                ToolsDiscovered = toolsDiscovered,
                ToolsTestPassed = toolsPassed,
                AiReadinessScore = 85,
                ToolResults = new List<IndividualToolResult>()
            },
            PerformanceTesting = new PerformanceTestResult
            {
                Status = TestStatus.Skipped
            }
        };
    }
}
