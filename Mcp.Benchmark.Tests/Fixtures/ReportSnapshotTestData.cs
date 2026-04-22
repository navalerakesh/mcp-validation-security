using Mcp.Benchmark.Core.Models;
using ModelContextProtocol.Protocol;

namespace Mcp.Benchmark.Tests.Fixtures;

public static class ReportSnapshotTestData
{
    public static ValidationResult CreateComprehensiveResult()
    {
        return new ValidationResult
        {
            ValidationId = "validation-snapshot-001",
            StartTime = new DateTime(2026, 04, 20, 10, 00, 00, DateTimeKind.Utc),
            EndTime = new DateTime(2026, 04, 20, 10, 00, 12, DateTimeKind.Utc),
            OverallStatus = ValidationStatus.Failed,
            ComplianceScore = 81.4,
            ProtocolVersion = "2025-11-25",
            ServerConfig = new McpServerConfig
            {
                Endpoint = "https://snapshot.example.test/mcp",
                Transport = "http",
                ProtocolVersion = "2025-11-25"
            },
            ServerProfile = McpServerProfile.Authenticated,
            ServerProfileSource = ServerProfileSource.UserDeclared,
            ValidationConfig = new McpValidatorConfiguration
            {
                Reporting = new ReportingConfig
                {
                    DetailLevel = ReportDetailLevel.Full,
                    IncludeDetailedLogs = true,
                    SpecProfile = "2025-11-25",
                    IncludeSpecReferences = true
                }
            },
            Summary = new ValidationSummary
            {
                TotalTests = 7,
                PassedTests = 4,
                FailedTests = 2,
                SkippedTests = 1,
                CriticalIssues = 1,
                Warnings = 2,
                CoverageRatio = 0.875
            },
            InitializationHandshake = new TransportResult<InitializeResult>
            {
                IsSuccessful = false,
                Error = "401 Unauthorized — bootstrap encountered an authentication challenge.",
                Transport = new TransportMetadata
                {
                    StatusCode = 401,
                    Duration = TimeSpan.FromMilliseconds(84.2)
                }
            },
            BootstrapHealth = new HealthCheckResult
            {
                IsHealthy = false,
                Disposition = HealthCheckDisposition.Protected,
                ResponseTimeMs = 84.2,
                ServerVersion = "2.4.1",
                ProtocolVersion = "2025-11-25",
                ErrorMessage = "401 Unauthorized — bootstrap encountered an authentication challenge.",
                InitializationDetails = new TransportResult<InitializeResult>
                {
                    IsSuccessful = false,
                    Error = "401 Unauthorized — bootstrap encountered an authentication challenge.",
                    Transport = new TransportMetadata
                    {
                        StatusCode = 401,
                        Duration = TimeSpan.FromMilliseconds(84.2)
                    }
                }
            },
            CriticalErrors = new List<string>
            {
                "Prompt validation hit an unsafe reflection path."
            },
            Recommendations = new List<string>
            {
                "Add destructive hints to write-capable tools.",
                "Return the negotiated protocol version explicitly during initialize."
            },
            ProtocolCompliance = new ComplianceTestResult
            {
                Status = TestStatus.Failed,
                ComplianceScore = 84,
                Duration = TimeSpan.FromSeconds(2.1),
                Message = "Protocol negotiation had one blocking violation.",
                Findings = new List<ValidationFinding>
                {
                    new()
                    {
                        RuleId = ValidationFindingRuleIds.ToolCallMissingResultObject,
                        Category = "ProtocolCompliance",
                        Component = "tools/call",
                        Severity = ValidationFindingSeverity.Medium,
                        Summary = "One tool response omitted the outer result object."
                    }
                },
                Violations = new List<ComplianceViolation>
                {
                    new()
                    {
                        CheckId = "MCP.INIT.VERSION_NEGOTIATION",
                        Category = "Initialization",
                        Severity = ViolationSeverity.High,
                        Description = "Server ignored the requested MCP protocol version.",
                        Rule = "initialize.result.protocolVersion must reflect negotiation.",
                        Recommendation = "Echo the negotiated protocol version in initialize.result.",
                        SpecReference = "https://modelcontextprotocol.io/specification/2025-11-25/basic/lifecycle"
                    }
                }
            },
            ToolValidation = new ToolTestResult
            {
                Status = TestStatus.Passed,
                Duration = TimeSpan.FromSeconds(1.8),
                Score = 91,
                Message = "Tool catalog loaded and executed.",
                ToolsDiscovered = 2,
                ToolsTestPassed = 2,
                ToolsTestFailed = 0,
                ToolResults = new List<IndividualToolResult>
                {
                    new()
                    {
                        ToolName = "delete_repo",
                        DisplayTitle = "Delete Repository",
                        Status = TestStatus.Passed,
                        ExecutionTimeMs = 140,
                        Findings = new List<ValidationFinding>
                        {
                            new()
                            {
                                RuleId = ValidationFindingRuleIds.ToolGuidelineDestructiveHintMissing,
                                Category = "McpGuideline",
                                Component = "delete_repo",
                                Severity = ValidationFindingSeverity.High,
                                Summary = "Tool 'delete_repo' does not declare annotations.destructiveHint."
                            }
                        }
                    },
                    new()
                    {
                        ToolName = "list_repos",
                        DisplayTitle = "List Repositories",
                        Status = TestStatus.Passed,
                        ExecutionTimeMs = 80
                    }
                },
                AuthenticationSecurity = new AuthenticationSecurityResult
                {
                    AuthenticationRequired = true,
                    RejectsUnauthenticated = true,
                    HasProperAuthHeaders = true,
                    SecurityScore = 95
                },
                AiReadinessFindings = new List<ValidationFinding>
                {
                    new()
                    {
                        RuleId = ValidationFindingRuleIds.AiReadinessMissingParameterDescriptions,
                        Category = "AiReadiness",
                        Component = "delete_repo",
                        Severity = ValidationFindingSeverity.Medium,
                        Summary = "Tool 'delete_repo' has parameters without descriptions."
                    }
                }
            },
            ResourceTesting = new ResourceTestResult
            {
                Status = TestStatus.Passed,
                Duration = TimeSpan.FromSeconds(0.9),
                Message = "Resource access passed.",
                ResourcesDiscovered = 1,
                ResourcesAccessible = 1,
                ResourceResults = new List<IndividualResourceResult>
                {
                    new()
                    {
                        ResourceName = "repo://snapshot/README.md",
                        ResourceUri = "repo://snapshot/README.md",
                        MimeType = "text/markdown",
                        Status = TestStatus.Passed
                    }
                }
            },
            PromptTesting = new PromptTestResult
            {
                Status = TestStatus.Skipped,
                Duration = TimeSpan.FromSeconds(0.4),
                Message = "Server does not advertise prompts capability.",
                PromptsDiscovered = 0,
                PromptsTestPassed = 0,
                PromptsTestFailed = 0
            },
            SecurityTesting = new SecurityTestResult
            {
                Status = TestStatus.Failed,
                Duration = TimeSpan.FromSeconds(2.6),
                SecurityScore = 69,
                Message = "Security testing found one exploitable prompt reflection.",
                Vulnerabilities = new List<SecurityVulnerability>
                {
                    new()
                    {
                        Id = "MCP.SECURITY.PROMPT_INJECTION",
                        Name = "Prompt injection reflection",
                        Category = "PromptInjection",
                        AffectedComponent = "prompt:get",
                        Severity = VulnerabilitySeverity.Critical,
                        Description = "Server reflected untrusted prompt content without sanitization.",
                        Remediation = "Sanitize and label untrusted content before returning it.",
                        IsExploitable = true,
                        CvssScore = 9.1
                    }
                },
                SecurityRecommendations = new List<string>
                {
                    "Label or sanitize untrusted prompt content before returning it."
                }
            },
            PerformanceTesting = new PerformanceTestResult
            {
                Status = TestStatus.Passed,
                Score = 100,
                Duration = TimeSpan.FromSeconds(1.2),
                Message = "Performance is acceptable under moderate load.",
                LoadTesting = new LoadTestResult
                {
                    TotalRequests = 120,
                    SuccessfulRequests = 120,
                    FailedRequests = 0,
                    AverageResponseTimeMs = 120.5,
                    P95ResponseTimeMs = 210.4,
                    RequestsPerSecond = 18.2
                }
            },
            ErrorHandling = new ErrorHandlingTestResult
            {
                Status = TestStatus.Passed,
                Duration = TimeSpan.FromSeconds(0.7),
                Message = "Error scenarios were handled cleanly.",
                ErrorScenariosTestCount = 3,
                ErrorScenariosHandledCorrectly = 3
            },
            TrustAssessment = new McpTrustAssessment
            {
                TrustLevel = McpTrustLevel.L3_Acceptable,
                ProtocolCompliance = 84,
                SecurityPosture = 69,
                AiSafety = 75,
                OperationalReadiness = 88,
                BoundaryFindings = new List<AiBoundaryFinding>
                {
                    new()
                    {
                        Category = "PromptInjection",
                        Component = "prompt:get",
                        Severity = "Critical",
                        Description = "Prompt output includes reflected untrusted content.",
                        Mitigation = "Sanitize or clearly label untrusted data."
                    }
                }
            },
            PolicyOutcome = new ValidationPolicyOutcome
            {
                Mode = ValidationPolicyModes.Strict,
                Passed = false,
                RecommendedExitCode = 1,
                Summary = "Strict policy blocked the validation result with 2 unsuppressed signal(s).",
                Reasons = new List<string>
                {
                    "Trust level L3_Acceptable is below the strict minimum of L4_Trusted.",
                    "Tool 'delete_repo' does not declare annotations.destructiveHint."
                }
            }
        };
    }
}