using Mcp.Benchmark.ClientProfiles;
using Mcp.Benchmark.Core.Models;
using ModelContextProtocol.Protocol;

namespace Mcp.Benchmark.Tests.Unit.ClientProfiles;

public class ClientProfileEvaluatorTests
{
    private readonly ClientProfileEvaluator _evaluator = new();

    [Fact]
    public void Evaluate_WithoutExplicitProfiles_ShouldDefaultToAllSupportedProfiles()
    {
        var result = CreateToolOnlyValidationResult();

        var compatibility = _evaluator.Evaluate(result, options: null);

        compatibility.Should().NotBeNull();
        compatibility!.Assessments.Select(assessment => assessment.ProfileId)
            .Should().Equal(ClientProfileCatalog.SupportedProfileIds);
    }

    [Fact]
    public void Evaluate_WithAllProfiles_ShouldReturnCatalogProfilesInOrder()
    {
        var result = CreateToolOnlyValidationResult();

        var compatibility = _evaluator.Evaluate(result, new ClientProfileOptions
        {
            Profiles = new List<string> { ClientProfileCatalog.AllProfilesToken }
        });

        compatibility.Should().NotBeNull();
        compatibility!.Assessments.Select(assessment => assessment.ProfileId)
            .Should().Equal(ClientProfileCatalog.SupportedProfileIds);
        // Minimal fixture exposes one tool with no resources/prompts; the comprehensive
        // per-client checks will satisfy required gates but may surface advisory warnings
        // (e.g. extra surface absent, listChanged not advertised). No profile should be
        // Incompatible against this baseline fixture.
        compatibility.Assessments.Should().OnlyContain(assessment =>
            assessment.Status == ClientProfileCompatibilityStatus.Compatible ||
            assessment.Status == ClientProfileCompatibilityStatus.CompatibleWithWarnings);
        compatibility.Assessments.Should().OnlyContain(assessment => assessment.FailedRequirements == 0);
    }

    [Fact]
    public void Evaluate_ShouldRecordAppliedProfilePackAndCompatibilityLayer()
    {
        var result = CreateToolOnlyValidationResult();

        var compatibility = _evaluator.Evaluate(result, new ClientProfileOptions
        {
            Profiles = new List<string> { "github-copilot-cli" }
        });

        compatibility.Should().NotBeNull();
        result.Evidence.AppliedPacks.Select(pack => pack.Key.Value).Should().Contain("client-profile-pack/built-in");
        result.Assessments.Layers.Should().ContainSingle(layer =>
            layer.LayerId == "client-profiles" &&
            layer.Status == TestStatus.Passed);
        result.Evidence.Coverage.Should().ContainSingle(coverage =>
            coverage.LayerId == "client-profiles" &&
            coverage.Status == ValidationCoverageStatus.Covered);
    }

    [Fact]
    public void Evaluate_GitHubCopilotCloudAgent_ShouldWarnWhenPromptAndResourceSurfacesExist()
    {
        var result = CreateToolOnlyValidationResult();
        result.PromptTesting = new PromptTestResult
        {
            Status = TestStatus.Passed,
            PromptsDiscovered = 1,
            PromptResults = new List<IndividualPromptResult>
            {
                new() { PromptName = "triage_issue", Status = TestStatus.Passed }
            }
        };
        result.ResourceTesting = new ResourceTestResult
        {
            Status = TestStatus.Passed,
            ResourcesDiscovered = 1,
            ResourceResults = new List<IndividualResourceResult>
            {
                new() { ResourceUri = "file:///repo/README.md", ResourceName = "README", Status = TestStatus.Passed }
            }
        };

        var compatibility = _evaluator.Evaluate(result, new ClientProfileOptions
        {
            Profiles = new List<string> { "github-copilot-cloud-agent" }
        });

        compatibility.Should().NotBeNull();
        var assessment = compatibility!.Assessments.Should().ContainSingle().Subject;
        assessment.Status.Should().Be(ClientProfileCompatibilityStatus.CompatibleWithWarnings);
        assessment.Summary.Should().Be("Required compatibility checks passed; 1 advisory requirement still needs follow-up.");
        assessment.Requirements.Should().Contain(requirement =>
            requirement.RequirementId == "surface-tools-only-surface" &&
            requirement.Outcome == ClientProfileRequirementOutcome.Warning);
    }

