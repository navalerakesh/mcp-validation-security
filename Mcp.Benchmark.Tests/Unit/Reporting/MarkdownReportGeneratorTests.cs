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
        result.ValidationConfig.Reporting.IncludeDetailedLogs = true;
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
        result.ValidationConfig.Reporting.IncludeDetailedLogs = true;
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
        result.ValidationConfig.Reporting.IncludeDetailedLogs = true;
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
        result.ValidationConfig.Reporting.IncludeDetailedLogs = true;
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
        result.ValidationConfig.Reporting.IncludeDetailedLogs = true;
        result.ToolValidation = new ToolTestResult
        {
            Status = TestStatus.Passed,
            Score = 100,
            ToolsDiscovered = 5,
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
        report.Should().Contain("`guideline`");
        report.Should().Contain("Coverage shows how prevalent each issue is across the discovered tool catalog");
        report.Should().Contain("1/5 (20%)");
        report.Should().Contain(ValidationFindingRuleIds.ToolGuidelineDestructiveHintMissing);
    }

    [Fact]
    public void GenerateReport_WithRepeatedAiReadinessFindings_ShouldShowCoverageInsteadOfRawRows()
    {
        var result = BuildMinimalResult();
        result.ValidationConfig.Reporting.IncludeDetailedLogs = true;
        result.ToolValidation = new ToolTestResult
        {
            Status = TestStatus.Passed,
            Score = 100,
            ToolsDiscovered = 5,
            AiReadinessScore = 72,
            AiReadinessIssues = new List<string> { "placeholder" },
            AiReadinessFindings = new List<ValidationFinding>
            {
                new()
                {
                    RuleId = ValidationFindingRuleIds.AiReadinessMissingParameterDescriptions,
                    Category = "AiReadiness",
                    Component = "tool_1",
                    Severity = ValidationFindingSeverity.Medium,
                    Summary = "Tool 'tool_1': 1/2 parameters lack descriptions (increases hallucination risk)"
                },
                new()
                {
                    RuleId = ValidationFindingRuleIds.AiReadinessMissingParameterDescriptions,
                    Category = "AiReadiness",
                    Component = "tool_2",
                    Severity = ValidationFindingSeverity.Medium,
                    Summary = "Tool 'tool_2': 1/2 parameters lack descriptions (increases hallucination risk)"
                }
            }
        };

        var report = _generator.GenerateReport(result);

        report.Should().Contain("AI Readiness Assessment");
        report.Should().Contain("2/5 (40%)");
        report.Should().Contain(ValidationFindingRuleIds.AiReadinessMissingParameterDescriptions);
    }

    [Fact]
    public void GenerateReport_WithProtocolCriticalErrors_ShouldIncludeThemEvenWithoutViolations()
    {
        var result = BuildMinimalResult();
        result.ValidationConfig.Reporting.IncludeDetailedLogs = true;
        result.ProtocolCompliance = new ComplianceTestResult
        {
            Status = TestStatus.Failed,
            ComplianceScore = 0,
            CriticalErrors = new List<string> { "Operation timed out or was cancelled" },
            Violations = new List<ComplianceViolation>()
        };

        var report = _generator.GenerateReport(result);

        report.Should().Contain("Critical Errors");
        report.Should().Contain("Operation timed out or was cancelled");
    }

    [Fact]
    public void GenerateReport_WithTimedOutPerformanceAndNoMeasurements_ShouldMarkMetricsUnavailable()
    {
        var result = BuildMinimalResult();
        result.ValidationConfig.Reporting.IncludeDetailedLogs = true;
        result.PerformanceTesting = new PerformanceTestResult
        {
            Status = TestStatus.Failed,
            CriticalErrors = new List<string> { "Operation timed out or was cancelled" }
        };

        var report = _generator.GenerateReport(result);

        report.Should().Contain("Performance Metrics");
        report.Should().Contain("**Measurements:** unavailable");
        report.Should().Contain("Operation timed out or was cancelled");
        report.Should().NotContain("0.00ms | 🚀 Excellent");
    }

    [Fact]
    public void GenerateReport_MinimalMode_ShouldPreferExecutiveSections()
    {
        var result = BuildMinimalResult();
        result.ValidationConfig.Reporting.ApplyDetailLevel(ReportDetailLevel.Minimal);
        result.CriticalErrors.Add("Top-level transport issue detected.");
        result.ProtocolCompliance = new ComplianceTestResult
        {
            Status = TestStatus.Failed,
            ComplianceScore = 50,
            Violations = new List<ComplianceViolation>
            {
                new()
                {
                    CheckId = "MCP.TEST.FAILURE",
                    Description = "Protocol contract failed.",
                    Severity = ViolationSeverity.High,
                    Category = "Protocol"
                }
            }
        };

        var report = _generator.GenerateReport(result);

        report.Should().Contain("Priority Findings");
        report.Should().Contain("Top-level transport issue detected.");
        report.Should().Contain("MCP.TEST.FAILURE: Protocol contract failed.");
        report.Should().NotContain("## 6. Security Assessment");
        report.Should().NotContain("## 7. Tool Validation");
    }

    [Fact]
    public void GenerateReport_WithBootstrapHealth_ShouldIncludeConnectivitySection()
    {
        var result = BuildMinimalResult();
        result.BootstrapHealth = new HealthCheckResult
        {
            IsHealthy = false,
            Disposition = HealthCheckDisposition.TransientFailure,
            ResponseTimeMs = 125.4,
            ErrorMessage = "HTTP 429 Too Many Requests"
        };

        var report = _generator.GenerateReport(result);

        report.Should().Contain("Connectivity & Session Bootstrap");
        report.Should().Contain("Transient Failure");
        report.Should().Contain("calibrated advisory bootstrap state");
    }

    [Fact]
    public void GenerateReport_WithClientCompatibility_ShouldIncludeCompatibilitySection()
    {
        var result = BuildMinimalResult();
        result.ClientCompatibility = new ClientCompatibilityReport
        {
            RequestedProfiles = new List<string> { "claude-code" },
            Assessments = new List<ClientProfileAssessment>
            {
                new()
                {
                    ProfileId = "claude-code",
                    DisplayName = "Claude Code",
                    Status = ClientProfileCompatibilityStatus.CompatibleWithWarnings,
                    Summary = "Required compatibility checks passed, with 1 advisory gap.",
                    PassedRequirements = 2,
                    WarningRequirements = 1,
                    Requirements = new List<ClientProfileRequirementAssessment>
                    {
                        new()
                        {
                            RequirementId = "tool-tool-metadata",
                            Title = "Tool presentation and approval metadata is complete",
                            Outcome = ClientProfileRequirementOutcome.Warning,
                            Level = ClientProfileRequirementLevel.Recommended,
                            Summary = "Advisory tool guidance gaps affect 1/1 tool(s).",
                            RuleIds = new List<string> { ValidationFindingRuleIds.ToolGuidelineDisplayTitleMissing },
                            ExampleComponents = new List<string> { "search_docs" }
                        }
                    }
                }
            }
        };

        var report = _generator.GenerateReport(result);

        report.Should().Contain("Client Profile Compatibility");
        report.Should().Contain("Claude Code");
        report.Should().Contain("Compatible with warnings");
        report.Should().Contain("Tool presentation and approval metadata is complete");
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
