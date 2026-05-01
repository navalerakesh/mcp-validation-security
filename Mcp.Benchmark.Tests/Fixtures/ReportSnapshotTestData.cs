using Mcp.Benchmark.Core.Models;
using Mcp.Benchmark.Core.Services;
using ModelContextProtocol.Protocol;

namespace Mcp.Benchmark.Tests.Fixtures;

public static class ReportSnapshotTestData
{
    public static ValidationResult CreateComprehensiveResult()
    {
        var authProbe = new ProbeContext
        {
            ProbeId = "probe-auth-tools-list-401",
            Method = "tools/list",
            Transport = "http",
            ProtocolVersion = "2025-11-25",
            AuthApplied = false,
            AuthStatus = ProbeAuthStatus.AuthRequired,
            ResponseClassification = ProbeResponseClassification.AuthenticationChallenge,
            Confidence = EvidenceConfidenceLevel.High,
            StatusCode = 401,
            Reason = "Unauthenticated tools/list returned WWW-Authenticate."
        };
        var attackDiscoveryProbe = new ProbeContext
        {
            ProbeId = "probe-attack-tools-list-200",
            Method = "tools/list",
            Transport = "http",
            ProtocolVersion = "2025-11-25",
            AuthApplied = true,
            AuthStatus = ProbeAuthStatus.Applied,
            ResponseClassification = ProbeResponseClassification.Success,
            Confidence = EvidenceConfidenceLevel.High,
            StatusCode = 200
        };
        var attackExecutionProbe = new ProbeContext
        {
            ProbeId = "probe-attack-tools-call-400",
            Method = "tools/call",
            Transport = "http",
            ProtocolVersion = "2025-11-25",
            AuthApplied = true,
            AuthStatus = ProbeAuthStatus.Applied,
            ResponseClassification = ProbeResponseClassification.ProtocolError,
            Confidence = EvidenceConfidenceLevel.High,
            StatusCode = 400,
            Reason = "Injected argument rejected with JSON-RPC error."
        };
        var promptInjectionProbe = new ProbeContext
        {
            ProbeId = "probe-prompt-get-reflection-200",
            Method = "prompts/get",
            Transport = "http",
            ProtocolVersion = "2025-11-25",
            AuthApplied = true,
            AuthStatus = ProbeAuthStatus.Applied,
            ResponseClassification = ProbeResponseClassification.Success,
            Confidence = EvidenceConfidenceLevel.High,
            StatusCode = 200,
            Reason = "Prompt response reflected untrusted content."
        };

        var result = new ValidationResult
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
            Assessments = new ValidationAssessmentDocument
            {
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
                            CvssScore = 9.1,
                            ProbeContexts = new List<ProbeContext> { promptInjectionProbe }
                        }
                    },
                    AuthenticationTestResult = new AuthenticationTestResult
                    {
                        Status = TestStatus.Passed,
                        ComplianceScore = 100,
                        TestScenarios = new List<AuthenticationScenario>
                        {
                            new()
                            {
                                ScenarioName = "No Auth - tools/list",
                                TestType = "No Auth",
                                Method = "tools/list",
                                ExpectedBehavior = "4xx (Secure Rejection)",
                                ActualBehavior = "401 Unauthorized with challenge",
                                StatusCode = "401",
                                Analysis = "Returned 401 with WWW-Authenticate.",
                                IsCompliant = true,
                                IsSecure = true,
                                IsStandardsAligned = true,
                                AssessmentDisposition = AuthenticationAssessmentDisposition.StandardsAligned,
                                ComplianceReason = "Authentication challenge was standards-aligned.",
                                WwwAuthenticateHeader = "Bearer resource_metadata=\"https://snapshot.example.test/.well-known/oauth-protected-resource\"",
                                ProbeContext = authProbe
                            }
                        }
                    },
                    AttackSimulations = new List<AttackSimulationResult>
                    {
                        new()
                        {
                            AttackVector = "MCP-SEC-001",
                            Description = "JSON-RPC Error Smuggling",
                            AttackSuccessful = false,
                            DefenseSuccessful = true,
                            ServerResponse = "Rejected injected argument with JSON-RPC error -32602.",
                            ExecutionTimeMs = 18,
                            ProbeContexts = new List<ProbeContext> { attackDiscoveryProbe, attackExecutionProbe },
                            Evidence = new Dictionary<string, object>
                            {
                                ["outcome"] = "blocked",
                                ["probeIds"] = "probe-attack-tools-list-200,probe-attack-tools-call-400",
                                ["probeResponseClassifications"] = "Success,ProtocolError",
                                ["probeAuthStatuses"] = "Applied,Applied"
                            }
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
                    Score = 100,
                    Duration = TimeSpan.FromSeconds(0.7),
                    Message = "Validated 3 error scenario(s); 3 handled correctly.",
                    ErrorScenariosTestCount = 3,
                    ErrorScenariosHandledCorrectly = 3,
                    ErrorScenarioResults = new List<ErrorScenarioResult>
                    {
                        new()
                        {
                            ScenarioName = "Invalid Method Call",
                            ErrorType = "invalid-method",
                            HandledCorrectly = true,
                            ExpectedResponse = "JSON-RPC error code -32601 (Method not found).",
                            ActualResponse = "HTTP 200; JSON-RPC error -32601: Method not found",
                            ErrorHandlingTimeMs = 18.4,
                            GracefulRecovery = true
                        },
                        new()
                        {
                            ScenarioName = "Malformed JSON Payload",
                            ErrorType = "malformed-json",
                            HandledCorrectly = true,
                            ExpectedResponse = "JSON-RPC error code -32700 (Parse error).",
                            ActualResponse = "HTTP 400; JSON-RPC error -32700: Parse error",
                            ErrorHandlingTimeMs = 21.1,
                            GracefulRecovery = true
                        },
                        new()
                        {
                            ScenarioName = "Graceful Degradation On Invalid Request",
                            ErrorType = "invalid-request",
                            HandledCorrectly = true,
                            ExpectedResponse = "JSON-RPC error code -32600 (Invalid request).",
                            ActualResponse = "HTTP 400; JSON-RPC error -32600: Invalid Request",
                            ErrorHandlingTimeMs = 19.7,
                            GracefulRecovery = true
                        }
                    }
                },
                Layers = new List<ValidationLayerResult>
                {
                    new()
                    {
                        LayerId = "protocol-core",
                        DisplayName = "Protocol Compliance",
                        Status = TestStatus.Failed,
                        Summary = "Protocol negotiation had one blocking violation.",
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
                        }
                    },
                    new()
                    {
                        LayerId = "tool-surface",
                        DisplayName = "Tool Validation",
                        Status = TestStatus.Passed,
                        Summary = "Tool catalog loaded and executed."
                    },
                    new()
                    {
                        LayerId = "error-handling",
                        DisplayName = "Error Handling",
                        Status = TestStatus.Passed,
                        Summary = "Validated 3 error scenario(s); 3 handled correctly."
                    },
                    new()
                    {
                        LayerId = "client-profiles",
                        DisplayName = "Client Profile Compatibility",
                        Status = TestStatus.Passed,
                        Summary = "Evaluated 2 profile(s); 1 warning profile(s); 0 incompatible profile(s)."
                    }
                },
                Scenarios = new List<ValidationScenarioResult>
                {
                    new()
                    {
                        ScenarioId = "tool-catalog-smoke",
                        DisplayName = "Tool Catalog Smoke",
                        Status = TestStatus.Passed,
                        Summary = "Discovery and first invocation completed with expected metadata.",
                        Findings = new List<ValidationFinding>()
                    }
                    ,
                    new()
                    {
                        ScenarioId = "security-authentication-challenge",
                        DisplayName = "Authentication Challenge Matrix",
                        Status = TestStatus.Skipped,
                        Summary = "Authentication challenge completed under deferred bootstrap review.",
                        Findings = new List<ValidationFinding>()
                    },
                    new()
                    {
                        ScenarioId = "security-attack-simulations",
                        DisplayName = "Attack Simulation Matrix",
                        Status = TestStatus.Passed,
                        Summary = "Evaluated 1 attack simulation(s).",
                        Findings = new List<ValidationFinding>()
                    },
                    new()
                    {
                        ScenarioId = "error-handling-matrix",
                        DisplayName = "Error Handling Matrix",
                        Status = TestStatus.Passed,
                        Summary = "Validated 3 error scenario(s); 3 handled correctly.",
                        Findings = new List<ValidationFinding>()
                    }
                }
            },
            Evidence = new ValidationEvidenceDocument
            {
                Coverage = new List<ValidationCoverageDeclaration>
                {
                    new()
                    {
                        LayerId = "protocol-core",
                        Scope = "json-rpc",
                        Status = ValidationCoverageStatus.Covered,
                        Reason = null
                    },
                    new()
                    {
                        LayerId = "tool-surface",
                        Scope = "tools/list",
                        Status = ValidationCoverageStatus.Covered,
                        Reason = null
                    },
                    new()
                    {
                        LayerId = "prompt-surface",
                        Scope = "prompts/list",
                        Status = ValidationCoverageStatus.Skipped,
                        Reason = "Server does not advertise prompts capability."
                    },
                    new()
                    {
                        LayerId = "client-profiles",
                        Scope = "github-copilot-cli,github-copilot-cloud-agent",
                        Status = ValidationCoverageStatus.Covered,
                        Reason = null
                    },
                    new()
                    {
                        LayerId = "tool-surface",
                        Scope = "tool-catalog-smoke",
                        Status = ValidationCoverageStatus.Covered,
                        Reason = null
                    },
                    new()
                    {
                        LayerId = "security-boundaries",
                        Scope = "security-authentication-challenge",
                        Status = ValidationCoverageStatus.Covered,
                        Reason = null
                    },
                    new()
                    {
                        LayerId = "error-handling",
                        Scope = "error-handling",
                        Status = ValidationCoverageStatus.Covered,
                        Reason = null
                    },
                    new()
                    {
                        LayerId = "error-handling",
                        Scope = "error-handling-matrix",
                        Status = ValidationCoverageStatus.Covered,
                        Reason = null
                    }
                },
                AppliedPacks = new List<ValidationPackDescriptor>
                {
                    new()
                    {
                        Key = new ValidationDescriptorKey("protocol-features/mcp-embedded"),
                        Kind = ValidationPackKind.ProtocolFeatures,
                        Revision = new ValidationRevision("2026-04"),
                        DisplayName = "Embedded MCP Protocol Features",
                        Stability = ValidationStability.Stable,
                        DocumentationUrl = "https://spec.modelcontextprotocol.io/"
                    },
                    new()
                    {
                        Key = new ValidationDescriptorKey("rule-pack/protocol-core"),
                        Kind = ValidationPackKind.RulePack,
                        Revision = new ValidationRevision("2026-04"),
                        DisplayName = "Built-in Protocol Rule Pack",
                        Stability = ValidationStability.Stable,
                        DocumentationUrl = "https://spec.modelcontextprotocol.io/"
                    },
                    new()
                    {
                        Key = new ValidationDescriptorKey("client-profile-pack/built-in"),
                        Kind = ValidationPackKind.ClientProfilePack,
                        Revision = new ValidationRevision("2026-04"),
                        DisplayName = "Built-in Client Profile Pack",
                        Stability = ValidationStability.Stable
                    },
                    new()
                    {
                        Key = new ValidationDescriptorKey("scenario-pack/observed-surface"),
                        Kind = ValidationPackKind.ScenarioPack,
                        Revision = new ValidationRevision("2026-04"),
                        DisplayName = "Built-in Observed Surface Scenario Pack",
                        Stability = ValidationStability.Stable
                    }
                },
                Observations = new List<ValidationObservation>
                {
                    new()
                    {
                        Id = "error-invalid-method-call",
                        LayerId = "error-handling",
                        Component = "invalid-method",
                        ObservationKind = "error-scenario",
                        ScenarioId = "error-handling-matrix",
                        RedactedPayloadPreview = "HTTP 200; JSON-RPC error -32601: Method not found",
                        Metadata = new Dictionary<string, string>
                        {
                            ["handledCorrectly"] = "True",
                            ["gracefulRecovery"] = "True"
                        }
                    },
                    new()
                    {
                        Id = "error-malformed-json-payload",
                        LayerId = "error-handling",
                        Component = "malformed-json",
                        ObservationKind = "error-scenario",
                        ScenarioId = "error-handling-matrix",
                        RedactedPayloadPreview = "HTTP 400; JSON-RPC error -32700: Parse error",
                        Metadata = new Dictionary<string, string>
                        {
                            ["handledCorrectly"] = "True",
                            ["gracefulRecovery"] = "True"
                        }
                    },
                    new()
                    {
                        Id = "error-graceful-degradation-on-invalid-request",
                        LayerId = "error-handling",
                        Component = "invalid-request",
                        ObservationKind = "error-scenario",
                        ScenarioId = "error-handling-matrix",
                        RedactedPayloadPreview = "HTTP 400; JSON-RPC error -32600: Invalid Request",
                        Metadata = new Dictionary<string, string>
                        {
                            ["handledCorrectly"] = "True",
                            ["gracefulRecovery"] = "True"
                        }
                    },
                    new()
                    {
                        Id = "obs-init-401",
                        LayerId = "protocol-core",
                        Component = "initialize",
                        ObservationKind = "handshake-response",
                        RedactedPayloadPreview = "401 Unauthorized — bootstrap encountered an authentication challenge.",
                        Metadata = new Dictionary<string, string>
                        {
                            ["httpStatus"] = "401"
                        }
                    },
                    new()
                    {
                        Id = "obs-tool-delete-repo",
                        LayerId = "tool-surface",
                        Component = "delete_repo",
                        ObservationKind = "tool-metadata",
                        ScenarioId = "tool-catalog-smoke",
                        RedactedPayloadPreview = "Tool metadata missing annotations.destructiveHint.",
                        Metadata = new Dictionary<string, string>
                        {
                            ["displayTitle"] = "Delete Repository"
                        }
                    },
                    new()
                    {
                        Id = "auth-unauthorized-tools-list-challenge",
                        LayerId = "security-boundaries",
                        Component = "tools/list",
                        ObservationKind = "authentication-scenario",
                        ScenarioId = "security-authentication-challenge",
                        RedactedPayloadPreview = "Returned 401 with WWW-Authenticate.",
                        Metadata = new Dictionary<string, string>
                        {
                            ["statusCode"] = "401",
                            ["disposition"] = "StandardsAligned"
                        },
                        ProbeContexts = new List<ProbeContext> { authProbe }
                    },
                    new()
                    {
                        Id = "attack-mcp-sec-001",
                        LayerId = "security-boundaries",
                        Component = "MCP-SEC-001",
                        ObservationKind = "attack-simulation",
                        ScenarioId = "security-attack-simulations",
                        RedactedPayloadPreview = "Rejected injected argument with JSON-RPC error -32602.",
                        Metadata = new Dictionary<string, string>
                        {
                            ["attackSuccessful"] = "False",
                            ["defenseSuccessful"] = "True"
                        },
                        ProbeContexts = new List<ProbeContext> { attackDiscoveryProbe, attackExecutionProbe }
                    }
                }
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

        foreach (var tool in result.ToolValidation!.ToolResults)
        {
            var controlAnalysis = AiSafetyControlAnalyzer.AnalyzeTool(new AiSafetyControlTarget
            {
                Name = tool.ToolName,
                Description = tool.Description,
                ReadOnlyHint = tool.ReadOnlyHint,
                DestructiveHint = tool.DestructiveHint,
                OpenWorldHint = tool.OpenWorldHint,
                ParameterNames = tool.InputParameterNames
            });
            tool.AiSafetyControlEvidence.AddRange(controlAnalysis.Evidence);
            result.ToolValidation.AiSafetyControlEvidence.AddRange(controlAnalysis.Evidence);
        }

        result.VerdictAssessment = ValidationVerdictEngine.Calculate(result);
        result.OverallStatus = ValidationVerdictEngine.IsPassing(result.VerdictAssessment)
            ? ValidationStatus.Passed
            : ValidationStatus.Failed;
        result.PolicyOutcome = ValidationPolicyEvaluator.Evaluate(result, new ValidationPolicyConfig
        {
            Mode = ValidationPolicyModes.Strict
        });

        return result;
    }
}