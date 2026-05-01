using System.Text.Json;
using System.Text.RegularExpressions;
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

        results.EnumerateArray()
            .Single(resultElement => resultElement.GetProperty("ruleId").GetString() == "MCP.INIT.VERSION_NEGOTIATION")
            .GetProperty("properties").GetProperty("authorityPriority").GetInt32().Should().Be(0);

        results.EnumerateArray()
            .Single(resultElement => resultElement.GetProperty("ruleId").GetString() == ValidationFindingRuleIds.ToolGuidelineOpenWorldHintMissing)
            .GetProperty("properties").GetProperty("authorityPriority").GetInt32().Should().Be(1);

        results.EnumerateArray()
            .Single(resultElement => resultElement.GetProperty("ruleId").GetString() == "MCP.SECURITY.PROMPT_INJECTION")
            .GetProperty("properties").GetProperty("authorityLegend").GetString().Should().Contain("Authority order");
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
        root.Attribute("tests")!.Value.Should().Be("17");

        var policySuite = root.Elements("testsuite").Single(suite => suite.Attribute("name")!.Value == "host-policy");
        policySuite.Attribute("failures")!.Value.Should().Be("1");

        var policyCase = policySuite.Elements("testcase").Single();
        policyCase.Attribute("name")!.Value.Should().Be("Policy Gate (strict)");
        policyCase.Element("failure")!.Value.Should().Contain("Strict policy blocked the validation result");

        var promptSuite = root.Elements("testsuite").Single(suite => suite.Attribute("name")!.Value == "prompt-testing");
        promptSuite.Attribute("skipped")!.Value.Should().Be("1");

        var coverageSuite = root.Elements("testsuite").Single(suite => suite.Attribute("name")!.Value == "evidence-coverage");
        coverageSuite.Attribute("tests")!.Value.Should().Be("8");
        coverageSuite.Attribute("skipped")!.Value.Should().Be("1");
        var promptCoverageCase = coverageSuite.Elements("testcase").Single(testCase => testCase.Attribute("name")!.Value == "Coverage: prompt-surface/prompts/list");
        promptCoverageCase.Element("skipped")!.Value.Should().Contain("EvidenceId: coverage:prompt-surface:prompts-list:Skipped");

        var securitySuite = root.Elements("testsuite").Single(suite => suite.Attribute("name")!.Value == "security-testing");
        securitySuite.Elements("testcase").Single().Element("failure")!.Value.Should().Contain("MCP.SECURITY.PROMPT_INJECTION");
    }

    [Fact]
    public void GenerateHtmlReport_WithBootstrapHealth_ShouldIncludeConnectivitySection()
    {
        var result = ReportSnapshotTestData.CreateComprehensiveResult();

        var html = _renderer.GenerateHtmlReport(result, result.ValidationConfig.Reporting, verbose: true);

        html.Should().Contain("Connectivity &amp; Session Bootstrap");
        html.Should().Contain("Protected endpoint");
        html.Should().Contain("Deferred Validation");
    }

    [Fact]
    public void GenerateHtmlReport_WithMixedEvidenceConfidence_ShouldRenderLayerConfidenceSummary()
    {
        var result = new ValidationResult
        {
            ValidationId = "validation-confidence",
            ServerConfig = new McpServerConfig { Endpoint = "https://example.test/mcp", Transport = "http" },
            ValidationConfig = new McpValidatorConfiguration
            {
                Reporting = new ReportingConfig { SpecProfile = "2025-11-25" }
            },
            OverallStatus = ValidationStatus.Passed,
            ComplianceScore = 90
        };
        result.Evidence.Coverage.Add(new ValidationCoverageDeclaration
        {
            LayerId = "tool-surface",
            Scope = "tools/list",
            Status = ValidationCoverageStatus.Covered,
            Confidence = EvidenceConfidenceLevel.Low,
            Reason = "Only partial parser-boundary evidence was available."
        });

        var html = _renderer.GenerateHtmlReport(result, result.ValidationConfig.Reporting, verbose: true);

        html.Should().Contain("<th>Coverage</th><th>Confidence</th>");
        html.Should().Contain("<code>tool-surface</code></td><td>100.0%</td><td>Low (35.0%)</td>");
    }

    [Fact]
    public void GenerateHtmlReport_ShouldRenderSectionCardsCollapsedByDefaultWithVisibleToggleAffordance()
    {
        var result = ReportSnapshotTestData.CreateComprehensiveResult();

        var html = _renderer.GenerateHtmlReport(result, result.ValidationConfig.Reporting, verbose: true);

        Regex.Matches(html, @"<details class=""section-card[^""]*"" open>").Count.Should().Be(0);
        html.Should().Contain("section-card__toggle-label section-card__toggle-label--collapsed\">Expand details</span>");
        html.Should().Contain("section-card__toggle-label section-card__toggle-label--expanded\">Collapse details</span>");
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
                    Summary = "Required compatibility checks passed; 1 advisory requirement still needs follow-up.",
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
    public void GenerateHtmlReport_WithMultipleCompatibilityWarnings_ShouldRenderAllAdvisoryRequirements()
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
                    Status = ClientProfileCompatibilityStatus.CompatibleWithWarnings,
                    Summary = "Required compatibility checks passed; 3 advisory requirements still need follow-up.",
                    PassedRequirements = 3,
                    WarningRequirements = 3,
                    Requirements = new List<ClientProfileRequirementAssessment>
                    {
                        new() { RequirementId = "tool-tool-metadata", Title = "Tool presentation and approval metadata is complete", Outcome = ClientProfileRequirementOutcome.Warning, Summary = "Advisory tool guidance gaps affect 1/1 tool(s)." },
                        new() { RequirementId = "tool-tool-schema", Title = "Tool schemas are clear enough for agent planning", Outcome = ClientProfileRequirementOutcome.Warning, Summary = "Advisory schema guidance gaps affect 1/1 tool(s)." },
                        new() { RequirementId = "prompt-prompt-metadata", Title = "Prompt guidance is explicit enough for callers", Outcome = ClientProfileRequirementOutcome.Warning, Summary = "Advisory prompt guidance gaps affect 1/1 prompt(s)." }
                    }
                }
            }
        };

        var html = _renderer.GenerateHtmlReport(result, result.ValidationConfig.Reporting, verbose: true);

        html.Should().Contain("3 advisory requirements still need follow-up.");
        html.Should().Contain("Tool presentation and approval metadata is complete");
        html.Should().Contain("Tool schemas are clear enough for agent planning");
        html.Should().Contain("Prompt guidance is explicit enough for callers");
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
    public void GenerateHtmlReport_WithToolCatalogAdvisories_ShouldShowAuthorityBreakdown()
    {
        var result = ReportSnapshotTestData.CreateComprehensiveResult();

        var html = _renderer.GenerateHtmlReport(result, result.ValidationConfig.Reporting, verbose: true);

        html.Should().Contain("Tool Catalog Advisory Breakdown");
        html.Should().Contain("Remaining tool-catalog debt grouped by specification, MCP guidance, and AI-oriented heuristics.");
    html.Should().Contain("authority-summary-grid");
    html.Should().Contain("authority-card__title\">Spec");
        html.Should().Contain("authority-card__metric-label\">Coverage");
    html.Should().Contain("authority-card__title\">Guideline");
    html.Should().Contain("authority-card__metric-value\">1/2 (50%)");
    html.Should().Contain("authority-card__title\">Heuristic");
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