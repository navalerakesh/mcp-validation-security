using FluentAssertions;
using Mcp.Benchmark.Core.Models;
using Mcp.Benchmark.Infrastructure.Scenarios;
using Xunit;

namespace Mcp.Benchmark.Tests.Unit.Scenarios;

public class BuiltInObservedSurfaceScenarioPackTests
{
    public static IEnumerable<object[]> CatalogCases() =>
        from resource in new[] { false, true }
        from variant in new[] { "complete", "empty", "missing-result", "wrong-identity", "duplicate-result", "missing-inventory",
            "unknown-inventory", "unfinished", "wrong-count", "skipped", "unexecuted", "metadata-only", "failed", "error", "auth-required" }
        select new object[] { resource, variant };

    [Theory]
    [MemberData(nameof(CatalogCases))]
    public async Task ContentCatalog_ShouldRequireIndependentIdentitiesAndConfiguredExecution(bool resource, string variant)
    {
        var count = variant == "empty" ? 0 : 2;
        var identities = Enumerable.Range(0, count).Select(i => resource ? $"https://example.invalid/{i}" : $"prompt-{i}").ToList();
        var retained = identities.ToList();
        if (variant == "missing-result") retained.RemoveAt(1);
        if (variant == "duplicate-result") retained[1] = retained[0];
        if (variant == "wrong-identity") retained[1] = resource ? "https://example.invalid/unrelated" : "unrelated";
        if (variant == "missing-inventory") identities.RemoveAt(1);
        var catalog = new CatalogEnumerationEvidence
        {
            IsComplete = variant != "unfinished", Termination = variant == "unfinished" ? CatalogEnumerationTermination.Cancelled : CatalogEnumerationTermination.Complete,
            RawItemsObserved = count, RecoveredItems = variant == "wrong-count" ? 1 : count
        };
        var aggregate = variant switch { "failed" => TestStatus.Failed, "error" => TestStatus.Error, "auth-required" => TestStatus.AuthRequired, _ => TestStatus.Passed };
        var context = CreateContext(new SecurityTestResult());
        var executionRequired = variant != "metadata-only";
        context.ValidationConfiguration.Validation.Categories.ResourceTesting.TestResourceReading = executionRequired;
        context.ValidationConfiguration.Validation.Categories.PromptTesting.TestPromptExecution = executionRequired;
        if (resource)
            context.ValidationResult.ResourceTesting = new()
            {
                Status = aggregate, ResourcesDiscovered = count, DiscoveredResourceUris = variant == "unknown-inventory" ? null : identities,
                CatalogEnumeration = catalog,
                ResourceResults = retained.Select(identity => new IndividualResourceResult
                {
                    ResourceUri = identity, Status = variant == "skipped" ? TestStatus.Skipped : TestStatus.Passed,
                    AccessSuccessful = variant is not ("unexecuted" or "metadata-only")
                }).ToList()
            };
        else
            context.ValidationResult.PromptTesting = new()
            {
                Status = aggregate, PromptsDiscovered = count, DiscoveredPromptNames = variant == "unknown-inventory" ? null : identities,
                CatalogEnumeration = catalog,
                PromptResults = retained.Select(identity => new IndividualPromptResult
                {
                    PromptName = identity, Status = variant == "skipped" ? TestStatus.Skipped : TestStatus.Passed,
                    ExecutionSuccessful = variant is not ("unexecuted" or "metadata-only")
                }).ToList()
            };
        var scenario = new BuiltInObservedSurfaceScenarioPack().GetScenarios().Single(item =>
            item.Descriptor.Key.Value == (resource ? "resource-catalog-smoke" : "prompt-catalog-smoke"));
        var execution = await scenario.ExecuteAsync(context, CancellationToken.None);
        var expected = aggregate != TestStatus.Passed ? aggregate :
            variant is "complete" or "empty" or "metadata-only" ? TestStatus.Passed : TestStatus.Inconclusive;
        execution.Scenario.Status.Should().Be(expected);
        context.ValidationResult.Evidence.Coverage.AddRange(execution.Coverage);
        var clone = context.ValidationResult.CloneWithoutSecrets();
        clone.Evidence.Coverage.Single().ObservedOutcome.Should().Be(execution.Coverage.Single().ObservedOutcome);
        if (resource)
            clone.ResourceTesting!.DiscoveredResourceUris.Should().BeEquivalentTo(variant == "unknown-inventory" ? null : identities);
        else
            clone.PromptTesting!.DiscoveredPromptNames.Should().BeEquivalentTo(variant == "unknown-inventory" ? null : identities);
    }