    [Fact]
    public void Evaluate_ClaudeCode_ShouldFailWhenPromptContractIsBroken()
    {
        var result = CreateToolOnlyValidationResult();
        result.PromptTesting = new PromptTestResult
        {
            Status = TestStatus.Failed,
            PromptsDiscovered = 1,
            Findings = new List<ValidationFinding>
            {
                new()
                {
                    RuleId = ValidationFindingRuleIds.PromptGetMissingMessagesArray,
                    Component = "triage_issue",
                    Severity = ValidationFindingSeverity.High,
                    Summary = "Prompt 'triage_issue' did not return messages[]."
                }
            },
            PromptResults = new List<IndividualPromptResult>
            {
                new() { PromptName = "triage_issue", Status = TestStatus.Failed }
            }
        };

        var compatibility = _evaluator.Evaluate(result, new ClientProfileOptions
        {
            Profiles = new List<string> { "claude-code" }
        });

        compatibility.Should().NotBeNull();
        var assessment = compatibility!.Assessments.Should().ContainSingle().Subject;
        assessment.Status.Should().Be(ClientProfileCompatibilityStatus.Incompatible);
        assessment.FailedRequirements.Should().BeGreaterThan(0);
        assessment.Requirements.Should().Contain(requirement =>
            requirement.RequirementId == "prompt-prompts-contract" &&
            requirement.RuleIds.Contains(ValidationFindingRuleIds.PromptGetMissingMessagesArray));
    }

    [Fact]
    public void Evaluate_ClaudeCode_ShouldWarnWhenPromptCategoryFailsWithoutPromptContractFinding()
    {
        var result = CreateToolOnlyValidationResult();
        result.PromptTesting = new PromptTestResult
        {
            Status = TestStatus.Failed,
            PromptsDiscovered = 1,
            Findings = new List<ValidationFinding>
            {
                new()
                {
                    RuleId = ValidationFindingRuleIds.PromptArgumentComplexityGuidanceMissing,
                    Component = "triage_issue",
                    Severity = ValidationFindingSeverity.Low,
                    Summary = "Prompt requires multiple inputs without enough caller guidance."
                }
            },
            PromptResults = new List<IndividualPromptResult>
            {
                new() { PromptName = "triage_issue", Status = TestStatus.Passed, ExecutionSuccessful = true }
            }
        };

        var compatibility = _evaluator.Evaluate(result, new ClientProfileOptions
        {
            Profiles = new List<string> { "claude-code" }
        });

        compatibility.Should().NotBeNull();
        var assessment = compatibility!.Assessments.Should().ContainSingle().Subject;
        assessment.Status.Should().Be(ClientProfileCompatibilityStatus.CompatibleWithWarnings);
        assessment.Requirements.Should().Contain(requirement =>
            requirement.RequirementId == "prompt-prompts-contract" &&
            requirement.Outcome == ClientProfileRequirementOutcome.Warning);
    }

    [Fact]
    public void Evaluate_ClaudeCode_ShouldWarnWhenInitializeInstructionsAreMissing()
    {
        var result = CreateToolOnlyValidationResult();
        result.InitializationHandshake = new TransportResult<InitializeResult>
        {
            IsSuccessful = true,
            Payload = new InitializeResult
            {
                ProtocolVersion = "2025-11-25",
                ServerInfo = new Implementation { Name = "Docs MCP", Version = "1.0.0" },
                Capabilities = new ModelContextProtocol.Protocol.ServerCapabilities()
            }
        };

        var compatibility = _evaluator.Evaluate(result, new ClientProfileOptions
        {
            Profiles = new List<string> { "claude-code" }
        });

        compatibility.Should().NotBeNull();
        var assessment = compatibility!.Assessments.Should().ContainSingle().Subject;
        assessment.Status.Should().Be(ClientProfileCompatibilityStatus.CompatibleWithWarnings);
        assessment.Requirements.Should().Contain(requirement =>
            requirement.RequirementId == "initialize-server-instructions" &&
            requirement.Outcome == ClientProfileRequirementOutcome.Warning &&
            requirement.EvidenceBasis == ClientProfileEvidenceBasis.Documented &&
            requirement.DocumentationUrl == "https://code.claude.com/docs/en/mcp#scale-with-mcp-tool-search");
    }

