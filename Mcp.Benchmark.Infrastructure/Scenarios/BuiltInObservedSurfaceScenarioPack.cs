using Mcp.Benchmark.Core.Abstractions;
using Mcp.Benchmark.Core.Models;

namespace Mcp.Benchmark.Infrastructure.Scenarios;

public sealed class BuiltInObservedSurfaceScenarioPack : IValidationScenarioPack
{
    private readonly IReadOnlyList<IValidationScenario> _scenarios;

    public BuiltInObservedSurfaceScenarioPack()
    {
        _scenarios =
        [
            CreateScenario("bootstrap-initialize-handshake", "Initialize Handshake", "bootstrap", ExecuteBootstrapScenario),
            CreateScenario("protocol-compliance-review", "Protocol Compliance Review", "protocol-core", ExecuteProtocolScenario),
            CreateScenario("tool-catalog-smoke", "Tool Catalog Smoke", "tool-surface", ExecuteToolScenario),
            CreateScenario("resource-catalog-smoke", "Resource Catalog Smoke", "resource-surface", ExecuteResourceScenario),
            CreateScenario("prompt-catalog-smoke", "Prompt Catalog Smoke", "prompt-surface", ExecutePromptScenario),
            CreateScenario("security-authentication-challenge", "Authentication Challenge Matrix", "security-boundaries", ExecuteAuthenticationScenario),
            CreateScenario("security-attack-simulations", "Attack Simulation Matrix", "security-boundaries", ExecuteAttackScenario),
            CreateScenario("error-handling-matrix", "Error Handling Matrix", "error-handling", ExecuteErrorHandlingScenario)
        ];
    }

    public ValidationPackDescriptor Descriptor => new()
    {
        Key = new ValidationDescriptorKey("scenario-pack/observed-surface"),
        Kind = ValidationPackKind.ScenarioPack,
        Revision = new ValidationRevision("2026-04"),
        DisplayName = "Built-in Observed Surface Scenario Pack",
        Stability = ValidationStability.Stable
    };

    public ValidationApplicability Applicability => new();

    public IReadOnlyList<IValidationScenario> GetScenarios()
    {
        return _scenarios;
    }

    private static IValidationScenario CreateScenario(
        string key,
        string displayName,
        string layerKey,
        Func<ValidationScenarioContext, ValidationScenarioExecutionResult> executor)
    {
        return new DelegateScenario(
            new ValidationScenarioDescriptor
            {
                Key = new ValidationDescriptorKey(key),
                Revision = new ValidationRevision("2026-04"),
                DisplayName = displayName,
                LayerKey = layerKey,
                Stability = ValidationStability.Stable,
                MutatesState = false
            },
            executor);
    }

    private static ValidationScenarioExecutionResult ExecuteBootstrapScenario(ValidationScenarioContext context)
    {
        var enabled = context.ValidationConfiguration.Validation?.Categories?.ProtocolCompliance.TestInitialization != false;
        if (!enabled)
        {
            return CreateSkippedScenario("bootstrap-initialize-handshake", "Initialize Handshake", "bootstrap", "Initialization validation disabled by configuration.");
        }

        var bootstrapHealth = context.ValidationResult.BootstrapHealth;
        if (bootstrapHealth == null)
        {
            return CreateUnavailableScenario("bootstrap-initialize-handshake", "Initialize Handshake", "bootstrap", "Bootstrap health was not recorded for this run.");
        }

        var status = bootstrapHealth.IsHealthy
            ? TestStatus.Passed
            : bootstrapHealth.AllowsDeferredValidation
                ? TestStatus.Skipped
                : TestStatus.Failed;

        return new ValidationScenarioExecutionResult
        {
            Scenario = new ValidationScenarioResult
            {
                ScenarioId = "bootstrap-initialize-handshake",
                DisplayName = "Initialize Handshake",
                Status = status,
                Summary = FirstNonEmpty(
                    bootstrapHealth.ErrorMessage,
                    bootstrapHealth.InitializationDetails?.Error,
                    bootstrapHealth.IsHealthy
                        ? "Bootstrap health checks completed successfully."
                        : "Bootstrap health checks did not complete cleanly.")
            },
            Coverage =
            [
                new ValidationCoverageDeclaration
                {
                    LayerId = "bootstrap",
                    Scope = "bootstrap-initialize-handshake",
                    Status = ValidationCoverageStatus.Covered,
                    Reason = null
                }
            ],
            Observations =
            [
                new ValidationObservation
                {
                    Id = "bootstrap-initialize",
                    LayerId = "bootstrap",
                    Component = "initialize",
                    ObservationKind = "bootstrap-health",
                    RedactedPayloadPreview = FirstNonEmpty(
                        bootstrapHealth.ErrorMessage,
                        bootstrapHealth.InitializationDetails?.Error,
                        bootstrapHealth.Disposition.ToString()),
                    Metadata = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                    {
                        ["disposition"] = bootstrapHealth.Disposition.ToString(),
                        ["httpStatus"] = bootstrapHealth.InitializationDetails?.Transport.StatusCode?.ToString() ?? string.Empty,
                        ["protocolVersion"] = bootstrapHealth.ProtocolVersion ?? string.Empty
                    }
                }
            ]
        };
    }

