using FluentAssertions;
using Mcp.Benchmark.CLI.Services;
using Mcp.Benchmark.Core.Models;

namespace Mcp.Benchmark.Tests.Unit.Services;

public class GitHubActionsReporterTests
{
    private readonly GitHubActionsReporter _reporter = new();

    [Fact]
    public void BuildValidationSummary_ShouldIncludePolicyTrustAndArtifacts()
    {
        var result = CreateValidationResult();

        var summary = _reporter.BuildValidationSummary(result, new[]
        {
            "./reports/mcp-validation-20260420-100000-report.md",
            "./reports/mcp-validation-20260420-100000-results.sarif.json"
        });

        summary.Should().Contain("## MCP Validator");
        summary.Should().Contain("strict blocked");
        summary.Should().Contain("L3: Acceptable");
        summary.Should().Contain("Blocking Reasons");
        summary.Should().Contain("Top Findings");
        summary.Should().Contain("Client Profiles");
        summary.Should().Contain("Claude Code");
        summary.Should().Contain("./reports/mcp-validation-20260420-100000-results.sarif.json");
    }

    [Fact]
    public void BuildAnnotations_ShouldPrioritizePolicyFailureAndDeduplicateFindings()
    {
        var result = CreateValidationResult();

        var annotations = _reporter.BuildAnnotations(result);

        annotations.Should().NotBeEmpty();
        annotations[0].Level.Should().Be("error");
        annotations[0].Title.Should().Be("MCP Validator Policy");
        annotations.Should().Contain(annotation => annotation.Title.Contains("MCP.GUIDELINE.TOOL.DESTRUCTIVE_HINT_MISSING", StringComparison.Ordinal));
        annotations.Count(annotation => annotation.Title.Contains("MCP.GUIDELINE.TOOL.DESTRUCTIVE_HINT_MISSING", StringComparison.Ordinal)).Should().Be(1);
    }

    private static ValidationResult CreateValidationResult()
    {
        return new ValidationResult
        {
            ValidationId = "validation-456",
            OverallStatus = ValidationStatus.Failed,
            ComplianceScore = 72.4,
            ServerConfig = new McpServerConfig
            {
                Endpoint = "https://example.test/mcp",
                Transport = "http"
            },
            ToolValidation = new ToolTestResult
            {
                ToolResults = new List<IndividualToolResult>
                {
                    new()
                    {
                        ToolName = "delete_repo",
                        Findings = new List<ValidationFinding>
                        {
                            new()
                            {
                                RuleId = ValidationFindingRuleIds.ToolGuidelineDestructiveHintMissing,
                                Category = "McpGuideline",
                                Component = "delete_repo",
                                Severity = ValidationFindingSeverity.High,
                                Summary = "Tool 'delete_repo' does not declare annotations.destructiveHint."
                            },
                            new()
                            {
                                RuleId = ValidationFindingRuleIds.ToolGuidelineDestructiveHintMissing,
                                Category = "McpGuideline",
                                Component = "delete_repo",
                                Severity = ValidationFindingSeverity.High,
                                Summary = "Tool 'delete_repo' does not declare annotations.destructiveHint."
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
                        Description = "Server ignored the requested MCP protocol version."
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
                        AffectedComponent = "prompt:get",
                        Severity = VulnerabilitySeverity.Critical,
                        Description = "Server reflected untrusted prompt content without sanitization."
                    }
                }
            },
            TrustAssessment = new McpTrustAssessment
            {
                TrustLevel = McpTrustLevel.L3_Acceptable
            },
            PolicyOutcome = new ValidationPolicyOutcome
            {
                Mode = ValidationPolicyModes.Strict,
                Passed = false,
                Summary = "Strict policy blocked the validation result with 2 unsuppressed signal(s).",
                Reasons = new List<string>
                {
                    "Trust level L3_Acceptable is below the strict minimum of L4_Trusted.",
                    "Tool 'delete_repo' does not declare annotations.destructiveHint."
                }
            },
            ClientCompatibility = new ClientCompatibilityReport
            {
                RequestedProfiles = new List<string> { "claude-code" },
                Assessments = new List<ClientProfileAssessment>
                {
                    new()
                    {
                        ProfileId = "claude-code",
                        DisplayName = "Claude Code",
                        Status = ClientProfileCompatibilityStatus.CompatibleWithWarnings,
                        Summary = "Required compatibility checks passed; 1 advisory requirement still needs follow-up."
                    }
                }
            }
        };
    }
}