    [Fact]
    public void Evaluate_ClaudeCode_ShouldRecognizeAnthropicMaxResultSizeAnnotation()
    {
        var result = CreateToolOnlyValidationResult();
        result.ToolValidation!.ToolResults[0].AnthropicMaxResultSizeChars = 200000;

        var compatibility = _evaluator.Evaluate(result, new ClientProfileOptions
        {
            Profiles = new List<string> { "claude-code" }
        });

        compatibility.Should().NotBeNull();
        var assessment = compatibility!.Assessments.Should().ContainSingle().Subject;
        assessment.Requirements.Should().Contain(requirement =>
            requirement.RequirementId == "tool-output-budget" &&
            requirement.Outcome == ClientProfileRequirementOutcome.Satisfied &&
            requirement.DocumentationUrl == "https://code.claude.com/docs/en/mcp#raise-the-limit-for-a-specific-tool");
    }

    [Fact]
    public void Evaluate_ClaudeCode_ShouldRecognizeListChangedSupportAcrossSurfaces()
    {
        var result = CreateToolOnlyValidationResult();
        result.PromptTesting = new PromptTestResult
        {
            Status = TestStatus.Passed,
            PromptsDiscovered = 1,
            PromptResults = new List<IndividualPromptResult>
            {
                new() { PromptName = "triage_issue", Status = TestStatus.Passed, ExecutionSuccessful = true }
            }
        };
        result.ResourceTesting = new ResourceTestResult
        {
            Status = TestStatus.Passed,
            ResourcesDiscovered = 1,
            ResourceResults = new List<IndividualResourceResult>
            {
                new() { ResourceUri = "file:///repo/README.md", ResourceName = "README", Status = TestStatus.Passed }
            }
        };
        result.InitializationHandshake = new TransportResult<InitializeResult>
        {
            IsSuccessful = true,
            Payload = new InitializeResult
            {
                ProtocolVersion = "2025-11-25",
                ServerInfo = new Implementation { Name = "Docs MCP", Version = "1.0.0" },
                Capabilities = new ModelContextProtocol.Protocol.ServerCapabilities
                {
                    Tools = new ToolsCapability { ListChanged = true },
                    Prompts = new PromptsCapability { ListChanged = true },
                    Resources = new ResourcesCapability { ListChanged = true }
                }
            }
        };

        var compatibility = _evaluator.Evaluate(result, new ClientProfileOptions
        {
            Profiles = new List<string> { "claude-code" }
        });

        compatibility.Should().NotBeNull();
        var assessment = compatibility!.Assessments.Should().ContainSingle().Subject;
        assessment.Requirements.Should().Contain(requirement =>
            requirement.RequirementId == "list-changed" &&
            requirement.Outcome == ClientProfileRequirementOutcome.Satisfied &&
            requirement.DocumentationUrl == "https://code.claude.com/docs/en/mcp#dynamic-tool-updates");
    }

    [Fact]
    public void Evaluate_GitHubCopilotCloudAgent_ShouldFailWhenRemoteOAuthIsObserved()
    {
        var result = CreateToolOnlyValidationResult();
        result.ToolValidation!.AuthenticationSecurity = new AuthenticationSecurityResult
        {
            AuthMetadata = new AuthMetadata
            {
                Resource = "https://example.test/mcp",
                AuthorizationServers = new List<string> { "https://login.example.test" }
            },
            WwwAuthenticateHeader = "Bearer realm=\"example\""
        };

        var compatibility = _evaluator.Evaluate(result, new ClientProfileOptions
        {
            Profiles = new List<string> { "github-copilot-cloud-agent" }
        });

        compatibility.Should().NotBeNull();
        var assessment = compatibility!.Assessments.Should().ContainSingle().Subject;
        assessment.Status.Should().Be(ClientProfileCompatibilityStatus.Incompatible);
        assessment.Requirements.Should().Contain(requirement =>
            requirement.RequirementId == "transport-remote-auth" &&
            requirement.Outcome == ClientProfileRequirementOutcome.Failed &&
            requirement.DocumentationUrl == "https://docs.github.com/en/copilot/how-tos/use-copilot-agents/cloud-agent/extend-cloud-agent-with-mcp");
    }