    private static ValidationScenarioExecutionResult ExecuteProtocolScenario(ValidationScenarioContext context)
    {
        var enabled = context.ValidationConfiguration.Validation?.Categories?.ProtocolCompliance.TestJsonRpcCompliance != false;
        if (!enabled)
        {
            return CreateSkippedScenario("protocol-compliance-review", "Protocol Compliance Review", "protocol-core", "Protocol compliance validation disabled by configuration.");
        }

        var protocolResult = context.ValidationResult.ProtocolCompliance;
        if (protocolResult == null)
        {
            return CreateUnavailableScenario("protocol-compliance-review", "Protocol Compliance Review", "protocol-core", "Protocol compliance validation did not produce a result.");
        }

        return new ValidationScenarioExecutionResult
        {
            Scenario = new ValidationScenarioResult
            {
                ScenarioId = "protocol-compliance-review",
                DisplayName = "Protocol Compliance Review",
                Status = protocolResult.Status,
                Summary = protocolResult.Message,
                Findings = protocolResult.Findings.ToList()
            },
            Coverage =
            [
                new ValidationCoverageDeclaration
                {
                    LayerId = "protocol-core",
                    Scope = "protocol-compliance-review",
                    Status = MapCoverageStatus(protocolResult.Status),
                    Reason = protocolResult.Status is TestStatus.Passed or TestStatus.Failed ? null : protocolResult.Message
                }
            ],
            Observations = protocolResult.Violations
                .Select(violation => new ValidationObservation
                {
                    Id = $"protocol-{SanitizeIdentifier(FirstNonEmpty(violation.CheckId, violation.Rule) ?? "violation")}",
                    LayerId = "protocol-core",
                    Component = FirstNonEmpty(violation.Category, violation.CheckId) ?? "protocol",
                    ObservationKind = "protocol-violation",
                    RedactedPayloadPreview = FirstNonEmpty(violation.Description, violation.Recommendation),
                    Metadata = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                    {
                        ["severity"] = violation.Severity.ToString(),
                        ["checkId"] = violation.CheckId ?? string.Empty,
                        ["specReference"] = violation.SpecReference ?? string.Empty
                    }
                })
                .ToList()
        };
    }

