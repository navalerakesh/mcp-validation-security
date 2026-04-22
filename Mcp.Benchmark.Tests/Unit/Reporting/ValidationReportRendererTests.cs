using System.Text.Json;
using System.Xml.Linq;
using FluentAssertions;
using Mcp.Benchmark.Core.Models;
using Mcp.Benchmark.Tests.Fixtures;
using Mcp.Benchmark.Infrastructure.Services.Reporting;
using Xunit;

namespace Mcp.Benchmark.Tests.Unit.Reporting;

public class ValidationReportRendererTests
{
    private readonly ValidationReportRenderer _renderer = new();

    [Fact]
    public void GenerateSarifReport_ShouldIncludeStructuredFindingsViolationsAndVulnerabilities()
    {
        var result = new ValidationResult
        {
            ValidationId = "validation-123",
            StartTime = new DateTime(2026, 04, 20, 10, 00, 00, DateTimeKind.Utc),
            EndTime = new DateTime(2026, 04, 20, 10, 00, 10, DateTimeKind.Utc),
            OverallStatus = ValidationStatus.Failed,
            ServerConfig = new McpServerConfig
            {
                Endpoint = "https://example.test/mcp",
                Transport = "http"
            },
            ValidationConfig = new McpValidatorConfiguration
            {
                Reporting = new ReportingConfig
                {
                    SpecProfile = "2025-11-25"
                }
            },
            ToolValidation = new ToolTestResult
            {
                ToolResults = new List<IndividualToolResult>
                {
                    new()
                    {
                        ToolName = "fetch_document",
                        Findings = new List<ValidationFinding>
                        {
                            new()
                            {
                                RuleId = ValidationFindingRuleIds.ToolGuidelineOpenWorldHintMissing,
                                Category = "McpGuideline",
                                Component = "fetch_document",
                                Severity = ValidationFindingSeverity.Low,
                                Summary = "Tool 'fetch_document' does not declare annotations.openWorldHint.",
                                Recommendation = "Declare open-world behavior explicitly."
                            }
                        }
                    }
                }
            },
            ProtocolCompliance = new ComplianceTestResult
            {
                Violations = new List<ComplianceViolation>
                {
                    new()
                    {
                        CheckId = "MCP.INIT.VERSION_NEGOTIATION",
                        Category = "Initialization",
                        Severity = ViolationSeverity.High,
                        Description = "Server ignored the requested MCP protocol version.",
                        Rule = "initialize.result.protocolVersion must reflect negotiation.",
                        Recommendation = "Return the negotiated protocol version.",
                        SpecReference = "https://modelcontextprotocol.io/specification/2025-11-25/basic/lifecycle"
                    }
                }
            },
            SecurityTesting = new SecurityTestResult
            {
                Vulnerabilities = new List<SecurityVulnerability>
                {
                    new()
                    {
                        Id = "MCP.SECURITY.PROMPT_INJECTION",
                        Name = "Prompt injection reflection",
                        Description = "Server reflected untrusted prompt content without sanitization.",
                        Category = "PromptInjection",
                        AffectedComponent = "prompt:get",
                        Severity = VulnerabilitySeverity.Critical,
                        Remediation = "Sanitize and label untrusted content before returning it.",
                        IsExploitable = true,
                        CvssScore = 9.1
                    }
                }
            }
        };

        var sarif = _renderer.GenerateSarifReport(result);

        using var document = JsonDocument.Parse(sarif);
        var root = document.RootElement;
        root.GetProperty("version").GetString().Should().Be("2.1.0");
        root.GetProperty("$schema").GetString().Should().Be("https://json.schemastore.org/sarif-2.1.0.json");

        var run = root.GetProperty("runs")[0];
        run.GetProperty("automationDetails").GetProperty("id").GetString().Should().Be("validation-123");
        run.GetProperty("invocations")[0].GetProperty("properties").GetProperty("specProfile").GetString().Should().Be("2025-11-25");

        var results = run.GetProperty("results");
        results.GetArrayLength().Should().Be(3);
        results.EnumerateArray().Select(result => result.GetProperty("ruleId").GetString()).Should().Contain(new[]
        {
            ValidationFindingRuleIds.ToolGuidelineOpenWorldHintMissing,
            "MCP.INIT.VERSION_NEGOTIATION",
            "MCP.SECURITY.PROMPT_INJECTION"
        });

        results.EnumerateArray()
            .Single(resultElement => resultElement.GetProperty("ruleId").GetString() == "MCP.INIT.VERSION_NEGOTIATION")
            .GetProperty("level").GetString().Should().Be("error");

        results.EnumerateArray()
            .Single(resultElement => resultElement.GetProperty("ruleId").GetString() == ValidationFindingRuleIds.ToolGuidelineOpenWorldHintMissing)
            .GetProperty("locations")[0]
            .GetProperty("logicalLocations")[0]
            .GetProperty("name").GetString().Should().Be("fetch_document");

        var rules = run.GetProperty("tool").GetProperty("driver").GetProperty("rules");
        rules.EnumerateArray()
            .Single(rule => rule.GetProperty("id").GetString() == "MCP.INIT.VERSION_NEGOTIATION")
            .GetProperty("helpUri").GetString().Should().Be("https://modelcontextprotocol.io/specification/2025-11-25/basic/lifecycle");

        rules.EnumerateArray()
            .Single(rule => rule.GetProperty("id").GetString() == ValidationFindingRuleIds.ToolGuidelineOpenWorldHintMissing)
            .GetProperty("properties").GetProperty("source").GetString().Should().Be("guideline");

        results.EnumerateArray()
            .Single(resultElement => resultElement.GetProperty("ruleId").GetString() == "MCP.SECURITY.PROMPT_INJECTION")
            .GetProperty("properties").GetProperty("authority").GetString().Should().Be("heuristic");
    }

