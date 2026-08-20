using Mcp.Benchmark.Core.Models;
using Mcp.Benchmark.Core.Constants;
using FluentAssertions;
using Xunit;
using System.Text.Json;
using ModelContextProtocol.Protocol;
using Mcp.Benchmark.Core.Services;

namespace Mcp.Benchmark.Tests.Unit.Core;

/// <summary>
/// Tests for core model classes — constructors, computed properties, clone methods.
/// </summary>
public class CoreModelTests
{
    // ─── ValidationResult ────────────────────────────────────────────
    [Fact]
    public void ValidationResult_Duration_ShouldCalculateFromStartEnd()
    {
        var result = new ValidationResult { StartTime = DateTime.UtcNow.AddSeconds(-10), EndTime = DateTime.UtcNow };
        result.Duration.Should().NotBeNull();
        result.Duration!.Value.TotalSeconds.Should().BeApproximately(10, 1);
    }

    [Fact]
    public void ValidationResult_Duration_ShouldBeNullWithoutEndTime()
    {
        var result = new ValidationResult();
        result.Duration.Should().BeNull();
    }

    [Fact]
    public void ValidationResult_CloneWithoutSecrets_ShouldPreserveTrustAssessment()
    {
        var result = new ValidationResult
        {
            TrustAssessment = new McpTrustAssessment { TrustLevel = McpTrustLevel.L4_Trusted },
            ServerConfig = new McpServerConfig { Endpoint = "test", Authentication = new AuthenticationConfig { Token = "secret" } }
        };

        var clone = result.CloneWithoutSecrets();

        clone.TrustAssessment.Should().NotBeNull();
        clone.TrustAssessment!.TrustLevel.Should().Be(McpTrustLevel.L4_Trusted);
        clone.ServerConfig.Authentication!.Token.Should().NotBe("secret");
    }