    private static ValidationScenarioExecutionResult ExecuteToolScenario(ValidationScenarioContext context)
    {
        var categories = context.ValidationConfiguration.Validation?.Categories;
        var enabled = categories?.ToolTesting.TestToolDiscovery != false;
        if (!enabled)
        {
            return CreateSkippedScenario("tool-catalog-smoke", "Tool Catalog Smoke", "tool-surface", "Tool discovery validation disabled by configuration.");
        }

        var toolResult = context.ValidationResult.ToolValidation;
        if (toolResult == null)
        {
            return CreateUnavailableScenario("tool-catalog-smoke", "Tool Catalog Smoke", "tool-surface", "Tool validation did not produce a result.");
        }

        var findings = toolResult.Findings
            .Concat(toolResult.AiReadinessFindings)
            .Concat(toolResult.ToolResults.SelectMany(tool => tool.Findings))
            .DistinctBy(finding => (finding.RuleId, finding.Component, finding.Summary))
            .ToList();

        return new ValidationScenarioExecutionResult
        {
            Scenario = new ValidationScenarioResult
            {
                ScenarioId = "tool-catalog-smoke",
                DisplayName = "Tool Catalog Smoke",
                Status = toolResult.Status,
                Summary = FirstNonEmpty(toolResult.Message, toolResult.ToolResults.Count > 0 ? "Tool validation completed." : "No tools were discovered during validation."),
                Findings = findings
            },
            Coverage =
            [
                new ValidationCoverageDeclaration
                {
                    LayerId = "tool-surface",
                    Scope = "tool-catalog-smoke",
                    Status = MapCoverageStatus(toolResult.Status),
                    Reason = toolResult.Status is TestStatus.Passed or TestStatus.Failed ? null : toolResult.Message
                }
            ],
            Observations = toolResult.ToolResults
                .Where(tool => tool.Findings.Count > 0 || tool.Issues.Count > 0 || tool.Status != TestStatus.Passed)
                .Select(tool => new ValidationObservation
                {
                    Id = $"tool-{SanitizeIdentifier(tool.ToolName)}",
                    LayerId = "tool-surface",
                    Component = tool.ToolName,
                    ObservationKind = "tool-result",
                    ScenarioId = "tool-catalog-smoke",
                    RedactedPayloadPreview = FirstNonEmpty(tool.Issues.FirstOrDefault(), tool.Findings.FirstOrDefault()?.Summary, tool.Description),
                    Metadata = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                    {
                        ["status"] = tool.Status.ToString(),
                        ["displayTitle"] = tool.DisplayTitle ?? string.Empty
                    }
                })
                .ToList()
        };
    }

    private static ValidationScenarioExecutionResult ExecuteResourceScenario(ValidationScenarioContext context)
    {
        var enabled = context.ValidationConfiguration.Validation?.Categories?.ResourceTesting.TestResourceDiscovery != false;
        if (!enabled)
        {
            return CreateSkippedScenario("resource-catalog-smoke", "Resource Catalog Smoke", "resource-surface", "Resource discovery validation disabled by configuration.");
        }

        var resourceResult = context.ValidationResult.ResourceTesting;
        if (resourceResult == null)
        {
            return CreateUnavailableScenario("resource-catalog-smoke", "Resource Catalog Smoke", "resource-surface", "Resource validation did not produce a result.");
        }

        var findings = resourceResult.Findings
            .Concat(resourceResult.ResourceResults.SelectMany(resource => resource.Findings))
            .DistinctBy(finding => (finding.RuleId, finding.Component, finding.Summary))
            .ToList();

        return new ValidationScenarioExecutionResult
        {
            Scenario = new ValidationScenarioResult
            {
                ScenarioId = "resource-catalog-smoke",
                DisplayName = "Resource Catalog Smoke",
                Status = resourceResult.Status,
                Summary = FirstNonEmpty(resourceResult.Message, resourceResult.ResourceResults.Count > 0 ? "Resource validation completed." : "No resources were discovered during validation."),
                Findings = findings
            },
            Coverage =
            [
                new ValidationCoverageDeclaration
                {
                    LayerId = "resource-surface",
                    Scope = "resource-catalog-smoke",
                    Status = MapCoverageStatus(resourceResult.Status),
                    Reason = resourceResult.Status is TestStatus.Passed or TestStatus.Failed ? null : resourceResult.Message
                }
            ],
            Observations = resourceResult.ResourceResults
                .Where(resource => resource.Findings.Count > 0 || resource.Issues.Count > 0 || resource.Status != TestStatus.Passed)
                .Select(resource => new ValidationObservation
                {
                    Id = $"resource-{SanitizeIdentifier(FirstNonEmpty(resource.ResourceName, resource.ResourceUri) ?? "resource")}",
                    LayerId = "resource-surface",
                    Component = FirstNonEmpty(resource.ResourceUri, resource.ResourceName) ?? "resource",
                    ObservationKind = "resource-result",
                    ScenarioId = "resource-catalog-smoke",
                    RedactedPayloadPreview = FirstNonEmpty(resource.Issues.FirstOrDefault(), resource.Findings.FirstOrDefault()?.Summary, resource.MimeType),
                    Metadata = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                    {
                        ["status"] = resource.Status.ToString(),
                        ["mimeType"] = resource.MimeType ?? string.Empty
                    }
                })
                .ToList()
        };
    }