    [Fact]
    public void GenerateSarifReport_ShouldDeduplicateRepeatedEntries()
    {
        var duplicateFinding = new ValidationFinding
        {
            RuleId = ValidationFindingRuleIds.ToolGuidelineDisplayTitleMissing,
            Category = "McpGuideline",
            Component = "search_docs",
            Severity = ValidationFindingSeverity.Low,
            Summary = "Tool 'search_docs' does not declare a display title."
        };

        var result = new ValidationResult
        {
            ValidationId = "validation-dup",
            ServerConfig = new McpServerConfig { Endpoint = "https://example.test/mcp", Transport = "http" },
            ToolValidation = new ToolTestResult
            {
                ToolResults = new List<IndividualToolResult>
                {
                    new()
                    {
                        ToolName = "search_docs",
                        Findings = new List<ValidationFinding>
                        {
                            duplicateFinding,
                            new()
                            {
                                RuleId = duplicateFinding.RuleId,
                                Category = duplicateFinding.Category,
                                Component = duplicateFinding.Component,
                                Severity = duplicateFinding.Severity,
                                Summary = duplicateFinding.Summary
                            }
                        }
                    }
                }
            }
        };

        var sarif = _renderer.GenerateSarifReport(result);

        using var document = JsonDocument.Parse(sarif);
        var results = document.RootElement.GetProperty("runs")[0].GetProperty("results");
        results.GetArrayLength().Should().Be(1);
    }

