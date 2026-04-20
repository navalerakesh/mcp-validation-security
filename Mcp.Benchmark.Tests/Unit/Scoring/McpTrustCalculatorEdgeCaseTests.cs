using Mcp.Benchmark.Core.Models;
using Mcp.Benchmark.Core.Constants;
using Mcp.Benchmark.Infrastructure.Scoring;
using FluentAssertions;
using Xunit;

namespace Mcp.Benchmark.Tests.Unit.Scoring;

public class McpTrustCalculatorEdgeCaseTests
{
    [Fact]
    public void Calculate_WithNullResults_ShouldReturnL2OrLower()
    {
        var result = new ValidationResult();
        var trust = McpTrustCalculator.Calculate(result);
        // With no data, MUST checks may still pass (null-safe), 
        // but dimensions score 0 → L1 or L2
        trust.TrustLevel.Should().BeOneOf(McpTrustLevel.L1_Untrusted, McpTrustLevel.L2_Caution);
    }

    [Theory]
    [InlineData(90, 90, McpTrustLevel.L5_CertifiedSecure)]
    [InlineData(75, 100, McpTrustLevel.L4_Trusted)]
    [InlineData(50, 100, McpTrustLevel.L3_Acceptable)]
    [InlineData(25, 100, McpTrustLevel.L2_Caution)]
    [InlineData(10, 100, McpTrustLevel.L1_Untrusted)]
    public void Calculate_TrustLevelBoundaries(double protocolScore, double securityScore, McpTrustLevel expected)
    {
        var result = BuildResult(protocolScore, securityScore);
        result.PerformanceTesting = new PerformanceTestResult { Status = TestStatus.Passed, Score = 100 };
        result.ToolValidation!.AiReadinessScore = 100;
        result.ToolValidation.ToolResults = new List<IndividualToolResult>();

        var trust = McpTrustCalculator.Calculate(result);

        trust.TrustLevel.Should().Be(expected);
    }

    [Fact]
    public void Calculate_MustFailure_AlwaysCapsAtL2()
    {
        var result = BuildResult(100, 100);
        result.PerformanceTesting = new PerformanceTestResult { Status = TestStatus.Passed, Score = 100 };
        result.ToolValidation!.AiReadinessScore = 100;
        result.ToolValidation.ToolResults = new List<IndividualToolResult>();
        // Add capabilities violation (MUST failure)
        result.ProtocolCompliance!.Violations = new List<ComplianceViolation>
        {
            new() { Description = "Server did not return capabilities in initialize response (MUST per spec)", Severity = ViolationSeverity.High }
        };

        var trust = McpTrustCalculator.Calculate(result);

        trust.MustFailCount.Should().BeGreaterThan(0);
        trust.TrustLevel.Should().Be(McpTrustLevel.L2_Caution);
    }

    [Fact]
    public void Calculate_ShouldCountAllTiers()
    {
        var result = BuildResult(100, 100);
        result.ToolValidation!.AiReadinessScore = 90;
        result.ToolValidation.ToolResults = new List<IndividualToolResult>();

        var trust = McpTrustCalculator.Calculate(result);

        trust.MustTotalCount.Should().BeGreaterThan(0);
        trust.ShouldTotalCount.Should().BeGreaterThan(0);
        trust.MayTotal.Should().BeGreaterThan(0);
        trust.TierChecks.Should().NotBeEmpty();
    }

    [Fact]
    public void Calculate_WithDestructiveTools_ShouldFlag()
    {
        var result = BuildResult(100, 100);
        result.ToolValidation!.AiReadinessScore = 90;
        result.ToolValidation.ToolResults = new List<IndividualToolResult>
        {
            new() { ToolName = "delete_user", Status = TestStatus.Passed },
            new() { ToolName = "remove_all", Status = TestStatus.Passed },
            new() { ToolName = "drop_database", Status = TestStatus.Passed },
            new() { ToolName = "destroy_records", Status = TestStatus.Passed },
            new() { ToolName = "read_only_tool", Status = TestStatus.Passed }
        };

        var trust = McpTrustCalculator.Calculate(result);

        trust.DestructiveToolCount.Should().Be(4);
        trust.BoundaryFindings.Should().Contain(f => f.Category == "Destructive");
    }