    private static ValidationScenarioExecutionResult ExecutePromptScenario(ValidationScenarioContext context)
    {
        var enabled = context.ValidationConfiguration.Validation?.Categories?.PromptTesting.TestPromptDiscovery != false;
        if (!enabled)
        {
            return CreateSkippedScenario("prompt-catalog-smoke", "Prompt Catalog Smoke", "prompt-surface", "Prompt discovery validation disabled by configuration.");
        }

        var promptResult = context.ValidationResult.PromptTesting;
        if (promptResult == null)
        {
            return CreateUnavailableScenario("prompt-catalog-smoke", "Prompt Catalog Smoke", "prompt-surface", "Prompt validation did not produce a result.");
        }

        var findings = promptResult.Findings
            .Concat(promptResult.PromptResults.SelectMany(prompt => prompt.Findings))
            .DistinctBy(finding => (finding.RuleId, finding.Component, finding.Summary))
            .ToList();

        return new ValidationScenarioExecutionResult
        {
            Scenario = new ValidationScenarioResult
            {
                ScenarioId = "prompt-catalog-smoke",
                DisplayName = "Prompt Catalog Smoke",
                Status = promptResult.Status,
                Summary = FirstNonEmpty(promptResult.Message, promptResult.PromptResults.Count > 0 ? "Prompt validation completed." : "No prompts were discovered during validation."),
                Findings = findings
            },
            Coverage =
            [
                new ValidationCoverageDeclaration
                {
                    LayerId = "prompt-surface",
                    Scope = "prompt-catalog-smoke",
                    Status = MapCoverageStatus(promptResult.Status),
                    Reason = promptResult.Status is TestStatus.Passed or TestStatus.Failed ? null : promptResult.Message
                }
            ],
            Observations = promptResult.PromptResults
                .Where(prompt => prompt.Findings.Count > 0 || prompt.Issues.Count > 0 || prompt.Status != TestStatus.Passed)
                .Select(prompt => new ValidationObservation
                {
                    Id = $"prompt-{SanitizeIdentifier(prompt.PromptName)}",
                    LayerId = "prompt-surface",
                    Component = prompt.PromptName,
                    ObservationKind = "prompt-result",
                    ScenarioId = "prompt-catalog-smoke",
                    RedactedPayloadPreview = FirstNonEmpty(prompt.Issues.FirstOrDefault(), prompt.Findings.FirstOrDefault()?.Summary, prompt.Description),
                    Metadata = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                    {
                        ["status"] = prompt.Status.ToString(),
                        ["arguments"] = prompt.ArgumentsCount.ToString()
                    }
                })
                .ToList()
        };
    }

    private static ValidationScenarioExecutionResult ExecuteAuthenticationScenario(ValidationScenarioContext context)
    {
        var enabled = context.ValidationConfiguration.Validation?.Categories?.SecurityTesting.TestInputValidation != false;
        if (!enabled)
        {
            return CreateSkippedScenario("security-authentication-challenge", "Authentication Challenge Matrix", "security-boundaries", "Security validation disabled by configuration.");
        }

        var authenticationResult = context.ValidationResult.SecurityTesting?.AuthenticationTestResult;
        if (authenticationResult?.TestScenarios.Count > 0 != true)
        {
            return CreateNotApplicableScenario("security-authentication-challenge", "Authentication Challenge Matrix", "security-boundaries", "No authentication challenge scenarios were recorded for this run.");
        }

        var status = authenticationResult.TestScenarios
            .Select(MapAuthenticationScenarioStatus)
            .DefaultIfEmpty(TestStatus.NotRun)
            .Aggregate(TestStatus.Passed, AggregateScenarioStatus);

        return new ValidationScenarioExecutionResult
        {
            Scenario = new ValidationScenarioResult
            {
                ScenarioId = "security-authentication-challenge",
                DisplayName = "Authentication Challenge Matrix",
                Status = status,
                Summary = $"Evaluated {authenticationResult.TestScenarios.Count} authentication challenge scenario(s)."
            },
            Coverage =
            [
                new ValidationCoverageDeclaration
                {
                    LayerId = "security-boundaries",
                    Scope = "security-authentication-challenge",
                    Status = ValidationCoverageStatus.Covered,
                    Reason = null
                }
            ],
            Observations = authenticationResult.TestScenarios
                .Select(scenario => new ValidationObservation
                {
                    Id = $"auth-{SanitizeIdentifier(scenario.ScenarioName)}",
                    LayerId = "security-boundaries",
                    Component = FirstNonEmpty(scenario.Method, scenario.TestType, scenario.ScenarioName) ?? "authentication",
                    ObservationKind = "authentication-scenario",
                    ScenarioId = "security-authentication-challenge",
                    RedactedPayloadPreview = FirstNonEmpty(scenario.Analysis, scenario.ActualBehavior, scenario.ComplianceReason),
                    Metadata = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                    {
                        ["statusCode"] = scenario.StatusCode,
                        ["disposition"] = scenario.AssessmentDisposition.ToString()
                    },
                    ProbeContexts = BuildProbeContexts(scenario.ProbeContext)
                })
                .ToList()
        };
    }

