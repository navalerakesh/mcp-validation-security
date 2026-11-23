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
    public void ValidationResult_CloneWithoutSecrets_ShouldPreserveAssurance()
    {
        var result = new ValidationResult
        {
            Assurance = new AssuranceEvaluation
            {
                Profile = AssuranceProfileCatalog.Ci,
                CertificateEligibility = new CertificateEligibilityAssessment
                {
                    ProfileId = AssuranceProfileCatalog.Ci.ProfileId,
                    Status = CertificateEligibilityStatus.Eligible
                }
            },
            ServerConfig = new McpServerConfig { Endpoint = "test", Authentication = new AuthenticationConfig { Token = "secret" } }
        };

        var clone = result.CloneWithoutSecrets();

        clone.Assurance.Profile.ProfileId.Should().Be(AssuranceProfileCatalog.Ci.ProfileId);
        clone.Assurance.CertificateEligibility.Status.Should().Be(CertificateEligibilityStatus.Eligible);
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
    public void ScenarioArguments_ShouldRemainPrivateRuntimeInputsAcrossConfigurationClones()
    {
        const string canary = "private-scenario-argument-canary";
        var configuration = new McpValidatorConfiguration();
        configuration.Validation.Categories.ToolTesting.CustomScenarios.Add(new ToolTestScenario
        {
            ToolName = "owned-tool", Parameters = new() { ["text"] = canary, ["nested"] = new Dictionary<string, object> { ["value"] = canary } }
        });
        var request = ValidationRunRequest.Capture(configuration);
        configuration.Validation.Categories.ToolTesting.CustomScenarios[0].ExpectedResult = canary;

        var redacted = configuration.CloneWithoutSecrets();
        var deterministic = configuration.CloneForDeterministicResult();
        var result = new ValidationResult { ValidationConfig = configuration }.CloneWithoutSecrets();

        JsonSerializer.Serialize(redacted).Should().NotContain(canary);
        JsonSerializer.Serialize(deterministic).Should().NotContain(canary);
        JsonSerializer.Serialize(result).Should().NotContain(canary);
        redacted.Validation.Categories.ToolTesting.CustomScenarios.Should().ContainSingle().Which.ToolName.Should().Be("owned-tool");
        redacted.Validation.Categories.ToolTesting.CustomScenarios[0].Parameters.Should().BeNull();
        JsonSerializer.Serialize(configuration).Should().Contain(canary);
        JsonSerializer.Serialize(request.CreateConfiguration()).Should().Contain(canary);
        JsonSerializer.Serialize(request).Should().NotContain(canary);
        var replayRedacted = () => ValidationRunRequest.Capture(redacted);
        replayRedacted.Should().Throw<ArgumentException>();
    }

    [Theory]
    [InlineData("valid", true)]
    [InlineData("duplicate", false)]
    [InlineData("unavailable", false)]
    [InlineData("pattern", false)]
    [InlineData("expected-error", false)]
    [InlineData("oversized", false)]
    [InlineData("many-values", false)]
    [InlineData("non-json", false)]
    public void PositiveToolScenarioCapture_ShouldBeBoundedAndIsolated(string variation, bool valid)
    {
        var parameters = new Dictionary<string, object> { ["value"] = "original" };
        var scenario = new ToolTestScenario { ToolName = "owned-tool", Parameters = parameters };
        var config = new ToolTestingConfig { CustomScenarios = [scenario] };
        if (variation == "duplicate") config.CustomScenarios.Add(scenario);
        if (variation == "unavailable") scenario.Parameters = null;
        if (variation == "pattern") scenario.ExpectedResult = "";
        if (variation == "expected-error") scenario.ExpectError = true;
        if (variation == "oversized") parameters["value"] = new string('a', Mcp.Benchmark.Core.Services.ToolScenarioInputs.MaximumArgumentBytes + 1);
        if (variation == "many-values") parameters["value"] = Enumerable.Range(0, Mcp.Benchmark.Core.Services.ToolScenarioInputs.MaximumArgumentValues).ToArray();
        if (variation == "non-json") parameters["value"] = new Version(1, 2);
        var capture = () => Mcp.Benchmark.Core.Services.ToolScenarioInputs.CapturePositiveScenarios(config);
        if (!valid) capture.Should().Throw<ArgumentException>();
        else
        {
            var captured = capture();
            parameters["value"] = "changed";
            captured.Should().ContainSingle();
            captured["owned-tool"].GetProperty("value").GetString().Should().Be("original");
        }
    }

    [Fact]
    public void PositiveToolScenarioCapture_ShouldBoundAllocationBeforeMaterializingLargeJsonStrings()
    {
        var config = new ToolTestingConfig { CustomScenarios = [new ToolTestScenario { ToolName = "owned", Parameters = new() { ["value"] = "warmup" } }] };
        Mcp.Benchmark.Core.Services.ToolScenarioInputs.CapturePositiveScenarios(config);
        using var document = JsonDocument.Parse(JsonSerializer.Serialize(new string('a', 4 * 1024 * 1024)));
        config.CustomScenarios[0].Parameters!["value"] = document.RootElement;
        var before = GC.GetAllocatedBytesForCurrentThread();
        var capture = () => Mcp.Benchmark.Core.Services.ToolScenarioInputs.CapturePositiveScenarios(config);
        capture.Should().Throw<ArgumentException>();
        (GC.GetAllocatedBytesForCurrentThread() - before).Should().BeLessThan(1024 * 1024);
    }

    [Fact]
    public void PositiveToolScenarioCapture_ShouldAcceptAnExactByteLimitObject()
    {
        var value = new string('a', Mcp.Benchmark.Core.Services.ToolScenarioInputs.MaximumArgumentBytes - 12);
        var config = new ToolTestingConfig { CustomScenarios = [new ToolTestScenario { ToolName = "owned", Parameters = new() { ["value"] = value } }] };
        JsonSerializer.SerializeToUtf8Bytes(config.CustomScenarios[0].Parameters).Length.Should().Be(Mcp.Benchmark.Core.Services.ToolScenarioInputs.MaximumArgumentBytes);
        Mcp.Benchmark.Core.Services.ToolScenarioInputs.CapturePositiveScenarios(config)["owned"].GetProperty("value").GetString().Should().Be(value);
    }

    [Fact]
    public void PositiveToolScenarioCapture_ShouldRejectEscapedByteOverflowBeforeReturningArguments()
    {
        var config = new ToolTestingConfig { CustomScenarios = [new ToolTestScenario
        { ToolName = "owned", Parameters = new() { ["value"] = new string('\u0001', 16 * 1024) } }] };
        JsonSerializer.SerializeToUtf8Bytes(config.CustomScenarios[0].Parameters).Length
            .Should().BeGreaterThan(Mcp.Benchmark.Core.Services.ToolScenarioInputs.MaximumArgumentBytes);
        var capture = () => Mcp.Benchmark.Core.Services.ToolScenarioInputs.CapturePositiveScenarios(config);
        capture.Should().Throw<ArgumentException>().Which.Message.Should().NotBeNullOrWhiteSpace();
    }

    [Theory]
    [InlineData(62, true)]
    [InlineData(63, false)]
    public void PositiveToolScenarioCapture_ShouldCountValuesInsideParsedJsonArrays(int count, bool accepted)
    {
        using var document = JsonDocument.Parse(JsonSerializer.Serialize(Enumerable.Range(0, count)));
        var config = new ToolTestingConfig { CustomScenarios = [new ToolTestScenario
        { ToolName = "owned", Parameters = new() { ["value"] = document.RootElement } }] };
        var capture = () => Mcp.Benchmark.Core.Services.ToolScenarioInputs.CapturePositiveScenarios(config);
        if (accepted) capture.Should().NotThrow().Which["owned"].GetProperty("value").GetArrayLength().Should().Be(count);
        else capture.Should().Throw<ArgumentException>().Which.Message.Should().Contain("structural bounds");
    }

    [Theory]
    [InlineData(true, 15, true)]
    [InlineData(true, 16, false)]
    [InlineData(false, 15, true)]
    [InlineData(false, 16, false)]
    public void PositiveToolScenarioCapture_ShouldCountDepthInsideParsedJson(bool arrays, int levels, bool accepted)
    {
        var json = arrays ? new string('[', levels) + "0" + new string(']', levels)
            : string.Concat(Enumerable.Repeat("{\"nested\":", levels)) + "0" + new string('}', levels);
        using var document = JsonDocument.Parse(json);
        var config = new ToolTestingConfig { CustomScenarios = [new ToolTestScenario
        { ToolName = "owned", Parameters = new() { ["value"] = document.RootElement } }] };
        var capture = () => Mcp.Benchmark.Core.Services.ToolScenarioInputs.CapturePositiveScenarios(config);
        if (accepted) capture.Should().NotThrow();
        else capture.Should().Throw<ArgumentException>().Which.Message.Should().Contain("structural bounds");
    }

    [Theory]
    [InlineData("array")]
    [InlineData("list")]
    [InlineData("dictionary")]
    public void PositiveToolScenarioCapture_ShouldRejectNonJsonValuesInsideClrCollections(string collection)
    {
        object unsupported = new Version(1, 2);
        var nested = collection switch
        {
            "array" => (object)new object[] { unsupported },
            "list" => new List<object> { unsupported },
            _ => new Dictionary<string, object> { ["nested"] = unsupported }
        };
        var config = new ToolTestingConfig { CustomScenarios = [new ToolTestScenario
        { ToolName = "owned", Parameters = new() { ["value"] = nested } }] };
        var capture = () => Mcp.Benchmark.Core.Services.ToolScenarioInputs.CapturePositiveScenarios(config);
        capture.Should().Throw<ArgumentException>().Which.Message.Should().Contain("only bounded JSON values");
    }

    [Fact]
    public void PositiveToolScenarioCapture_ShouldPreserveEmptySelectionAndRejectNullConfiguration()
    {
        var config = new ToolTestingConfig { SpecificTools = ["ordinary-selection"] };
        Mcp.Benchmark.Core.Services.ToolScenarioInputs.CapturePositiveScenarios(config).Should().BeEmpty();
        var capture = () => Mcp.Benchmark.Core.Services.ToolScenarioInputs.CapturePositiveScenarios(null!);
        capture.Should().Throw<ArgumentNullException>();
    }

    public static IEnumerable<object[]> PositiveArgumentPrimitiveCases()
    {
        foreach (var value in new object?[] { null, true, (byte)1, (sbyte)-1, (short)-2, (ushort)2, 3, 3u, 4L, 4UL, 1.25m, 1.5d, 1.5f,
            "text", new object[] { "nested", 1 }, new List<object> { "nested", false }, new Dictionary<string, object> { ["nested"] = 1 } })
            yield return [value!];
    }

    [Theory]
    [MemberData(nameof(PositiveArgumentPrimitiveCases))]
    public void PositiveToolScenarioCapture_ShouldPreserveSupportedJsonValues(object? value)
    {
        var config = new ToolTestingConfig { CustomScenarios = [new ToolTestScenario { ToolName = "owned", Parameters = new() { ["value"] = value! } }] };
        var expected = JsonSerializer.Serialize(config.CustomScenarios[0].Parameters);
        var captured = Mcp.Benchmark.Core.Services.ToolScenarioInputs.CapturePositiveScenarios(config);
        captured["owned"].GetRawText().Should().Be(expected);
    }

    [Theory]
    [InlineData("count", false)]
    [InlineData("count", true)]
    [InlineData("values", false)]
    [InlineData("values", true)]
    [InlineData("depth", false)]
    [InlineData("depth", true)]
    [InlineData("name", false)]
    [InlineData("name", true)]
    public void PositiveToolScenarioCapture_ShouldEnforceExactStructuralBounds(string kind, bool exceeds)
    {
        var config = new ToolTestingConfig { CustomScenarios = [new ToolTestScenario { ToolName = "owned", Parameters = new() }] };
        var extra = exceeds ? 1 : 0;
        if (kind == "count") config.CustomScenarios = Enumerable.Range(0, Mcp.Benchmark.Core.Services.ToolScenarioInputs.MaximumScenarios + extra)
            .Select(index => new ToolTestScenario { ToolName = $"tool-{index}", Parameters = new() }).ToList();
        if (kind == "name") config.CustomScenarios[0].ToolName = new string('n', Mcp.Benchmark.Core.Services.ToolScenarioInputs.MaximumToolNameCharacters + extra);
        if (kind == "values")
            for (var index = 0; index < Mcp.Benchmark.Core.Services.ToolScenarioInputs.MaximumArgumentValues - 1 + extra; index++)
                config.CustomScenarios[0].Parameters![$"value-{index}"] = index;
        if (kind == "depth")
        {
            var cursor = config.CustomScenarios[0].Parameters!;
            for (var depth = 0; depth < Mcp.Benchmark.Core.Services.ToolScenarioInputs.MaximumArgumentDepth + extra; depth++)
            {
                var next = new Dictionary<string, object>();
                cursor["nested"] = next;
                cursor = next;
            }
        }
        var capture = () => Mcp.Benchmark.Core.Services.ToolScenarioInputs.CapturePositiveScenarios(config);
        if (exceeds) capture.Should().Throw<ArgumentException>().Which.Message.Should().NotBeNullOrWhiteSpace();
        else capture.Should().NotThrow();
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void PositiveToolScenarioCapture_ShouldEnforceAggregateBytes(bool exceeds)
    {
        var payload = new string('a', Mcp.Benchmark.Core.Services.ToolScenarioInputs.MaximumArgumentBytes - 12);
        var count = Mcp.Benchmark.Core.Services.ToolScenarioInputs.MaximumAggregateArgumentBytes / Mcp.Benchmark.Core.Services.ToolScenarioInputs.MaximumArgumentBytes;
        var config = new ToolTestingConfig { CustomScenarios = Enumerable.Range(0, count)
            .Select(index => new ToolTestScenario { ToolName = $"tool-{index}", Parameters = new() { ["value"] = payload } }).ToList() };
        if (exceeds) config.CustomScenarios.Add(new ToolTestScenario { ToolName = "over-budget", Parameters = new() });
        var capture = () => Mcp.Benchmark.Core.Services.ToolScenarioInputs.CapturePositiveScenarios(config);
        if (exceeds) capture.Should().Throw<ArgumentException>().Which.Message.Should().NotBeNullOrWhiteSpace();
        else capture.Should().NotThrow().Which.Should().HaveCount(count);
    }

    [Theory]
    [InlineData("null-inventory")]
    [InlineData("null-entry")]
    [InlineData("blank-name")]
    [InlineData("null-selection")]
    [InlineData("mixed-selection")]
    [InlineData("nan")]
    [InlineData("infinite")]
    [InlineData("duplicate-json")]
    [InlineData("undefined-json")]
    public void PositiveToolScenarioCapture_ShouldRejectMalformedInputsWithoutValues(string variation)
    {
        var config = new ToolTestingConfig { CustomScenarios = [new ToolTestScenario { ToolName = "owned", Parameters = new() { ["value"] = "input-canary" } }] };
        using var duplicate = JsonDocument.Parse("{\"value\":1,\"value\":2}");
        if (variation == "null-inventory") config.CustomScenarios = null!;
        if (variation == "null-entry") config.CustomScenarios[0] = null!;
        if (variation == "blank-name") config.CustomScenarios[0].ToolName = " ";
        if (variation == "null-selection") config.SpecificTools = null!;
        if (variation == "mixed-selection") config.SpecificTools.Add("owned");
        if (variation == "nan") config.CustomScenarios[0].Parameters!["value"] = double.NaN;
        if (variation == "infinite") config.CustomScenarios[0].Parameters!["value"] = float.PositiveInfinity;
        if (variation == "duplicate-json") config.CustomScenarios[0].Parameters!["value"] = duplicate.RootElement;
        if (variation == "undefined-json") config.CustomScenarios[0].Parameters!["value"] = default(JsonElement);
        var capture = () => Mcp.Benchmark.Core.Services.ToolScenarioInputs.CapturePositiveScenarios(config);
        capture.Should().Throw<ArgumentException>().Which.Message.Should().NotBeNullOrWhiteSpace().And.NotContain("input-canary");
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
    public void ScoringConstants_VulnPenalties_ShouldDescendBySeverity()
    {
        ScoringConstants.VulnPenaltyCritical.Should().BeGreaterThan(ScoringConstants.VulnPenaltyHigh);
        ScoringConstants.VulnPenaltyHigh.Should().BeGreaterThan(ScoringConstants.VulnPenaltyMedium);
        ScoringConstants.VulnPenaltyMedium.Should().BeGreaterThan(ScoringConstants.VulnPenaltyLow);
        ScoringConstants.VulnPenaltyLow.Should().BeGreaterThan(ScoringConstants.VulnPenaltyInfo);
    }

}