    [Fact]
    public void GenerateJunitReport_ShouldIncludeOverallPolicyAndCategorySuites()
    {
        var result = ReportSnapshotTestData.CreateComprehensiveResult();

        var junit = _renderer.GenerateJunitReport(result);

        var document = XDocument.Parse(junit);
        var root = document.Root;
        root.Should().NotBeNull();
        root!.Name.LocalName.Should().Be("testsuites");
        root.Attribute("tests")!.Value.Should().Be("9");

        var policySuite = root.Elements("testsuite").Single(suite => suite.Attribute("name")!.Value == "host-policy");
        policySuite.Attribute("failures")!.Value.Should().Be("1");

        var policyCase = policySuite.Elements("testcase").Single();
        policyCase.Attribute("name")!.Value.Should().Be("Policy Gate (strict)");
        policyCase.Element("failure")!.Value.Should().Contain("Strict policy blocked the validation result");

        var promptSuite = root.Elements("testsuite").Single(suite => suite.Attribute("name")!.Value == "prompt-testing");
        promptSuite.Attribute("skipped")!.Value.Should().Be("1");

        var securitySuite = root.Elements("testsuite").Single(suite => suite.Attribute("name")!.Value == "security-testing");
        securitySuite.Elements("testcase").Single().Element("failure")!.Value.Should().Contain("MCP.SECURITY.PROMPT_INJECTION");
    }

    [Fact]
    public void GenerateHtmlReport_WithBootstrapHealth_ShouldIncludeConnectivitySection()
    {
        var result = ReportSnapshotTestData.CreateComprehensiveResult();

        var html = _renderer.GenerateHtmlReport(result, result.ValidationConfig.Reporting, verbose: true);

        html.Should().Contain("Connectivity & Session Bootstrap");
        html.Should().Contain("Protected endpoint");
        html.Should().Contain("Deferred Validation");
    }

    [Fact]
    public void GenerateHtmlReport_WithClientCompatibility_ShouldIncludeCompatibilitySection()
    {
        var result = ReportSnapshotTestData.CreateComprehensiveResult();
        result.ClientCompatibility = new ClientCompatibilityReport
        {
            RequestedProfiles = new List<string> { "github-copilot-cloud-agent" },
            Assessments = new List<ClientProfileAssessment>
            {
                new()
                {
                    ProfileId = "github-copilot-cloud-agent",
                    DisplayName = "GitHub Copilot Cloud Agent",
                    Revision = "2026-04",
                    Status = ClientProfileCompatibilityStatus.CompatibleWithWarnings,
                    Summary = "Required compatibility checks passed, with 1 advisory gap.",
                    PassedRequirements = 2,
                    WarningRequirements = 1,
                    Requirements = new List<ClientProfileRequirementAssessment>
                    {
                        new()
                        {
                            RequirementId = "surface-tools-only-surface",
                            Title = "Only the tool surface is currently consumed",
                            Outcome = ClientProfileRequirementOutcome.Warning,
                            Level = ClientProfileRequirementLevel.Recommended,
                            Summary = "This profile currently consumes tools only; 1 prompt(s) will not contribute to compatibility.",
                            ExampleComponents = new List<string> { "triage_issue" }
                        }
                    }
                }
            }
        };

        var html = _renderer.GenerateHtmlReport(result, result.ValidationConfig.Reporting, verbose: true);

        html.Should().Contain("Client Compatibility");
        html.Should().Contain("GitHub Copilot Cloud Agent");
        html.Should().Contain("tools only");
    }

    [Fact]
    public void GenerateHtmlReport_WithTimedOutPerformanceAndNoMeasurements_ShouldShowUnavailableReason()
    {
        var result = ReportSnapshotTestData.CreateComprehensiveResult();
        result.PerformanceTesting = new PerformanceTestResult
        {
            Status = TestStatus.Failed,
            Duration = TimeSpan.FromMinutes(2),
            CriticalErrors = new List<string> { "Operation timed out or was cancelled" }
        };

        var html = _renderer.GenerateHtmlReport(result, result.ValidationConfig.Reporting, verbose: true);

        html.Should().Contain("Measurements:</strong> Unavailable");
        html.Should().Contain("Operation timed out or was cancelled");
        html.Should().NotContain("<div class=\"metric-value\">0.00 ms</div><div class=\"metric-label\">Average Latency</div>");
    }