    [Fact]
    public void Evaluate_GitHubCopilotCloudAgent_ShouldNotFlagBearerPatAsOAuth()
    {
        // GitHub Copilot Cloud Agent docs explicitly say the BUILT-IN GitHub MCP works.
        // That server uses static PAT bearer headers, which trigger a `Bearer realm=...`
        // 401 challenge but are NOT OAuth. The remote-auth gate must not fire on bare
        // bearer challenges — only on real OAuth-flow signals (RFC 9728 metadata,
        // resource_metadata/authorization_uri parameters, interactive login).
        var result = CreateToolOnlyValidationResult();
        result.ToolValidation!.AuthenticationSecurity = new AuthenticationSecurityResult
        {
            // No AuthMetadata, no interactive flow, just a generic Bearer realm challenge.
            WwwAuthenticateHeader = "Bearer realm=\"github\""
        };

        var compatibility = _evaluator.Evaluate(result, new ClientProfileOptions
        {
            Profiles = new List<string> { "github-copilot-cloud-agent" }
        });

        var assessment = compatibility!.Assessments.Should().ContainSingle().Subject;
        assessment.Requirements.Should().Contain(requirement =>
            requirement.RequirementId == "transport-remote-auth" &&
            requirement.Outcome == ClientProfileRequirementOutcome.Satisfied);
    }

    [Fact]
    public void Evaluate_GitHubCopilotCloudAgent_ShouldFlagOAuthDiscoveryHeaderAsOAuth()
    {
        // RFC 9728 §5.1 says OAuth-aware resource servers add resource_metadata=URL to
        // their WWW-Authenticate challenge. That IS an OAuth-flow signal even when the
        // protected resource metadata fetch hasn't been completed.
        var result = CreateToolOnlyValidationResult();
        result.ToolValidation!.AuthenticationSecurity = new AuthenticationSecurityResult
        {
            WwwAuthenticateHeader =
                "Bearer realm=\"example\", resource_metadata=\"https://example.test/.well-known/oauth-protected-resource\""
        };

        var compatibility = _evaluator.Evaluate(result, new ClientProfileOptions
        {
            Profiles = new List<string> { "github-copilot-cloud-agent" }
        });

        var assessment = compatibility!.Assessments.Should().ContainSingle().Subject;
        assessment.Requirements.Should().Contain(requirement =>
            requirement.RequirementId == "transport-remote-auth" &&
            requirement.Outcome == ClientProfileRequirementOutcome.Failed);
    }

    [Fact]
    public void Evaluate_ClaudeCode_ShouldWarnWhenToolDescriptionExceeds2KB()
    {
        // Claude Code docs: "truncates tool descriptions and server instructions at 2KB each".
        var result = CreateToolOnlyValidationResult();
        result.ToolValidation!.ToolResults[0].Description = new string('x', 3000);

        var compatibility = _evaluator.Evaluate(result, new ClientProfileOptions
        {
            Profiles = new List<string> { "claude-code" }
        });

        var assessment = compatibility!.Assessments.Should().ContainSingle().Subject;
        assessment.Requirements.Should().Contain(requirement =>
            requirement.RequirementId == "tool-description-size" &&
            requirement.Outcome == ClientProfileRequirementOutcome.Warning &&
            requirement.Summary.Contains("2KB", StringComparison.Ordinal));
    }

    [Fact]
    public void Evaluate_ClaudeCode_ShouldPassWhenDescriptionsAreUnder2KB()
    {
        var result = CreateToolOnlyValidationResult();
        result.ToolValidation!.ToolResults[0].Description = new string('x', 1024);

        var compatibility = _evaluator.Evaluate(result, new ClientProfileOptions
        {
            Profiles = new List<string> { "claude-code" }
        });

        var assessment = compatibility!.Assessments.Should().ContainSingle().Subject;
        assessment.Requirements.Should().Contain(requirement =>
            requirement.RequirementId == "tool-description-size" &&
            requirement.Outcome == ClientProfileRequirementOutcome.Satisfied);
    }