    [Fact]
    public void ValidationResult_CloneWithoutSecrets_ShouldPreserveClientCompatibility()
    {
        var result = new ValidationResult
        {
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
                        Summary = "Required checks passed; 1 advisory requirement still needs follow-up."
                    }
                }
            },
            ServerConfig = new McpServerConfig
            {
                Endpoint = "test",
                Authentication = new AuthenticationConfig { Token = "secret" }
            }
        };

        var clone = result.CloneWithoutSecrets();

        clone.ClientCompatibility.Should().NotBeNull();
        clone.ClientCompatibility!.Assessments.Should().ContainSingle();
        clone.ClientCompatibility.Assessments[0].ProfileId.Should().Be("claude-code");
        clone.ServerConfig.Authentication!.Token.Should().NotBe("secret");
    }

    [Fact]
    public void ValidationResult_CloneWithoutSecrets_ShouldRemoveRawBootstrapAndDiscoveryData()
    {
        const string canary = "canonical-secret-canary";
        var result = new ValidationResult
        {
            InitializationHandshake = new TransportResult<InitializeResult>
            {
                IsSuccessful = true,
                Payload = new InitializeResult
                {
                    ProtocolVersion = "2026-07-28",
                    Capabilities = new ModelContextProtocol.Protocol.ServerCapabilities(),
                    ServerInfo = new Implementation { Name = "fixture", Version = "1.0.0" },
                    Instructions = canary
                },
                Transport = new TransportMetadata
                {
                    Headers = new Dictionary<string, string> { ["Set-Cookie"] = canary },
                    RawContent = canary
                }
            },
            ModernDiscovery = new TransportResult<ModernDiscoveryEvidence>
            {
                IsSuccessful = false,
                Error = canary,
                Payload = new ModernDiscoveryEvidence { IsValid = false, Instructions = canary, Errors = [canary] },
                Transport = new TransportMetadata { RawContent = canary }
            },
            CapabilitySnapshot = new TransportResult<CapabilitySummary>
            {
                Payload = new CapabilitySummary
                {
                    ToolListResponse = new Mcp.Benchmark.Core.Models.JsonRpcResponse
                    {
                        RawJson = canary,
                        Error = canary,
                        Headers = new Dictionary<string, string> { ["Authorization"] = canary },
                        ProbeContext = new ProbeContext { Reason = canary, Metadata = new Dictionary<string, string> { ["raw"] = canary } }
                    }
                }
            },
            BootstrapHealth = new HealthCheckResult
            {
                ErrorMessage = canary,
                ServerMetadata = new Dictionary<string, object> { ["secret"] = canary },
                InitializationDetails = new TransportResult<InitializeResult>
                {
                    Error = canary,
                    Transport = new TransportMetadata { RawContent = canary }
                }
            },
            ExecutionLogs = [new ValidationLogEntry { Message = canary, Exception = canary, Context = new Dictionary<string, object> { ["secret"] = canary } }],
            CriticalErrors = [canary],
            SecurityTesting = new SecurityTestResult
            {
                Vulnerabilities = [new SecurityVulnerability { ProofOfConcept = canary }],
                InputValidationResults = [new InputValidationResult { TestPayload = canary, ActualResponse = canary }],
                AttackSimulations = [new AttackSimulationResult { ServerResponse = canary, Evidence = new Dictionary<string, object> { ["secret"] = canary } }]
            },
            ProtocolCompliance = new ComplianceTestResult
            {
                StreamableHttpTransport = new StreamableHttpTransportTestResult
                {
                    Probes =
                    [
                        new StreamableHttpTransportProbeResult
                        {
                            ProbeId = "http-preview",
                            CheckId = "HTTP.PREVIEW",
                            Requirement = "No raw payload persistence",
                            Expected = "Redacted",
                            Actual = "Observed",
                            BodyPreview = canary
                        }
                    ]
                },
                StdioTransport = new StdioTransportTestResult
                {
                    Probes =
                    [
                        new StdioTransportProbeResult
                        {
                            ProbeId = "stdio-preview",
                            CheckId = "STDIO.PREVIEW",
                            Requirement = "No raw payload persistence",
                            Expected = "Redacted",
                            Actual = "Observed",
                            StdoutPreview = canary,
                            StderrPreview = canary
                        }
                    ]
                }
            },
            Evidence = new ValidationEvidenceDocument
            {
                Observations = [new ValidationObservation
                {
                    Id = "hostile-observation",
                    LayerId = "security",
                    Component = "target",
                    ObservationKind = "response",
                    RedactedPayloadPreview = canary
                }]
            }
        };

        var serialized = JsonSerializer.Serialize(result.CloneWithoutSecrets());

        serialized.Should().NotContain(canary);
        serialized.Should().NotContain("Set-Cookie");
        serialized.Should().NotContain("Authorization");
    }

    [Fact]
    public void SecretFreeClones_ShouldNotShareMutableConfigurationOrEvidence()
    {
        var configuration = new McpValidatorConfiguration();
        configuration.Reporting.SpecProfile = "original";
        var configurationClone = configuration.CloneWithoutSecrets();
        configurationClone.Reporting.SpecProfile = "changed";

        var result = new ValidationResult();
        result.Evidence.Coverage.Add(ValidationCoverageFactory.FromOutcome("layer", "scope", ValidationOutcome.Succeeded));
        var resultClone = result.CloneWithoutSecrets();
        resultClone.Evidence.Coverage.Clear();

        configuration.Reporting.SpecProfile.Should().Be("original");
        result.Evidence.Coverage.Should().ContainSingle();
    }

    [Fact]
    public void ServerClone_ShouldRedactEntireStdioCommand()
    {
        var server = new McpServerConfig { Transport = "stdio", Endpoint = "node server.js --token secret-canary" };

        server.CloneWithoutSecrets().Endpoint.Should().Be("__STDIO_COMMAND_REDACTED__");
    }

    [Fact]
    public void McpValidatorConfiguration_CloneWithoutSecrets_ShouldPreserveClientProfiles()
    {
        var configuration = new McpValidatorConfiguration
        {
            ClientProfiles = new ClientProfileOptions
            {
                Profiles = new List<string> { "claude-code", "github-copilot-cli" }
            },
            Server = new McpServerConfig
            {
                Endpoint = "https://test.com",
                Authentication = new AuthenticationConfig { Token = "secret" }
            }
        };

        var clone = configuration.CloneWithoutSecrets();

        clone.ClientProfiles.Should().NotBeNull();
        clone.ClientProfiles!.Profiles.Should().Equal("claude-code", "github-copilot-cli");
        clone.Server.Authentication!.Token.Should().NotBe("secret");
    }

    [Fact]
    public void McpValidatorConfiguration_CloneWithoutSecrets_ShouldPreserveExecutionAndEvaluationPolicies()
    {
        var configuration = new McpValidatorConfiguration
        {
            Execution = new ExecutionPolicy
            {
                Mode = ExecutionMode.Elevated,
                DryRun = true,
                AllowedHosts = new List<string> { "example.test" },
                PersistenceMode = PersistenceMode.Session,
                RedactLevel = RedactionLevel.Standard,
                TraceMode = TraceMode.Redacted,
                ConfirmElevatedRisk = true
            },
            Evaluation = new EvaluationPolicy
            {
                ModelEvaluation = new ModelEvaluationPolicy
                {
                    Enabled = true,
                    Provider = "github-models",
                    Model = "gpt-4.1",
                    PromptSet = "baseline-v1"
                }
            }
        };

        var clone = configuration.CloneWithoutSecrets();
        clone.Execution.Should().NotBeNull();
        clone.Evaluation.Should().NotBeNull();
        var execution = clone.Execution!;
        var modelEvaluation = clone.Evaluation!.ModelEvaluation;

        execution.Mode.Should().Be(ExecutionMode.Elevated);
        execution.DryRun.Should().BeTrue();
        execution.AllowedHosts.Should().ContainSingle().Which.Should().Be("example.test");
        execution.PersistenceMode.Should().Be(PersistenceMode.Session);
        execution.RedactLevel.Should().Be(RedactionLevel.Standard);
        execution.TraceMode.Should().Be(TraceMode.Redacted);
        execution.ConfirmElevatedRisk.Should().BeTrue();
        modelEvaluation.Enabled.Should().BeTrue();
        modelEvaluation.Provider.Should().Be("github-models");
        modelEvaluation.Model.Should().Be("gpt-4.1");
        modelEvaluation.PromptSet.Should().Be("baseline-v1");
    }

    [Fact]
    public void ValidationRunRequest_Capture_ShouldIsolateNestedConfigurationAndPolicyState()
    {
        var configuration = new McpValidatorConfiguration
        {
            Server = new McpServerConfig
            {
                Endpoint = "https://example.test/mcp",
                Authentication = new AuthenticationConfig { Token = "test-token" }
            },
            Execution = new ExecutionPolicy
            {
                AllowedHosts = ["example.test"],
                MaxRequests = 7,
                MaxResponseBytes = 4096
            }
        };

        var request = ValidationRunRequest.Capture(configuration);
        configuration.Server.Endpoint = "https://changed.test/mcp";
        configuration.Server.Authentication!.Token = "changed-token";
        configuration.Execution!.AllowedHosts.Clear();
        configuration.Execution.MaxRequests = 99;

        var captured = request.CreateConfiguration();

        request.Target.Should().Be("https://example.test/mcp");
        request.OperationPolicy.AllowedHosts.Should().ContainSingle("example.test");
        request.OperationPolicy.MaxRequests.Should().Be(7);
        request.OperationPolicy.MaxResponseBytes.Should().Be(4096);
        captured.Server.Endpoint.Should().Be("https://example.test/mcp");
        captured.Server.Authentication!.Token.Should().Be("test-token");
        captured.Execution!.AllowedHosts.Should().ContainSingle("example.test");
    }

    // ─── McpServerConfig ──────────────────────────────────────────
    [Fact]
    public void McpServerConfig_CloneWithoutSecrets_ShouldRedactAuthHeaders()
    {
        var config = new McpServerConfig
        {
            Endpoint = "https://test.com",
            Headers = new Dictionary<string, string>
            {
                { "Authorization", "Bearer secret123" },
                { "X-Custom", "visible" }
            }
        };

        var clone = config.CloneWithoutSecrets();

        clone.Headers["Authorization"].Should().Be("__HEADER-REDACTED__");
        clone.Headers["X-Custom"].Should().Be("visible");
    }

    [Fact]
    public void McpServerConfig_CloneWithoutSecrets_ShouldRemoveUrlCredentialsAndQuerySecrets()
    {
        var config = new McpServerConfig
        {
            Endpoint = "https://user:password@example.test/mcp?access_token=secret#fragment",
            Transport = "http"
        };

        var clone = config.CloneWithoutSecrets();

        clone.Endpoint.Should().Be("https://example.test/mcp");
        clone.Endpoint.Should().NotContainAny("user", "password", "access_token", "secret", "fragment");
    }

    [Fact]
    public void McpServerConfig_CloneWithoutSecrets_ShouldRedactSensitiveEnvironmentValues()
    {
        var config = new McpServerConfig
        {
            Environment = new Dictionary<string, string>
            {
                ["MCP_TOKEN"] = "secret-token",
                ["DATABASE_PASSWORD"] = "secret-password",
                ["LOG_LEVEL"] = "debug"
            }
        };

        var clone = config.CloneWithoutSecrets();

        clone.Environment["MCP_TOKEN"].Should().Be("__ENVIRONMENT-REDACTED__");
        clone.Environment["DATABASE_PASSWORD"].Should().Be("__ENVIRONMENT-REDACTED__");
        clone.Environment["LOG_LEVEL"].Should().Be("debug");
    }

    [Fact]
    public void McpServerConfig_CloneForExecution_ShouldPreserveEraAndIsolateNestedCollections()
    {
        var config = new McpServerConfig
        {
            ProtocolEra = McpProtocolEraSelection.Modern,
            Authentication = new AuthenticationConfig
            {
                Token = "runtime-token",
                Scopes = ["scope-a"],
                CustomHeaders = new Dictionary<string, string> { ["X-Test"] = "value" },
                ClientRegistration = new OAuthClientRegistrationConfig
                {
                    Mode = OAuthClientRegistrationMode.Static,
                    ClientId = "client-id",
                    RedirectUris = ["https://client.example/callback"]
                },
                ConformanceCredentials = new AuthenticationConformanceCredentials
                {
                    WrongAudienceResource = "https://other-resource.example/mcp",
                    WrongAudienceScopes = ["tools:read"],
                    PreviouslyGrantedScopes = ["tools:read"]
                }
            },
            Headers = new Dictionary<string, string> { ["X-Server"] = "value" },
            Environment = new Dictionary<string, string> { ["LOG_LEVEL"] = "debug" }
        };

        var clone = config.CloneForExecution();
        config.Authentication.Scopes[0] = "changed";
        config.Authentication.CustomHeaders["X-Test"] = "changed";
        config.Authentication.ClientRegistration!.RedirectUris[0] = "https://changed.example/callback";
        config.Authentication.ConformanceCredentials!.WrongAudienceResource = "https://changed.example/mcp";
        config.Authentication.ConformanceCredentials.WrongAudienceScopes[0] = "changed";
        config.Authentication.ConformanceCredentials.PreviouslyGrantedScopes[0] = "changed";
        config.Headers["X-Server"] = "changed";
        config.Environment["LOG_LEVEL"] = "changed";

        clone.ProtocolEra.Should().Be(McpProtocolEraSelection.Modern);
        clone.Authentication!.Token.Should().Be("runtime-token");
        clone.Authentication.Scopes.Should().Equal("scope-a");
        clone.Authentication.CustomHeaders["X-Test"].Should().Be("value");
        clone.Authentication.ClientRegistration!.RedirectUris.Should().Equal("https://client.example/callback");
        clone.Authentication.ConformanceCredentials!.WrongAudienceResource.Should().Be("https://other-resource.example/mcp");
        clone.Authentication.ConformanceCredentials.WrongAudienceScopes.Should().Equal("tools:read");
        clone.Authentication.ConformanceCredentials.PreviouslyGrantedScopes.Should().Equal("tools:read");
        clone.Headers["X-Server"].Should().Be("value");
        clone.Environment["LOG_LEVEL"].Should().Be("debug");
    }

    [Fact]
    public void AuthenticationConfig_CloneWithoutSecrets_ShouldRetainReferenceAndRedactLegacyToken()
    {
        var authentication = new AuthenticationConfig
        {
            Type = "bearer",
            Token = "legacy-secret",
            TokenRef = new SecretRef { Provider = SecretRefProviders.Environment, Name = "MCPVAL_TOKEN" }
        };

        var clone = authentication.CloneWithoutSecrets();

        clone.Token.Should().Be("__TOKEN-REDACTED__");
        clone.TokenRef.Should().NotBeSameAs(authentication.TokenRef);
        clone.TokenRef!.Provider.Should().Be(SecretRefProviders.Environment);
        clone.TokenRef.Name.Should().Be("MCPVAL_TOKEN");
    }

    // ─── McpTrustAssessment ──────────────────────────────────────
    [Theory]
    [InlineData(McpTrustLevel.L5_CertifiedSecure, "High Assurance")]
    [InlineData(McpTrustLevel.L4_Trusted, "Strong")]
    [InlineData(McpTrustLevel.L3_Acceptable, "Acceptable")]
    [InlineData(McpTrustLevel.L2_Caution, "Caution")]
    [InlineData(McpTrustLevel.L1_Untrusted, "Untrusted")]
    [InlineData(McpTrustLevel.Unknown, "Unknown")]
    public void McpTrustAssessment_TrustLabel_ShouldMatchLevel(McpTrustLevel level, string expectedContains)
    {
        new McpTrustAssessment { TrustLevel = level }.TrustLabel.Should().Contain(expectedContains);
    }

    // ─── LoadTestResult ──────────────────────────────────────────
    [Fact]
    public void LoadTestResult_ErrorRate_ShouldCalculateCorrectly()
    {
        var result = new LoadTestResult { TotalRequests = 100, FailedRequests = 5 };
        result.ErrorRate.Should().BeApproximately(5.0, 0.1);
    }

    [Fact]
    public void LoadTestResult_ErrorRate_ShouldBeZeroWithNoRequests()
    {
        var result = new LoadTestResult { TotalRequests = 0, FailedRequests = 0 };
        result.ErrorRate.Should().Be(0);
    }

    [Fact]
    public void LoadTestResult_NonRateLimitedFailedRequests_ShouldExcludeRateLimitedRequests()
    {
        var result = new LoadTestResult { FailedRequests = 5, RateLimitedRequests = 2 };

        result.NonRateLimitedFailedRequests.Should().Be(3);
    }

    // ─── ScoringConstants ────────────────────────────────────────
    [Fact]
    public void ScoringConstants_WeightsShouldSumTo1()
    {
        var sum = ScoringConstants.WeightProtocol + ScoringConstants.WeightSecurity +
                  ScoringConstants.WeightTools + ScoringConstants.WeightResources +
                  ScoringConstants.WeightPrompts + ScoringConstants.WeightErrorHandling +
                  ScoringConstants.WeightPerformance;
        sum.Should().Be(1.0);
    }

    [Fact]
    public void ScoringConstants_ThresholdsShouldBeOrdered()
    {
        ScoringConstants.TrustL5Threshold.Should().BeGreaterThan(ScoringConstants.TrustL4Threshold);
        ScoringConstants.TrustL4Threshold.Should().BeGreaterThan(ScoringConstants.TrustL3Threshold);
        ScoringConstants.TrustL3Threshold.Should().BeGreaterThan(ScoringConstants.TrustL2Threshold);
    }

    [Fact]
    public void ScoringConstants_VulnPenalties_ShouldDescendBySeverity()
    {
        ScoringConstants.VulnPenaltyCritical.Should().BeGreaterThan(ScoringConstants.VulnPenaltyHigh);
        ScoringConstants.VulnPenaltyHigh.Should().BeGreaterThan(ScoringConstants.VulnPenaltyMedium);
        ScoringConstants.VulnPenaltyMedium.Should().BeGreaterThan(ScoringConstants.VulnPenaltyLow);
        ScoringConstants.VulnPenaltyLow.Should().BeGreaterThan(ScoringConstants.VulnPenaltyInfo);
    }

    // ─── McpComplianceTiers ──────────────────────────────────────
    [Fact]
    public void McpComplianceTiers_MustConstants_ShouldStartWithMUST()
    {
        McpComplianceTiers.Must.InitializeResponse.Should().StartWith("MUST:");
        McpComplianceTiers.Must.ServerInfoPresent.Should().StartWith("MUST:");
        McpComplianceTiers.Must.ToolHasName.Should().StartWith("MUST:");
        McpComplianceTiers.Must.StandardErrorCodes.Should().StartWith("MUST:");
    }

    [Fact]
    public void McpComplianceTiers_ShouldConstants_ShouldStartWithSHOULD()
    {
        McpComplianceTiers.Should.ToolHasDescription.Should().StartWith("SHOULD:");
        McpComplianceTiers.Should.SanitizeToolOutputs.Should().StartWith("SHOULD:");
    }

    [Fact]
    public void McpComplianceTiers_MayConstants_ShouldStartWithMAY()
    {
        McpComplianceTiers.May.InstructionsField.Should().StartWith("MAY:");
        McpComplianceTiers.May.ToolAnnotations.Should().StartWith("MAY:");
        McpComplianceTiers.May.Logging.Should().StartWith("MAY:");
    }

    // ─── ComplianceTierCheck ─────────────────────────────────────
    [Fact]
    public void ComplianceTierCheck_ShouldHaveDefaultValues()
    {
        var check = new ComplianceTierCheck();
        check.Tier.Should().Be("MUST");
        check.Passed.Should().BeFalse();
        check.Component.Should().BeEmpty();
    }

    // ─── AiBoundaryFinding ───────────────────────────────────────
    [Fact]
    public void AiBoundaryFinding_ShouldHaveDefaultSeverity()
    {
        var finding = new AiBoundaryFinding();
        finding.Severity.Should().Be("Medium");
        finding.Category.Should().BeEmpty();
    }
}