    [Fact]
    public void GenerateHtmlReport_WithObservedProbePressureSignals_ShouldShowCalibrationTelemetry()
    {
        var result = ReportSnapshotTestData.CreateComprehensiveResult();
        result.PerformanceTesting = new PerformanceTestResult
        {
            Status = TestStatus.Passed,
            Score = 91,
            LoadTesting = new LoadTestResult
            {
                TotalRequests = 20,
                SuccessfulRequests = 18,
                FailedRequests = 2,
                AverageResponseTimeMs = 125,
                P95ResponseTimeMs = 240,
                RequestsPerSecond = 15,
                ProbeRoundsExecuted = 2,
                ObservedRateLimitedRequests = 3,
                ObservedTransientFailures = 1
            }
        };

        var html = _renderer.GenerateHtmlReport(result, result.ValidationConfig.Reporting, verbose: true);

        html.Should().Contain("Probe rounds executed: 2");
        html.Should().Contain("Rate-limited requests observed across calibration: 3");
        html.Should().Contain("Retryable transient failures observed across calibration: 1");
    }

    [Fact]
    public void GenerateXmlReport_WithClientCompatibility_ShouldIncludeProfileAssessments()
    {
        var result = ReportSnapshotTestData.CreateComprehensiveResult();
        result.ClientCompatibility = new ClientCompatibilityReport
        {
            RequestedProfiles = new List<string> { "claude-code" },
            Assessments = new List<ClientProfileAssessment>
            {
                new()
                {
                    ProfileId = "claude-code",
                    DisplayName = "Claude Code",
                    Revision = "2026-04",
                    Status = ClientProfileCompatibilityStatus.Incompatible,
                    Summary = "1 required compatibility check failed.",
                    FailedRequirements = 1,
                    Requirements = new List<ClientProfileRequirementAssessment>
                    {
                        new()
                        {
                            RequirementId = "prompt-prompts-contract",
                            Title = "Prompt get/list contract is structurally valid",
                            Outcome = ClientProfileRequirementOutcome.Failed,
                            Level = ClientProfileRequirementLevel.Required,
                            Summary = "Blocking prompt contract gaps affect 1/1 prompt(s).",
                            RuleIds = new List<string> { ValidationFindingRuleIds.PromptGetMissingMessagesArray },
                            ExampleComponents = new List<string> { "triage_issue" }
                        }
                    }
                }
            }
        };

        var xml = _renderer.GenerateXmlReport(result, verbose: true);

        var document = XDocument.Parse(xml);
        var compatibility = document.Root!.Element("ClientCompatibility");
        compatibility.Should().NotBeNull();
        compatibility!.Elements("Profile").Should().ContainSingle();
        compatibility.Elements("Profile").Single().Attribute("id")!.Value.Should().Be("claude-code");
        compatibility.Descendants("Requirement").Single().Attribute("outcome")!.Value.Should().Be(ClientProfileRequirementOutcome.Failed.ToString());
    }

    [Fact]
    public void GenerateXmlReport_WithTimedOutPerformanceAndNoMeasurements_ShouldMarkMetricsUnavailable()
    {
        var result = ReportSnapshotTestData.CreateComprehensiveResult();
        result.PerformanceTesting = new PerformanceTestResult
        {
            Status = TestStatus.Failed,
            Duration = TimeSpan.FromMinutes(2),
            CriticalErrors = new List<string> { "Operation timed out or was cancelled" }
        };

        var xml = _renderer.GenerateXmlReport(result, verbose: true);

        var document = XDocument.Parse(xml);
        var performance = document.Root!
            .Element("TestCategories")!
            .Element("PerformanceTesting");

        performance.Should().NotBeNull();
        performance!.Element("MeasurementStatus")!.Value.Should().Be("Unavailable");
        performance.Element("Reason")!.Value.Should().Be("Operation timed out or was cancelled");
        performance.Element("LoadTesting").Should().BeNull();
    }
}