    [Fact]
    public void Evaluate_ClaudeCode_ShouldWarnWhenToolDescriptionMissing()
    {
        var result = CreateToolOnlyValidationResult();
        result.ToolValidation!.ToolResults[0].Description = null;

        var compatibility = _evaluator.Evaluate(result, new ClientProfileOptions
        {
            Profiles = new List<string> { "claude-code" }
        });

        var assessment = compatibility!.Assessments.Should().ContainSingle().Subject;
        assessment.Requirements.Should().Contain(requirement =>
            requirement.RequirementId == "tool-tool-descriptions" &&
            requirement.Outcome == ClientProfileRequirementOutcome.Warning);
    }

    [Fact]
    public void Evaluate_ClaudeCode_ShouldWarnWhenToolHasNoAnnotations()
    {
        var result = CreateToolOnlyValidationResult();
        var t = result.ToolValidation!.ToolResults[0];
        t.ReadOnlyHint = null;
        t.DestructiveHint = null;
        t.IdempotentHint = null;
        t.OpenWorldHint = null;

        var compatibility = _evaluator.Evaluate(result, new ClientProfileOptions
        {
            Profiles = new List<string> { "claude-code" }
        });

        var assessment = compatibility!.Assessments.Should().ContainSingle().Subject;
        assessment.Requirements.Should().Contain(requirement =>
            requirement.RequirementId == "tool-tool-annotations" &&
            requirement.Outcome == ClientProfileRequirementOutcome.Warning);
    }

    [Fact]
    public void Evaluate_GitHubCopilotCloudAgent_ShouldFailWhenWriteToolMissesDestructiveHint()
    {
        // Cloud Agent calls tools autonomously without per-call approval; missing
        // destructiveHint on a write-capable tool is a Required-tier failure here.
        var result = CreateToolOnlyValidationResult();
        var t = result.ToolValidation!.ToolResults[0];
        t.ToolName = "delete_branch";
        t.ReadOnlyHint = false;
        t.DestructiveHint = null; // missing

        var compatibility = _evaluator.Evaluate(result, new ClientProfileOptions
        {
            Profiles = new List<string> { "github-copilot-cloud-agent" }
        });

        var assessment = compatibility!.Assessments.Should().ContainSingle().Subject;
        assessment.Status.Should().Be(ClientProfileCompatibilityStatus.Incompatible);
        assessment.Requirements.Should().Contain(requirement =>
            requirement.RequirementId == "tool-destructive-hint" &&
            requirement.Outcome == ClientProfileRequirementOutcome.Failed);
    }

    [Fact]
    public void Evaluate_VisualStudioCopilot_ShouldWarnWhenToolMissesDisplayTitle()
    {
        var result = CreateToolOnlyValidationResult();
        result.ToolValidation!.ToolResults[0].DisplayTitle = null;

        var compatibility = _evaluator.Evaluate(result, new ClientProfileOptions
        {
            Profiles = new List<string> { "visual-studio-copilot" }
        });

        var assessment = compatibility!.Assessments.Should().ContainSingle().Subject;
        assessment.Requirements.Should().Contain(requirement =>
            requirement.RequirementId == "tool-tool-titles" &&
            requirement.Outcome == ClientProfileRequirementOutcome.Warning);
    }

    [Fact]
    public void Evaluate_ClaudeCode_RemoteOAuthDiscovery_ShouldBeSatisfiedWhenAuthMetadataPresent()
    {
        var result = CreateToolOnlyValidationResult();
        result.ToolValidation!.AuthenticationSecurity = new AuthenticationSecurityResult
        {
            AuthenticationRequired = true,
            AuthMetadata = new AuthMetadata
            {
                Resource = "https://example.test/mcp",
                AuthorizationServers = new List<string> { "https://login.example.test" }
            }
        };

        var compatibility = _evaluator.Evaluate(result, new ClientProfileOptions
        {
            Profiles = new List<string> { "claude-code" }
        });

        var assessment = compatibility!.Assessments.Should().ContainSingle().Subject;
        assessment.Requirements.Should().Contain(requirement =>
            requirement.RequirementId == "transport-oauth-discovery" &&
            requirement.Outcome == ClientProfileRequirementOutcome.Satisfied);
    }