    [Fact]
    public void Calculate_WithReadOnlyHint_ShouldOverrideDestructiveNameHeuristic()
    {
        var result = BuildResult(100, 100);
        result.ToolValidation!.AiReadinessScore = 90;
        result.ToolValidation.ToolResults = new List<IndividualToolResult>
        {
            new() { ToolName = "delete_preview", ReadOnlyHint = true, Status = TestStatus.Passed }
        };

        var trust = McpTrustCalculator.Calculate(result);

        trust.DestructiveToolCount.Should().Be(0);
        trust.BoundaryFindings.Should().NotContain(f => f.Category == "Destructive");
    }

    [Fact]
    public void Calculate_WithExfiltrationRisk_ShouldFlag()
    {
        var result = BuildResult(100, 100);
        result.ToolValidation!.AiReadinessScore = 90;
        result.ToolValidation.ToolResults = new List<IndividualToolResult>
        {
            new() { ToolName = "fetch_url_content", Status = TestStatus.Passed },
            new() { ToolName = "webhook_sender", Status = TestStatus.Passed }
        };

        var trust = McpTrustCalculator.Calculate(result);

        trust.DataExfiltrationRiskCount.Should().BeGreaterThan(0);
        trust.BoundaryFindings.Should().Contain(f => f.Category == "Exfiltration");
    }

    [Fact]
    public void Calculate_WithReflectedInjection_ShouldPenalizeAiSafety()
    {
        var result = BuildResult(100, 100);
        result.ToolValidation!.AiReadinessScore = 90;
        result.ToolValidation.ToolResults = new List<IndividualToolResult>();
        result.SecurityTesting!.AttackSimulations = new List<AttackSimulationResult>
        {
            new() { AttackSuccessful = true, DefenseSuccessful = false },
            new() { AttackSuccessful = true, DefenseSuccessful = false },
            new() { AttackSuccessful = false, DefenseSuccessful = true }
        };

        var trust = McpTrustCalculator.Calculate(result);

        trust.AiSafety.Should().BeLessThan(90);
        trust.BoundaryFindings.Should().Contain(f => f.Category == "Injection");
    }

    [Fact]
    public void Calculate_WithSkippedPerformance_ShouldBeNeutral()
    {
        var result = BuildResult(100, 100);
        result.ToolValidation!.AiReadinessScore = 95;
        result.ToolValidation.ToolResults = new List<IndividualToolResult>();
        result.PerformanceTesting = new PerformanceTestResult { Status = TestStatus.Skipped };

        var trust = McpTrustCalculator.Calculate(result);

        trust.OperationalReadiness.Should().Be(50.0);
    }

    [Fact]
    public void Calculate_WithLlmFriendlinessScore_ShouldBeParsed()
    {
        var result = BuildResult(100, 100);
        result.ToolValidation!.AiReadinessScore = 90;
        result.ToolValidation.ToolResults = new List<IndividualToolResult>
        {
            new()
            {
                ToolName = "test",
                Status = TestStatus.Passed,
                Findings = new List<ValidationFinding>
                {
                    new()
                    {
                        RuleId = ValidationFindingRuleIds.ToolLlmFriendliness,
                        Category = "AiReadiness",
                        Component = "test",
                        Severity = ValidationFindingSeverity.Info,
                        Summary = "🟢 LLM-Friendliness: 85/100 (Pro-LLM) — Error helps AI self-correct",
                        Metadata = new Dictionary<string, string> { ["score"] = "85", ["grade"] = "Pro-LLM" }
                    }
                }
            }
        };

        var trust = McpTrustCalculator.Calculate(result);

        trust.LlmFriendlinessScore.Should().Be(85.0);
    }

