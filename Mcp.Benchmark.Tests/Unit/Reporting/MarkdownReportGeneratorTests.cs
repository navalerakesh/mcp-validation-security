using Mcp.Benchmark.Core.Models;
using Mcp.Benchmark.Infrastructure.Services.Reporting;
using FluentAssertions;
using Xunit;

namespace Mcp.Benchmark.Tests.Unit.Reporting;

/// <summary>
/// Tests for MarkdownReportGenerator — verifies all report sections are generated correctly.
/// </summary>
public class MarkdownReportGeneratorTests
{
    private readonly MarkdownReportGenerator _generator = new();

    [Fact]
    public void GenerateReport_ShouldIncludeExecutiveSummary()
    {
        var result = BuildMinimalResult();

        var report = _generator.GenerateReport(result);

        report.Should().Contain("Executive Summary");
        report.Should().Contain("Compliance Score");
        report.Should().Contain("test-endpoint");
    }

    [Fact]
    public void GenerateReport_WithTrustAssessment_ShouldIncludeTrustLevel()
    {
        var result = BuildMinimalResult();
        result.TrustAssessment = new McpTrustAssessment
        {
            TrustLevel = McpTrustLevel.L4_Trusted,
            ProtocolCompliance = 100,
            SecurityPosture = 100,
            AiSafety = 85,
            OperationalReadiness = 90
        };

        var report = _generator.GenerateReport(result);

        report.Should().Contain("MCP Trust Level");
        report.Should().Contain("L4");
        report.Should().Contain("MCP Trust Assessment");
        report.Should().Contain("Protocol Compliance");
        report.Should().Contain("AI Safety");
    }

    [Fact]
    public void GenerateReport_WithBoundaryFindings_ShouldIncludeBoundaryTable()
    {
        var result = BuildMinimalResult();
        result.TrustAssessment = new McpTrustAssessment
        {
            TrustLevel = McpTrustLevel.L3_Acceptable,
            BoundaryFindings = new List<AiBoundaryFinding>
            {
                new AiBoundaryFinding
                {
                    Category = "Destructive",
                    Component = "delete_tool",
                    Severity = "High",
                    Description = "Tool appears destructive"
                }
            }
        };

        var report = _generator.GenerateReport(result);

        report.Should().Contain("AI Boundary Findings");
        report.Should().Contain("Destructive");
        report.Should().Contain("delete_tool");
    }

    [Fact]
    public void GenerateReport_WithComplianceTiers_ShouldIncludeMustShouldMay()
    {
        var result = BuildMinimalResult();
        result.TrustAssessment = new McpTrustAssessment
        {
            TrustLevel = McpTrustLevel.L4_Trusted,
            MustPassCount = 6,
            MustFailCount = 0,
            MustTotalCount = 6,
            ShouldPassCount = 5,
            ShouldFailCount = 2,
            ShouldTotalCount = 7,
            MaySupported = 3,
            MayTotal = 5,
            TierChecks = new List<ComplianceTierCheck>
            {
                new ComplianceTierCheck { Tier = "MUST", Requirement = "Test req", Passed = true, Component = "init" },
                new ComplianceTierCheck { Tier = "SHOULD", Requirement = "Test should", Passed = false, Component = "tools" },
                new ComplianceTierCheck { Tier = "MAY", Requirement = "Test may", Passed = true, Component = "caps" }
            }
        };

        var report = _generator.GenerateReport(result);

        report.Should().Contain("RFC 2119 Tiers");
        report.Should().Contain("MUST");
        report.Should().Contain("SHOULD");
        report.Should().Contain("MAY");
        report.Should().Contain("Fully compliant");
    }

    [Fact]
    public void GenerateReport_WithSkippedPerformance_ShouldShowSkippedNotZeros()
    {
        var result = BuildMinimalResult();
        result.PerformanceTesting = new PerformanceTestResult
        {
            Status = TestStatus.Skipped,
            Message = "Auth required"
        };

        var report = _generator.GenerateReport(result);

        report.Should().Contain("Skipped");
        report.Should().NotContain("0.00ms");
    }

    [Fact]
    public void GenerateReport_WithAttackSimulations_ShouldShowBlockedAndReflected()
    {
        var result = BuildMinimalResult();
        result.SecurityTesting = new SecurityTestResult
        {
            Status = TestStatus.Passed,
            SecurityScore = 85,
            AttackSimulations = new List<AttackSimulationResult>
            {
                new AttackSimulationResult { AttackVector = "SQLi", Description = "SQL Injection", DefenseSuccessful = true, ServerResponse = "Blocked" },
                new AttackSimulationResult { AttackVector = "XSS", Description = "XSS", DefenseSuccessful = false, AttackSuccessful = true, ServerResponse = "Skipped: no tools" }
            }
        };

        var report = _generator.GenerateReport(result);

        report.Should().Contain("BLOCKED");
        report.Should().Contain("SKIPPED");
    }

    [Fact]
    public void GenerateReport_WithToolGuidelineFindings_ShouldIncludeGuidelineSection()
    {
        var result = BuildMinimalResult();
        result.ToolValidation = new ToolTestResult
        {
            Status = TestStatus.Passed,
            Score = 100,
            ToolsDiscovered = 1,
            ToolResults = new List<IndividualToolResult>
            {
                new()
                {
                    ToolName = "plain_tool",
                    Status = TestStatus.Passed,
                    DisplayTitle = "Plain Tool",
                    ReadOnlyHint = true,
                    Findings = new List<ValidationFinding>
                    {
                        new()
                        {
                            RuleId = ValidationFindingRuleIds.ToolGuidelineDestructiveHintMissing,
                            Category = "McpGuideline",
                            Component = "plain_tool",
                            Severity = ValidationFindingSeverity.Low,
                            Summary = "Tool 'plain_tool' does not declare annotations.destructiveHint."
                        }
                    }
                }
            }
        };

        var report = _generator.GenerateReport(result);

        report.Should().Contain("MCP Guideline Findings");
        report.Should().Contain("Display Title");
        report.Should().Contain("readOnlyHint");
        report.Should().Contain(ValidationFindingRuleIds.ToolGuidelineDestructiveHintMissing);
    }

    private static ValidationResult BuildMinimalResult()
    {
        return new ValidationResult
        {
            ValidationId = "test-id",
            ServerConfig = new McpServerConfig { Endpoint = "test-endpoint", Transport = "http" },
            OverallStatus = ValidationStatus.Passed,
            ComplianceScore = 90.0,
            ProtocolCompliance = new ComplianceTestResult { Status = TestStatus.Passed, Score = 100 },
            ToolValidation = new ToolTestResult { Status = TestStatus.Passed, Score = 100 },
            ResourceTesting = new ResourceTestResult { Status = TestStatus.Passed, Score = 100 },
            PromptTesting = new PromptTestResult { Status = TestStatus.Passed, Score = 100 },
            PerformanceTesting = new PerformanceTestResult { Status = TestStatus.Passed, Score = 100, LoadTesting = new LoadTestResult() }
        };
    }
}