    [Fact]
    public void Evaluate_VisualStudioCopilot_ShouldSurfaceSamplingSupportWhenDetected()
    {
        var result = CreateToolOnlyValidationResult();
        result.ProtocolCompliance = new ComplianceTestResult
        {
            Findings = new List<ValidationFinding>
            {
                new()
                {
                    RuleId = ValidationFindingRuleIds.OptionalCapabilitySamplingSupported,
                    Severity = ValidationFindingSeverity.Info,
                    Summary = "Server supports the optional sampling capability."
                }
            }
        };

        var compatibility = _evaluator.Evaluate(result, new ClientProfileOptions
        {
            Profiles = new List<string> { "visual-studio-copilot" }
        });

        compatibility.Should().NotBeNull();
        var assessment = compatibility!.Assessments.Should().ContainSingle().Subject;
        assessment.Requirements.Should().Contain(requirement =>
            requirement.RequirementId == "sampling-supported" &&
            requirement.Outcome == ClientProfileRequirementOutcome.Satisfied &&
            requirement.RuleIds.Contains(ValidationFindingRuleIds.OptionalCapabilitySamplingSupported));
    }

    [Fact]
    public void Evaluate_VisualStudioCopilot_ShouldRecognizeToolListChangedSupport()
    {
        var result = CreateToolOnlyValidationResult();
        result.InitializationHandshake = new TransportResult<InitializeResult>
        {
            IsSuccessful = true,
            Payload = new InitializeResult
            {
                ProtocolVersion = "2025-11-25",
                ServerInfo = new Implementation { Name = "Docs MCP", Version = "1.0.0" },
                Capabilities = new ModelContextProtocol.Protocol.ServerCapabilities
                {
                    Tools = new ToolsCapability { ListChanged = true }
                }
            }
        };

        var compatibility = _evaluator.Evaluate(result, new ClientProfileOptions
        {
            Profiles = new List<string> { "visual-studio-copilot" }
        });

        compatibility.Should().NotBeNull();
        var assessment = compatibility!.Assessments.Should().ContainSingle().Subject;
        assessment.Requirements.Should().Contain(requirement =>
            requirement.RequirementId == "list-changed" &&
            requirement.Outcome == ClientProfileRequirementOutcome.Satisfied &&
            requirement.DocumentationUrl == "https://learn.microsoft.com/en-us/visualstudio/ide/mcp-servers?view=vs-2022#tool-lifecycle");
    }

    [Fact]
    public void Evaluate_UnknownProfile_ShouldThrowArgumentException()
    {
        var action = () => _evaluator.Evaluate(CreateToolOnlyValidationResult(), new ClientProfileOptions
        {
            Profiles = new List<string> { "unknown-profile" }
        });

        action.Should().Throw<ArgumentException>()
            .WithMessage("*Supported profiles*");
    }

    private static ValidationResult CreateToolOnlyValidationResult()
    {
        var result = new ValidationResult
        {
            OverallStatus = ValidationStatus.Passed,
            ServerConfig = new McpServerConfig { Endpoint = "https://example.test/mcp", Transport = "http" },
            ToolValidation = new ToolTestResult
            {
                Status = TestStatus.Passed,
                ToolsDiscovered = 1,
                ToolResults = new List<IndividualToolResult>
                {
                    new()
                    {
                        ToolName = "search_docs",
                        Status = TestStatus.Passed,
                        DiscoveredCorrectly = true,
                        MetadataValid = true,
                        ExecutionSuccessful = true,
                        DisplayTitle = "Search Docs",
                        Description = "Search the documentation index for matching topics.",
                        ReadOnlyHint = true,
                        DestructiveHint = false,
                        OpenWorldHint = true,
                        IdempotentHint = true
                    }
                }
            }
        };
        // Provide a minimal handshake so server-identity / initialize-aware checks find evidence.
        result.InitializationHandshake = new TransportResult<InitializeResult>
        {
            IsSuccessful = true,
            Payload = new InitializeResult
            {
                ProtocolVersion = "2025-11-25",
                ServerInfo = new Implementation { Name = "example-mcp", Version = "1.0.0" },
                Capabilities = new ModelContextProtocol.Protocol.ServerCapabilities()
            }
        };
        return result;
    }
}