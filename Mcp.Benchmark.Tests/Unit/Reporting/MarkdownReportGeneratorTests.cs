using System.Globalization;
using Mcp.Benchmark.Core.Models;
using Mcp.Benchmark.Core.Services;
using Mcp.Benchmark.Tests.Fixtures;
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

        report.Should().Contain("Benchmark Trust Level");
        report.Should().Contain("L4");
        report.Should().Contain("Benchmark Trust Profile");
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
    public void GenerateReport_WithZeroCatalogs_ShouldUseApplicabilityLanguage()
    {
        var result = BuildMinimalResult();
        result.ToolValidation = new ToolTestResult
        {
            Status = TestStatus.Skipped,
            Score = 100,
            Message = "Server does not advertise the tools capability.",
            Issues = new List<string> { "Tools capability was not advertised during initialize; tools/list and tools/call probes were skipped." }
        };
        result.ResourceTesting = new ResourceTestResult
        {
            Status = TestStatus.Skipped,
            Score = 100,
            Message = "Server does not advertise the resources capability.",
            Issues = new List<string> { "Resources capability was not advertised during initialize; resources/list and resources/read probes were skipped." }
        };
        result.PromptTesting = new PromptTestResult
        {
            Status = TestStatus.Skipped,
            Score = 100,
            Message = "Server does not advertise the prompts capability.",
            Issues = new List<string> { "Prompts capability was not advertised during initialize; prompts/list and prompts/get probes were skipped." }
        };

        var report = _generator.GenerateReport(result);

        report.Should().Contain("Tools capability was not advertised during initialize; tools/list and tools/call probes were skipped; no tool executions were required.");
        report.Should().Contain("Resources capability was not advertised during initialize; resources/list and resources/read probes were skipped; no resource reads were required.");
        report.Should().Contain("Prompts capability was not advertised during initialize; prompts/list and prompts/get probes were skipped; no prompt executions were required.");
        report.Should().NotContain("0 tools discovered and validated");
        report.Should().NotContain("0 resources discovered and validated");
        report.Should().NotContain("0 prompts discovered and validated");
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
    public void GenerateReport_WithMixedAuthorityFindings_ShouldShowNormativeOrderAndLegend()
    {
        var result = BuildMinimalResult();
        result.ProtocolCompliance = new ComplianceTestResult
        {
            Violations = new List<ComplianceViolation>
            {
                new()
                {
                    CheckId = "MCP.SPEC.BLOCKER",
                    Category = "Protocol",
                    Description = "Spec violation.",
                    Severity = ViolationSeverity.High,
                    Recommendation = "Fix the spec violation."
                }
            }
        };
        result.ToolValidation = new ToolTestResult
        {
            Findings = new List<ValidationFinding>
            {
                new()
                {
                    RuleId = "MCP.GUIDELINE.HINT",
                    Category = "Guidance",
                    Component = "tool",
                    Source = ValidationRuleSource.Guideline,
                    Severity = ValidationFindingSeverity.High,
                    Summary = "Guideline finding."
                }
            }
        };
        result.SecurityTesting = new SecurityTestResult
        {
            Vulnerabilities = new List<SecurityVulnerability>
            {
                new()
                {
                    Id = "MCP.HEURISTIC.WARNING",
                    Category = "Security",
                    AffectedComponent = "tool",
                    Severity = VulnerabilitySeverity.Critical,
                    Description = "Heuristic warning."
                }
            }
        };
        result.VerdictAssessment = ValidationVerdictEngine.Calculate(result);

        var report = _generator.GenerateReport(result);

        var specIndex = report.IndexOf("- [Spec]", StringComparison.Ordinal);
        var guidelineIndex = report.IndexOf("- [Guideline]", StringComparison.Ordinal);
        var heuristicIndex = report.IndexOf("- [Heuristic]", StringComparison.Ordinal);

        specIndex.Should().BeGreaterThanOrEqualTo(0);
        guidelineIndex.Should().BeGreaterThan(specIndex);
        heuristicIndex.Should().BeGreaterThan(guidelineIndex);
        report.Should().Contain(ValidationAuthorityHierarchy.Legend);
    }

    [Fact]
    public void GenerateReport_WithHighSeverityViolationContext_ShouldRenderContextDetails()
    {
        var result = BuildMinimalResult();
        result.ProtocolCompliance = new ComplianceTestResult
        {
            Violations = new List<ComplianceViolation>
            {
                new()
                {
                    CheckId = "MCP.INIT.VERSION_NEGOTIATION",
                    Category = "Initialization",
                    Description = "Server ignored the requested MCP protocol version.",
                    Severity = ViolationSeverity.High,
                    Recommendation = "Return the negotiated protocol version.",
                    Context = new Dictionary<string, object>
                    {
                        ["requestedProtocolVersion"] = "2025-11-25",
                        ["serverProtocolVersion"] = "2025-03-26",
                        ["expected"] = "initialize.result.protocolVersion matches negotiated version",
                        ["actual"] = "2025-03-26"
                    }
                },
                new()
                {
                    CheckId = "MCP.LOW.CONTEXT",
                    Category = "Initialization",
                    Description = "Low severity detail.",
                    Severity = ViolationSeverity.Low,
                    Context = new Dictionary<string, object>
                    {
                        ["probeId"] = "low-severity-probe"
                    }
                }
            }
        };

        var report = _generator.GenerateReport(result);

        report.Should().Contain("#### Violation Context");
        report.Should().Contain("| `MCP.INIT.VERSION_NEGOTIATION` | **Requested protocol:** `2025-11-25`; **Server protocol:** `2025-03-26`; **Expected:** `initialize.result.protocolVersion matches negotiated version`; **Actual:** `2025-03-26` |");
        report.Should().NotContain("low-severity-probe");
    }

    [Fact]
    public void GenerateReport_WithMixedRemediationSignals_ShouldShowDependencyOrderedRemediationOrder()
    {
        var result = BuildMinimalResult();
        result.ProtocolCompliance = new ComplianceTestResult
        {
            Violations = new List<ComplianceViolation>
            {
                new()
                {
                    CheckId = "MCP.INIT.VERSION_NEGOTIATION",
                    Category = "Initialization",
                    Description = "Server ignored the requested MCP protocol version.",
                    Severity = ViolationSeverity.High,
                    Recommendation = "Return the negotiated protocol version."
                }
            }
        };
        result.SecurityTesting = new SecurityTestResult
        {
            Vulnerabilities = new List<SecurityVulnerability>
            {
                new()
                {
                    Id = "MCP.SECURITY.PROMPT_INJECTION",
                    Category = "PromptInjection",
                    AffectedComponent = "prompt:get",
                    Description = "Prompt reflected untrusted instructions.",
                    Severity = VulnerabilitySeverity.High,
                    Remediation = "Sanitize and label untrusted prompt content."
                }
            }
        };
        result.VerdictAssessment = new VerdictAssessment
        {
            BlockingDecisions =
            {
                new()
                {
                    DecisionId = "auth-boundary",
                    Lane = EvaluationLane.Baseline,
                    Authority = ValidationRuleSource.Spec,
                    Origin = EvidenceOrigin.DeterministicObservation,
                    Gate = GateOutcome.ReviewRequired,
                    Severity = ValidationFindingSeverity.High,
                    Category = "Authentication",
                    Component = "oauth",
                    Summary = "Bearer token audience was not enforced.",
                    ImpactAreas = [ImpactArea.AuthenticationBoundary],
                    Metadata = new Dictionary<string, string>
                    {
                        ["recommendation"] = "Reject bearer tokens with the wrong resource audience."
                    }
                },
                new()
                {
                    DecisionId = "capability-contract",
                    Lane = EvaluationLane.Baseline,
                    Authority = ValidationRuleSource.Spec,
                    Origin = EvidenceOrigin.DeterministicObservation,
                    Gate = GateOutcome.ReviewRequired,
                    Severity = ValidationFindingSeverity.High,
                    Category = "CapabilityCoverage",
                    Component = "tools/call",
                    Summary = "tools/call was exercised even though tools were not advertised.",
                    ImpactAreas = [ImpactArea.CapabilityContract],
                    Metadata = new Dictionary<string, string>
                    {
                        ["recommendation"] = "Align initialize capabilities with implemented tool support."
                    }
                }
            }
        };

        var report = _generator.GenerateReport(result);

        report.Should().Contain("Recommended Remediation Order");
        var bootstrapIndex = report.IndexOf("Priority 1: Bootstrap & Protocol Version", StringComparison.Ordinal);
        var authIndex = report.IndexOf("Priority 2: Authentication Boundary", StringComparison.Ordinal);
        var capabilityIndex = report.IndexOf("Priority 3: Advertised Capabilities", StringComparison.Ordinal);
        var safetyIndex = report.IndexOf("Priority 4: AI Safety, Security, And Performance", StringComparison.Ordinal);

        bootstrapIndex.Should().BeGreaterThanOrEqualTo(0);
        authIndex.Should().BeGreaterThan(bootstrapIndex);
        capabilityIndex.Should().BeGreaterThan(authIndex);
        safetyIndex.Should().BeGreaterThan(capabilityIndex);
        report.Should().Contain("Return the negotiated protocol version.");
        report.Should().Contain("Reject bearer tokens with the wrong resource audience.");
        report.Should().Contain("Align initialize capabilities with implemented tool support.");
        report.Should().Contain("Sanitize and label untrusted prompt content.");
        report.Should().Contain("Impact after fix");
    }

    [Fact]
    public void GenerateReport_WithRepeatedAiReadinessFindings_ShouldShowCoverageInsteadOfRawRows()
    {
        var originalCulture = CultureInfo.CurrentCulture;
        var originalUiCulture = CultureInfo.CurrentUICulture;
        var testCulture = CultureInfo.GetCultureInfo("fr-FR");

        CultureInfo.CurrentCulture = testCulture;
        CultureInfo.CurrentUICulture = testCulture;

        try
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
            report.Should().Contain("**Evidence basis:** Deterministic schema and payload heuristics.");
            report.Should().Contain("| Rule ID | Evidence Basis | Source | Coverage | Severity | Finding |");
            report.Should().Contain("Deterministic schema heuristic");
            report.Should().Contain("2/5 (40%)");
            report.Should().NotContain("2/5 (40 %)");
            report.Should().Contain(ValidationFindingRuleIds.AiReadinessMissingParameterDescriptions);
        }
        finally
        {
            CultureInfo.CurrentCulture = originalCulture;
            CultureInfo.CurrentUICulture = originalUiCulture;
        }
    }

    [Fact]
    public void GenerateReport_WithToolCatalogAdvisories_ShouldSeparateAuthorityBreakdown()
    {
        var result = ReportSnapshotTestData.CreateComprehensiveResult();

        var report = _generator.GenerateReport(result);

        report.Should().Contain("Tool Catalog Advisory Breakdown");
        report.Should().Contain("| Spec | 0 | 0/2 (0%) | - | No current catalog-wide tool advisories. |");
        report.Should().Contain("| Guideline | 1 | 1/2 (50%) | 🟠 High |");
        report.Should().Contain("| Heuristic | 1 | 1/2 (50%) | 🟡 Medium |");
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
        report.Should().Contain("| Performance | ❌ Failed | Unavailable | - |");
        report.Should().NotContain("0.00ms | 🚀 Excellent");
    }

    [Fact]
    public void GenerateReport_WithRateLimitedFailures_ShouldExplainTheyDoNotDriveFailurePenalty()
    {
        var result = BuildMinimalResult();
        result.ValidationConfig.Reporting.IncludeDetailedLogs = true;
        result.PerformanceTesting = new PerformanceTestResult
        {
            Status = TestStatus.Failed,
            Score = 95,
            LoadTesting = new LoadTestResult
            {
                TotalRequests = 10,
                SuccessfulRequests = 7,
                FailedRequests = 3,
                RateLimitedRequests = 3,
                AverageResponseTimeMs = 300,
                P95ResponseTimeMs = 420,
                RequestsPerSecond = 12
            }
        };

        var report = _generator.GenerateReport(result);

        report.Should().Contain("Rate limiting | surfaced separately | 3 rate-limited requests | −0 points");
        report.Should().NotContain("3 failed | −15 points");
    }

    [Fact]
    public void GenerateReport_WithObservedProbePressureSignals_ShouldShowCalibrationTelemetry()
    {
        var result = BuildMinimalResult();
        result.ValidationConfig.Reporting.IncludeDetailedLogs = true;
        result.PerformanceTesting = new PerformanceTestResult
        {
            Status = TestStatus.Passed,
            Score = 92,
            LoadTesting = new LoadTestResult
            {
                TotalRequests = 20,
                SuccessfulRequests = 18,
                FailedRequests = 2,
                AverageResponseTimeMs = 180,
                P95ResponseTimeMs = 350,
                RequestsPerSecond = 14,
                ProbeRoundsExecuted = 3,
                ObservedRateLimitedRequests = 4,
                ObservedTransientFailures = 2
            }
        };

        var report = _generator.GenerateReport(result);

        report.Should().Contain("**Probe Rounds** | 3 | ℹ️ Calibrated");
        report.Should().Contain("**Observed Rate Limits** | 4 request(s) | ⚠️ Throttling observed");
        report.Should().Contain("**Observed Transient Failures** | 2 request(s) | ⚠️ Retry pressure observed");
    }

    [Fact]
    public void GenerateReport_WithPerformanceCalibrationOverride_ShouldShowAuditEvidence()
    {
        var result = BuildMinimalResult();
        result.ValidationConfig.Reporting.IncludeDetailedLogs = true;
        result.PerformanceTesting = new PerformanceTestResult
        {
            Status = TestStatus.Skipped,
            Score = 70,
            LoadTesting = new LoadTestResult
            {
                TotalRequests = 20,
                SuccessfulRequests = 18,
                FailedRequests = 2,
                AverageResponseTimeMs = 250
            },
            CalibrationOverrides =
            [
                new()
                {
                    RuleId = ValidationFindingRuleIds.PerformancePublicRemoteAdvisory,
                    Reason = "Synthetic load probe hit remote capacity limits.",
                    AffectedTests = ["performance/load-testing"],
                    Inputs = new Dictionary<string, string>
                    {
                        ["serverProfile"] = "Public",
                        ["successRatio"] = "0.900"
                    },
                    BeforeStatus = TestStatus.Failed,
                    AfterStatus = TestStatus.Skipped,
                    BeforeScore = 55,
                    AfterScore = 70,
                    BeforeSeverity = ValidationFindingSeverity.Medium,
                    AfterSeverity = ValidationFindingSeverity.Info
                }
            ]
        };

        var report = _generator.GenerateReport(result);

        report.Should().Contain("### Calibration Override Audit");
        report.Should().Contain("`MCP.GUIDELINE.PERFORMANCE.PUBLIC_REMOTE_ADVISORY` | performance/load-testing | `Failed` -> `Skipped` | 55.0 -> 70.0 | `Medium` -> `Info` | Preserved | Synthetic load probe hit remote capacity limits.");
        report.Should().Contain("serverProfile=Public; successRatio=0.900");
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
        report.Should().Contain("[Spec] 1 protocol violation(s), led by MCP.TEST.FAILURE: Protocol contract failed.");
        report.Should().Contain("[Operational] 1 critical execution error(s), led by: Top-level transport issue detected.");
        report.Should().NotContain("## 6. Security Assessment");
        report.Should().NotContain("## 7. Tool Validation");
    }

    [Fact]
    public void GenerateReport_ShouldSeparateSpecSkipAndHeuristicPriorityFindings()
    {
        var result = BuildMinimalResult();
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
        result.ToolValidation = new ToolTestResult
        {
            Status = TestStatus.Passed,
            Score = 100,
            AiReadinessFindings = new List<ValidationFinding>
            {
                new()
                {
                    RuleId = ValidationFindingRuleIds.AiReadinessVagueStringSchema,
                    Category = "AiReadiness",
                    Component = "search_docs",
                    Severity = ValidationFindingSeverity.Medium,
                    Summary = "Tool 'search_docs' exposes a vague freeform parameter."
                }
            }
        };
        result.Evidence.Coverage.Add(new ValidationCoverageDeclaration
        {
            LayerId = "protocol-core",
            Scope = "batch-processing",
            Status = ValidationCoverageStatus.Skipped,
            Reason = "Batch envelopes are not advertised for this schema profile."
        });

        var report = _generator.GenerateReport(result);

        report.Should().Contain("[Spec] 1 protocol violation(s), led by MCP.TEST.FAILURE: Protocol contract failed.");
        report.Should().Contain("[Guideline/Skip] 1 scope(s) skipped by validator design, led by batch-processing: Batch envelopes are not advertised for this schema profile.");
        report.Should().Contain("[Heuristic] 1 deterministic AI-readiness advisory signal(s), led by AI.TOOL.SCHEMA.STRING_CONSTRAINT_MISSING: Tool 'search_docs' exposes a vague freeform parameter.");
    }

    [Fact]
    public void GenerateReport_WithMixedEvidenceConfidence_ShouldShowLayerConfidenceSummary()
    {
        var result = BuildMinimalResult();
        result.Evidence.Coverage.Add(new ValidationCoverageDeclaration
        {
            LayerId = "tool-surface",
            Scope = "tools/list",
            Status = ValidationCoverageStatus.Covered,
            Confidence = EvidenceConfidenceLevel.Low,
            Reason = "Only partial parser-boundary evidence was available."
        });
        result.Evidence.Coverage.Add(new ValidationCoverageDeclaration
        {
            LayerId = "security-boundaries",
            Scope = "attack-simulation",
            Status = ValidationCoverageStatus.AuthRequired,
            Blocker = ValidationEvidenceBlocker.AuthRequired,
            Confidence = EvidenceConfidenceLevel.Low,
            Reason = "Protected surface required credentials."
        });

        var report = _generator.GenerateReport(result);

        report.Should().Contain("### Evidence Confidence By Layer");
        report.Should().Contain("| `tool-surface` | 100.0% | Low (35.0%) | 1 | 0 | 0 | 0 | 0 |");
        report.Should().Contain("| `security-boundaries` | 0.0% | Low (35.0%) | 0 | 1 | 0 | 0 | 0 |");
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
                    Summary = "Required compatibility checks passed; 1 advisory requirement still needs follow-up.",
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

    [Fact]
    public void GenerateReport_WithWarningsOnlyClientCompatibility_ShouldKeepExecutiveSummaryFocusedOnBlockers()
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
                    Summary = "Required compatibility checks passed; 1 advisory requirement still needs follow-up.",
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
                            Summary = "Advisory tool guidance gaps affect 1/1 tool(s)."
                        }
                    }
                }
            }
        };

        var report = _generator.GenerateReport(result);

        report.Should().NotContain("- Client profile Claude Code:");
        report.Should().NotContain("Client compatibility: Claude Code");
        report.Should().Contain("Client Profile Compatibility");
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
