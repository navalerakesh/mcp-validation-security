using FluentAssertions;
using Mcp.Benchmark.Core.Models;
using Mcp.Benchmark.Infrastructure.Scenarios;
using Xunit;

namespace Mcp.Benchmark.Tests.Unit.Scenarios;

public class BuiltInObservedSurfaceScenarioPackTests
{
    [Fact]
    public async Task AttackScenario_ShouldPreserveProbeContextInObservation()
    {
        var probeContext = CreateProbeContext("attack-probe", "tools/call", ProbeResponseClassification.ProtocolError);
        var scenario = new BuiltInObservedSurfaceScenarioPack()
            .GetScenarios()
            .Single(item => item.Descriptor.Key.Value == "security-attack-simulations");

        var result = await scenario.ExecuteAsync(CreateContext(new SecurityTestResult
        {
            AttackSimulations = new List<AttackSimulationResult>
            {
                new()
                {
                    AttackVector = "MCP-SEC-001",
                    Description = "JSON-RPC error smuggling",
                    DefenseSuccessful = true,
                    ServerResponse = "Rejected with JSON-RPC error",
                    ProbeContexts = new List<ProbeContext> { probeContext }
                }
            }
        }), CancellationToken.None);

        var observation = result.Observations.Should().ContainSingle().Subject;
        observation.ProbeContexts.Should().NotBeNull();
        observation.ProbeContexts!.Should().ContainSingle(context => context.ProbeId == "attack-probe");
    }

    [Fact]
    public async Task AttackScenario_WithBlockedAndSkippedProbes_ShouldRemainCovered()
    {
        var scenario = new BuiltInObservedSurfaceScenarioPack()
            .GetScenarios()
            .Single(item => item.Descriptor.Key.Value == "security-attack-simulations");
        var result = await scenario.ExecuteAsync(CreateContext(new SecurityTestResult
        {
            AttackSimulations =
            [
                new AttackSimulationResult { AttackVector = "blocked", Outcome = ValidationOutcome.Succeeded, DefenseSuccessful = true },
                new AttackSimulationResult { AttackVector = "optional", Outcome = ValidationOutcome.Skipped }
            ]
        }), CancellationToken.None);

        result.Scenario.Status.Should().Be(TestStatus.Passed);
        result.Coverage.Should().ContainSingle(declaration =>
            declaration.Status == ValidationCoverageStatus.Covered &&
            declaration.ObservedOutcome == ValidationOutcome.Succeeded);
    }

    [Fact]
    public async Task AuthenticationScenario_ShouldPreserveProbeContextInObservation()
    {
        var probeContext = CreateProbeContext("auth-probe", "tools/list", ProbeResponseClassification.AuthenticationChallenge);
        var scenario = new BuiltInObservedSurfaceScenarioPack()
            .GetScenarios()
            .Single(item => item.Descriptor.Key.Value == "security-authentication-challenge");

        var result = await scenario.ExecuteAsync(CreateContext(new SecurityTestResult
        {
            AuthenticationTestResult = new AuthenticationTestResult
            {
                TestScenarios = new List<AuthenticationScenario>
                {
                    new()
                    {
                        ScenarioName = "No Auth - tools/list",
                        Method = "tools/list",
                        StatusCode = "401",
                        AssessmentDisposition = AuthenticationAssessmentDisposition.StandardsAligned,
                        ProbeContext = probeContext
                    }
                }
            }
        }), CancellationToken.None);

        var observation = result.Observations.Should().ContainSingle().Subject;
        observation.ProbeContexts.Should().NotBeNull();
        observation.ProbeContexts!.Should().ContainSingle(context => context.ProbeId == "auth-probe");
    }

    [Fact]
    public async Task AuthenticationScenario_WithStdioTransport_ShouldBeNotApplicable()
    {
        var scenario = new BuiltInObservedSurfaceScenarioPack()
            .GetScenarios()
            .Single(item => item.Descriptor.Key.Value == "security-authentication-challenge");
        var context = CreateContext(new SecurityTestResult
        {
            AuthenticationTestResult = new AuthenticationTestResult
            {
                TestScenarios = [new AuthenticationScenario { ScenarioName = "STDIO environment check" }]
            }
        }, transport: "stdio");

        var result = await scenario.ExecuteAsync(context, CancellationToken.None);

        result.Scenario.Status.Should().Be(TestStatus.Skipped);
        result.Coverage.Should().ContainSingle(declaration =>
            declaration.Status == ValidationCoverageStatus.NotApplicable &&
            declaration.ObservedOutcome == ValidationOutcome.NotApplicable &&
            declaration.Reason!.Contains("do not apply to STDIO", StringComparison.Ordinal));
    }

    private static ValidationScenarioContext CreateContext(SecurityTestResult security, string transport = "http")
    {
        return new ValidationScenarioContext
        {
            ServerConfig = new McpServerConfig { Endpoint = "https://example.test/mcp", Transport = transport },
            ApplicabilityContext = new ValidationApplicabilityContext
            {
                NegotiatedProtocolVersion = "2025-11-25",
                SchemaVersion = "2025-11-25",
                Transport = transport,
                AccessMode = "public"
            },
            ValidationConfiguration = new McpValidatorConfiguration(),
            ValidationResult = new ValidationResult { SecurityTesting = security }
        };
    }

    private static ProbeContext CreateProbeContext(string probeId, string method, ProbeResponseClassification classification)
    {
        return new ProbeContext
        {
            ProbeId = probeId,
            Method = method,
            Transport = "http",
            AuthApplied = true,
            AuthStatus = ProbeAuthStatus.Applied,
            ResponseClassification = classification,
            Confidence = EvidenceConfidenceLevel.High,
            StatusCode = classification == ProbeResponseClassification.AuthenticationChallenge ? 401 : 400
        };
    }
}