    private static ValidationScenarioExecutionResult ExecuteAttackScenario(ValidationScenarioContext context)
    {
        var enabled = context.ValidationConfiguration.Validation?.Categories?.SecurityTesting.TestInputValidation != false;
        if (!enabled)
        {
            return CreateSkippedScenario("security-attack-simulations", "Attack Simulation Matrix", "security-boundaries", "Security validation disabled by configuration.");
        }

        var attacks = context.ValidationResult.SecurityTesting?.AttackSimulations;
        if (attacks?.Count > 0 != true)
        {
            return CreateNotApplicableScenario("security-attack-simulations", "Attack Simulation Matrix", "security-boundaries", "No attack simulation results were recorded for this run.");
        }

        var status = attacks
            .Select(attack => attack.DefenseSuccessful ? TestStatus.Passed : attack.AttackSuccessful ? TestStatus.Failed : TestStatus.Skipped)
            .DefaultIfEmpty(TestStatus.NotRun)
            .Aggregate(TestStatus.Passed, AggregateScenarioStatus);

        return new ValidationScenarioExecutionResult
        {
            Scenario = new ValidationScenarioResult
            {
                ScenarioId = "security-attack-simulations",
                DisplayName = "Attack Simulation Matrix",
                Status = status,
                Summary = $"Evaluated {attacks.Count} attack simulation(s)."
            },
            Coverage =
            [
                new ValidationCoverageDeclaration
                {
                    LayerId = "security-boundaries",
                    Scope = "security-attack-simulations",
                    Status = ValidationCoverageStatus.Covered,
                    Reason = null
                }
            ],
            Observations = attacks
                .Select(attack => new ValidationObservation
                {
                    Id = $"attack-{SanitizeIdentifier(attack.AttackVector)}",
                    LayerId = "security-boundaries",
                    Component = attack.AttackVector,
                    ObservationKind = "attack-simulation",
                    ScenarioId = "security-attack-simulations",
                    RedactedPayloadPreview = FirstNonEmpty(attack.ServerResponse, attack.Description),
                    Metadata = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                    {
                        ["attackSuccessful"] = attack.AttackSuccessful.ToString(),
                        ["defenseSuccessful"] = attack.DefenseSuccessful.ToString()
                    },
                    ProbeContexts = attack.ProbeContexts
                })
                .ToList()
        };
    }

    private static List<ProbeContext>? BuildProbeContexts(params ProbeContext?[] probeContexts)
    {
        var contexts = probeContexts.Where(context => context != null).Cast<ProbeContext>().ToList();
        return contexts.Count > 0 ? contexts : null;
    }