    [Theory]
    [InlineData("complete", TestStatus.Passed)]
    [InlineData("missing-result", TestStatus.Inconclusive)]
    [InlineData("wrong-name", TestStatus.Inconclusive)]
    [InlineData("duplicate-result", TestStatus.Inconclusive)]
    [InlineData("missing-inventory", TestStatus.Inconclusive)]
    [InlineData("unfinished-catalog", TestStatus.Inconclusive)]
    [InlineData("skipped-result", TestStatus.Inconclusive)]
    [InlineData("unexecuted-result", TestStatus.Inconclusive)]
    [InlineData("metadata-only", TestStatus.Passed)]
    [InlineData("wrong-recovered-count", TestStatus.Inconclusive)]
    [InlineData("rejected-items", TestStatus.Inconclusive)]
    [InlineData("known-failure", TestStatus.Failed)]
    public async Task ToolScenario_ShouldRequireEvidenceForEveryDiscoveredTool(string variant, TestStatus expected)
    {
        var tools = new ToolTestResult
        {
            Status = variant == "known-failure" ? TestStatus.Failed : TestStatus.Passed,
            ToolsDiscovered = 2, DiscoveredToolNames = ["first", "second"],
            CatalogEnumeration = new() { IsComplete = variant != "unfinished-catalog", Termination = CatalogEnumerationTermination.Complete,
                RecoveredItems = variant == "wrong-recovered-count" ? 1 : 2, RejectedItems = variant == "rejected-items" ? 1 : 0 },
            ToolResults =
            [
                new() { ToolName = "first", Status = TestStatus.Passed, ExecutionSuccessful = true },
                new() { ToolName = "second", Status = TestStatus.Passed, ExecutionSuccessful = true }
            ]
        };
        if (variant is "missing-result" or "known-failure") tools.ToolResults.RemoveAt(1);
        if (variant == "wrong-name") tools.ToolResults[1].ToolName = "unrelated";
        if (variant == "duplicate-result") tools.ToolResults[1].ToolName = "first";
        if (variant == "missing-inventory") tools.DiscoveredToolNames.RemoveAt(1);
        if (variant == "skipped-result") tools.ToolResults[1].Status = TestStatus.Skipped;
        if (variant is "unexecuted-result" or "metadata-only") tools.ToolResults[1].ExecutionSuccessful = false;
        var context = CreateContext(new SecurityTestResult());
        context.ValidationResult.ToolValidation = tools;
        if (variant == "metadata-only")
        {
            context.ValidationConfiguration.Validation.Categories.ToolTesting.TestToolExecution = false;
            context.ValidationConfiguration.Validation.Categories.ToolTesting.TestParameterValidation = false;
        }
        var scenario = new BuiltInObservedSurfaceScenarioPack().GetScenarios().Single(item => item.Descriptor.Key.Value == "tool-catalog-smoke");
        var result = await scenario.ExecuteAsync(context, CancellationToken.None);
        result.Scenario.Status.Should().Be(expected);
        result.Coverage.Single().ObservedOutcome.Should().Be(expected == TestStatus.Passed ? ValidationOutcome.Succeeded :
            expected == TestStatus.Failed ? ValidationOutcome.Failed : ValidationOutcome.Inconclusive);
        tools.Status.Should().Be(variant == "known-failure" ? TestStatus.Failed : TestStatus.Passed);
    }

    [Theory]
    [InlineData(true, true, false, true)]
    [InlineData(false, true, false, false)]
    [InlineData(true, false, false, false)]
    [InlineData(true, true, true, false)]
    public async Task PublicAuthenticationInformation_ShouldNotBecomeProtectedScopeDebt(bool observed, bool secure, bool required, bool notApplicable)
    {
        var context = CreateContext(new SecurityTestResult
        {
            AuthenticationTestResult = new AuthenticationTestResult
            {
                Status = TestStatus.Passed,
                TestScenarios = [new AuthenticationScenario
                {
                    ScenarioName = "Public discovery", Method = "tools/list", IsCompliant = true, IsSecure = secure,
                    AssessmentDisposition = AuthenticationAssessmentDisposition.Informational,
                    ProbeContext = observed ? new ProbeContext { ProbeId = "public-discovery", StatusCode = 200 } : null
                }]
            }
        });
        context.ServerConfig.Profile = McpServerProfile.Public;
        context.ServerConfig.Authentication = new AuthenticationConfig { Required = required };
        var scenario = new BuiltInObservedSurfaceScenarioPack().GetScenarios().Single(item => item.Descriptor.Key.Value == "security-authentication-challenge");

        var execution = await scenario.ExecuteAsync(context, CancellationToken.None);

        execution.Coverage.Single().Status.Should().Be(notApplicable ? ValidationCoverageStatus.NotApplicable : ValidationCoverageStatus.Skipped);
        execution.Observations.Should().ContainSingle();
        execution.Coverage.Single().ObservedOutcome.Should().NotBe(ValidationOutcome.Succeeded);
    }

    [Theory]
    [InlineData("security-authentication-challenge")]
    [InlineData("security-attack-simulations")]
    public async Task EnabledSecurityScenario_WithNoResults_ShouldRemainUnavailable(string scenarioId)
    {
        var scenario = new BuiltInObservedSurfaceScenarioPack().GetScenarios()
            .Single(candidate => candidate.Descriptor.Key.Value == scenarioId);
        var result = await scenario.ExecuteAsync(CreateContext(new SecurityTestResult(), transport: "http"), CancellationToken.None);

        result.Coverage.Should().ContainSingle(coverage => coverage.Status == ValidationCoverageStatus.Unavailable);
        result.Scenario.Status.Should().Be(TestStatus.NotRun);
    }

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