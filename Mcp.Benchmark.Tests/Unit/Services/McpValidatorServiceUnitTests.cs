using System.Collections.Generic;
using Mcp.Benchmark.Core.Abstractions;
using Mcp.Benchmark.Core.Constants;
using Mcp.Benchmark.Core.Models;
using Mcp.Benchmark.Core.Services;
using Mcp.Benchmark.Infrastructure.Registries;
using Mcp.Benchmark.Infrastructure.Scenarios;
using Mcp.Benchmark.Infrastructure.Services;
using Mcp.Benchmark.Infrastructure.Services.Reporting;
using System.Text.Json;
using Mcp.Benchmark.Infrastructure.Rules.Protocol;
using Microsoft.Extensions.Logging;
using Moq;
using FluentAssertions;
using Xunit;
using Mcp.Compliance.Spec;
using Mcp.Benchmark.Core.Utilities;
using System.Security.Cryptography;
using System.Text.Json.Serialization;

namespace Mcp.Benchmark.Tests.Unit;

/// <summary>
/// Unit tests for the McpValidatorService orchestrator.
/// Tests validation workflow coordination, result aggregation, and error handling.
/// </summary>
public class McpValidatorServiceUnitTests
{
    [Theory]
    [InlineData("matching", false)]
    [InlineData("wrong-scenario", true)]
    [InlineData("wrong-scope", true)]
    [InlineData("wrong-layer", true)]
    [InlineData("wrong-lane", true)]
    [InlineData("duplicate", true)]
    [InlineData("extension", false)]
    [InlineData("extension-implicit", false)]
    [InlineData("extension-wrong-lane", true)]
    public async Task ValidateServerAsync_ShouldRequireEvidenceForThePlannedScenario(string variant, bool rejected)
    {
        var extension = variant.StartsWith("extension", StringComparison.Ordinal);
        var scenario = new Mock<IValidationScenario>();
        scenario.SetupGet(value => value.Descriptor).Returns(new ValidationScenarioDescriptor
        {
            Key = new("planned"), LayerKey = "controlled-application-state", Lane = extension ? null : EvaluationLane.ControlledApplication,
            Revision = new("fixture-v1"), DisplayName = "Planned obligation", Stability = ValidationStability.Stable
        });
        var coverage = new ValidationCoverageDeclaration
        {
            LayerId = variant == "wrong-layer" ? "unrelated" : "controlled-application-state",
            Scope = variant == "wrong-scope" ? "unrelated" : "planned",
            Lane = variant == "wrong-lane" ? EvaluationLane.ExternalConformance :
                variant == "extension-implicit" ? null : variant == "extension-wrong-lane" ? EvaluationLane.Baseline :
                extension ? EvaluationLane.ExtensionReadiness : EvaluationLane.ControlledApplication,
            Status = ValidationCoverageStatus.Covered, ObservedOutcome = ValidationOutcome.Succeeded,
            Confidence = EvidenceConfidenceLevel.High
        };
        scenario.Setup(value => value.ExecuteAsync(It.IsAny<ValidationScenarioContext>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ValidationScenarioExecutionResult
            {
                Scenario = new() { ScenarioId = variant == "wrong-scenario" ? "unrelated" : "planned", Status = TestStatus.Passed },
                Coverage = variant == "duplicate" ? [coverage, coverage] : [coverage]
            });
        var pack = new Mock<IValidationScenarioPack>();
        pack.SetupGet(value => value.Descriptor).Returns(new ValidationPackDescriptor
        {
            Key = new("fixture-pack"), Kind = ValidationPackKind.ScenarioPack, Lane = extension ? null : EvaluationLane.ControlledApplication,
            ExtensionId = extension ? "fixture.extension" : null, ExtensionStage = extension ? ExtensionLifecycleStage.Stable : null,
            SpecificationRevision = "fixture-v1", SpecificationSha256 = new string('a', 64),
            Revision = new("fixture-v1"), DisplayName = "Fixture pack", Stability = ValidationStability.Stable
        });
        pack.Setup(value => value.GetScenarios()).Returns([scenario.Object]);
        var registry = new Mock<IValidationPackRegistry<IValidationScenarioPack>>();
        registry.Setup(value => value.Resolve(It.IsAny<ValidationApplicabilityContext>())).Returns([pack.Object]);
        var configuration = new McpValidatorConfiguration
        {
            Assurance = new() { Profile = "inventory" }, Server = new() { Endpoint = "https://example.invalid/mcp", Transport = "http" }
        };
        var result = await CreateService(registry.Object).ValidateServerAsync(configuration);
        var retainedCoverage = result.Evidence.Coverage.Concat(result.Assessments.Extensions.SelectMany(item => item.Coverage)).ToArray();
        if (rejected)
        {
            result.OverallStatus.Should().Be(ValidationStatus.Error);
            retainedCoverage.Should().Contain(entry => entry.Scope == "planned" &&
                (extension || entry.Lane == EvaluationLane.ControlledApplication) && entry.Status == ValidationCoverageStatus.Unavailable);
            retainedCoverage.Should().NotContain(entry => entry.Scope == "unrelated");
            var clone = result.CloneWithoutSecrets();
            clone.Evidence.Coverage.Concat(clone.Assessments.Extensions.SelectMany(item => item.Coverage))
                .Should().Contain(entry => entry.Scope == "planned" && entry.Status == ValidationCoverageStatus.Unavailable);
            AssuranceExitCodeMapper.Map(clone).Should().Be(AssuranceExitCodeMapper.RuntimeIntegrityFailure);
        }
        else
        {
            result.OverallStatus.Should().NotBe(ValidationStatus.Error);
            retainedCoverage.Should().Contain(entry => entry.Scope == "planned" && entry.Status == ValidationCoverageStatus.Covered);
            if (extension) result.Evidence.Coverage.Should().NotContain(entry => entry.Scope == "planned");
        }
    }

    [Theory]
    [InlineData("same", false)]
    [InlineData("SAME", false)]
    [InlineData("same", true)]
    public async Task ValidateServerAsync_ShouldRejectAmbiguousPlannedObligationsBeforeExecution(string secondKey, bool differentPack)
    {
        var scenarios = new[] { "same", secondKey }.Select(key =>
        {
            var scenario = new Mock<IValidationScenario>();
            scenario.SetupGet(value => value.Descriptor).Returns(new ValidationScenarioDescriptor
            {
                Key = new(key), LayerKey = "fixture-layer", Revision = new("fixture-v1"),
                DisplayName = "Fixture", Stability = ValidationStability.Stable
            });
            return scenario;
        }).ToArray();
        var packs = Enumerable.Range(0, differentPack ? 2 : 1).Select(index =>
        {
            var pack = new Mock<IValidationScenarioPack>();
            pack.SetupGet(value => value.Descriptor).Returns(new ValidationPackDescriptor
            {
                Key = new($"fixture-pack-{index}"), Kind = ValidationPackKind.ScenarioPack, Revision = new("fixture-v1"),
                DisplayName = "Fixture", Stability = ValidationStability.Stable
            });
            pack.Setup(value => value.GetScenarios()).Returns(
                differentPack ? [scenarios[index].Object] : scenarios.Select(scenario => scenario.Object).ToArray());
            return pack.Object;
        }).ToArray();
        var registry = new Mock<IValidationPackRegistry<IValidationScenarioPack>>();
        registry.Setup(value => value.Resolve(It.IsAny<ValidationApplicabilityContext>())).Returns(packs);
        var result = await CreateService(registry.Object).ValidateServerAsync(new McpValidatorConfiguration
        {
            Assurance = new() { Profile = "inventory" }, Server = new() { Endpoint = "https://example.invalid/mcp", Transport = "http" }
        });
        result.OverallStatus.Should().Be(ValidationStatus.Error);
        result.Evidence.Coverage.Should().Contain(entry => entry.Scope == "same" && entry.Status == ValidationCoverageStatus.Unavailable);
        foreach (var scenario in scenarios)
            scenario.Verify(value => value.ExecuteAsync(It.IsAny<ValidationScenarioContext>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Theory]
    [InlineData(false, false, false, "root")]
    [InlineData(false, true, false, "root")]
    [InlineData(true, false, false, "root")]
    [InlineData(true, true, false, "root")]
    [InlineData(false, false, true, "root")]
    [InlineData(false, true, true, "root")]
    [InlineData(true, false, true, "root")]
    [InlineData(true, true, true, "root")]
    [InlineData(false, false, false, "shared-child")]
    [InlineData(true, true, false, "shared-child")]
    [InlineData(false, true, true, "shared-child")]
    [InlineData(true, false, true, "shared-child")]
    [InlineData(false, false, false, "unique-child")]
    [InlineData(true, false, false, "unique-child")]
    [InlineData(false, false, true, "unique-child")]
    [InlineData(true, false, true, "unique-child")]
    public async Task ScenarioCoverage_ShouldEnforceOwnershipWithoutLosingUniqueChildren(bool reverse, bool alias, bool extension, string shape)
    {
        var lane = extension ? EvaluationLane.ExtensionReadiness : EvaluationLane.ControlledApplication;
        ValidationCoverageDeclaration Coverage(string scope, ValidationOutcome outcome) => new()
        {
            LayerId = "fixture-layer", Scope = scope, Lane = lane, Status = ValidationCoverageStatus.Covered,
            ObservedOutcome = outcome, Confidence = EvidenceConfidenceLevel.High, Reason = $"Original {scope}: {outcome}."
        };
        var scenarios = new[] { "a", "b" }.Select(key =>
        {
            var scenario = new Mock<IValidationScenario>();
            scenario.SetupGet(value => value.Descriptor).Returns(new ValidationScenarioDescriptor
            {
                Key = new(key), LayerKey = "fixture-layer", Lane = lane,
                Revision = new("fixture-v1"), DisplayName = key, Stability = ValidationStability.Stable
            });
            scenario.Setup(value => value.ExecuteAsync(It.IsAny<ValidationScenarioContext>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(new ValidationScenarioExecutionResult
                {
                    Scenario = new() { ScenarioId = key, Status = key == "a" ? TestStatus.Passed : TestStatus.Failed },
                    Coverage = key == "a"
                        ? [Coverage("a", ValidationOutcome.Succeeded),
                            Coverage(shape == "root" ? (alias ? "B" : "b") : shape == "shared-child" ? (alias ? "SHARED" : "shared") : "a:child",
                                ValidationOutcome.Succeeded)]
                        : shape == "root" ? [Coverage("b", ValidationOutcome.Failed)]
                        : [Coverage("b", ValidationOutcome.Failed), Coverage(shape == "shared-child" ? "shared" : "b:child", ValidationOutcome.Failed)]
                });
            return scenario.Object;
        }).ToArray();
        var packs = (reverse ? scenarios.Reverse() : scenarios).Select(scenario =>
        {
            var pack = new Mock<IValidationScenarioPack>();
            pack.SetupGet(value => value.Descriptor).Returns(new ValidationPackDescriptor
            {
                Key = new("pack-" + scenario.Descriptor.Key.Value), Kind = ValidationPackKind.ScenarioPack, Lane = lane,
                Revision = new("fixture-v1"), DisplayName = "Fixture", Stability = ValidationStability.Stable,
                ExtensionId = extension ? "fixture.extension" : null, ExtensionStage = ExtensionLifecycleStage.Stable,
                SpecificationRevision = "fixture-v1", SpecificationSha256 = new string('a', 64)
            });
            pack.Setup(value => value.GetScenarios()).Returns([scenario]);
            return pack.Object;
        }).ToArray();
        var registry = new Mock<IValidationPackRegistry<IValidationScenarioPack>>();
        registry.Setup(value => value.Resolve(It.IsAny<ValidationApplicabilityContext>())).Returns(packs);
        var result = await CreateService(registry.Object).ValidateServerAsync(new McpValidatorConfiguration
        {
            Assurance = new() { Profile = "inventory" }, Server = new() { Endpoint = "https://example.invalid/mcp", Transport = "http" }
        });
        var retained = result.Evidence.Coverage.Concat(result.Assessments.Extensions.SelectMany(item => item.Coverage)).ToArray();
        if (shape == "unique-child")
        {
            result.OverallStatus.Should().NotBe(ValidationStatus.Error);
            retained.Should().Contain(item => item.Scope == "a:child" && item.Outcome == ValidationOutcome.Succeeded);
            retained.Should().Contain(item => item.Scope == "b:child" && item.Outcome == ValidationOutcome.Failed);
            return;
        }
        result.OverallStatus.Should().Be(ValidationStatus.Error);
        retained.Should().NotContain(item => item.Scope.Equals("b", StringComparison.OrdinalIgnoreCase) && item.ObservedOutcome == ValidationOutcome.Succeeded);
        if (shape == "shared-child")
        {
            retained.Where(item => item.Scope.Equals("shared", StringComparison.OrdinalIgnoreCase))
                .Should().NotBeEmpty().And.OnlyContain(item => item.Outcome == ValidationOutcome.Blocked);
            var conflicts = result.Evidence.Observations.Where(item => item.ObservationKind == "scenario-coverage-conflict").ToArray();
            conflicts.Should().HaveCount(2);
            foreach (var owner in new[] { "a", "b" })
            {
                var observation = conflicts.Should().ContainSingle(item => item.ScenarioId == owner).Which;
                var scope = owner == "a" && alias ? "SHARED" : "shared";
                var outcome = owner == "a" ? ValidationOutcome.Succeeded : ValidationOutcome.Failed;
                observation.Component.Should().Be("scenario-framework");
                observation.Metadata["packId"].Should().Be("pack-" + owner);
                observation.Metadata["scope"].Should().Be(scope);
                observation.Metadata["returnedOutcome"].Should().Be(outcome.ToString());
                observation.Metadata["returnedReason"].Should().Be($"Original {scope}: {outcome}.");
                observation.Metadata["creditEligible"].Should().Be("false");
                observation.Metadata["attribution"].Should().Be("validator-framework");
            }
            result.CloneWithoutSecrets().Evidence.Observations.Where(item => item.ObservationKind == "scenario-coverage-conflict")
                .Should().BeEquivalentTo(conflicts);
            var claim = result.Assurance.Claims.Single(item => extension
                ? item.Dimension == AssuranceDimension.Extension && item.Subject == "fixture.extension"
                : item.Subject.Equals("shared", StringComparison.OrdinalIgnoreCase));
            claim.Outcome.Should().NotBe(AssuranceClaimOutcome.Satisfied);
            claim.EvidenceIds.Should().Contain(conflicts.Select(item => item.Id));
            var clone = result.CloneWithoutSecrets();
            clone.Assurance.Claims.Single(item => item.ClaimId == claim.ClaimId).EvidenceIds.Should().Equal(claim.EvidenceIds);
            var xml = System.Xml.Linq.XDocument.Parse(new ValidationReportRenderer().GenerateXmlReport(clone, verbose: true));
            xml.Descendants("Claim").Single(item => item.Attribute("id")?.Value == claim.ClaimId)
                .Element("EvidenceReferences")!.Elements("EvidenceId").Select(item => item.Value)
                .Should().Contain(conflicts.Select(item => item.Id));
            var markdown = new MarkdownReportGenerator().GenerateReport(clone);
            var start = markdown.IndexOf($"#### `{claim.ClaimId}`", StringComparison.Ordinal);
            start.Should().BeGreaterThanOrEqualTo(0);
            var end = markdown.IndexOf("\n#### ", start + 1, StringComparison.Ordinal);
            var section = end < 0 ? markdown[start..] : markdown[start..end];
            foreach (var observation in conflicts) section.Should().Contain(observation.Id);
            var renderer = new ValidationReportRenderer();
            var html = renderer.GenerateHtmlReport(clone, new ReportingConfig(), verbose: true);
            var articleStart = html.IndexOf($"data-claim-id=\"{claim.ClaimId}\"", StringComparison.Ordinal);
            articleStart.Should().BeGreaterThanOrEqualTo(0);
            var articleEnd = html.IndexOf("</article>", articleStart, StringComparison.Ordinal);
            foreach (var observation in conflicts) html[articleStart..articleEnd].Should().Contain(observation.Id);
            using var sarif = JsonDocument.Parse(renderer.GenerateSarifReport(clone));
            var sarifClaim = sarif.RootElement.GetProperty("runs")[0].GetProperty("results").EnumerateArray()
                .Single(item => item.GetProperty("properties").TryGetProperty("claimId", out var id) && id.ValueEquals(claim.ClaimId));
            sarifClaim.GetProperty("properties").GetProperty("evidenceIds").EnumerateArray().Select(item => item.GetString())
                .Should().Contain(conflicts.Select(item => item.Id));
            var junit = System.Xml.Linq.XDocument.Parse(renderer.GenerateJunitReport(clone));
            var junitClaim = junit.Descendants("testcase").Single(item => item.Attribute("name")?.Value == claim.ClaimId);
            foreach (var observation in conflicts) junitClaim.ToString().Should().Contain(observation.Id);
        }
        retained.Should().Contain(item => item.Scope == "b" &&
            (reverse ? item.ObservedOutcome == ValidationOutcome.Failed : item.Status == ValidationCoverageStatus.Unavailable));
        AssuranceExitCodeMapper.Map(result).Should().Be(AssuranceExitCodeMapper.RuntimeIntegrityFailure);
    }

    private readonly Mock<ILogger<McpValidatorService>> _loggerMock;
    private readonly Mock<IProtocolComplianceValidator> _protocolValidatorMock;
    private readonly Mock<IToolValidator> _toolValidatorMock;
    private readonly Mock<IResourceValidator> _resourceValidatorMock;
    private readonly Mock<IPromptValidator> _promptValidatorMock;
    private readonly Mock<ISecurityValidator> _securityValidatorMock;
    private readonly Mock<IPerformanceValidator> _performanceValidatorMock;
    private readonly Mock<IErrorHandlingValidator> _errorHandlingValidatorMock;
    private readonly Mock<IMcpHttpClient> _httpClientMock;
    private readonly Mock<IValidationSessionBuilder> _sessionBuilderMock;
    private readonly Mock<IValidationApplicabilityResolver> _applicabilityResolverMock;
    private readonly IValidationPackRegistry<IProtocolFeaturePack> _protocolFeaturePackRegistry;
    private readonly IValidationPackRegistry<IValidationScenarioPack> _scenarioPackRegistry;
    private readonly IProtocolRuleRegistry _protocolRuleRegistry;
    private readonly Mock<IAggregateScoringStrategy> _scoringStrategyMock;
    private readonly Mock<IHealthCheckService> _healthCheckServiceMock;
    private readonly Mock<ITelemetryService> _telemetryServiceMock;
    private readonly TransportResult<CapabilitySummary> _defaultCapabilitySnapshot;
    private readonly McpValidatorService _validatorService;

    public McpValidatorServiceUnitTests()
    {
        _loggerMock = new Mock<ILogger<McpValidatorService>>();
        _protocolValidatorMock = new Mock<IProtocolComplianceValidator>();
        _toolValidatorMock = new Mock<IToolValidator>();
        _resourceValidatorMock = new Mock<IResourceValidator>();
        _promptValidatorMock = new Mock<IPromptValidator>();
        _securityValidatorMock = new Mock<ISecurityValidator>();
        _performanceValidatorMock = new Mock<IPerformanceValidator>();
        _errorHandlingValidatorMock = new Mock<IErrorHandlingValidator>();
        _httpClientMock = new Mock<IMcpHttpClient>();
        _sessionBuilderMock = new Mock<IValidationSessionBuilder>();
        _applicabilityResolverMock = new Mock<IValidationApplicabilityResolver>();
        _protocolFeaturePackRegistry = new ValidationPackRegistry<IProtocolFeaturePack>(
            new IProtocolFeaturePack[] { new BuiltInProtocolFeaturePack(new EmbeddedSchemaRegistry()) });
        _scenarioPackRegistry = new ValidationPackRegistry<IValidationScenarioPack>(
            new IValidationScenarioPack[] { new BuiltInObservedSurfaceScenarioPack() });
        _protocolRuleRegistry = new ProtocolRuleRegistry(
            new ValidationPackRegistry<IValidationRulePack<ProtocolValidationContext>>(
                new IValidationRulePack<ProtocolValidationContext>[] { new BuiltInProtocolRulePack() }));
        _scoringStrategyMock = new Mock<IAggregateScoringStrategy>();
        _healthCheckServiceMock = new Mock<IHealthCheckService>();
        _telemetryServiceMock = new Mock<ITelemetryService>();

        _defaultCapabilitySnapshot = new TransportResult<CapabilitySummary>
        {
            IsSuccessful = true,
            Payload = new CapabilitySummary
            {
                ToolListingSucceeded = true,
                ToolInvocationSucceeded = true,
                DiscoveredToolsCount = 0
            },
            Transport = TransportMetadata.Empty
        };

        _sessionBuilderMock
            .Setup(x => x.BuildAsync(It.IsAny<McpValidatorConfiguration>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((McpValidatorConfiguration cfg, CancellationToken _) =>
            {
                var serverClone = new McpServerConfig
                {
                    Endpoint = cfg.Server.Endpoint,
                    Transport = cfg.Server.Transport,
                    Profile = cfg.Server.Profile,
                    ProtocolVersion = cfg.Server.ProtocolVersion,
                    Authentication = cfg.Server.Authentication,
                    TimeoutMs = cfg.Server.TimeoutMs,
                    Headers = new Dictionary<string, string>(cfg.Server.Headers),
                    Environment = new Dictionary<string, string>(cfg.Server.Environment)
                };

                return new ValidationSessionContext(cfg, serverClone)
                {
                    CapabilitySnapshot = _defaultCapabilitySnapshot,
                    ProtocolVersion = cfg.Server.ProtocolVersion ?? "2025-11-25"
                };
            });

        // Setup default scoring strategy behavior
        _scoringStrategyMock.Setup(x => x.CalculateScore(It.IsAny<ValidationResult>()))
            .Returns(new ScoringResult { OverallScore = 100.0, Status = ValidationStatus.Passed });

        // Setup default health check behavior
        _healthCheckServiceMock.Setup(x => x.PerformHealthCheckAsync(It.IsAny<McpServerConfig>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new HealthCheckResult { IsHealthy = true });

        _errorHandlingValidatorMock
            .Setup(x => x.ValidateErrorHandlingAsync(It.IsAny<McpServerConfig>(), It.IsAny<ErrorHandlingConfig>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ErrorHandlingTestResult
            {
                Status = TestStatus.Passed,
                Score = 100,
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
                        GracefulRecovery = true
                    }
                }
            });

        _applicabilityResolverMock
            .Setup(x => x.Build(It.IsAny<ValidationSessionContext>(), It.IsAny<McpValidatorConfiguration>(), It.IsAny<IReadOnlyList<string>>()))
            .Returns((ValidationSessionContext session, McpValidatorConfiguration configuration, IReadOnlyList<string> selectedProfiles) => new ValidationApplicabilityContext
            {
                NegotiatedProtocolVersion = session.ProtocolVersion ?? configuration.Server.ProtocolVersion ?? "2025-11-25",
                SchemaVersion = session.ProtocolVersion ?? configuration.Server.ProtocolVersion ?? "2025-11-25",
                Transport = configuration.Server.Transport,
                AccessMode = configuration.Server.Profile.ToString(),
                ServerProfile = session.ServerProfile.ToString(),
                IsAuthenticated = !string.IsNullOrWhiteSpace(configuration.Server.Authentication?.Token),
                AdvertisedCapabilities = Array.Empty<string>(),
                AdvertisedSurfaces = Array.Empty<string>(),
                SelectedClientProfiles = selectedProfiles,
                EnvironmentHints = new Dictionary<string, string>()
            });

        _validatorService = CreateService(_scenarioPackRegistry);
    }

    private McpValidatorService CreateService(IValidationPackRegistry<IValidationScenarioPack> scenarioRegistry, IValidatorProcessResourceSampler? resourceSampler = null,
        TimeProvider? timeProvider = null) =>
        new McpValidatorService(
            _protocolValidatorMock.Object,
            _toolValidatorMock.Object,
            _resourceValidatorMock.Object,
            _promptValidatorMock.Object,
            _securityValidatorMock.Object,
            _performanceValidatorMock.Object,
            _errorHandlingValidatorMock.Object,
            _sessionBuilderMock.Object,
            _applicabilityResolverMock.Object,
            _protocolFeaturePackRegistry,
            scenarioRegistry,
            _protocolRuleRegistry,
            _httpClientMock.Object,
            _scoringStrategyMock.Object,
            _healthCheckServiceMock.Object,
            _telemetryServiceMock.Object,
            _loggerMock.Object,
            resourceSampler,
            timeProvider);

    [Theory]
    [InlineData("positive", ValidationOutcome.Succeeded)]
    [InlineData("wrong-actor", ValidationOutcome.Failed)]
    [InlineData("missing-hop", ValidationOutcome.Inconclusive)]
    public async Task AdmittedRelay_ShouldSurviveOrchestrationWithoutTargetProbesOrCoreCredit(string mode, ValidationOutcome expected)
    {
        var configuration = CreateFunctionalCategoryConfiguration();
        var request = ValidationRunRequest.Capture(configuration, "relay-orchestrator-run");
        var original = Services.ControlledRelayEvaluatorTests.Contract();
        var contract = original with
        {
            Boundaries = original.Boundaries.Select((boundary, index) => boundary with
            {
                Binding = boundary.Binding with
                {
                    RunId = request.RequestId,
                    TargetIdentitySha256 = index == 0 ? request.TargetIdentitySha256 : boundary.Binding.TargetIdentitySha256
                }
            }).ToArray()
        };
        var clock = DateTimeOffset.UtcNow;
        var observations = contract.Boundaries.Select((boundary, index) => Services.ControlledRelayEvaluatorTests.Observation(boundary, index) with
        {
            StartedAt = clock.AddSeconds(-5 + index), CompletedAt = clock.AddSeconds(-1 - index), ObservedThrough = clock,
            EffectiveActorMatched = !(mode == "wrong-actor" && index == 1)
        }).Take(mode == "missing-hop" ? 1 : 2).ToArray();
        using var key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var document = Services.ControlledRelayEvaluatorTests.Sign(key, contract, observations) with
        {
            IssuedAt = clock, ExpiresAt = clock.AddMinutes(1)
        };
        document = document with { SignatureBase64 = Convert.ToBase64String(key.SignHash(
            Convert.FromHexString(document.ComputePayloadDigest()), DSASignatureFormat.IeeeP1363FixedFieldConcatenation)) };
        var pack = new ControlledRelayEvidenceImporter().ImportPack(JsonSerializer.Serialize(document, OfficialConformanceContract.CreateJsonOptions()),
            contract, key.ExportSubjectPublicKeyInfoPem(), request.RequestId, request.TargetIdentitySha256, clock);
        _protocolValidatorMock.Setup(validator => validator.ValidateJsonRpcComplianceAsync(
            It.IsAny<McpServerConfig>(), It.IsAny<ProtocolComplianceConfig>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ComplianceTestResult { Status = TestStatus.Passed });
        var baseline = await CreateService(new ValidationPackRegistry<IValidationScenarioPack>([])).ValidateServerAsync(request);
        var calls = _httpClientMock.Invocations.Select(invocation => invocation.Method).ToArray();
        _httpClientMock.Invocations.Clear();

        var result = await CreateService(new ValidationPackRegistry<IValidationScenarioPack>([pack])).ValidateServerAsync(request);

        var scenario = result.Assessments.Scenarios.Should().ContainSingle().Which;
        scenario.ControlledRelay!.Outcome.Should().Be(expected);
        result.Evidence.Coverage.Should().ContainSingle(entry => entry.Scope == scenario.ScenarioId && entry.Outcome == expected);
        result.Evidence.Coverage.Where(entry => entry.Scope.StartsWith(scenario.ScenarioId, StringComparison.Ordinal))
            .Should().OnlyContain(entry => entry.Lane == EvaluationLane.ControlledApplication);
        _httpClientMock.Invocations.Select(invocation => invocation.Method).Should().Equal(calls);
        result.Assurance.Assessments.CoreConformance.Should().BeEquivalentTo(baseline.Assurance.Assessments.CoreConformance);
    }

    [Fact]
    public async Task GlobalRunDeadline_ShouldCancelOnceAndRetainCompletedCategories()
    {
        var time = new ManualRunTimeProvider();
        var config = CreateFunctionalCategoryConfiguration();
        config.TestExecution.EnableParallelExecution = false;
        config.TestExecution.GlobalTimeoutMinutes = 2;
        _protocolValidatorMock.Setup(validator => validator.ValidateJsonRpcComplianceAsync(It.IsAny<McpServerConfig>(),
            It.IsAny<ProtocolComplianceConfig>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ComplianceTestResult { Status = TestStatus.Passed, Message = "Completed before deadline" });
        _toolValidatorMock.Setup(validator => validator.ValidateToolDiscoveryAsync(It.IsAny<McpServerConfig>(),
            It.IsAny<ToolTestingConfig>(), It.IsAny<CancellationToken>()))
            .Returns((McpServerConfig _, ToolTestingConfig _, CancellationToken token) =>
            {
                time.Fire();
                return Task.FromCanceled<ToolTestResult>(token);
            });
        var result = await CreateService(_scenarioPackRegistry, timeProvider: time).ValidateServerAsync(config);
        result.OverallStatus.Should().Be(ValidationStatus.Cancelled);
        result.ProtocolCompliance!.Message.Should().Be("Completed before deadline");
        result.Evidence.Coverage.Should().Contain(coverage => coverage.LayerId == "resource-surface" &&
            coverage.ObservedOutcome == ValidationOutcome.Cancelled);
        AssuranceExitCodeMapper.Map(result).Should().Be(AssuranceExitCodeMapper.TimeoutOrCancellation);
        time.DueTime.Should().Be(TimeSpan.FromMinutes(2));
        time.TimerCount.Should().Be(1);
        time.Disposed.Should().BeTrue();
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(int.MaxValue)]
    public async Task GlobalRunDeadline_ShouldRejectInvalidLimitsBeforeTargetSetup(int minutes)
    {
        var config = CreateFunctionalCategoryConfiguration();
        config.TestExecution.GlobalTimeoutMinutes = minutes;
        var validate = () => _validatorService.ValidateServerAsync(config);
        await validate.Should().ThrowAsync<ValidationSessionException>();
        _httpClientMock.Verify(client => client.ConfigureExecutionPolicy(It.IsAny<ExecutionPolicy>()), Times.Never);
    }

    private sealed class ManualRunTimeProvider : TimeProvider
    {
        private TimerCallback? _callback;
        private object? _state;
        public TimeSpan DueTime { get; private set; }
        public int TimerCount { get; private set; }
        public bool Disposed { get; private set; }
        public void Fire() => _callback!(_state);
        public override ITimer CreateTimer(TimerCallback callback, object? state, TimeSpan dueTime, TimeSpan period)
        {
            _callback = callback;
            _state = state;
            DueTime = dueTime;
            TimerCount++;
            return new ManualTimer(this);
        }
        private sealed class ManualTimer(ManualRunTimeProvider owner) : ITimer
        {
            public bool Change(TimeSpan dueTime, TimeSpan period) => throw new InvalidOperationException("The total-run deadline must not restart.");
            public void Dispose() => owner.Disposed = true;
            public ValueTask DisposeAsync() { Dispose(); return ValueTask.CompletedTask; }
        }
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public async Task ComprehensiveLibraryAdmission_ShouldRejectBeforeAnyTargetSetup(bool captured, bool dryRun)
    {
        var configuration = new McpValidatorConfiguration();
        configuration.Execution!.DryRun = dryRun;
        var action = () => captured
            ? _validatorService.ValidateServerAsync(ValidationRunRequest.Capture(configuration))
            : _validatorService.ValidateServerAsync(configuration);

        await action.Should().ThrowAsync<ValidationSessionException>();
        _httpClientMock.Invocations.Should().BeEmpty();
        _sessionBuilderMock.Invocations.Should().BeEmpty();
        _healthCheckServiceMock.Invocations.Should().BeEmpty();
    }

    [Fact]
    public async Task ComprehensiveLibraryAdmission_ShouldVerifySuppliedEvidenceBeforeTargetSetup()
    {
        var configuration = new McpValidatorConfiguration();
        configuration.Execution!.Mode = ExecutionMode.Standard;
        ComprehensiveAssessmentRequirements.GetPreflightErrors(configuration, true).Should().BeEmpty();
        var action = () => _validatorService.ValidateServerAsync(ValidationRunRequest.CaptureWithConformance(configuration, "{}"));

        await action.Should().ThrowAsync<Mcp.Benchmark.Core.Exceptions.OfficialConformanceEvidenceException>();
        _httpClientMock.Invocations.Should().BeEmpty();
        _sessionBuilderMock.Invocations.Should().BeEmpty();
    }

    [Theory]
    [InlineData("valid")]
    [InlineData("target")]
    [InlineData("protocol")]
    [InlineData("unpinned")]
    [InlineData("missing-baseline")]
    [InlineData("baseline-target")]
    [InlineData("baseline-tampered")]
    [InlineData("baseline-selection")]
    public async Task ComprehensiveLibraryAdmission_ShouldBindVerifiedEvidenceBeforeBootstrap(string mode)
    {
        var configuration = new McpValidatorConfiguration();
        configuration.Server.Endpoint = "https://example.invalid/mcp";
        configuration.Server.Transport = "http";
        configuration.Server.ProtocolVersion = OfficialConformanceContract.RequirementSetRevision;
        configuration.Execution!.Mode = ExecutionMode.Standard;
        var document = Mcp.Benchmark.Tests.Unit.Services.OfficialConformanceEvidenceImporterTests.CreateDocument(
            [Mcp.Benchmark.Tests.Unit.Services.OfficialConformanceEvidenceImporterTests.CreateCheck("fixture-pass", OfficialConformanceCheckStatus.Success)],
            runnerVersion: mode == "unpinned" ? "untrusted-version" : null);
        var node = JsonSerializer.SerializeToNode(document, OfficialConformanceContract.CreateJsonOptions())!;
        node["target"]!["identitySha256"] = mode == "target" ? new string('b', 64) : ValidationTargetIdentity.ComputeDigest(configuration.Server);
        if (mode == "protocol") node["target"]!["protocolVersion"] = "2025-11-25";
        var request = ValidationRunRequest.CaptureWithConformance(configuration, node.ToJsonString());

        if (mode.StartsWith("baseline-", StringComparison.Ordinal))
        {
            request = WithCurrentSecurityBaseline(request);
            var controls = request.IdentityControls!;
            controls = mode switch
            {
                "baseline-target" => controls with { Admission = controls.Admission with { TargetIdentitySha256 = new string('b', 64) } },
                "baseline-tampered" => controls with { SignedEvidenceJson = "{}" },
                _ => controls with { ScopeEnforcementCaseId = "absent" }
            };
            request = request.WithIdentityControls(controls);
        }

        if (mode == "valid")
        {
            request = WithCurrentSecurityBaseline(request);
            var result = await _validatorService.ValidateServerAsync(request);
            _sessionBuilderMock.Verify(builder => builder.BuildAsync(It.IsAny<McpValidatorConfiguration>(), It.IsAny<CancellationToken>()), Times.Once);
            result.Assessments.OfficialConformance.Should().NotBeNull();
            result.Assessments.OfficialConformance!.VerificationStatus.Should().Be(OfficialConformanceVerificationStatus.Verified);
            result.Evidence.Coverage.Should().Contain(entry => entry.Lane == EvaluationLane.ExternalConformance);
            result.Run.ExecutionMode.Should().Be(ExecutionMode.Standard);
            result.Assurance.Claims.Where(claim => claim.ClaimId.StartsWith("security-baseline:", StringComparison.Ordinal))
                .Should().HaveCount(3).And.OnlyContain(claim => claim.Outcome == AssuranceClaimOutcome.Satisfied);
        }
        else
        {
            var action = () => _validatorService.ValidateServerAsync(request);
            if (mode is "unpinned" or "missing-baseline" or "baseline-selection") await action.Should().ThrowAsync<ValidationSessionException>();
            else if (mode is "baseline-target" or "baseline-tampered") await action.Should().ThrowAsync<InvalidDataException>();
            else await action.Should().ThrowAsync<Mcp.Benchmark.Core.Exceptions.OfficialConformanceEvidenceException>();
            _httpClientMock.Invocations.Should().BeEmpty();
            _sessionBuilderMock.Invocations.Should().BeEmpty();
        }
    }

    [Theory]
    [InlineData("bootstrap-failure")]
    [InlineData("bootstrap-cancelled")]
    [InlineData("negotiated-mismatch")]
    [InlineData("inventory-negotiated")]
    public async Task ConformanceAdmission_ShouldRequireSuccessfulNegotiatedBindingBeforeCredit(string mode)
    {
        using var cancellation = new CancellationTokenSource();
        var configuration = new McpValidatorConfiguration();
        configuration.Server.Endpoint = "https://example.invalid/mcp";
        configuration.Server.Transport = "http";
        configuration.Server.ProtocolVersion = OfficialConformanceContract.RequirementSetRevision;
        configuration.Execution!.Mode = ExecutionMode.Standard;
        if (mode == "inventory-negotiated")
        {
            configuration.Assurance.Profile = "inventory";
            configuration.Server.ProtocolVersion = "2025-11-25";
        }
        var document = Mcp.Benchmark.Tests.Unit.Services.OfficialConformanceEvidenceImporterTests.CreateDocument(
            [Mcp.Benchmark.Tests.Unit.Services.OfficialConformanceEvidenceImporterTests.CreateCheck("fixture-pass", OfficialConformanceCheckStatus.Success)]);
        var node = JsonSerializer.SerializeToNode(document, OfficialConformanceContract.CreateJsonOptions())!;
        node["target"]!["identitySha256"] = ValidationTargetIdentity.ComputeDigest(configuration.Server);
        _sessionBuilderMock.Setup(builder => builder.BuildAsync(It.IsAny<McpValidatorConfiguration>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((McpValidatorConfiguration captured, CancellationToken token) =>
            {
                if (mode == "bootstrap-failure") throw new ValidationSessionException("Fixture bootstrap unavailable.", ValidationStatus.Failed);
                if (mode == "bootstrap-cancelled")
                {
                    cancellation.Cancel();
                    token.ThrowIfCancellationRequested();
                }
                return new ValidationSessionContext(captured, captured.Server)
                {
                    ProtocolVersion = mode == "negotiated-mismatch" ? "2025-11-25" : OfficialConformanceContract.RequirementSetRevision
                };
            });

        var request = ValidationRunRequest.CaptureWithConformance(configuration, node.ToJsonString());
        if (mode != "inventory-negotiated") request = WithCurrentSecurityBaseline(request);
        var result = await _validatorService.ValidateServerAsync(request, cancellation.Token);

        if (mode == "inventory-negotiated")
        {
            result.ProtocolVersion.Should().Be(OfficialConformanceContract.RequirementSetRevision);
            result.Assessments.OfficialConformance!.VerificationStatus.Should().Be(OfficialConformanceVerificationStatus.Verified);
        }
        else
        {
            result.Assessments.OfficialConformance.Should().BeNull();
            result.Assurance.Claims.Should().NotContain(claim => claim.Authority == AssuranceAuthority.OfficialConformance && claim.Outcome == AssuranceClaimOutcome.Satisfied);
            _protocolValidatorMock.Invocations.Should().BeEmpty();
            _toolValidatorMock.Invocations.Should().BeEmpty();
            result.Run.TerminationOutcome.Should().Be(mode == "bootstrap-cancelled" ? ValidationOutcome.Cancelled : ValidationOutcome.Inconclusive);
        }
    }

    private static ValidationRunRequest WithCurrentSecurityBaseline(ValidationRunRequest request)
    {
        var result = new ValidationResult();
        Mcp.Benchmark.Tests.Unit.Services.ControlledIdentityEvaluatorTests.AddSecurityBaseline(result, runId: request.RequestId,
            targetDigest: request.TargetIdentitySha256, assessedAt: DateTimeOffset.UtcNow);
        return request.WithIdentityControls(result.Assurance.RequiredIdentityControls!);
    }

    [Theory]
    [InlineData("written", ValidationOutcome.Succeeded)]
    [InlineData("dropped", ValidationOutcome.Failed)]
    [InlineData("other-run", ValidationOutcome.Blocked)]
    [InlineData("other-target", ValidationOutcome.Blocked)]
    [InlineData("cancelled", ValidationOutcome.Cancelled)]
    public async Task ValidateServerAsync_SignedStatePack_ShouldPreserveAdmittedRunAndTarget(string mode, ValidationOutcome expected)
    {
        var configuration = CreateFunctionalCategoryConfiguration();
        var request = ValidationRunRequest.Capture(configuration, "admitted-state-run");
        var binding = new ControlledOperationBinding(
            mode == "other-run" ? "different-run" : request.RequestId, "operation-1", "attempt-1", "request-1", "boundary-1",
            CanonicalJsonDigest.ComputeText(mode == "other-target" ? "https://different.invalid/mcp" : configuration.Server.Endpoint!),
            new string('b', 64), new string('c', 64), "actor-1", "object-1");
        var contract = new ControlledStateScenarioContract
        {
            ScenarioId = "state-write", PolicyRevision = "fixture-policy-v1", ToolName = "fixture_write",
            ObserverId = "observer-1", PredicateId = "expected-state", PreconditionId = "fixture-ready", CleanupOwner = "fixture-owner",
            Binding = binding, StateExpectation = ControlledStateExpectation.ChangeRequired,
            ExpectedApplicationOutcome = ControlledApplicationOutcome.Succeeded, ConsistencyWindow = TimeSpan.FromSeconds(5),
            MaximumBaselineAge = TimeSpan.FromSeconds(5), MaximumEvidenceAge = TimeSpan.FromMinutes(1), MaximumObserverReads = 3
        };
        var start = new DateTimeOffset(2026, 11, 11, 0, 0, 0, TimeSpan.Zero);
        var evidence = new ControlledStateEvidence
        {
            Binding = binding, ObserverId = contract.ObserverId, PredicateId = contract.PredicateId, PreconditionId = contract.PreconditionId,
            BaselineAt = start.AddSeconds(-1), OperationStartedAt = start, OperationEndedAt = start.AddSeconds(1), ObservedThrough = start.AddSeconds(6),
            ObserverOutcome = ValidationOutcome.Succeeded, ObservationComplete = true, PreconditionsSatisfied = true,
            BeforePredicateSatisfied = false, AfterPredicateSatisfied = mode != "dropped", StateChanged = mode != "dropped",
            ApplicationOutcome = ControlledApplicationOutcome.Succeeded, ObserverReads = 2, CleanupOutcome = ValidationOutcome.Succeeded
        };
        using var key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var document = new ControlledStateEvidenceDocument
        {
            Contract = contract, Evidence = evidence, IssuedAt = start.AddSeconds(6), ExpiresAt = start.AddMinutes(2),
            SigningKeyId = CanonicalJsonDigest.ComputeBytes(key.ExportSubjectPublicKeyInfo()), SignatureBase64 = string.Empty
        };
        document = document with { SignatureBase64 = Convert.ToBase64String(key.SignHash(
            Convert.FromHexString(document.ComputePayloadDigest()), DSASignatureFormat.IeeeP1363FixedFieldConcatenation)) };
        var options = new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };
        options.Converters.Add(new JsonStringEnumConverter(JsonNamingPolicy.CamelCase));
        var json = JsonSerializer.Serialize(document, options);
        var pack = new ControlledStateEvidenceImporter().ImportPack(json, contract, key.ExportSubjectPublicKeyInfoPem(), start.AddMinutes(1));
        _protocolValidatorMock.Setup(validator => validator.ValidateJsonRpcComplianceAsync(
            It.IsAny<McpServerConfig>(), It.IsAny<ProtocolComplianceConfig>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ComplianceTestResult { Status = TestStatus.Passed });
        var baseline = await CreateService(new ValidationPackRegistry<IValidationScenarioPack>([])).ValidateServerAsync(request);
        var transportCalls = _httpClientMock.Invocations.Select(invocation => invocation.Method).ToArray();
        _httpClientMock.Invocations.Clear();
        using var cancellation = new CancellationTokenSource();
        if (mode == "cancelled")
        {
            _protocolValidatorMock.Setup(validator => validator.ValidateJsonRpcComplianceAsync(
                It.IsAny<McpServerConfig>(), It.IsAny<ProtocolComplianceConfig>(), It.IsAny<CancellationToken>()))
                .Returns(() =>
                {
                    cancellation.Cancel();
                    return Task.FromCanceled<ComplianceTestResult>(cancellation.Token);
                });
        }

        var result = await CreateService(new ValidationPackRegistry<IValidationScenarioPack>([pack])).ValidateServerAsync(request, cancellation.Token);

        result.ValidationId.Should().Be(request.RequestId);
        result.Run.TargetIdentitySha256.Should().Be(request.TargetIdentitySha256);
        result.Evidence.Coverage.Should().ContainSingle(entry => entry.Lane == EvaluationLane.ControlledApplication && entry.Outcome == expected);
        _httpClientMock.Invocations.Select(invocation => invocation.Method).Should().Equal(transportCalls);
        if (mode == "cancelled")
        {
            result.Run.TerminationOutcome.Should().Be(ValidationOutcome.Cancelled);
            result.Assessments.Scenarios.Should().BeEmpty();
            return;
        }

        var assessment = result.Assessments.Scenarios.Should().ContainSingle().Which.ControlledState!;
        assessment.Outcome.Should().Be(expected);
        assessment.VerifiedObserverKeyId.Should().Be(document.SigningKeyId);
        assessment.SourceEvidenceSha256.Should().Be(CanonicalJsonDigest.ComputeText(json));
        result.Assurance.Assessments.CoreConformance.Should().BeEquivalentTo(baseline.Assurance.Assessments.CoreConformance);
    }

    [Theory]
    [InlineData("valid")]
    [InlineData("unavailable")]
    [InlineData("regressing")]
    public async Task ValidateServerAsync_ProcessResourceFaults_ShouldNotInventMeasurements(string mode)
    {
        var sampler = new Mock<IValidatorProcessResourceSampler>();
        if (mode == "unavailable") sampler.Setup(value => value.Capture()).Throws(new InvalidOperationException("synthetic-resource-canary"));
        else sampler.SetupSequence(value => value.Capture())
            .Returns(new ValidatorProcessResourceSnapshot(TimeSpan.FromMilliseconds(10), 100))
            .Returns(new ValidatorProcessResourceSnapshot(TimeSpan.FromMilliseconds(mode == "valid" ? 25 : 5), 200));

        var result = await CreateService(_scenarioPackRegistry, sampler.Object).ValidateServerAsync(new McpValidatorConfiguration
            { Assurance = new AssurancePolicyConfig { Profile = "inventory" }, Server = new McpServerConfig { Endpoint = "https://example.invalid/mcp", Transport = "http" } });

        result.CriticalErrors.Should().BeEmpty();
        result.Run.TerminationOutcome.Should().Be(ValidationOutcome.Succeeded,
            "a completed execution is separate from missing assurance or unavailable process counters");
        var resources = result.Run.OperationalMetrics!.ValidatorProcessResources!;
        resources.ProcessorTimeDeltaMs.Should().Be(mode == "valid" ? 15 : null);
        resources.WorkingSetStartBytes.Should().Be(mode == "unavailable" ? null : 100);
        resources.WorkingSetEndBytes.Should().Be(mode == "unavailable" ? null : 200);
        JsonSerializer.Serialize(result).Should().NotContain("synthetic-resource-canary");
        sampler.Verify(value => value.Capture(), Times.Exactly(2));
    }

    [Fact]
    public async Task ValidateServerAsync_ShouldAttributeMeasuredResourcesToValidatorProcess()
    {
        var result = await _validatorService.ValidateServerAsync(new McpValidatorConfiguration
            { Assurance = new AssurancePolicyConfig { Profile = "inventory" }, Server = new McpServerConfig { Endpoint = "https://example.invalid/mcp", Transport = "http" } });
        var document = JsonSerializer.SerializeToNode(result.CloneWithoutSecrets(), new JsonSerializerOptions
            { PropertyNamingPolicy = JsonNamingPolicy.CamelCase })!;
        var resources = document["run"]!["operationalMetrics"]!["validatorProcessResources"];

        resources.Should().NotBeNull();
        resources!["scope"]!.GetValue<string>().Should().Be("validatorProcess");
        resources["includesConcurrentActivity"]!.GetValue<bool>().Should().BeTrue();
        resources["processorTimeDeltaMs"]!.GetValue<double>().Should().BeGreaterThanOrEqualTo(0);
        resources["workingSetStartBytes"]!.GetValue<long>().Should().BeGreaterThan(0);
        resources["workingSetEndBytes"]!.GetValue<long>().Should().BeGreaterThan(0);
    }

    [Theory]
    [InlineData(TestStatus.Error, ValidationOutcome.Error, 70)]
    [InlineData(TestStatus.Failed, ValidationOutcome.Succeeded, 0)]
    public async Task ValidateServerAsync_ReturnedCategoryOutcome_ShouldSeparateExecutionFromTargetFailure(
        TestStatus status, ValidationOutcome expectedTermination, int expectedExit)
    {
        _protocolValidatorMock
            .Setup(validator => validator.ValidateJsonRpcComplianceAsync(
                It.IsAny<McpServerConfig>(), It.IsAny<ProtocolComplianceConfig>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ComplianceTestResult
            {
                Status = status,
                CriticalErrors = status == TestStatus.Error ? ["Synthetic unexpected validator exception."] : []
            });
        var service = CreateService(new ValidationPackRegistry<IValidationScenarioPack>([]));

        var result = await service.ValidateServerAsync(CreateFunctionalCategoryConfiguration());

        result.Run.TerminationOutcome.Should().Be(expectedTermination);
        AssuranceExitCodeMapper.Map(result).Should().Be(expectedExit);
    }

    [Theory]
    [InlineData(false, false, false, false)]
    [InlineData(true, false, false, false)]
    [InlineData(true, true, false, false)]
    [InlineData(false, false, true, false)]
    [InlineData(false, false, false, true)]
    [InlineData(true, false, false, true)]
    [InlineData(true, true, false, true)]
    [InlineData(false, false, true, true)]
    public async Task ValidateServerAsync_InterruptedPack_ShouldRetainLaterResolvedObligations(
        bool callerCancellation, bool beforeScenarioPhase, bool planningFailure, bool controlled)
    {
        using var cancellation = new CancellationTokenSource();
        var scenarios = Enumerable.Range(0, 3).Select(index =>
        {
            var scenario = new Mock<IValidationScenario>();
            scenario.SetupGet(value => value.Descriptor).Returns(new ValidationScenarioDescriptor
            {
                Key = new ValidationDescriptorKey($"obligation-{index}"),
                Lane = controlled ? EvaluationLane.ControlledApplication : null,
                Revision = new ValidationRevision("fixture-v1"), DisplayName = "Fixture obligation",
                LayerKey = $"obligation-layer-{index}", Stability = ValidationStability.Stable
            });
            return scenario;
        }).ToArray();
        scenarios[0].Setup(value => value.ExecuteAsync(It.IsAny<ValidationScenarioContext>(), It.IsAny<CancellationToken>()))
            .Returns(() =>
            {
                if (callerCancellation) cancellation.Cancel();
                return Task.FromException<ValidationScenarioExecutionResult>(callerCancellation
                    ? new OperationCanceledException(cancellation.Token) : new InvalidOperationException("Synthetic pack failure."));
            });
        var packs = scenarios.Select((scenario, index) =>
        {
            var pack = new Mock<IValidationScenarioPack>();
            pack.SetupGet(value => value.Descriptor).Returns(new ValidationPackDescriptor
            {
                Key = new ValidationDescriptorKey($"obligation-pack-{index}"), Kind = ValidationPackKind.ScenarioPack,
                Lane = controlled ? EvaluationLane.ControlledApplication : null,
                Revision = new ValidationRevision("fixture-v1"), DisplayName = "Fixture pack", Stability = ValidationStability.Stable,
                ExtensionId = index == 2 ? "fixture.unstarted-extension" : null,
                ExtensionStage = index == 2 ? ExtensionLifecycleStage.Stable : null,
                SpecificationRevision = "fixture-v1", SpecificationSha256 = new string('a', 64)
            });
            if (planningFailure && index == 2)
                pack.Setup(value => value.GetScenarios()).Throws(new InvalidOperationException("Synthetic plan enumeration failure."));
            else pack.Setup(value => value.GetScenarios()).Returns([scenario.Object]);
            return pack.Object;
        }).ToArray();
        var registry = new Mock<IValidationPackRegistry<IValidationScenarioPack>>();
        registry.Setup(value => value.Resolve(It.IsAny<ValidationApplicabilityContext>())).Returns(packs);
        var configuration = new McpValidatorConfiguration
            { Assurance = new AssurancePolicyConfig { Profile = "inventory" }, Server = new McpServerConfig { Endpoint = "https://example.invalid/mcp", Transport = "http" } };
        if (beforeScenarioPhase)
        {
            configuration.TestExecution.EnableParallelExecution = false;
            _protocolValidatorMock.Setup(value => value.ValidateJsonRpcComplianceAsync(It.IsAny<McpServerConfig>(),
                    It.IsAny<ProtocolComplianceConfig>(), It.IsAny<CancellationToken>()))
                .Returns(() =>
                {
                    cancellation.Cancel();
                    return Task.FromCanceled<ComplianceTestResult>(cancellation.Token);
                });
        }

        var result = await CreateService(registry.Object).ValidateServerAsync(configuration, cancellation.Token);

        result.OverallStatus.Should().Be(callerCancellation ? ValidationStatus.Cancelled : ValidationStatus.Error);
        result.Evidence.Coverage.Should().Contain(coverage => coverage.Scope == "obligation-1" &&
            coverage.ObservedOutcome == (callerCancellation ? ValidationOutcome.Cancelled : ValidationOutcome.Unavailable) &&
            coverage.Confidence == EvidenceConfidenceLevel.None &&
            coverage.Lane == (controlled ? EvaluationLane.ControlledApplication : null));
        var extension = result.Assessments.Extensions.Should().ContainSingle(value =>
            value.ExtensionId == "fixture.unstarted-extension").Which;
        extension.Verdict.Should().Be(ValidationVerdict.ReviewRequired);
        extension.EvidenceSummary.EvidenceCoverageRatio.Should().Be(0);
        var canonical = JsonSerializer.SerializeToNode(result.CloneWithoutSecrets(), new JsonSerializerOptions
            { PropertyNamingPolicy = JsonNamingPolicy.CamelCase })!;
        var persistedExtension = canonical["measurements"]!["extensions"]!.AsArray()
            .Single(value => value!["extensionId"]!.GetValue<string>() == "fixture.unstarted-extension")!;
        persistedExtension["coverage"].Should().NotBeNull("individual extension obligations must survive canonical persistence");
        persistedExtension["coverage"]!.AsArray().Should().Contain(value =>
            value!["scope"]!.GetValue<string>() == (planningFailure ? "obligation-pack-2" : "obligation-2"));
        foreach (var scenario in scenarios.Skip(1))
            scenario.Verify(value => value.ExecuteAsync(It.IsAny<ValidationScenarioContext>(), It.IsAny<CancellationToken>()), Times.Never);
        if (beforeScenarioPhase || planningFailure)
            scenarios[0].Verify(value => value.ExecuteAsync(It.IsAny<ValidationScenarioContext>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ValidateServerAsync_MalformedScenarioEvidence_ShouldRetainEachExtensionOnce(bool emptyCoverage)
    {
        var packs = Enumerable.Range(0, 3).Select(index =>
        {
            var scenario = new Mock<IValidationScenario>();
            scenario.SetupGet(value => value.Descriptor).Returns(new ValidationScenarioDescriptor
            {
                Key = new ValidationDescriptorKey($"scenario-{index}"), LayerKey = $"extension-{index}",
                Revision = new ValidationRevision("fixture-v1"), DisplayName = "Fixture", Stability = ValidationStability.Stable
            });
            scenario.Setup(value => value.ExecuteAsync(It.IsAny<ValidationScenarioContext>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(new ValidationScenarioExecutionResult
                {
                    Scenario = new ValidationScenarioResult { ScenarioId = $"scenario-{index}", Status = TestStatus.Passed },
                    Coverage = index == 1 ? (emptyCoverage ? [] : [null!]) : [ValidationCoverageFactory.FromTestStatus($"extension-{index}", $"scenario-{index}", TestStatus.Passed)]
                });
            var pack = new Mock<IValidationScenarioPack>();
            pack.SetupGet(value => value.Descriptor).Returns(new ValidationPackDescriptor
            {
                Key = new ValidationDescriptorKey($"pack-{index}"), Kind = ValidationPackKind.ScenarioPack,
                Revision = new ValidationRevision("fixture-v1"), DisplayName = "Fixture", Stability = ValidationStability.Stable,
                ExtensionId = $"fixture.extension-{index}", ExtensionStage = ExtensionLifecycleStage.Stable,
                SpecificationRevision = "fixture-v1", SpecificationSha256 = new string('a', 64)
            });
            pack.Setup(value => value.GetScenarios()).Returns([scenario.Object]);
            return pack.Object;
        }).ToArray();
        var registry = new Mock<IValidationPackRegistry<IValidationScenarioPack>>();
        registry.Setup(value => value.Resolve(It.IsAny<ValidationApplicabilityContext>())).Returns(packs);

        var result = await CreateService(registry.Object).ValidateServerAsync(new McpValidatorConfiguration
            { Assurance = new AssurancePolicyConfig { Profile = "inventory" }, Server = new McpServerConfig { Endpoint = "https://example.invalid/mcp", Transport = "http" } });

        result.OverallStatus.Should().Be(ValidationStatus.Error);
        result.Assessments.Extensions.Should().HaveCount(3).And.OnlyHaveUniqueItems(value => value.ExtensionId);
        result.Assessments.Extensions.Single(value => value.ExtensionId == "fixture.extension-0")
            .Coverage.Should().ContainSingle(value => value.ObservedOutcome == ValidationOutcome.Succeeded);
        result.Assessments.Extensions.Single(value => value.ExtensionId == "fixture.extension-1")
            .Coverage.Should().ContainSingle(value => value.ObservedOutcome == ValidationOutcome.Unavailable);
    }

    [Fact]
    public async Task ValidateServerAsync_InterruptedExtensionPack_ShouldRetainCompletedViolation()
    {
        var first = new Mock<IValidationScenario>();
        var second = new Mock<IValidationScenario>();
        foreach (var scenario in new[] { first, second })
            scenario.SetupGet(value => value.Descriptor).Returns(new ValidationScenarioDescriptor
            {
                Key = new ValidationDescriptorKey(scenario == first ? "completed" : "interrupted"),
                Revision = new ValidationRevision("fixture-v1"), DisplayName = "Fixture", LayerKey = "fixture-extension", Stability = ValidationStability.Stable
            });
        first.Setup(scenario => scenario.ExecuteAsync(It.IsAny<ValidationScenarioContext>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ValidationScenarioExecutionResult
            {
                Scenario = new ValidationScenarioResult
                {
                    ScenarioId = "completed", Status = TestStatus.Failed,
                    Findings = [new ValidationFinding { RuleId = "FIXTURE.EXT.FAILURE", Source = ValidationRuleSource.Spec,
                        Severity = ValidationFindingSeverity.High, Summary = "retained-extension-violation", Component = "completed" }]
                },
                Coverage = [ValidationCoverageFactory.FromTestStatus("fixture-extension", "completed", TestStatus.Failed)]
            });
        second.Setup(scenario => scenario.ExecuteAsync(It.IsAny<ValidationScenarioContext>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new OperationCanceledException());
        var pack = new Mock<IValidationScenarioPack>();
        pack.SetupGet(value => value.Descriptor).Returns(new ValidationPackDescriptor
        {
            Key = new ValidationDescriptorKey("fixture-pack"), Kind = ValidationPackKind.ScenarioPack,
            Revision = new ValidationRevision("fixture-v1"), DisplayName = "Fixture", Stability = ValidationStability.Stable,
            ExtensionId = "fixture.extension", ExtensionStage = ExtensionLifecycleStage.Stable,
            SpecificationRevision = "fixture-v1", SpecificationSha256 = new string('a', 64)
        });
        pack.Setup(value => value.GetScenarios()).Returns(new[] { first.Object, second.Object });
        var registry = new Mock<IValidationPackRegistry<IValidationScenarioPack>>();
        registry.Setup(value => value.Resolve(It.IsAny<ValidationApplicabilityContext>())).Returns(new[] { pack.Object });
        var configuration = new McpValidatorConfiguration { Assurance = new AssurancePolicyConfig { Profile = "inventory" }, Server = new McpServerConfig { Endpoint = "https://example.invalid/mcp", Transport = "http" } };

        var result = await CreateService(registry.Object).ValidateServerAsync(configuration);

        result.OverallStatus.Should().Be(ValidationStatus.Error);
        result.Assessments.Extensions.Should().ContainSingle().Which.Findings.Should().Contain(finding => finding.RuleId == "FIXTURE.EXT.FAILURE");
        result.Assurance.Claims.Should().Contain(claim => claim.Summary == "retained-extension-violation" && claim.Outcome == AssuranceClaimOutcome.Violated);
        new ValidationReportRenderer().GenerateSarifReport(result).Should().Contain("retained-extension-violation");
    }

    [Fact]
    public async Task ValidateServerAsync_WithCancelledSibling_ShouldRetainCompletedCategory()
    {
        foreach (var parallel in new[] { false, true })
        foreach (var callerCancellation in new[] { false, true })
        {
            using var cancellation = new CancellationTokenSource();
            var completed = new ComplianceTestResult { Status = TestStatus.Failed, Message = "retained protocol evidence" };
            completed.Violations.Add(new ComplianceViolation
            {
                CheckId = "AR07.RETAINED", Description = "retained-protocol-violation", Category = "protocol"
            });
            _protocolValidatorMock.Setup(validator => validator.ValidateJsonRpcComplianceAsync(
                It.IsAny<McpServerConfig>(), It.IsAny<ProtocolComplianceConfig>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(completed);
            _toolValidatorMock.Setup(validator => validator.ValidateToolDiscoveryAsync(
                It.IsAny<McpServerConfig>(), It.IsAny<ToolTestingConfig>(), It.IsAny<CancellationToken>()))
                .Returns(() =>
                {
                    if (callerCancellation) cancellation.Cancel();
                    return Task.FromException<ToolTestResult>(new OperationCanceledException(cancellation.Token));
                });
            var configuration = new McpValidatorConfiguration
            { Assurance = new AssurancePolicyConfig { Profile = "inventory" },
                Server = new McpServerConfig { Endpoint = "https://example.invalid/mcp", Transport = "http" }
            };
            configuration.TestExecution.EnableParallelExecution = parallel;
            configuration.Validation.Categories.ProtocolCompliance.TestJsonRpcCompliance = true;
            configuration.Validation.Categories.ToolTesting.TestToolDiscovery = true;
            configuration.Validation.Categories.ResourceTesting.TestResourceDiscovery = false;
            configuration.Validation.Categories.PromptTesting.TestPromptDiscovery = false;
            configuration.Validation.Categories.SecurityTesting.TestInputValidation = false;

            var result = await _validatorService.ValidateServerAsync(configuration, cancellation.Token);

            result.ProtocolCompliance.Should().BeSameAs(completed);
            result.OverallStatus.Should().Be(callerCancellation ? ValidationStatus.Cancelled : ValidationStatus.Error);
            result.Assessments.Layers.Should().Contain(layer => layer.LayerId == "protocol-core" && layer.Status == TestStatus.Failed);
            result.Evidence.Coverage.Should().Contain(coverage => coverage.Scope == "json-rpc" && coverage.ObservedOutcome == ValidationOutcome.Failed);
            result.Evidence.Coverage.Should().Contain(coverage => coverage.Scope == "tools/list" &&
                coverage.ObservedOutcome == (callerCancellation ? ValidationOutcome.Cancelled : ValidationOutcome.Unavailable));
            result.Assurance.Claims.Should().Contain(claim => claim.Summary == "retained-protocol-violation" && claim.Outcome == AssuranceClaimOutcome.Violated);
            result.Summary.FailedTests.Should().Be(1);
            result.Summary.TotalTests.Should().BeGreaterThanOrEqualTo(1);
            var renderer = new ValidationReportRenderer();
            var reports = new[]
            {
                JsonSerializer.Serialize(result.CloneWithoutSecrets()),
                new MarkdownReportGenerator().GenerateReport(result),
                renderer.GenerateHtmlReport(result, result.ValidationConfig.Reporting, verbose: true),
                renderer.GenerateXmlReport(result, verbose: true),
                renderer.GenerateSarifReport(result),
                renderer.GenerateJunitReport(result)
            };
            reports.Should().OnlyContain(report => report.Contains("retained-protocol-violation", StringComparison.Ordinal));
        }
    }

    [Fact]
    public async Task ValidateServerAsync_ApplicabilityFailure_ShouldRetainBootstrapAndFinalizeSafely()
    {
        _applicabilityResolverMock.Setup(resolver => resolver.Build(It.IsAny<ValidationSessionContext>(),
                It.IsAny<McpValidatorConfiguration>(), It.IsAny<IReadOnlyList<string>>()))
            .Throws(new InvalidOperationException("synthetic-applicability-secret"));

        var result = await _validatorService.ValidateServerAsync(new McpValidatorConfiguration
        { Assurance = new AssurancePolicyConfig { Profile = "inventory" },
            Server = new McpServerConfig { Endpoint = "https://example.invalid/mcp", Transport = "http" }
        });

        result.OverallStatus.Should().Be(ValidationStatus.Error);
        result.CapabilitySnapshot.Should().NotBeNull();
        result.Evidence.Coverage.Should().NotBeEmpty();
        JsonSerializer.Serialize(result).Should().NotContain("synthetic-applicability-secret");
        _protocolValidatorMock.Invocations.Should().BeEmpty();
    }

    [Fact]
    public async Task ValidateServerAsync_BootstrapFrameworkFailure_ShouldFinalizeWithoutLeakingExceptionPayload()
    {
        _sessionBuilderMock.Setup(builder => builder.BuildAsync(It.IsAny<McpValidatorConfiguration>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("synthetic-bootstrap-secret"));

        var result = await _validatorService.ValidateServerAsync(new McpValidatorConfiguration
        { Assurance = new AssurancePolicyConfig { Profile = "inventory" },
            Server = new McpServerConfig { Endpoint = "https://example.invalid/mcp", Transport = "http" }
        });

        result.OverallStatus.Should().Be(ValidationStatus.Error);
        result.Evidence.Coverage.Should().NotBeEmpty();
        result.Assurance.Should().NotBeNull();
        result.CriticalErrors.Should().NotBeEmpty();
        JsonSerializer.Serialize(result).Should().NotContain("synthetic-bootstrap-secret");
        _protocolValidatorMock.Invocations.Should().BeEmpty();
    }

    [Fact]
    public async Task ValidateServerAsync_TypedBootstrapRejection_ShouldNotClaimCompletedExecution()
    {
        _sessionBuilderMock.Setup(builder => builder.BuildAsync(It.IsAny<McpValidatorConfiguration>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new ValidationSessionException("Synthetic bootstrap rejection.", ValidationStatus.Failed));
        var config = CreateFunctionalCategoryConfiguration();
        config.Validation.Categories.ToolTesting.TestToolDiscovery = false;

        var result = await _validatorService.ValidateServerAsync(config);
        var reloaded = JsonSerializer.Deserialize<ValidationResult>(JsonSerializer.Serialize(result.CloneWithoutSecrets()))!;

        result.Run.TerminationOutcome.Should().Be(ValidationOutcome.Inconclusive);
        reloaded.Run.TerminationOutcome.Should().Be(ValidationOutcome.Inconclusive);
        AssuranceExitCodeMapper.Map(result).Should().Be(2);
        AssuranceExitCodeMapper.Map(reloaded).Should().Be(2);
        _protocolValidatorMock.Invocations.Should().BeEmpty();
        _toolValidatorMock.Invocations.Should().BeEmpty();
    }

    [Fact]
    public async Task ValidateServerAsync_PartialSemantics_ShouldNotDependOnParallelCompletionOrder()
    {
        string? expected = null;
        foreach (var cancelFirst in new[] { false, true })
        {
            var protocol = new TaskCompletionSource<ComplianceTestResult>(TaskCreationOptions.RunContinuationsAsynchronously);
            var tools = new TaskCompletionSource<ToolTestResult>(TaskCreationOptions.RunContinuationsAsynchronously);
            var bothStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var starts = 0;
            _protocolValidatorMock.Setup(validator => validator.ValidateJsonRpcComplianceAsync(
                It.IsAny<McpServerConfig>(), It.IsAny<ProtocolComplianceConfig>(), It.IsAny<CancellationToken>()))
                .Returns(() => { if (Interlocked.Increment(ref starts) == 2) bothStarted.SetResult(); return protocol.Task; });
            _toolValidatorMock.Setup(validator => validator.ValidateToolDiscoveryAsync(
                It.IsAny<McpServerConfig>(), It.IsAny<ToolTestingConfig>(), It.IsAny<CancellationToken>()))
                .Returns(() => { if (Interlocked.Increment(ref starts) == 2) bothStarted.SetResult(); return tools.Task; });
            var configuration = new McpValidatorConfiguration
            { Assurance = new AssurancePolicyConfig { Profile = "inventory" },
                Server = new McpServerConfig { Endpoint = "https://example.invalid/mcp", Transport = "http" }
            };
            configuration.TestExecution.EnableParallelExecution = true;
            configuration.Validation.Categories.ProtocolCompliance.TestJsonRpcCompliance = true;
            configuration.Validation.Categories.ToolTesting.TestToolDiscovery = true;
            configuration.Validation.Categories.ResourceTesting.TestResourceDiscovery = false;
            configuration.Validation.Categories.PromptTesting.TestPromptDiscovery = false;
            configuration.Validation.Categories.SecurityTesting.TestInputValidation = false;
            var run = _validatorService.ValidateServerAsync(configuration);
            await bothStarted.Task.WaitAsync(TimeSpan.FromSeconds(10));
            var completed = new ComplianceTestResult { Status = TestStatus.Failed };
            completed.Violations.Add(new ComplianceViolation { CheckId = "AR07.ORDER", Description = "stable-order-violation", Category = "protocol" });
            if (cancelFirst) tools.SetException(new OperationCanceledException());
            protocol.SetResult(completed);
            if (!cancelFirst) tools.SetException(new OperationCanceledException());
            var result = await run.WaitAsync(TimeSpan.FromSeconds(10));
            var semantic = JsonSerializer.Serialize(new { result.OverallStatus, result.Assessments.Layers, result.Evidence.Coverage, result.Assurance.Claims });
            if (expected is null) expected = semantic;
            else semantic.Should().Be(expected);
        }
    }

    [Fact]
    public async Task ValidateServerAsync_WithReviewRequiredFindings_ShouldReturnFailedResult()
    {
        // Arrange
        var config = new McpValidatorConfiguration
        { Assurance = new AssurancePolicyConfig { Profile = "inventory" },
            Server = new McpServerConfig { Endpoint = "https://test-server.com/mcp" },
            Validation = new ValidationConfig
            {
                Categories = new ValidationScenarios
                {
                    ProtocolCompliance = new ProtocolComplianceConfig
                    {
                        TestJsonRpcCompliance = true,
                        TestInitialization = true
                    },
                    ToolTesting = new ToolTestingConfig
                    {
                        TestToolDiscovery = true
                    },
                    SecurityTesting = new SecurityTestingConfig
                    {
                        TestAuthenticationBypass = true
                    },
                    PerformanceTesting = new PerformanceTestingConfig
                    {
                        TestConcurrentRequests = true
                    }
                }
            }
        };

        // Mock successful protocol validation
        _protocolValidatorMock
            .Setup(x => x.ValidateJsonRpcComplianceAsync(It.IsAny<McpServerConfig>(), It.IsAny<ProtocolComplianceConfig>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ComplianceTestResult
            {
                Status = TestStatus.Passed,
                ComplianceScore = 100.0,
                Violations = new List<ComplianceViolation>
                {
                    new()
                    {
                        CheckId = "MCP.INIT.VERSION_NEGOTIATION",
                        Category = "Initialization",
                        Severity = ViolationSeverity.Medium,
                        Description = "Server negotiated the requested version after retry."
                    }
                }
            });

        _protocolValidatorMock
            .Setup(x => x.ValidateInitializationAsync(It.IsAny<McpServerConfig>(), It.IsAny<ProtocolComplianceConfig>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ComplianceTestResult { Status = TestStatus.Passed, ComplianceScore = 100.0 });

        _protocolValidatorMock
            .Setup(x => x.ValidateNotificationHandlingAsync(It.IsAny<McpServerConfig>(), It.IsAny<ProtocolComplianceConfig>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ComplianceTestResult { Status = TestStatus.Passed, ComplianceScore = 100.0 });

        // Mock successful tool validation
        _toolValidatorMock
            .Setup(x => x.ValidateToolDiscoveryAsync(
                It.IsAny<McpServerConfig>(),
                It.Is<ToolTestingConfig>(cfg => cfg.CapabilitySnapshot != null),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ToolTestResult
            {
                Status = TestStatus.Passed,
                ToolsDiscovered = 1,
                DiscoveredToolNames = ["delete_repo"],
                CatalogEnumeration = new() { IsComplete = true, Termination = CatalogEnumerationTermination.Complete, RecoveredItems = 1 },
                ToolResults = new List<IndividualToolResult>
                {
                    new()
                    {
                        ToolName = "delete_repo",
                        DisplayTitle = "Delete Repository",
                        Status = TestStatus.Passed,
                        ExecutionSuccessful = true,
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
                    }
                }
            });

        // Mock successful security validation using correct interface method
        _securityValidatorMock
            .Setup(x => x.PerformSecurityAssessmentAsync(It.IsAny<McpServerConfig>(), It.IsAny<SecurityTestingConfig>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new SecurityTestResult
            {
                Status = TestStatus.Passed,
                AuthenticationTestResult = new AuthenticationTestResult
                {
                    Status = TestStatus.Passed,
                    TestScenarios = new List<AuthenticationScenario>
                    {
                        new()
                        {
                            ScenarioName = "Unauthorized tools/list challenge",
                            Method = "tools/list",
                            TestType = "challenge",
                            ActualBehavior = "Returned 401 with WWW-Authenticate.",
                            Analysis = "Challenge flow aligned with the expected bearer-token pattern.",
                            IsCompliant = true,
                            IsSecure = true,
                            IsStandardsAligned = true,
                            AssessmentDisposition = AuthenticationAssessmentDisposition.StandardsAligned,
                            StatusCode = "401"
                        }
                    }
                }
            });

        // Mock successful performance validation using correct interface method
        _performanceValidatorMock
            .Setup(x => x.PerformLoadTestingAsync(It.IsAny<McpServerConfig>(), It.IsAny<PerformanceTestingConfig>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PerformanceTestResult { Status = TestStatus.Passed });

        // Act
        var result = await _validatorService.ValidateServerAsync(config, CancellationToken.None);

        // Assert
        result.Should().NotBeNull();
        result.OverallStatus.Should().Be(ValidationStatus.Failed, string.Join("; ", result.CriticalErrors));
        result.ComplianceScore.Should().BeGreaterThan(0);
        result.ScoringDetails.Should().NotBeNull();
        result.ScoringDetails!.OverallScore.Should().Be(result.ComplianceScore);
        result.Assurance.Should().NotBeNull();
        result.Assurance.Assessments.CoreConformance.Status.Should().Be(CoreConformanceStatus.Nonconformant);
        result.Run.OperationalMetrics.Should().NotBeNull();
        result.Run.OperationalMetrics!.RunCorrelationId.Should().Be(result.ValidationId);
        result.Run.OperationalMetrics.TotalRunDurationMs.Should().BeGreaterThan(0);
        result.Run.OperationalMetrics.StageDurationMs.Should().ContainKeys(
            "session.bootstrap",
            "validation.categories",
            "validation.scoring");
        result.Run.OperationalMetrics.StageDurationMs.Keys.Should().NotContain(key => key.StartsWith("rule.", StringComparison.Ordinal));
        result.Run.OperationalMetrics.EvidenceCoverageRatio.Should().Be(result.Assurance.EvidenceSummary.EvidenceCoverageRatio);
        config.Validation.Categories.ToolTesting.CapabilitySnapshot.Should().BeNull();
        config.Validation.Categories.ResourceTesting.CapabilitySnapshot.Should().BeNull();
        config.Validation.Categories.PromptTesting.CapabilitySnapshot.Should().BeNull();
        result.ValidationConfig.Validation.Categories.ToolTesting.CapabilitySnapshot.Should().BeNull();
        result.ValidationConfig.Validation.Categories.ResourceTesting.CapabilitySnapshot.Should().BeNull();
        result.ValidationConfig.Validation.Categories.PromptTesting.CapabilitySnapshot.Should().BeNull();
        result.CapabilitySnapshot.Should().BeSameAs(_defaultCapabilitySnapshot);
        result.Run.SchemaVersion.Should().Be(result.ProtocolVersion);
        result.Run.ApplicabilityContext.Should().NotBeNull();
        result.Run.ApplicabilityContext!.NegotiatedProtocolVersion.Should().Be(result.ProtocolVersion);
        result.Run.ApplicabilityContext.Transport.Should().Be(config.Server.Transport);
        result.Evidence.AppliedPacks.Select(pack => pack.Key.Value)
            .Should().Contain(["protocol-features/mcp-embedded", "scenario-pack/observed-surface", "rule-pack/protocol-core"]);
        result.Assessments.Layers.Should().Contain(layer => layer.LayerId == "protocol-core" && layer.Status == TestStatus.Passed);
        result.Evidence.Coverage.Should().Contain(coverage =>
            coverage.LayerId == "protocol-core" &&
            coverage.Scope == "json-rpc" &&
            coverage.Status == ValidationCoverageStatus.Covered);
        result.Assessments.Scenarios.Should().Contain(scenario =>
            scenario.ScenarioId == "tool-catalog-smoke" &&
            scenario.Status == TestStatus.Passed);
        result.Assessments.Scenarios.Should().Contain(scenario =>
            scenario.ScenarioId == "security-authentication-challenge" &&
            scenario.Status == TestStatus.Skipped &&
            scenario.Summary!.Contains("do not apply to STDIO", StringComparison.Ordinal));
        result.Evidence.Coverage.Should().Contain(coverage =>
            coverage.Scope == "security-authentication-challenge" &&
            coverage.Status == ValidationCoverageStatus.NotApplicable &&
            coverage.ObservedOutcome == ValidationOutcome.NotApplicable);
        result.Assessments.Scenarios.Should().Contain(scenario =>
            scenario.ScenarioId == "error-handling-matrix" &&
            scenario.Status == TestStatus.Passed);
        result.Evidence.Observations.Should().Contain(observation =>
            observation.Id == "tool-delete-repo" &&
            observation.LayerId == "tool-surface");
        result.Evidence.Observations.Should().Contain(observation =>
            observation.Id == "protocol-mcp-init-version-negotiation" &&
            observation.LayerId == "protocol-core");
        result.Evidence.Observations.Should().Contain(observation =>
            observation.Id == "error-invalid-method-call" &&
            observation.LayerId == "error-handling");
        result.Evidence.Coverage.Should().Contain(coverage =>
            coverage.LayerId == "error-handling" &&
            coverage.Scope == "error-handling" &&
            coverage.Status == ValidationCoverageStatus.Covered);
    }

    [Fact]
    public async Task ValidateServerAsync_WithAuthenticatedProfile_ShouldClampFunctionalProbeConcurrency()
    {
        var config = new McpValidatorConfiguration
        { Assurance = new AssurancePolicyConfig { Profile = "inventory" },
            Server = new McpServerConfig
            {
                Endpoint = "https://test-server.com/mcp",
                Profile = McpServerProfile.Authenticated,
                Authentication = new AuthenticationConfig { Type = "bearer", Token = "token" }
            },
            Validation = new ValidationConfig
            {
                Categories = new ValidationScenarios()
            }
        };

        await _validatorService.ValidateServerAsync(config, CancellationToken.None);

        _httpClientMock.Verify(client => client.SetConcurrencyLimit(1), Times.AtLeastOnce);
    }

    [Fact]
    public async Task ValidateServerAsync_WithProtocolFailure_ShouldReturnFailedResult()
    {
        // Arrange
        var config = new McpValidatorConfiguration
        { Assurance = new AssurancePolicyConfig { Profile = "inventory" },
            Server = new McpServerConfig { Endpoint = "https://test-server.com/mcp" },
            Validation = new ValidationConfig
            {
                Categories = new ValidationScenarios
                {
                    ProtocolCompliance = new ProtocolComplianceConfig
                    {
                        TestJsonRpcCompliance = true
                    }
                }
            }
        };

        // Mock failed protocol validation with proper ComplianceViolation objects
        _protocolValidatorMock
            .Setup(x => x.ValidateJsonRpcComplianceAsync(It.IsAny<McpServerConfig>(), It.IsAny<ProtocolComplianceConfig>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ComplianceTestResult 
            { 
                Status = TestStatus.Failed, 
                ComplianceScore = 50.0,
                Violations = new List<ComplianceViolation> 
                { 
                    new ComplianceViolation 
                    { 
                        Category = "JSON-RPC", 
                        Description = "Missing jsonrpc field",
                        Severity = ViolationSeverity.High
                    } 
                }
            });

        // Setup scoring strategy to reflect failure
        _scoringStrategyMock.Setup(x => x.CalculateScore(It.IsAny<ValidationResult>()))
            .Returns(new ScoringResult { OverallScore = 50.0, Status = ValidationStatus.Failed });

        // Act
        var result = await _validatorService.ValidateServerAsync(config, CancellationToken.None);

        // Assert
        result.Should().NotBeNull();
        result.OverallStatus.Should().Be(ValidationStatus.Failed, string.Join("; ", result.CriticalErrors));
        result.ComplianceScore.Should().BeLessThan(100);
        config.Validation.Categories.ToolTesting.CapabilitySnapshot.Should().BeNull();
        config.Validation.Categories.ResourceTesting.CapabilitySnapshot.Should().BeNull();
        config.Validation.Categories.PromptTesting.CapabilitySnapshot.Should().BeNull();
        result.CapabilitySnapshot.Should().BeSameAs(_defaultCapabilitySnapshot);
    }

    [Fact]
    public async Task ValidateServerAsync_ShouldRunPerformanceAfterCoreCategories()
    {
        var protocolCompleted = false;

        var config = new McpValidatorConfiguration
        { Assurance = new AssurancePolicyConfig { Profile = "inventory" },
            Server = new McpServerConfig { Endpoint = "https://test-server.com/mcp" },
            Validation = new ValidationConfig
            {
                Categories = new ValidationScenarios
                {
                    ProtocolCompliance = new ProtocolComplianceConfig
                    {
                        TestJsonRpcCompliance = true
                    },
                    PerformanceTesting = new PerformanceTestingConfig
                    {
                        TestConcurrentRequests = true
                    }
                }
            }
        };

        _protocolValidatorMock
            .Setup(x => x.ValidateJsonRpcComplianceAsync(It.IsAny<McpServerConfig>(), It.IsAny<ProtocolComplianceConfig>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(() =>
            {
                protocolCompleted = true;
                return new ComplianceTestResult { Status = TestStatus.Passed, ComplianceScore = 100.0 };
            });

        _performanceValidatorMock
            .Setup(x => x.PerformLoadTestingAsync(It.IsAny<McpServerConfig>(), It.IsAny<PerformanceTestingConfig>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(() =>
            {
                protocolCompleted.Should().BeTrue();
                return new PerformanceTestResult { Status = TestStatus.Passed, Score = 100.0 };
            });

        await _validatorService.ValidateServerAsync(config, CancellationToken.None);

        protocolCompleted.Should().BeTrue();
    }

    [Fact]
    public async Task ValidateServerAsync_WithPublicTimeoutAndNoPerformanceMeasurements_ShouldCalibrateToAdvisory()
    {
        var config = new McpValidatorConfiguration
        { Assurance = new AssurancePolicyConfig { Profile = "inventory" },
            Server = new McpServerConfig
            {
                Endpoint = "https://test-server.com/mcp",
                Profile = McpServerProfile.Public
            },
            Validation = new ValidationConfig
            {
                Categories = new ValidationScenarios
                {
                    ProtocolCompliance = new ProtocolComplianceConfig
                    {
                        TestJsonRpcCompliance = false,
                        TestInitialization = false,
                        TestCapabilities = false,
                        TestNotifications = false,
                        TestMessageFormat = false
                    },
                    ToolTesting = new ToolTestingConfig
                    {
                        TestToolDiscovery = false,
                        TestToolExecution = false,
                        TestParameterValidation = false
                    },
                    ResourceTesting = new ResourceTestingConfig
                    {
                        TestResourceDiscovery = false,
                        TestResourceReading = false,
                        TestUriValidation = false,
                        TestSubscriptions = false
                    },
                    PromptTesting = new PromptTestingConfig
                    {
                        TestPromptDiscovery = false,
                        TestPromptExecution = false,
                        TestArgumentValidation = false
                    },
                    SecurityTesting = new SecurityTestingConfig
                    {
                        TestInputValidation = false,
                        TestInjectionAttacks = false,
                        TestAuthenticationBypass = false,
                        TestBufferOverflow = false,
                        TestMalformedMessages = false,
                        TestResourceExhaustion = false
                    },
                    PerformanceTesting = new PerformanceTestingConfig
                    {
                        TestConcurrentRequests = true,
                        TestResponseTimes = false,
                        TestMemoryUsage = false,
                        TestThroughput = false
                    },
                    ErrorHandling = new ErrorHandlingConfig
                    {
                        TestInvalidMethods = false,
                        TestMalformedJson = false,
                        TestConnectionInterruption = false,
                        TestTimeoutHandling = false
                    }
                }
            }
        };

        _performanceValidatorMock
            .Setup(x => x.PerformLoadTestingAsync(It.IsAny<McpServerConfig>(), It.IsAny<PerformanceTestingConfig>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PerformanceTestResult
            {
                Status = TestStatus.Failed,
                Score = 0,
                MeasurementDisposition = PerformanceMeasurementDisposition.TimedOut,
                Message = ValidationConstants.Messages.OperationTimedOutOrWasCancelled,
                CriticalErrors = new List<string> { ValidationConstants.Messages.OperationTimedOutOrWasCancelled }
            });

        var result = await _validatorService.ValidateServerAsync(config, CancellationToken.None);

        result.PerformanceTesting.Should().NotBeNull();
        result.PerformanceTesting!.Status.Should().Be(TestStatus.Skipped);
        result.PerformanceTesting.Score.Should().Be(ValidationCalibration.AdvisoryPerformanceScore);
        result.PerformanceTesting.Message.Should().Contain("advisory");
        result.PerformanceTesting.Findings.Should().Contain(f => f.RuleId == ValidationFindingRuleIds.PerformancePublicRemoteTimeoutAdvisory);
        result.Summary.SkippedTests.Should().Be(1);
        result.Summary.FailedTests.Should().Be(0);
        result.Assurance.Assessments.OperationalReadiness.Status.Should().Be(OperationalReadinessStatus.Incomplete);
        result.Assurance.Claims.Should().Contain(claim =>
            claim.Metadata.GetValueOrDefault("ruleId") == ValidationFindingRuleIds.PerformancePublicRemoteTimeoutAdvisory &&
            claim.Outcome == AssuranceClaimOutcome.ReviewRequired);
    }

    [Fact]
    public async Task ValidateServerAsync_ShouldKeepScoringNotesSeparateFromRecommendations()
    {
        var config = new McpValidatorConfiguration
        { Assurance = new AssurancePolicyConfig { Profile = "inventory" },
            Server = new McpServerConfig { Endpoint = "https://test-server.com/mcp" },
            Validation = new ValidationConfig
            {
                Categories = new ValidationScenarios()
            }
        };

        const string scoringNote = "Coverage-weighted score preserved for reporting only.";

        _scoringStrategyMock.Setup(x => x.CalculateScore(It.IsAny<ValidationResult>()))
            .Returns(new ScoringResult
            {
                OverallScore = 100.0,
                Status = ValidationStatus.Passed,
                ScoringNotes = new List<string> { scoringNote }
            });

        var result = await _validatorService.ValidateServerAsync(config, CancellationToken.None);

        result.ScoringNotes.Should().Contain(scoringNote);
        result.Recommendations.Should().NotContain(scoringNote);
    }

    [Fact]
    public async Task ValidateServerAsync_ShouldGenerateEvidenceDerivedRecommendationsInsteadOfBoilerplate()
    {
        var config = new McpValidatorConfiguration
        { Assurance = new AssurancePolicyConfig { Profile = "inventory" },
            Server = new McpServerConfig { Endpoint = "https://test-server.com/mcp" },
            Validation = new ValidationConfig
            {
                Categories = new ValidationScenarios
                {
                    ProtocolCompliance = new ProtocolComplianceConfig
                    {
                        TestJsonRpcCompliance = true
                    },
                    ToolTesting = new ToolTestingConfig
                    {
                        TestToolDiscovery = true
                    },
                    PerformanceTesting = new PerformanceTestingConfig
                    {
                        TestConcurrentRequests = true
                    }
                }
            }
        };

        _protocolValidatorMock
            .Setup(x => x.ValidateJsonRpcComplianceAsync(It.IsAny<McpServerConfig>(), It.IsAny<ProtocolComplianceConfig>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ComplianceTestResult
            {
                Status = TestStatus.Failed,
                ComplianceScore = 81,
                Violations = new List<ComplianceViolation>
                {
                    new()
                    {
                        CheckId = "MCP.INIT.VERSION_NEGOTIATION",
                        Category = "Initialization",
                        Severity = ViolationSeverity.High,
                        Description = "Server ignored the requested MCP protocol version.",
                        Recommendation = "Echo the negotiated protocol version in initialize.result."
                    }
                }
            });

        _toolValidatorMock
            .Setup(x => x.ValidateToolDiscoveryAsync(
                It.IsAny<McpServerConfig>(),
                It.Is<ToolTestingConfig>(cfg => cfg.CapabilitySnapshot != null),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ToolTestResult
            {
                Status = TestStatus.Passed,
                ToolsDiscovered = 41,
                Findings = new List<ValidationFinding>
                {
                    new()
                    {
                        RuleId = ValidationFindingRuleIds.ToolGuidelineReadOnlyHintMissing,
                        Category = "McpGuideline",
                        Component = "create_branch",
                        Severity = ValidationFindingSeverity.Low,
                        Recommendation = "Declare readOnlyHint so agents can distinguish read-only tools from state-changing tools."
                    },
                    new()
                    {
                        RuleId = ValidationFindingRuleIds.AiReadinessVagueStringSchema,
                        Category = "AiReadiness",
                        Component = "create_branch",
                        Severity = ValidationFindingSeverity.Medium,
                        Recommendation = "Constrain string parameters with enum, pattern, or format metadata when possible."
                    }
                }
            });

        _performanceValidatorMock
            .Setup(x => x.PerformLoadTestingAsync(It.IsAny<McpServerConfig>(), It.IsAny<PerformanceTestingConfig>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PerformanceTestResult
            {
                Status = TestStatus.Failed,
                Score = 0,
                MeasurementDisposition = PerformanceMeasurementDisposition.TimedOut,
                Message = ValidationConstants.Messages.OperationTimedOutOrWasCancelled,
                CriticalErrors = new List<string> { ValidationConstants.Messages.OperationTimedOutOrWasCancelled }
            });

        _scoringStrategyMock.Setup(x => x.CalculateScore(It.IsAny<ValidationResult>()))
            .Returns(new ScoringResult
            {
                OverallScore = 68.4,
                Status = ValidationStatus.Passed
            });

        var result = await _validatorService.ValidateServerAsync(config, CancellationToken.None);

        result.Recommendations.Should().Contain(recommendation => recommendation.StartsWith("Spec:", StringComparison.Ordinal));
        result.Recommendations.Should().Contain(recommendation => recommendation.StartsWith("Guideline:", StringComparison.Ordinal));
        result.Recommendations.Should().Contain(recommendation => recommendation.StartsWith("Heuristic:", StringComparison.Ordinal));
        result.Recommendations.Should().Contain(recommendation => recommendation.StartsWith("Operational:", StringComparison.Ordinal));
        result.Recommendations.Should().NotContain(recommendation => recommendation.Contains("improving overall compliance score", StringComparison.OrdinalIgnoreCase));
        result.Recommendations.Should().NotContain(recommendation => recommendation.Contains("failed test cases", StringComparison.OrdinalIgnoreCase));
        result.Recommendations.Should().NotContain(recommendation => recommendation.Contains("Ensure full compliance with MCP protocol specification", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task ValidateServerAsync_CategoryFailures_ShouldReduceErrorsInStableCategoryOrder()
    {
        var config = CreateFunctionalCategoryConfiguration();
        _protocolValidatorMock
            .Setup(validator => validator.ValidateJsonRpcComplianceAsync(It.IsAny<McpServerConfig>(), It.IsAny<ProtocolComplianceConfig>(), It.IsAny<CancellationToken>()))
            .Returns(async (McpServerConfig _, ProtocolComplianceConfig _, CancellationToken ct) =>
            {
                await Task.Delay(50, ct);
                throw new InvalidOperationException("protocol-failure");
            });
        _toolValidatorMock
            .Setup(validator => validator.ValidateToolDiscoveryAsync(It.IsAny<McpServerConfig>(), It.IsAny<ToolTestingConfig>(), It.IsAny<CancellationToken>()))
            .Returns(async (McpServerConfig _, ToolTestingConfig _, CancellationToken ct) =>
            {
                await Task.Delay(40, ct);
                throw new InvalidOperationException("tool-failure");
            });
        _resourceValidatorMock
            .Setup(validator => validator.ValidateResourceDiscoveryAsync(It.IsAny<McpServerConfig>(), It.IsAny<ResourceTestingConfig>(), It.IsAny<CancellationToken>()))
            .Returns(async (McpServerConfig _, ResourceTestingConfig _, CancellationToken ct) =>
            {
                await Task.Delay(30, ct);
                throw new InvalidOperationException("resource-failure");
            });
        _promptValidatorMock
            .Setup(validator => validator.ValidatePromptDiscoveryAsync(It.IsAny<McpServerConfig>(), It.IsAny<PromptTestingConfig>(), It.IsAny<CancellationToken>()))
            .Returns(async (McpServerConfig _, PromptTestingConfig _, CancellationToken ct) =>
            {
                await Task.Delay(20, ct);
                throw new InvalidOperationException("prompt-failure");
            });
        _securityValidatorMock
            .Setup(validator => validator.PerformSecurityAssessmentAsync(It.IsAny<McpServerConfig>(), It.IsAny<SecurityTestingConfig>(), It.IsAny<CancellationToken>()))
            .Returns(async (McpServerConfig _, SecurityTestingConfig _, CancellationToken ct) =>
            {
                await Task.Delay(10, ct);
                throw new InvalidOperationException("security-failure");
            });

        var result = await _validatorService.ValidateServerAsync(config, CancellationToken.None);

        result.CriticalErrors.Take(5).Should().Equal(
            "Protocol compliance validation error: protocol-failure",
            "Tool validation error: tool-failure",
            "Resource validation error: resource-failure",
            "Prompt validation error: prompt-failure",
            "Security testing error: security-failure");
        result.ProtocolCompliance!.Status.Should().Be(TestStatus.Error);
        result.ToolValidation!.Status.Should().Be(TestStatus.Error);
        result.ResourceTesting!.Status.Should().Be(TestStatus.Error);
        result.PromptTesting!.Status.Should().Be(TestStatus.Error);
        result.SecurityTesting!.Status.Should().Be(TestStatus.Error);
        result.CriticalErrors.Should().NotContain(
            error => error.StartsWith("Validation framework error:", StringComparison.Ordinal),
            string.Join(Environment.NewLine, result.CriticalErrors));
        result.Assurance.Should().NotBeNull();
        result.OverallStatus.Should().Be(ValidationStatus.Error);
    }

    [Fact]
    public async Task ValidateServerAsync_CallerCancellation_ShouldReturnCancelledWithoutCategoryFailure()
    {
        var config = CreateFunctionalCategoryConfiguration();
        config.Validation.Categories.ToolTesting.TestToolDiscovery = false;
        config.Validation.Categories.ResourceTesting.TestResourceDiscovery = false;
        config.Validation.Categories.PromptTesting.TestPromptDiscovery = false;
        config.Validation.Categories.SecurityTesting.TestInputValidation = false;
        _protocolValidatorMock
            .Setup(validator => validator.ValidateJsonRpcComplianceAsync(It.IsAny<McpServerConfig>(), It.IsAny<ProtocolComplianceConfig>(), It.IsAny<CancellationToken>()))
            .Returns(async (McpServerConfig _, ProtocolComplianceConfig _, CancellationToken ct) =>
            {
                await Task.Delay(Timeout.InfiniteTimeSpan, ct);
                return new ComplianceTestResult();
            });
        using var cancellation = new CancellationTokenSource(TimeSpan.FromMilliseconds(50));

        var result = await _validatorService.ValidateServerAsync(config, cancellation.Token);

        result.OverallStatus.Should().Be(ValidationStatus.Cancelled);
        result.CriticalErrors.Should().BeEmpty();
    }

    [Fact]
    public async Task ValidateServerAsync_InternalBootstrapCancellation_ShouldReturnFrameworkError()
    {
        var config = CreateFunctionalCategoryConfiguration();
        _sessionBuilderMock
            .Setup(builder => builder.BuildAsync(It.IsAny<McpValidatorConfiguration>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new OperationCanceledException("internal timeout"));

        var result = await _validatorService.ValidateServerAsync(config, CancellationToken.None);

        result.OverallStatus.Should().Be(ValidationStatus.Error);
        result.CriticalErrors.Should().ContainSingle(error => error.Contains("internal operation timed out", StringComparison.Ordinal));
        result.Run.OperationalMetrics.Should().NotBeNull();
    }

    [Theory]
    [InlineData("category")]
    [InlineData("error-handling")]
    [InlineData("performance")]
    public async Task ValidateServerAsync_InternalStageCancellation_ShouldReturnFrameworkError(string stage)
    {
        var config = CreateFunctionalCategoryConfiguration();
        if (stage == "category")
        {
            _protocolValidatorMock
                .Setup(validator => validator.ValidateJsonRpcComplianceAsync(It.IsAny<McpServerConfig>(), It.IsAny<ProtocolComplianceConfig>(), It.IsAny<CancellationToken>()))
                .ThrowsAsync(new OperationCanceledException("internal category timeout"));
        }
        else if (stage == "error-handling")
        {
            config.Validation.Categories.ErrorHandling.TestTimeoutHandling = true;
            _errorHandlingValidatorMock
                .Setup(validator => validator.ValidateErrorHandlingAsync(It.IsAny<McpServerConfig>(), It.IsAny<ErrorHandlingConfig>(), It.IsAny<CancellationToken>()))
                .ThrowsAsync(new OperationCanceledException("internal error-handling timeout"));
        }
        else
        {
            config.Validation.Categories.PerformanceTesting.TestConcurrentRequests = true;
            _performanceValidatorMock
                .Setup(validator => validator.PerformLoadTestingAsync(It.IsAny<McpServerConfig>(), It.IsAny<PerformanceTestingConfig>(), It.IsAny<CancellationToken>()))
                .ThrowsAsync(new OperationCanceledException("internal performance timeout"));
        }

        var result = await _validatorService.ValidateServerAsync(config, CancellationToken.None);

        result.OverallStatus.Should().Be(ValidationStatus.Error);
        result.CriticalErrors.Should().ContainSingle(error => error.Contains("internal operation timed out", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData("http", true, 2)]
    [InlineData("http", false, 1)]
    [InlineData("stdio", true, 1)]
    public async Task ValidateServerAsync_CategoryScheduling_ShouldHonorPolicyAndTransport(
        string transport,
        bool parallelEnabled,
        int expectedMaximumConcurrency)
    {
        var config = CreateFunctionalCategoryConfiguration();
        config.Server.Transport = transport;
        config.TestExecution.EnableParallelExecution = parallelEnabled;
        config.Validation.Categories.ResourceTesting.TestResourceDiscovery = false;
        config.Validation.Categories.PromptTesting.TestPromptDiscovery = false;
        config.Validation.Categories.SecurityTesting.TestInputValidation = false;
        var active = 0;
        var maximum = 0;

        async Task<T> TrackAsync<T>(T value, CancellationToken cancellationToken)
        {
            var current = Interlocked.Increment(ref active);
            var observed = Volatile.Read(ref maximum);
            while (current > observed)
            {
                var prior = Interlocked.CompareExchange(ref maximum, current, observed);
                if (prior == observed)
                {
                    break;
                }

                observed = prior;
            }

            try
            {
                await Task.Delay(50, cancellationToken);
                return value;
            }
            finally
            {
                Interlocked.Decrement(ref active);
            }
        }

        _protocolValidatorMock
            .Setup(validator => validator.ValidateJsonRpcComplianceAsync(It.IsAny<McpServerConfig>(), It.IsAny<ProtocolComplianceConfig>(), It.IsAny<CancellationToken>()))
            .Returns((McpServerConfig _, ProtocolComplianceConfig _, CancellationToken ct) =>
                TrackAsync(new ComplianceTestResult { Status = TestStatus.Passed, Score = 100 }, ct));
        _toolValidatorMock
            .Setup(validator => validator.ValidateToolDiscoveryAsync(It.IsAny<McpServerConfig>(), It.IsAny<ToolTestingConfig>(), It.IsAny<CancellationToken>()))
            .Returns((McpServerConfig _, ToolTestingConfig _, CancellationToken ct) =>
                TrackAsync(new ToolTestResult { Status = TestStatus.Passed, Score = 100 }, ct));

        await _validatorService.ValidateServerAsync(config, CancellationToken.None);

        maximum.Should().Be(expectedMaximumConcurrency);
    }

    [Fact]
    public async Task PerformHealthCheckAsync_HostPolicyAlreadyConfigured_ShouldNotResetTransportPolicy()
    {
        _httpClientMock.SetupGet(client => client.IsExecutionPolicyConfigured).Returns(true);
        var server = new McpServerConfig { Endpoint = "https://example.test/mcp", Transport = "http" };

        await _validatorService.PerformHealthCheckAsync(server, CancellationToken.None);

        _httpClientMock.Verify(client => client.ConfigureExecutionPolicy(It.IsAny<ExecutionPolicy?>()), Times.Never);
    }

    [Fact]
    public async Task DiscoverServerCapabilitiesAsync_ModernProtocol_ShouldUseServerDiscoverSessionWithoutInitialize()
    {
        var server = new McpServerConfig
        {
            Endpoint = "https://example.test/mcp",
            Transport = "http",
            ProtocolVersion = "2026-07-28",
            ProtocolEra = McpProtocolEraSelection.Modern
        };
        _sessionBuilderMock
            .Setup(builder => builder.BuildAsync(
                It.Is<McpValidatorConfiguration>(configuration => configuration.Server.ProtocolEra == McpProtocolEraSelection.Modern),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync((McpValidatorConfiguration configuration, CancellationToken _) => new ValidationSessionContext(
                configuration,
                configuration.Server.CloneForExecution())
            {
                ProtocolVersion = "2026-07-28",
                ModernDiscovery = new TransportResult<ModernDiscoveryEvidence>
                {
                    IsSuccessful = true,
                    Payload = new ModernDiscoveryEvidence
                    {
                        IsValid = true,
                        SupportedVersions = ["2026-07-28"],
                        CapabilityNames = ["tools"],
                        ResultType = "complete",
                        CacheScope = "public",
                        TtlMs = 60000
                    },
                    Transport = TransportMetadata.Empty
                },
                CapabilitySnapshot = new TransportResult<CapabilitySummary>
                {
                    IsSuccessful = true,
                    Payload = new CapabilitySummary
                    {
                        ToolListingSucceeded = true,
                        DiscoveredToolsCount = 1,
                        ToolListResponse = new JsonRpcResponse
                        {
                            IsSuccess = true,
                            RawJson = "{\"jsonrpc\":\"2.0\",\"result\":{\"tools\":[{\"name\":\"actual-tool\",\"description\":\"Observed tool.\"}]},\"id\":1}"
                        }
                    },
                    Transport = TransportMetadata.Empty
                }
            });

        var result = await _validatorService.DiscoverServerCapabilitiesAsync(server, CancellationToken.None);

        result.ProtocolVersion.Should().Be("2026-07-28");
        result.SupportedTools.Should().ContainSingle(tool => tool.Name == "actual-tool");
        result.SupportedTools.Should().NotContain(tool => tool.Name == "tools-validated");
        _httpClientMock.Verify(client => client.ValidateInitializeAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task DiscoverServerCapabilitiesAsync_AutoModernFailure_ShouldReturnNegotiatedLegacyCapabilities()
    {
        var server = new McpServerConfig
        {
            Endpoint = "https://example.test/mcp",
            Transport = "http",
            ProtocolVersion = "2026-07-28",
            ProtocolEra = McpProtocolEraSelection.Auto
        };
        _sessionBuilderMock
            .Setup(builder => builder.BuildAsync(It.IsAny<McpValidatorConfiguration>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((McpValidatorConfiguration configuration, CancellationToken _) => new ValidationSessionContext(
                configuration,
                configuration.Server.CloneForExecution())
            {
                ProtocolVersion = "2025-06-18",
                InitializationHandshake = new TransportResult<ModelContextProtocol.Protocol.InitializeResult>
                {
                    IsSuccessful = true,
                    Payload = new ModelContextProtocol.Protocol.InitializeResult
                    {
                        ProtocolVersion = "2025-06-18",
                        ServerInfo = new ModelContextProtocol.Protocol.Implementation { Name = "legacy-server", Version = "1.0.0" },
                        Capabilities = new ModelContextProtocol.Protocol.ServerCapabilities()
                    }
                },
                ModernDiscovery = new TransportResult<ModernDiscoveryEvidence>
                {
                    IsSuccessful = false,
                    Error = "server/discover unavailable"
                },
                CapabilitySnapshot = new TransportResult<CapabilitySummary>
                {
                    IsSuccessful = true,
                    Payload = new CapabilitySummary
                    {
                        ToolListingSucceeded = true,
                        ResourceListingSucceeded = true,
                        PromptListingSucceeded = true,
                        DiscoveredToolsCount = 1,
                        DiscoveredResourcesCount = 1,
                        DiscoveredPromptsCount = 1,
                        ToolListResponse = new JsonRpcResponse
                        {
                            RawJson = "{\"result\":{\"tools\":[{\"name\":\"legacy-tool\"}]}}"
                        },
                        ResourceListResponse = new JsonRpcResponse
                        {
                            RawJson = "{\"result\":{\"resources\":[{\"uri\":\"file:///observed.txt\",\"mimeType\":\"text/plain\"}]}}"
                        },
                        PromptListResponse = new JsonRpcResponse
                        {
                            RawJson = "{\"result\":{\"prompts\":[{\"name\":\"observed-prompt\"}]}}"
                        }
                    }
                }
            });

        var result = await _validatorService.DiscoverServerCapabilitiesAsync(server, CancellationToken.None);

        result.ProtocolVersion.Should().Be("2025-06-18");
        result.Implementation.Name.Should().Be("legacy-server");
        result.SupportedTools.Should().ContainSingle(tool => tool.Name == "legacy-tool");
        result.SupportedResources.Should().ContainSingle(resource => resource.UriPattern == "file:///observed.txt");
        result.SupportedPrompts.Should().ContainSingle(prompt => prompt.Name == "observed-prompt");
        result.SupportedResources.Should().NotContain(resource => resource.UriPattern.Contains('*'));
    }

    [Fact]
    public async Task ValidateServerAsync_ReusedService_ShouldResetPolicyForEveryRun()
    {
        var first = CreateFunctionalCategoryConfiguration();
        first.Execution = new ExecutionPolicy { MaxRequests = 3, AllowedHosts = ["first.test"] };
        first.Server.Endpoint = "https://first.test/mcp";
        var second = CreateFunctionalCategoryConfiguration();
        second.Execution = new ExecutionPolicy { MaxRequests = 7, AllowedHosts = ["second.test"] };
        second.Server.Endpoint = "https://second.test/mcp";

        await _validatorService.ValidateServerAsync(first, CancellationToken.None);
        await _validatorService.ValidateServerAsync(second, CancellationToken.None);

        _httpClientMock.Verify(client => client.ConfigureExecutionPolicy(It.Is<ExecutionPolicy>(policy =>
            policy.MaxRequests == 3 && policy.AllowedHosts.Contains("first.test"))), Times.Once);
        _httpClientMock.Verify(client => client.ConfigureExecutionPolicy(It.Is<ExecutionPolicy>(policy =>
            policy.MaxRequests == 7 && policy.AllowedHosts.Contains("second.test"))), Times.Once);
    }

    [Theory]
    [InlineData(TestStatus.Skipped)]
    [InlineData(TestStatus.AuthRequired)]
    [InlineData(TestStatus.Inconclusive)]
    public async Task ValidateServerAsync_ShouldIncludeRecordedToolOutcomesInSummary(TestStatus outcome)
    {
        var config = CreateFunctionalCategoryConfiguration();
        var toolResult = new ToolTestResult
        {
            Status = TestStatus.Inconclusive,
            ToolsDiscovered = 2,
            ToolsTestPassed = 1,
            DiscoveredToolNames = ["complete", "incomplete"],
            ToolResults =
            [
                new IndividualToolResult { ToolName = "complete", Status = TestStatus.Passed, DiscoveredCorrectly = true },
                new IndividualToolResult { ToolName = "incomplete", Status = outcome, DiscoveredCorrectly = true },
                new IndividualToolResult { ToolName = "diagnostic-only", Status = outcome }
            ]
        };
        _toolValidatorMock.Setup(validator => validator.ValidateToolDiscoveryAsync(
            It.IsAny<McpServerConfig>(), It.IsAny<ToolTestingConfig>(), It.IsAny<CancellationToken>())).ReturnsAsync(toolResult);

        var result = await _validatorService.ValidateServerAsync(config, CancellationToken.None);

        result.ToolValidation.Should().NotBeNull();
        result.Summary.SkippedTests.Should().Be(outcome == TestStatus.Skipped ? 1 : 0);
        result.Summary.AuthRequiredTests.Should().Be(outcome == TestStatus.AuthRequired ? 1 : 0);
        result.Summary.InconclusiveTests.Should().Be(outcome == TestStatus.Inconclusive ? 1 : 0);
    }

    [Theory]
    [InlineData(true, TestStatus.Inconclusive)]
    [InlineData(false, TestStatus.Inconclusive)]
    [InlineData(true, TestStatus.AuthRequired)]
    [InlineData(false, TestStatus.AuthRequired)]
    [InlineData(true, TestStatus.Skipped)]
    [InlineData(false, TestStatus.Skipped)]
    public async Task ValidateServerAsync_ShouldIncludeResourceAndPromptOutcomesInSummary(bool resources, TestStatus outcome)
    {
        var config = CreateFunctionalCategoryConfiguration();
        if (resources)
            _resourceValidatorMock.Setup(validator => validator.ValidateResourceDiscoveryAsync(It.IsAny<McpServerConfig>(),
                It.IsAny<ResourceTestingConfig>(), It.IsAny<CancellationToken>())).ReturnsAsync(new ResourceTestResult
                {
                    Status = outcome, ResourcesDiscovered = 2,
                    ResourceResults = [new() { ResourceUri = "https://example.invalid/a", Status = TestStatus.Passed },
                        new() { ResourceUri = "https://example.invalid/b", Status = outcome }]
                });
        else
            _promptValidatorMock.Setup(validator => validator.ValidatePromptDiscoveryAsync(It.IsAny<McpServerConfig>(),
                It.IsAny<PromptTestingConfig>(), It.IsAny<CancellationToken>())).ReturnsAsync(new PromptTestResult
                {
                    Status = outcome, PromptsDiscovered = 2,
                    PromptResults = [new() { PromptName = "a", Status = TestStatus.Passed }, new() { PromptName = "b", Status = outcome }]
                });
        var result = await _validatorService.ValidateServerAsync(config);
        result.Summary.InconclusiveTests.Should().Be(outcome == TestStatus.Inconclusive ? 1 : 0);
        result.Summary.AuthRequiredTests.Should().Be(outcome == TestStatus.AuthRequired ? 1 : 0);
        result.Summary.SkippedTests.Should().Be(outcome == TestStatus.Skipped ? 1 : 0);
        result.Summary.PassRate.Should().BeLessThan(100);
    }

    private static McpValidatorConfiguration CreateFunctionalCategoryConfiguration()
    {
        return new McpValidatorConfiguration
        { Assurance = new AssurancePolicyConfig { Profile = "inventory" },
            Server = new McpServerConfig
            {
                Endpoint = "https://example.test/mcp",
                Transport = "http"
            },
            Validation = new ValidationConfig
            {
                Categories = new ValidationScenarios
                {
                    ProtocolCompliance = new ProtocolComplianceConfig { TestJsonRpcCompliance = true },
                    ToolTesting = new ToolTestingConfig { TestToolDiscovery = true },
                    ResourceTesting = new ResourceTestingConfig { TestResourceDiscovery = true },
                    PromptTesting = new PromptTestingConfig { TestPromptDiscovery = true },
                    SecurityTesting = new SecurityTestingConfig { TestInputValidation = true },
                    PerformanceTesting = new PerformanceTestingConfig { TestConcurrentRequests = false },
                    ErrorHandling = new ErrorHandlingConfig
                    {
                        TestInvalidMethods = false,
                        TestMalformedJson = false,
                        TestConnectionInterruption = false,
                        TestTimeoutHandling = false,
                        TestGracefulDegradation = false
                    }
                }
            }
        };
    }
}