    private static ValidationScenarioExecutionResult ExecuteErrorHandlingScenario(ValidationScenarioContext context)
    {
        var errorHandlingConfig = context.ValidationConfiguration.Validation?.Categories?.ErrorHandling;
        var enabled = errorHandlingConfig is null ||
            errorHandlingConfig.TestInvalidMethods ||
            errorHandlingConfig.TestMalformedJson ||
            errorHandlingConfig.TestConnectionInterruption ||
            errorHandlingConfig.TestTimeoutHandling ||
            errorHandlingConfig.TestGracefulDegradation ||
            errorHandlingConfig.CustomErrorScenarios.Count > 0;

        if (!enabled)
        {
            return CreateSkippedScenario("error-handling-matrix", "Error Handling Matrix", "error-handling", "Error-handling validation disabled by configuration.");
        }

        var errorHandling = context.ValidationResult.ErrorHandling;
        if (errorHandling == null)
        {
            return CreateUnavailableScenario("error-handling-matrix", "Error Handling Matrix", "error-handling", "Error-handling validation is configured but no validator currently populates this slice.");
        }

        return new ValidationScenarioExecutionResult
        {
            Scenario = new ValidationScenarioResult
            {
                ScenarioId = "error-handling-matrix",
                DisplayName = "Error Handling Matrix",
                Status = errorHandling.Status,
                Summary = errorHandling.Message,
                Findings = errorHandling.Findings.ToList()
            },
            Coverage =
            [
                new ValidationCoverageDeclaration
                {
                    LayerId = "error-handling",
                    Scope = "error-handling-matrix",
                    Status = MapCoverageStatus(errorHandling.Status),
                    Reason = errorHandling.Status is TestStatus.Passed or TestStatus.Failed ? null : errorHandling.Message
                }
            ],
            Observations = errorHandling.ErrorScenarioResults
                .Select(scenario => new ValidationObservation
                {
                    Id = $"error-{SanitizeIdentifier(scenario.ScenarioName)}",
                    LayerId = "error-handling",
                    Component = scenario.ErrorType,
                    ObservationKind = "error-scenario",
                    ScenarioId = "error-handling-matrix",
                    RedactedPayloadPreview = FirstNonEmpty(scenario.ActualResponse, scenario.ExpectedResponse),
                    Metadata = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                    {
                        ["handledCorrectly"] = scenario.HandledCorrectly.ToString(),
                        ["gracefulRecovery"] = scenario.GracefulRecovery.ToString()
                    }
                })
                .ToList()
        };
    }

    private static ValidationScenarioExecutionResult CreateSkippedScenario(string scenarioId, string displayName, string layerId, string reason)
    {
        return CreatePassiveScenario(scenarioId, displayName, layerId, TestStatus.Skipped, ValidationCoverageStatus.Skipped, reason);
    }

    private static ValidationScenarioExecutionResult CreateUnavailableScenario(string scenarioId, string displayName, string layerId, string reason)
    {
        return CreatePassiveScenario(scenarioId, displayName, layerId, TestStatus.NotRun, ValidationCoverageStatus.Unavailable, reason);
    }

    private static ValidationScenarioExecutionResult CreateNotApplicableScenario(string scenarioId, string displayName, string layerId, string reason)
    {
        return CreatePassiveScenario(scenarioId, displayName, layerId, TestStatus.Skipped, ValidationCoverageStatus.NotApplicable, reason);
    }

    private static ValidationScenarioExecutionResult CreatePassiveScenario(
        string scenarioId,
        string displayName,
        string layerId,
        TestStatus status,
        ValidationCoverageStatus coverageStatus,
        string reason)
    {
        return new ValidationScenarioExecutionResult
        {
            Scenario = new ValidationScenarioResult
            {
                ScenarioId = scenarioId,
                DisplayName = displayName,
                Status = status,
                Summary = reason
            },
            Coverage =
            [
                new ValidationCoverageDeclaration
                {
                    LayerId = layerId,
                    Scope = scenarioId,
                    Status = coverageStatus,
                    Blocker = MapCoverageBlocker(coverageStatus),
                    Confidence = MapCoverageConfidence(coverageStatus),
                    Reason = reason
                }
            ]
        };
    }

    private static TestStatus AggregateScenarioStatus(TestStatus current, TestStatus next)
    {
        return (current, next) switch
        {
            (TestStatus.Failed, _) or (_, TestStatus.Failed) => TestStatus.Failed,
            (TestStatus.Error, _) or (_, TestStatus.Error) => TestStatus.Error,
            (TestStatus.Cancelled, _) or (_, TestStatus.Cancelled) => TestStatus.Cancelled,
            (TestStatus.AuthRequired, _) or (_, TestStatus.AuthRequired) => TestStatus.AuthRequired,
            (TestStatus.Inconclusive, _) or (_, TestStatus.Inconclusive) => TestStatus.Inconclusive,
            (TestStatus.NotRun, _) => next,
            (_, TestStatus.NotRun) => current,
            (TestStatus.Skipped, TestStatus.Passed) => TestStatus.Skipped,
            (TestStatus.Passed, TestStatus.Skipped) => TestStatus.Skipped,
            _ => current
        };
    }