    [Fact]
    public void Calculate_WithAntiLlmErrors_ShouldPenalize()
    {
        var result = BuildResult(100, 100);
        result.ToolValidation!.AiReadinessScore = 90;
        result.ToolValidation.ToolResults = new List<IndividualToolResult>
        {
            new()
            {
                ToolName = "test",
                Status = TestStatus.Passed,
                Findings = new List<ValidationFinding>
                {
                    new()
                    {
                        RuleId = ValidationFindingRuleIds.ToolLlmFriendliness,
                        Category = "AiReadiness",
                        Component = "test",
                        Severity = ValidationFindingSeverity.High,
                        Summary = "🔴 LLM-Friendliness: 20/100 (Anti-LLM) — Error will cause AI hallucination/loops",
                        Metadata = new Dictionary<string, string> { ["score"] = "20", ["grade"] = "Anti-LLM" }
                    }
                }
            }
        };

        var trust = McpTrustCalculator.Calculate(result);

        trust.LlmFriendlinessScore.Should().Be(20.0);
        trust.AiSafety.Should().BeLessThan(90);
        trust.BoundaryFindings.Should().Contain(f => f.Category == "LLM-Hostile");
    }

    [Fact]
    public void Calculate_WithStructuredMustFindings_ShouldCapAtL2()
    {
        var result = BuildResult(100, 100);
        result.PerformanceTesting = new PerformanceTestResult { Status = TestStatus.Passed, Score = 100 };
        result.ToolValidation!.AiReadinessScore = 100;
        result.ToolValidation.ToolResults = new List<IndividualToolResult>
        {
            new()
            {
                ToolName = "broken_tool",
                Status = TestStatus.Failed,
                Findings = new List<ValidationFinding>
                {
                    new()
                    {
                        RuleId = ValidationFindingRuleIds.ToolCallMissingContentArray,
                        Category = "ProtocolCompliance",
                        Component = "broken_tool",
                        Severity = ValidationFindingSeverity.Critical,
                        Summary = "tools/call result missing 'content' array"
                    }
                }
            }
        };

        var trust = McpTrustCalculator.Calculate(result);

        trust.MustFailCount.Should().BeGreaterThan(0);
        trust.TrustLevel.Should().Be(McpTrustLevel.L2_Caution);
    }

    [Fact] 
    public void TrustLabel_ShouldReturnCorrectTextForEachLevel()
    {
        var a1 = new McpTrustAssessment { TrustLevel = McpTrustLevel.L5_CertifiedSecure };
        a1.TrustLabel.Should().Contain("Certified");

        var a2 = new McpTrustAssessment { TrustLevel = McpTrustLevel.L4_Trusted };
        a2.TrustLabel.Should().Contain("Trusted");

        var a3 = new McpTrustAssessment { TrustLevel = McpTrustLevel.L3_Acceptable };
        a3.TrustLabel.Should().Contain("Acceptable");

        var a4 = new McpTrustAssessment { TrustLevel = McpTrustLevel.L2_Caution };
        a4.TrustLabel.Should().Contain("Caution");

        var a5 = new McpTrustAssessment { TrustLevel = McpTrustLevel.L1_Untrusted };
        a5.TrustLabel.Should().Contain("Untrusted");

        var a0 = new McpTrustAssessment { TrustLevel = McpTrustLevel.Unknown };
        a0.TrustLabel.Should().Contain("Unknown");
    }

    private static ValidationResult BuildResult(double proto, double sec)
    {
        return new ValidationResult
        {
            ProtocolCompliance = new ComplianceTestResult
            {
                Status = TestStatus.Passed, Score = proto,
                Violations = new List<ComplianceViolation>(),
                JsonRpcCompliance = new JsonRpcComplianceResult { ErrorHandlingCompliant = true }
            },
            SecurityTesting = new SecurityTestResult
            {
                Status = TestStatus.Passed, SecurityScore = sec,
                AttackSimulations = new List<AttackSimulationResult>()
            },
            ToolValidation = new ToolTestResult { Status = TestStatus.Passed, ToolsDiscovered = 1, ToolsTestPassed = 1 },
            PerformanceTesting = new PerformanceTestResult { Status = TestStatus.Skipped }
        };
    }
}