    private static TestStatus MapAuthenticationScenarioStatus(AuthenticationScenario scenario)
    {
        return scenario.AssessmentDisposition switch
        {
            AuthenticationAssessmentDisposition.StandardsAligned or AuthenticationAssessmentDisposition.SecureCompatible => TestStatus.Passed,
            AuthenticationAssessmentDisposition.Insecure => TestStatus.Failed,
            AuthenticationAssessmentDisposition.Inconclusive => TestStatus.Inconclusive,
            AuthenticationAssessmentDisposition.Informational => TestStatus.Skipped,
            _ => scenario.IsSecure || scenario.IsCompliant ? TestStatus.Passed : TestStatus.Failed
        };
    }

    private static ValidationCoverageStatus MapCoverageStatus(TestStatus status)
    {
        return status switch
        {
            TestStatus.Passed or TestStatus.Failed => ValidationCoverageStatus.Covered,
            TestStatus.Skipped => ValidationCoverageStatus.Skipped,
            TestStatus.AuthRequired => ValidationCoverageStatus.AuthRequired,
            TestStatus.Inconclusive => ValidationCoverageStatus.Inconclusive,
            TestStatus.Error or TestStatus.Cancelled => ValidationCoverageStatus.Blocked,
            _ => ValidationCoverageStatus.Unavailable
        };
    }

    private static ValidationEvidenceBlocker MapCoverageBlocker(ValidationCoverageStatus status)
    {
        return status switch
        {
            ValidationCoverageStatus.Skipped => ValidationEvidenceBlocker.ConfigDisabled,
            ValidationCoverageStatus.AuthRequired => ValidationEvidenceBlocker.AuthRequired,
            ValidationCoverageStatus.Inconclusive => ValidationEvidenceBlocker.TransientFailure,
            ValidationCoverageStatus.Unavailable => ValidationEvidenceBlocker.Unimplemented,
            ValidationCoverageStatus.Blocked => ValidationEvidenceBlocker.TransportError,
            _ => ValidationEvidenceBlocker.None
        };
    }

    private static EvidenceConfidenceLevel MapCoverageConfidence(ValidationCoverageStatus status)
    {
        return status switch
        {
            ValidationCoverageStatus.Covered => EvidenceConfidenceLevel.High,
            ValidationCoverageStatus.AuthRequired or ValidationCoverageStatus.Inconclusive or ValidationCoverageStatus.Skipped => EvidenceConfidenceLevel.Low,
            ValidationCoverageStatus.NotApplicable => EvidenceConfidenceLevel.High,
            _ => EvidenceConfidenceLevel.None
        };
    }

    private static string SanitizeIdentifier(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return "unknown";
        }

        var characters = value
            .Trim()
            .ToLowerInvariant()
            .Select(character => char.IsLetterOrDigit(character) ? character : '-')
            .ToArray();

        var sanitized = new string(characters);
        while (sanitized.Contains("--", StringComparison.Ordinal))
        {
            sanitized = sanitized.Replace("--", "-", StringComparison.Ordinal);
        }

        return sanitized.Trim('-');
    }

    private static string? FirstNonEmpty(params string?[] values)
    {
        return values.FirstOrDefault(value => !string.IsNullOrWhiteSpace(value));
    }

    private sealed class DelegateScenario : IValidationScenario
    {
        private readonly Func<ValidationScenarioContext, ValidationScenarioExecutionResult> _executor;

        public DelegateScenario(
            ValidationScenarioDescriptor descriptor,
            Func<ValidationScenarioContext, ValidationScenarioExecutionResult> executor)
        {
            Descriptor = descriptor;
            _executor = executor;
        }

        public ValidationScenarioDescriptor Descriptor { get; }

        public Task<ValidationScenarioExecutionResult> ExecuteAsync(
            ValidationScenarioContext context,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult(_executor(context));
        }
    }
}