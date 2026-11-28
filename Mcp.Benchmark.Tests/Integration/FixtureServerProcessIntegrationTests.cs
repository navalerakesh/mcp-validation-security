using Mcp.Benchmark.Core.Abstractions;
using Mcp.Benchmark.Core.Constants;
using Mcp.Benchmark.Core.Models;
using Mcp.Benchmark.Core.Services;
using Mcp.Benchmark.Infrastructure.Authentication;
using Mcp.Benchmark.Infrastructure.Http;
using Mcp.Benchmark.Infrastructure.Services;
using Mcp.Benchmark.Infrastructure.Validators;
using Mcp.Compliance.Spec;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Security.Cryptography;
using Mcp.Benchmark.Core.Utilities;
using Mcp.Benchmark.Infrastructure.Utilities;

namespace Mcp.Benchmark.Tests.Integration;

public class FixtureServerProcessIntegrationTests
{
    [Fact]
    public async Task RawNotification_ShouldObserveSilenceWithoutTimingOutOrPoisoningTheSession()
    {
        var command = BuildFixtureCommand("compliant");
        await using var adapter = await StartFixtureServerAsync(command);
        await adapter.ValidateInitializeAsync(command);
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(2));

        var response = await adapter.SendRawJsonAsync(command,
            """{"jsonrpc":"2.0","method":"notifications/initialized"}""", deadline.Token);

        response.StatusCode.Should().Be(202);
        response.RawJson.Should().BeNullOrEmpty();
        (await adapter.CallAsync(command, "ping", null, deadline.Token)).IsSuccess.Should().BeTrue();
    }

    [Theory]
    [InlineData("""{"jsonrpc":"2.0","id":null,"method":"ping"}""")]
    [InlineData("""{"jsonrpc":"invalid","method":"notifications/initialized"}""")]
    [InlineData("""{"jsonrpc":"2.0","method":"ping"}""")]
    public async Task RawNotificationObservation_ShouldRetainUnexpectedAndRequiredResponses(string message)
    {
        var command = BuildFixtureCommand("compliant");
        await using var adapter = await StartFixtureServerAsync(command);
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(2));
        (await adapter.CallAsync(command, "ping", null, deadline.Token)).IsSuccess.Should().BeTrue();
        var response = await adapter.SendRawJsonAsync(command, message, deadline.Token);
        response.StatusCode.Should().Be(200);
        response.RawJson.Should().NotBeNullOrWhiteSpace();
    }

    [Fact]
    public async Task RawNotification_PartialResponse_ShouldNotCountAsSilenceOrPermitSessionReuse()
    {
        var command = BuildFixtureCommand("compliant") + " --partial-notification-response";
        await using var adapter = await StartFixtureServerAsync(command);
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(2));
        (await adapter.CallAsync(command, "ping", null, deadline.Token)).IsSuccess.Should().BeTrue();
        var observe = async () => await adapter.SendRawJsonAsync(command,
            """{"jsonrpc":"2.0","method":"notifications/initialized"}""", deadline.Token);

        await observe.Should().ThrowAsync<InvalidDataException>().WithMessage("*incomplete stdout frame*");
        var subsequent = await adapter.CallAsync(command, "ping", null, deadline.Token);
        subsequent.IsSuccess.Should().BeFalse();
        subsequent.Error.Should().Contain("restart");
    }

    [Fact]
    public async Task RawNotification_ClosedStdout_ShouldNotCountAsObservedSilence()
    {
        var command = BuildFixtureCommand("compliant") + " --close-notification-stdout";
        await using var adapter = await StartFixtureServerAsync(command);
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(2));
        (await adapter.CallAsync(command, "ping", null, deadline.Token)).IsSuccess.Should().BeTrue();
        var observe = async () => await adapter.SendRawJsonAsync(command,
            """{"jsonrpc":"2.0","method":"notifications/initialized"}""", deadline.Token);

        await observe.Should().ThrowAsync<EndOfStreamException>();
        (await adapter.CallAsync(command, "ping", null, deadline.Token)).IsSuccess.Should().BeFalse();
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ExplicitPrompt_ShouldCompleteBoundedRepliesOverRealStdio(bool mismatch)
    {
        var command = BuildFixtureCommand("client-input");
        await using var adapter = await StartFixtureServerAsync(command);
        adapter.ConfigureExecutionPolicy(new ExecutionPolicy { Mode = ExecutionMode.Standard, MaxRequests = 5 });
        adapter.SetProtocolVersion("2026-07-28");
        using var requests = JsonDocument.Parse(mismatch ? """{"other":{"method":"roots/list"}}""" : """{"roots":{"method":"roots/list"}}""");
        using var responses = JsonDocument.Parse(mismatch ? """{"other":{"roots":[]}}""" : """{"roots":{"roots":[]}}""");
        var safety = new Mock<IContentSafetyAnalyzer>();
        safety.Setup(analyzer => analyzer.AnalyzePrompt(It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<int>())).Returns([]);
        var validator = new PromptValidator(Mock.Of<ILogger<PromptValidator>>(), adapter, new JsonSchemaValidator(),
            new EmbeddedSchemaRegistry(), safety.Object);
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        var result = await validator.ValidatePromptDiscoveryAsync(new McpServerConfig
        { Endpoint = command, Transport = "stdio", ProtocolVersion = "2026-07-28" }, new PromptTestingConfig
        {
            CustomScenarios = [new PromptTestScenario { PromptName = "owned-client-input",
                Arguments = new() { ["value"] = "private-client-input-canary" },
                ClientInputRounds = [new() { ExpectedRequests = requests.RootElement, Responses = responses.RootElement }] }]
        }, deadline.Token);
        result.Status.Should().Be(mismatch ? TestStatus.Inconclusive : TestStatus.Passed);
        JsonSerializer.Serialize(result).Should().NotContain("client-input-canary");
        var stats = await adapter.CallAsync(command, "fixture/stats", null, deadline.Token);
        stats.IsSuccess.Should().BeTrue();
        using var document = JsonDocument.Parse(stats.RawJson!);
        document.RootElement.GetProperty("result").GetProperty("calls").GetInt32().Should().Be(mismatch ? 1 : 2);
    }

    [Theory]
    [InlineData(2, false)]
    [InlineData(3, true)]
    public async Task StdioBudget_ShouldIncludeImplicitInitializationNotification(int budget, bool toolAdmitted)
    {
        var root = Path.Combine(Path.GetTempPath(), "mcpval-stdio-budget-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        var audit = Path.Combine(root, "methods.jsonl");
        var command = BuildFixtureCommand("compliant") + $" --audit-methods \"{audit}\"";
        try
        {
            await using var adapter = await StartFixtureServerAsync(command);
            adapter.ConfigureExecutionPolicy(new ExecutionPolicy { Mode = ExecutionMode.Standard, MaxRequests = budget });
            adapter.SetProtocolVersion("2025-11-25");
            using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(10));
            using var telemetry = ValidationObservability.BeginRun("stdio-budget", budget);
            (await adapter.ValidateInitializeAsync(command, deadline.Token)).IsSuccessful.Should().BeTrue();
            var tool = await adapter.CallAsync(command, "tools/list", null, deadline.Token);
            tool.IsSuccess.Should().Be(toolAdmitted);
            var blocked = await adapter.CallAsync(command, "ping", null, deadline.Token);
            blocked.IsSuccess.Should().BeFalse();
            blocked.ProbeContext!.ResponseClassification.Should().Be(ProbeResponseClassification.PolicyBlocked);
            var metrics = telemetry.Complete(1);
            metrics.RequestsStarted.Should().Be(budget);
            metrics.RequestsCompleted.Should().Be(budget);
            metrics.RequestsFailed.Should().Be(0);
            var observed = await File.ReadAllTextAsync(audit, deadline.Token);
            observed.Should().Contain("initialize").And.Contain("notifications/initialized").And.NotContain("ping");
            if (toolAdmitted) observed.Should().Contain("tools/list");
            else observed.Should().NotContain("tools/list");
        }
        finally { Directory.Delete(root, recursive: true); }
    }

    [Theory]
    [InlineData("rpc")]
    [InlineData("raw")]
    [InlineData("probe")]
    public async Task StdioBudget_ShouldNotChargeCancellationBeforeDispatch(string surface)
    {
        var command = BuildFixtureCommand("compliant");
        await using var adapter = await StartFixtureServerAsync(command);
        adapter.ConfigureExecutionPolicy(new ExecutionPolicy { Mode = ExecutionMode.Standard, MaxRequests = 1 });
        using var cancelled = new CancellationTokenSource();
        cancelled.Cancel();
        const string body = """{"jsonrpc":"2.0","id":"cancelled","method":"tools/list"}""";
        async Task Send()
        {
            if (surface == "rpc") await adapter.CallAsync(command, "tools/list", null, cancelled.Token);
            else if (surface == "raw") await adapter.SendRawJsonAsync(command, body, cancelled.Token);
            else await adapter.SendStdioTransportProbeAsync(new StdioTransportProbeRequest
            { Endpoint = command, Kind = StdioTransportProbeKind.MessageExchange, ProbeId = "cancelled", RawMessage = body }, cancelled.Token);
        }
        await FluentActions.Awaiting(Send).Should().ThrowAsync<OperationCanceledException>();
        var response = await adapter.CallAsync(command, "tools/list");
        response.IsSuccess.Should().BeTrue();
    }

    [Theory]
    [InlineData("rpc")]
    [InlineData("raw")]
    [InlineData("probe")]
    public async Task StdioBudget_ShouldNotChargeAnUnavailableProcessOrRecordAnAttempt(string surface)
    {
        var command = BuildFixtureCommand("compliant");
        await using var adapter = new StdioMcpClientAdapter(Mock.Of<ILogger<StdioMcpClientAdapter>>());
        adapter.ConfigureExecutionPolicy(new ExecutionPolicy { Mode = ExecutionMode.Standard, MaxRequests = 1 });
        using var telemetry = ValidationObservability.BeginRun("stdio-unavailable-budget", 1);
        const string body = """{"jsonrpc":"2.0","id":"unavailable","method":"tools/list"}""";
        if (surface == "rpc") (await adapter.CallAsync(command, "tools/list")).IsSuccess.Should().BeFalse();
        else if (surface == "raw") (await adapter.SendRawJsonAsync(command, body, CancellationToken.None)).IsSuccess.Should().BeFalse();
        else (await adapter.SendStdioTransportProbeAsync(new StdioTransportProbeRequest
        { Endpoint = command, Kind = StdioTransportProbeKind.MessageExchange, ProbeId = "unavailable", RawMessage = body })).Executed.Should().BeFalse();
        await adapter.StartProcessAsync(command);
        (await adapter.CallAsync(command, "tools/list")).IsSuccess.Should().BeTrue();
        var metrics = telemetry.Complete(1);
        metrics.RequestsStarted.Should().Be(1);
        metrics.RequestsCompleted.Should().Be(1);
        metrics.RequestsFailed.Should().Be(0);
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(false, true)]
    [InlineData(true, true)]
    public async Task SyntheticClientInputs_ShouldUseTheRealStdioTransportWithoutLeakingOrReusingPrivateState(bool executionOnly, bool mismatch)
    {
        var command = BuildFixtureCommand("client-input");
        await using var adapter = await StartFixtureServerAsync(command);
        adapter.ConfigureExecutionPolicy(new ExecutionPolicy { Mode = ExecutionMode.Standard, MaxRequests = 5 });
        adapter.SetProtocolVersion("2026-07-28");
        var validator = new ToolValidator(Mock.Of<ILogger<ToolValidator>>(), adapter, new JsonSchemaValidator(),
            new EmbeddedSchemaRegistry(), Mock.Of<IAuthenticationService>(), Mock.Of<IContentSafetyAnalyzer>(), new ToolAiReadinessAnalyzer());
        using var requests = JsonDocument.Parse(mismatch ? """{"other":{"method":"roots/list"}}""" : """{"roots":{"method":"roots/list"}}""");
        using var responses = JsonDocument.Parse(mismatch ? """{"other":{"roots":[]}}""" : """{"roots":{"roots":[]}}""");
        var config = new ToolTestingConfig { CustomScenarios = [new ToolTestScenario
        {
            ToolName = "owned-client-input", Parameters = new() { ["value"] = "private-client-input-canary" },
            ClientInputRounds = [new ToolClientInputRound { ExpectedRequests = requests.RootElement, Responses = responses.RootElement }]
        }] };
        var server = new McpServerConfig { Endpoint = command, Transport = "stdio", ProtocolVersion = "2026-07-28" };
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        var result = executionOnly
            ? await validator.ValidateToolExecutionAsync(server, config, deadline.Token)
            : await validator.ValidateToolDiscoveryAsync(server, config, deadline.Token);
        var tool = result.ToolResults.Single(entry => entry.ToolName == "owned-client-input");
        tool.Status.Should().Be(mismatch ? TestStatus.Inconclusive : TestStatus.Passed, string.Join("; ", tool.Issues));
        tool.ExecutionSuccessful.Should().Be(!mismatch);
        JsonSerializer.Serialize(result).Should().NotContain("client-input-canary");
        var stats = await adapter.CallAsync(command, "fixture/stats", null, deadline.Token);
        stats.IsSuccess.Should().BeTrue();
        using var document = JsonDocument.Parse(stats.RawJson!);
        document.RootElement.GetProperty("result").GetProperty("calls").GetInt32().Should().Be(mismatch ? 1 : 2);
        using var process = System.Diagnostics.Process.GetProcessById(document.RootElement.GetProperty("result").GetProperty("pid").GetInt32());
        await adapter.DisposeAsync();
        process.HasExited.Should().BeTrue();
    }

    [Theory]
    [InlineData(false, false, false, ValidationOutcome.Succeeded)]
    [InlineData(true, false, false, ValidationOutcome.Failed)]
    [InlineData(false, true, false, ValidationOutcome.Inconclusive)]
    [InlineData(false, false, true, ValidationOutcome.Inconclusive)]
    public async Task OwnedHttpIdentityPilot_ShouldObserveIssuedPrincipalsAndPolicy(bool brokenPolicy, bool unavailableObserver, bool counterDrift, ValidationOutcome expected)
    {
        var packageRoot = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "mcpval-mcp"));
        File.Exists(Path.Combine(packageRoot, "node_modules", "@modelcontextprotocol", "sdk", "package.json"))
            .Should().BeTrue("run npm ci in mcpval-mcp before SDK-backed fixture tests");
        var tokens = Enumerable.Range(0, 4).Select(_ => Convert.ToHexString(RandomNumberGenerator.GetBytes(32)).ToLowerInvariant()).ToArray();
        var start = new System.Diagnostics.ProcessStartInfo("node")
        {
            UseShellExecute = false, RedirectStandardInput = true, RedirectStandardOutput = true, RedirectStandardError = true
        };
        start.ArgumentList.Add(Path.Combine(AppContext.BaseDirectory, "Fixtures", "Servers", "mcp-fixture-controlled-identity.cjs"));
        start.Environment.Clear();
        foreach (var name in new[] { "PATH", "SYSTEMROOT", "WINDIR", "PATHEXT" })
            if (Environment.GetEnvironmentVariable(name) is { } value) start.Environment[name] = value;
        start.Environment["MCPVAL_FIXTURE_PACKAGE_ROOT"] = packageRoot;
        start.Environment["MCPVAL_FIXTURE_CREDENTIALS"] = JsonSerializer.Serialize(new { actorA = tokens[0], actorB = tokens[1], readOnlyA = tokens[2], observer = tokens[3] });
        start.Environment["MCPVAL_FIXTURE_ALLOW_SPOOF"] = brokenPolicy ? "true" : "false";
        start.Environment["MCPVAL_FIXTURE_OBSERVER_UNAVAILABLE"] = unavailableObserver ? "true" : "false";
        start.Environment["MCPVAL_FIXTURE_COUNTER_DRIFT"] = counterDrift ? "true" : "false";
        using var process = new System.Diagnostics.Process { StartInfo = start };
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        ControlledIdentityMatrixAssessment? completedAssessment = null;
        string? signedEnvelope = null;
        string? observerPublicKey = null;
        string? canonicalArtifact = null;
        process.Start().Should().BeTrue();
        start.Environment.Remove("MCPVAL_FIXTURE_CREDENTIALS");
        void StopOnOverflow() { if (!process.HasExited) process.Kill(entireProcessTree: true); }
        var stderr = BoundedStreamReader.ReadUtf8Async(process.StandardError.BaseStream, 4096, BoundedStreamRetention.First, StopOnOverflow, deadline.Token);
        try
        {
            var ready = await ReadIdentityReadinessAsync(process, deadline.Token);
            ready.IsTruncated.Should().BeFalse("fixture readiness must stay bounded");
            using var readyDocument = JsonDocument.Parse(ready.Text);
            readyDocument.RootElement.GetProperty("pid").GetInt32().Should().Be(process.Id);
            var port = readyDocument.RootElement.GetProperty("port").GetInt32();
            port.Should().BeInRange(1, 65535);
            var endpoint = $"http://127.0.0.1:{port}/mcp";
            var runId = Guid.NewGuid().ToString("N");
            var targetDigest = CanonicalJsonDigest.ComputeText(endpoint);
            var routeDigest = CanonicalJsonDigest.ComputeText($"owned-http:{port}");
            using var observerHttp = new HttpClient(new HttpClientHandler { AllowAutoRedirect = false })
                { Timeout = TimeSpan.FromSeconds(5), MaxResponseContentBufferSize = 8192 };
            observerHttp.DefaultRequestHeaders.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", tokens[3]);
            using (var forbiddenRead = new HttpRequestMessage(HttpMethod.Get, $"http://127.0.0.1:{port}/observer"))
            {
                forbiddenRead.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", tokens[0]);
                using var denied = await observerHttp.SendAsync(forbiddenRead, deadline.Token);
                denied.StatusCode.Should().Be(System.Net.HttpStatusCode.Unauthorized, "target credentials must not grant observer access");
            }
            using (var forbiddenMcp = new HttpRequestMessage(HttpMethod.Post, endpoint))
            {
                forbiddenMcp.Content = new StringContent("{}", Encoding.UTF8, "application/json");
                using var denied = await observerHttp.SendAsync(forbiddenMcp, deadline.Token);
                denied.StatusCode.Should().Be(System.Net.HttpStatusCode.Unauthorized, "observer credentials must not grant target access");
            }
            int? baselineValue = null;
            using (var baselineResponse = await observerHttp.GetAsync($"http://127.0.0.1:{port}/observer", deadline.Token))
            {
                baselineResponse.StatusCode.Should().Be(unavailableObserver ? System.Net.HttpStatusCode.ServiceUnavailable : System.Net.HttpStatusCode.OK);
                if (baselineResponse.IsSuccessStatusCode)
                {
                    using var baseline = JsonDocument.Parse(await baselineResponse.Content.ReadAsStringAsync(deadline.Token));
                    baseline.RootElement.GetProperty("entries").GetArrayLength().Should().Be(0);
                    baselineValue = baseline.RootElement.GetProperty("value").GetInt32();
                    baselineValue.Should().Be(0);
                }
            }
            var cases = new List<ControlledIdentityCase>();
            var observations = new List<ControlledIdentityObservation>();
            string? observedProtocol = null;
            var modes = new[] { "control", "claimed-actor", "other-actor", "insufficient-scope" };
            for (var index = 0; index < modes.Length; index++)
            {
                var mode = modes[index];
                var principal = mode == "other-actor" ? "actor-b" : "actor-a";
                var claimed = mode is "claimed-actor" or "other-actor" ? "actor-b" : "actor-a";
                var token = mode == "other-actor" ? tokens[1] : mode == "insufficient-scope" ? tokens[2] : tokens[0];
                var arguments = new { @object = "object-a", claimedActor = claimed };
                var policy = new ExecutionPolicy
                {
                    Mode = ExecutionMode.Standard, MaxRequests = 8, AllowPrivateAddresses = true,
                    AllowedHosts = ["127.0.0.1"], AllowedOrigins = [$"http://127.0.0.1:{port}"]
                };
                var network = new RunNetworkPolicyContext();
                network.Configure(OperationPolicySnapshot.From(policy));
                using var provider = new GovernedHttpClientProvider(new GovernedConnectionFactory(network, new DnsHostAddressResolver(), new SocketEndpointConnector()));
                await using var factory = new McpClientFactory(Microsoft.Extensions.Logging.Abstractions.NullLogger<McpClientFactory>.Instance,
                    Microsoft.Extensions.Logging.Abstractions.NullLoggerFactory.Instance, provider);
                var sdk = new SdkMcpClient(factory, Microsoft.Extensions.Logging.Abstractions.NullLogger<SdkMcpClient>.Instance);
                using var client = new McpHttpClient(provider.Client, Microsoft.Extensions.Logging.Abstractions.NullLogger<McpHttpClient>.Instance, sdk, network);
                client.ConfigureExecutionPolicy(policy);
                client.SetProtocolVersion("2025-11-25");
                client.SetAuthentication(new AuthenticationConfig { Type = "bearer", Token = token });
                var initialization = await client.ValidateInitializeAsync(endpoint, deadline.Token);
                initialization.IsSuccessful.Should().BeTrue("issued credentials must authenticate, including insufficient tool scope");
                initialization.Payload!.ProtocolVersion.Should().Be("2025-11-25", "the fixture must actually negotiate the requested supported revision");
                if (observedProtocol == null) observedProtocol = initialization.Payload.ProtocolVersion;
                else initialization.Payload.ProtocolVersion.Should().Be(observedProtocol);
                var began = DateTimeOffset.UtcNow;
                var response = await client.CallAsync(endpoint, ValidationConstants.Methods.ToolsCall,
                    new { name = "owned_update", arguments }, deadline.Token);
                var ended = DateTimeOffset.UtcNow;
                response.IsSuccess.Should().BeTrue("allow and deny are valid correlated MCP application responses");
                var requestId = response.ProbeContext!.RequestId!;
                var binding = new ControlledOperationBinding(runId, mode, "attempt-" + mode, requestId, "owned-http-boundary",
                    targetDigest, routeDigest, CanonicalJsonDigest.ComputeObject(arguments), principal, "object-a");
                cases.Add(new ControlledIdentityCase
                {
                    CaseId = mode, Binding = binding, ScopePredicateId = "owned-object-effects", ExpectedEffectiveActorAlias = principal,
                    SecurityChallenge = mode switch
                    {
                        "claimed-actor" => ControlledIdentityChallenge.ActorSubstitution,
                        "other-actor" => ControlledIdentityChallenge.UnauthorizedActorOrObject,
                        "insufficient-scope" => ControlledIdentityChallenge.InsufficientScope,
                        _ => null
                    },
                    ExpectedDecision = index == 0 ? ControlledAuthorizationDecision.Allowed : ControlledAuthorizationDecision.Denied,
                    IdentityRole = ControlledIdentityRole.CallerAssertion, IsPositiveControl = index == 0, PositiveControlCaseId = index == 0 ? null : "control"
                });
                using var observedResponse = await observerHttp.GetAsync($"http://127.0.0.1:{port}/observer", deadline.Token);
                observedResponse.StatusCode.Should().Be(unavailableObserver ? System.Net.HttpStatusCode.ServiceUnavailable : System.Net.HttpStatusCode.OK);
                var observation = new ControlledIdentityObservation
                {
                    CaseId = mode, Binding = binding, ObserverId = "owned-auth-observer", ScopePredicateId = "owned-object-effects",
                    OperationStartedAt = began, OperationEndedAt = ended, ObservedAt = DateTimeOffset.UtcNow,
                    ObserverOutcome = ValidationOutcome.Unavailable
                };
                if (observedResponse.IsSuccessStatusCode)
                {
                    using var ledger = JsonDocument.Parse(await observedResponse.Content.ReadAsStringAsync(deadline.Token));
                    var rows = ledger.RootElement.GetProperty("entries").EnumerateArray().ToArray();
                    rows.Should().HaveCount(index + 1);
                    var observed = rows.Single(entry => entry.GetProperty("requestId").GetString() == requestId);
                    var allowed = observed.GetProperty("allowed").GetBoolean();
                    allowed.Should().Be(index == 0 || brokenPolicy && mode == "claimed-actor", "each policy decision must match its independent control");
                    observed.GetProperty("actor").GetString().Should().Be(principal);
                    observed.GetProperty("authenticated").GetBoolean().Should().BeTrue();
                    var counterMatches = ledger.RootElement.GetProperty("value").GetInt32() == observed.GetProperty("after").GetInt32();
                    counterMatches.Should().Be(!(counterDrift && mode == "insufficient-scope"));
                    observation = observation with
                    {
                        ObserverOutcome = ValidationOutcome.Succeeded, Complete = counterMatches,
                        PreconditionsSatisfied = baselineValue != null && observed.GetProperty("before").GetInt32() == baselineValue,
                        Decision = allowed ? ControlledAuthorizationDecision.Allowed : ControlledAuthorizationDecision.Denied,
                        EffectiveActorMatched = observed.GetProperty("actor").GetString() == principal,
                        AuthenticationBindingMatched = observed.GetProperty("authenticated").GetBoolean(),
                        ScopeMatched = observed.GetProperty("object").GetString() == binding.ObjectAlias &&
                            observed.GetProperty("after").GetInt32() == observed.GetProperty("before").GetInt32() + (allowed ? 1 : 0)
                    };
                    baselineValue = ledger.RootElement.GetProperty("value").GetInt32();
                }
                observations.Add(observation);
            }
            var contract = new ControlledIdentityMatrixContract
            {
                PolicyRevision = "owned-http-identity-v1", ToolName = "owned_update", ObserverId = "owned-auth-observer", Cases = cases,
                MaximumEvidenceAge = TimeSpan.FromMinutes(1), MaximumObservationDelay = TimeSpan.FromSeconds(15)
            };
            using var key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
            var now = DateTimeOffset.UtcNow;
            var document = new ControlledIdentityEvidenceDocument
            {
                Contract = contract, Observations = observations, IssuedAt = now, ExpiresAt = now.AddMinutes(1),
                SigningKeyId = CanonicalJsonDigest.ComputeBytes(key.ExportSubjectPublicKeyInfo()), SignatureBase64 = string.Empty
            };
            document = document with { SignatureBase64 = Convert.ToBase64String(key.SignHash(Convert.FromHexString(document.ComputePayloadDigest()), DSASignatureFormat.IeeeP1363FixedFieldConcatenation)) };
            var options = new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };
            options.Converters.Add(new JsonStringEnumConverter(JsonNamingPolicy.CamelCase));
            var serialized = JsonSerializer.Serialize(document, options);
            var pack = new ControlledIdentityEvidenceImporter().ImportPack(serialized, contract,
                new ControlledIdentityAdmission(runId, targetDigest, routeDigest, contract.ObserverId), key.ExportSubjectPublicKeyInfoPem(), DateTimeOffset.UtcNow);
            var protocol = observedProtocol ?? throw new InvalidOperationException("Owned identity sessions did not establish a protocol version.");
            var canonical = new ValidationResult { ValidationId = runId, ServerConfig = new McpServerConfig { Endpoint = endpoint, Transport = "http" } };
            canonical.Run.TargetIdentitySha256 = targetDigest;
            canonical.Run.ProtocolVersion = protocol;
            canonical.Run.SchemaVersion = protocol;
            canonical.Run.ExecutionMode = ExecutionMode.Standard;
            canonical.Run.TerminationOutcome = ValidationOutcome.Succeeded;
            var execution = await pack.GetScenarios().Single().ExecuteAsync(new ValidationScenarioContext
            {
                ValidationResult = canonical, ServerConfig = canonical.ServerConfig, ValidationConfiguration = new(),
                ApplicabilityContext = new ValidationApplicabilityContext { NegotiatedProtocolVersion = protocol, SchemaVersion = protocol, Transport = "http", AccessMode = "Authenticated" }
            }, deadline.Token);
            canonical.Assessments.Scenarios.Add(execution.Scenario);
            canonical.Evidence.Coverage.AddRange(execution.Coverage);
            canonical.EndTime = DateTime.UtcNow;
            var baselineRequirement = new ControlledIdentitySecurityRequirement(contract,
                new ControlledIdentityAdmission(runId, targetDigest, routeDigest, contract.ObserverId), serialized,
                key.ExportSubjectPublicKeyInfoPem(), "claimed-actor", "insufficient-scope", "other-actor");
            canonical.Assurance = AssuranceAssessmentEngine.Evaluate(canonical, AssuranceProfileCatalog.Comprehensive,
                new AssuranceEvaluationContext { RequiredIdentityControls = baselineRequirement, ExecutionMode = ExecutionMode.Standard });
            var securityClaims = canonical.Assurance.Claims.Where(claim => ControlledIdentitySecurityBaseline.RequiredClaimIds.Contains(claim.ClaimId)).ToArray();
            securityClaims.Should().HaveCount(3);
            securityClaims.Single(claim => claim.Subject == "actor-binding").Outcome.Should().Be(unavailableObserver ? AssuranceClaimOutcome.Incomplete
                : brokenPolicy ? AssuranceClaimOutcome.Violated : AssuranceClaimOutcome.Satisfied);
            securityClaims.Single(claim => claim.Subject == "scope-enforcement").Outcome.Should().Be(unavailableObserver || counterDrift
                ? AssuranceClaimOutcome.Incomplete : AssuranceClaimOutcome.Satisfied);
            securityClaims.Single(claim => claim.Subject == "actor-action-object-authorization").Outcome.Should().Be(unavailableObserver
                ? AssuranceClaimOutcome.Incomplete : AssuranceClaimOutcome.Satisfied);
            canonical.Assurance.Assessments.SecurityAssurance.Status.Should().Be(brokenPolicy ? SecurityAssuranceStatus.IssuesFound
                : unavailableObserver || counterDrift ? SecurityAssuranceStatus.Incomplete : SecurityAssuranceStatus.Verified);
            canonical.Assurance.Assessments.CoreConformance.RequiredClaims.Should().Be(0, "identity controls do not earn MCP core-conformance credit");
            var assessment = execution.Scenario.ControlledIdentity!;
            assessment.Outcome.Should().Be(expected);
            assessment.Cases.Should().HaveCount(4);
            foreach (var item in assessment.Cases)
            {
                var itemExpected = unavailableObserver || counterDrift && item.CaseId == "insufficient-scope" ? ValidationOutcome.Inconclusive
                    : brokenPolicy && item.CaseId == "claimed-actor" ? ValidationOutcome.Failed : ValidationOutcome.Succeeded;
                item.Outcome.Should().Be(itemExpected, $"case {item.CaseId} must retain its own observed outcome");
                if (!unavailableObserver && !(counterDrift && item.CaseId == "insufficient-scope"))
                {
                    item.EffectiveActorOutcome.Should().Be(ValidationOutcome.Succeeded);
                    item.AuthenticationBindingOutcome.Should().Be(ValidationOutcome.Succeeded);
                    item.DecisionOutcome.Should().Be(itemExpected);
                }
            }
            foreach (var token in tokens) serialized.Contains(token, StringComparison.Ordinal).Should().BeFalse("ephemeral credentials must not be exported");
            completedAssessment = assessment;
            signedEnvelope = serialized;
            observerPublicKey = key.ExportSubjectPublicKeyInfoPem();
            canonicalArtifact = JsonSerializer.Serialize(canonical.CloneWithoutSecrets(), options);
            using (var recorded = JsonDocument.Parse(canonicalArtifact))
                recorded.RootElement.GetProperty("run").GetProperty("executionMode").GetString().Should().Be("standard");
            var schemaPath = Path.Combine(packageRoot, "..", "docs", "Schemas", "mcpval-validation-result.schema.json");
            var schemaResult = new JsonSchemaValidator().Validate(System.Text.Json.Nodes.JsonNode.Parse(canonicalArtifact)!,
                System.Text.Json.Nodes.JsonNode.Parse(await File.ReadAllTextAsync(schemaPath, deadline.Token))!);
            schemaResult.IsValid.Should().BeTrue(string.Join(Environment.NewLine, schemaResult.Errors));
            foreach (var token in tokens) canonicalArtifact.Contains(token, StringComparison.Ordinal).Should().BeFalse("canonical output must not retain ephemeral credentials");
        }
        finally
        {
            process.StandardInput.Close();
            try { await process.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(5)); }
            catch (TimeoutException) { process.Kill(entireProcessTree: true); await process.WaitForExitAsync(); }
            process.HasExited.Should().BeTrue();
        }
        process.ExitCode.Should().Be(0);
        var errors = await stderr;
        errors.IsTruncated.Should().BeFalse();
        foreach (var token in tokens) errors.Text.Contains(token, StringComparison.Ordinal).Should().BeFalse("ephemeral credentials must not appear in process diagnostics");
        if (Environment.GetEnvironmentVariable("MCPVAL_IDENTITY_PILOT_OUTPUT") is { Length: > 0 } output)
        {
            Directory.CreateDirectory(output);
            var name = brokenPolicy ? "broken-policy" : unavailableObserver ? "observer-unavailable" : counterDrift ? "counter-drift" : "correct-policy";
            var options = new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase, WriteIndented = true };
            options.Converters.Add(new JsonStringEnumConverter(JsonNamingPolicy.CamelCase));
            foreach (var artifact in new Dictionary<string, string>
            {
                [name + ".json"] = JsonSerializer.Serialize(completedAssessment, options),
                [name + ".canonical.json"] = canonicalArtifact!,
                [name + ".evidence.json"] = signedEnvelope!,
                [name + ".observer-public.pem"] = observerPublicKey!
            })
            {
                await using var stream = new FileStream(Path.Combine(output, artifact.Key), FileMode.CreateNew, FileAccess.Write);
                await stream.WriteAsync(Encoding.UTF8.GetBytes(artifact.Value));
            }
        }
    }

    [Fact]
    public async Task OwnedHttpIdentityFixture_ShouldExitAtLifetimeWithParentInputOpen()
    {
        var packageRoot = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "mcpval-mcp"));
        var tokens = Enumerable.Range(0, 4).Select(_ => Convert.ToHexString(RandomNumberGenerator.GetBytes(32)).ToLowerInvariant()).ToArray();
        var start = new System.Diagnostics.ProcessStartInfo("node") { UseShellExecute = false, RedirectStandardInput = true, RedirectStandardOutput = true, RedirectStandardError = true };
        start.ArgumentList.Add(Path.Combine(AppContext.BaseDirectory, "Fixtures", "Servers", "mcp-fixture-controlled-identity.cjs"));
        start.Environment.Clear();
        foreach (var name in new[] { "PATH", "SYSTEMROOT", "WINDIR", "PATHEXT" })
            if (Environment.GetEnvironmentVariable(name) is { } value) start.Environment[name] = value;
        start.Environment["MCPVAL_FIXTURE_PACKAGE_ROOT"] = packageRoot;
        start.Environment["MCPVAL_FIXTURE_CREDENTIALS"] = JsonSerializer.Serialize(new { actorA = tokens[0], actorB = tokens[1], readOnlyA = tokens[2], observer = tokens[3] });
        start.Environment["MCPVAL_FIXTURE_LIFETIME_MS"] = "500";
        using var process = new System.Diagnostics.Process { StartInfo = start };
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        process.Start().Should().BeTrue();
        start.Environment.Remove("MCPVAL_FIXTURE_CREDENTIALS");
        void StopOnOverflow() { if (!process.HasExited) process.Kill(entireProcessTree: true); }
        var stderr = BoundedStreamReader.ReadUtf8Async(process.StandardError.BaseStream, 4096, BoundedStreamRetention.First, StopOnOverflow, deadline.Token);
        try
        {
            var ready = await ReadIdentityReadinessAsync(process, deadline.Token);
            ready.IsTruncated.Should().BeFalse();
            using var parsed = JsonDocument.Parse(ready.Text);
            process.StandardInput.BaseStream.CanWrite.Should().BeTrue();
            await process.WaitForExitAsync(deadline.Token);
            process.ExitCode.Should().Be(0);
            (await stderr).IsTruncated.Should().BeFalse();
        }
        finally
        {
            if (!process.HasExited) { process.Kill(entireProcessTree: true); await process.WaitForExitAsync(); }
        }
    }

    [Theory]
    [InlineData("write", ControlledStateExpectation.ChangeRequired, ControlledApplicationOutcome.Succeeded, ValidationOutcome.Succeeded)]
    [InlineData("drop", ControlledStateExpectation.ChangeRequired, ControlledApplicationOutcome.Succeeded, ValidationOutcome.Failed)]
    [InlineData("noop", ControlledStateExpectation.NoOpPermitted, ControlledApplicationOutcome.Succeeded, ValidationOutcome.Succeeded)]
    [InlineData("reject", ControlledStateExpectation.UnchangedRequired, ControlledApplicationOutcome.Rejected, ValidationOutcome.Succeeded)]
    [InlineData("partial-error", ControlledStateExpectation.UnchangedRequired, ControlledApplicationOutcome.Rejected, ValidationOutcome.Failed)]
    public async Task OwnedStateOracle_ShouldReconcileRealStdioEffectsWithSyntheticObservationWindow(
        string mode, ControlledStateExpectation expectation, ControlledApplicationOutcome application, ValidationOutcome expected)
    {
        var root = Path.Combine(Path.GetTempPath(), "mcpval-owned-state-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        var statePath = Path.Combine(root, "state.json");
        var before = mode == "noop" ? 1 : 0;
        await File.WriteAllTextAsync(statePath, JsonSerializer.Serialize(new { value = before }));
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(15));
        try
        {
            var command = BuildFixtureCommand("controlled-state") + $" --root \"{root}\" --mode {mode}";
            JsonRpcResponse response;
            await using (var adapter = await StartFixtureServerAsync(command))
            {
                adapter.ConfigureExecutionPolicy(new ExecutionPolicy { Mode = ExecutionMode.Standard, MaxRequests = 8 });
                (await adapter.ValidateInitializeAsync(command, deadline.Token)).IsSuccessful.Should().BeTrue();
                response = await adapter.CallAsync(command, ValidationConstants.Methods.ToolsCall,
                    new { name = "fixture_write", arguments = new { value = 1 } }, deadline.Token);
            }
            response.IsSuccess.Should().BeTrue("the fixture returns a valid correlated tool response, including application errors");
            using var wire = JsonDocument.Parse(response.RawJson!);
            var rejected = wire.RootElement.GetProperty("result").GetProperty("isError").GetBoolean();
            new FileInfo(statePath).Length.Should().BeLessThan(4096);
            using var observed = JsonDocument.Parse(await File.ReadAllTextAsync(statePath, deadline.Token));
            var after = observed.RootElement.GetProperty("value").GetInt32();
            using var audit = JsonDocument.Parse(await File.ReadAllTextAsync(Path.Combine(root, "audit.json"), deadline.Token));
            var auditMatched = audit.RootElement.GetProperty("attempted").GetBoolean() &&
                audit.RootElement.GetProperty("applicationRejected").GetBoolean() == rejected;
            Directory.Delete(root, recursive: true);
            Directory.Exists(root).Should().BeFalse();

            var syntheticWindowStart = new DateTimeOffset(2026, 11, 11, 0, 0, 0, TimeSpan.Zero);
            var binding = new ControlledOperationBinding(Guid.NewGuid().ToString("N"), "owned-operation", "owned-attempt",
                response.ProbeContext!.RequestId!, "owned-stdio-boundary", CanonicalJsonDigest.ComputeText(command),
                CanonicalJsonDigest.ComputeText("owned-stdio-route"), CanonicalJsonDigest.ComputeObject(new { value = 1 }),
                "synthetic-authorized-actor", "owned-counter");
            var contract = new ControlledStateScenarioContract
            {
                ScenarioId = "owned-synchronous-state-" + mode, PolicyRevision = "owned-synchronous-fixture-v1",
                ToolName = "fixture_write", ObserverId = "owned-file-observer", PredicateId = "counter-equals-one",
                PreconditionId = "owned-counter-ready", AuditPredicateId = "single-owned-operation", CleanupOwner = "test-owned-directory",
                Binding = binding, StateExpectation = expectation, ExpectedApplicationOutcome = application, AuditRequired = true,
                ConsistencyWindow = TimeSpan.FromSeconds(5), MaximumBaselineAge = TimeSpan.FromSeconds(5),
                MaximumEvidenceAge = TimeSpan.FromMinutes(1), MaximumObserverReads = 3
            };
            var evidence = new ControlledStateEvidence
            {
                Binding = binding, ObserverId = contract.ObserverId, PredicateId = contract.PredicateId,
                PreconditionId = contract.PreconditionId, AuditPredicateId = contract.AuditPredicateId,
                BaselineAt = syntheticWindowStart.AddSeconds(-1), OperationStartedAt = syntheticWindowStart,
                OperationEndedAt = syntheticWindowStart.AddSeconds(1), ObservedThrough = syntheticWindowStart.AddSeconds(6),
                ObserverOutcome = ValidationOutcome.Succeeded, ObservationComplete = true, PreconditionsSatisfied = true,
                BeforePredicateSatisfied = before == 1, AfterPredicateSatisfied = after == 1, StateChanged = before != after,
                ApplicationOutcome = rejected ? ControlledApplicationOutcome.Rejected : ControlledApplicationOutcome.Succeeded,
                AuditMatched = auditMatched, AuditObserverOutcome = ValidationOutcome.Succeeded, AuditObservationComplete = true,
                AuditObservedThrough = syntheticWindowStart.AddSeconds(6), ObserverReads = 3, CleanupOutcome = ValidationOutcome.Succeeded
            };
            using var key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
            var document = new ControlledStateEvidenceDocument
            {
                Contract = contract, Evidence = evidence, IssuedAt = syntheticWindowStart.AddSeconds(6), ExpiresAt = syntheticWindowStart.AddMinutes(2),
                SigningKeyId = Convert.ToHexString(SHA256.HashData(key.ExportSubjectPublicKeyInfo())).ToLowerInvariant(), SignatureBase64 = string.Empty
            };
            document = document with { SignatureBase64 = Convert.ToBase64String(key.SignHash(
                Convert.FromHexString(document.ComputePayloadDigest()), DSASignatureFormat.IeeeP1363FixedFieldConcatenation)) };
            var options = new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase, WriteIndented = true };
            options.Converters.Add(new JsonStringEnumConverter(JsonNamingPolicy.CamelCase));
            var assessment = new ControlledStateEvidenceImporter().Import(JsonSerializer.Serialize(document, options),
                contract, key.ExportSubjectPublicKeyInfoPem(), syntheticWindowStart.AddMinutes(1));

            assessment.Outcome.Should().Be(expected);
            assessment.ApplicationOutcome.Should().Be(ValidationOutcome.Succeeded);
            assessment.AuditOutcome.Should().Be(ValidationOutcome.Succeeded);
            assessment.CleanupOutcome.Should().Be(ValidationOutcome.Succeeded);
            assessment.RetryPermitted.Should().BeFalse();
            if (Environment.GetEnvironmentVariable("MCPVAL_CONTROLLED_STATE_OUTPUT") is { Length: > 0 } output)
            {
                Directory.CreateDirectory(output);
                await using var stream = new FileStream(Path.Combine(output, mode + ".json"), FileMode.CreateNew, FileAccess.Write);
                await JsonSerializer.SerializeAsync(stream, assessment, options, deadline.Token);
                await using var evidenceStream = new FileStream(Path.Combine(output, mode + ".evidence.json"), FileMode.CreateNew, FileAccess.Write);
                await JsonSerializer.SerializeAsync(evidenceStream, document, options, deadline.Token);
                await using var publicKeyStream = new FileStream(Path.Combine(output, mode + ".observer-public.pem"), FileMode.CreateNew, FileAccess.Write);
                await publicKeyStream.WriteAsync(Encoding.UTF8.GetBytes(key.ExportSubjectPublicKeyInfoPem()), deadline.Token);
            }
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
        }
    }

    [Theory]
    [InlineData(1, true, ExecutionPolicyDefaults.DefaultMaxResponseBytes)]
    [InlineData(64, true, ExecutionPolicyDefaults.DefaultMaxResponseBytes)]
    [InlineData(65, false, ExecutionPolicyDefaults.DefaultMaxResponseBytes)]
    [InlineData(3, false, 256)]
    public async Task StdioResponse_ShouldBoundInterleavedNotifications(int notifications, bool expectedSuccess, int byteLimit)
    {
        var command = BuildFixtureCommand("compliant") + $" --notifications-before-response {notifications}";
        await using var adapter = await StartFixtureServerAsync(command);
        adapter.ConfigureExecutionPolicy(new ExecutionPolicy { Mode = ExecutionMode.Standard, MaxResponseBytes = byteLimit });

        var response = await adapter.CallAsync(command, "ping");

        response.IsSuccess.Should().Be(expectedSuccess);
        if (expectedSuccess)
        {
            using var document = JsonDocument.Parse(response.RawJson!);
            document.RootElement.GetProperty("id").GetString().Should().Be(response.ProbeContext!.RequestId);
        }
        else ValidationOutcomeTaxonomy.From(response.ProbeContext!.ResponseClassification).Should().Be(ValidationOutcome.Blocked);
    }

    [Fact]
    public async Task StdioResponse_ShouldNotAcceptUnrelatedRequestIdentity()
    {
        var command = BuildFixtureCommand("compliant") + " --mismatch-response-id";
        await using var adapter = await StartFixtureServerAsync(command);

        var response = await adapter.CallAsync(command, "ping");

        response.IsSuccess.Should().BeFalse();
        response.ProbeContext!.ResponseClassification.Should().Be(ProbeResponseClassification.ParserBoundary);
        response.RawJson.Should().Contain("unrelated-fixture-response");
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task QueuedStdioRequest_ShouldRetainSelectedProtocol(bool raw)
    {
        var command = BuildFixtureCommand("compliant");
        await using var adapter = await StartFixtureServerAsync(command);
        adapter.SetProtocolVersion("2025-11-25");
        var gate = (SemaphoreSlim)typeof(StdioMcpClientAdapter).GetField("_lock",
            System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!.GetValue(adapter)!;
        await gate.WaitAsync();
        Task<JsonRpcResponse> pending;
        try
        {
            pending = raw
                ? adapter.SendRawJsonAsync(command, "{\"jsonrpc\":\"2.0\",\"id\":\"queued\",\"method\":\"ping\"}", CancellationToken.None)
                : adapter.CallAsync(command, "ping");
            adapter.SetProtocolVersion("2026-07-28");
        }
        finally { gate.Release(); }

        var response = await pending.WaitAsync(TimeSpan.FromSeconds(10));

        response.IsSuccess.Should().BeTrue();
        response.ProbeContext!.ProtocolVersion.Should().Be("2025-11-25");
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(false, true)]
    [InlineData(true, true)]
    public async Task CancelledStdioRequest_ShouldPreventUncertainSessionReuse(bool raw, bool structuredProbe)
    {
        var root = Path.Combine(Path.GetTempPath(), "mcpval-cancelled-session-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        var audit = Path.Combine(root, "methods.jsonl");
        var command = BuildFixtureCommand("compliant") + $" --hold-method tools/list --audit-methods \"{audit}\"";
        try
        {
            await using var adapter = await StartFixtureServerAsync(command);
            using var cancellation = new CancellationTokenSource();
            using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(10));
            var pending = raw
                ? adapter.SendRawJsonAsync(command, "{\"jsonrpc\":\"2.0\",\"id\":\"cancelled\",\"method\":\"tools/list\"}", cancellation.Token)
                : adapter.CallAsync(command, "tools/list", null, cancellation.Token);
            try
            {
                while (!File.Exists(audit) || !(await File.ReadAllTextAsync(audit, deadline.Token)).Contains("tools/list", StringComparison.Ordinal))
                    await Task.Delay(10, deadline.Token);
            }
            finally { cancellation.Cancel(); }
            await FluentActions.Awaiting(() => pending).Should().ThrowAsync<OperationCanceledException>();

            var timeoutProbe = await adapter.ProbeTimeoutRecoveryAsync(command, deadline.Token);
            timeoutProbe.Executed.Should().BeFalse();
            timeoutProbe.GracefulRecovery.Should().BeFalse();
            (await File.ReadAllTextAsync(audit, deadline.Token)).Should().NotContain("rpc.timeout.probe");

            if (structuredProbe)
            {
                var next = await adapter.SendStdioTransportProbeAsync(new StdioTransportProbeRequest
                {
                    Endpoint = command, ProbeId = "after-cancel", Kind = StdioTransportProbeKind.MessageExchange,
                    RawMessage = "{\"jsonrpc\":\"2.0\",\"id\":\"next\",\"method\":\"prompts/list\"}"
                }, deadline.Token);
                next.IsSuccess.Should().BeFalse();
                next.Executed.Should().BeFalse();
                ValidationOutcomeTaxonomy.From(next.ProbeContext!.ResponseClassification).Should().Be(ValidationOutcome.Unavailable);
            }
            else
            {
                var next = await adapter.CallAsync(command, "prompts/list", null, deadline.Token);
                next.IsSuccess.Should().BeFalse();
                ValidationOutcomeTaxonomy.From(next.ProbeContext!.ResponseClassification).Should().Be(ValidationOutcome.Unavailable);
            }
            (await File.ReadAllTextAsync(audit, deadline.Token)).Should().NotContain("prompts/list");
        }
        finally { Directory.Delete(root, recursive: true); }
    }

    [Theory]
    [InlineData(JsonRpcErrorProbeSelection.None, 0)]
    [InlineData(JsonRpcErrorProbeSelection.ParseError, 1)]
    [InlineData(JsonRpcErrorProbeSelection.MethodNotFound, 1)]
    [InlineData(JsonRpcErrorProbeSelection.ParseError | JsonRpcErrorProbeSelection.MethodNotFound, 2)]
    public async Task CompliantFixtureServer_SelectErrorProbes_ShouldMatchObservedAttemptCount(JsonRpcErrorProbeSelection selection, int expectedAttempts)
    {
        var command = BuildFixtureCommand("compliant");
        await using var adapter = await StartFixtureServerAsync(command);
        using var telemetry = ValidationObservability.BeginRun("selective-stdio", 10);

        var result = await adapter.ValidateErrorCodesAsync(command, selection);

        result.Tests.Should().HaveCount(expectedAttempts);
        telemetry.Complete(0).RequestsStarted.Should().Be(expectedAttempts);
        result.IsCompliant.Should().Be(expectedAttempts > 0);
    }

    [Fact]
    public async Task CompliantFixtureServer_ShouldSupportInitializeAndCapabilityListing()
    {
        var command = BuildFixtureCommand("compliant");
        await using var adapter = await StartFixtureServerAsync(command);

        var initialize = await adapter.ValidateInitializeAsync(command, CancellationToken.None);
        var toolsList = await adapter.CallAsync(command, "tools/list", null, CancellationToken.None);
        var promptsList = await adapter.CallAsync(command, "prompts/list", null, CancellationToken.None);
        var resourcesList = await adapter.CallAsync(command, "resources/list", null, CancellationToken.None);

        initialize.IsSuccessful.Should().BeTrue();
        initialize.Payload.Should().NotBeNull();
        toolsList.IsSuccess.Should().BeTrue();
        toolsList.RawJson.Should().Contain("list_repositories");
        promptsList.IsSuccess.Should().BeTrue();
        promptsList.RawJson.Should().Contain("code_review");
        resourcesList.IsSuccess.Should().BeTrue();
        resourcesList.RawJson.Should().Contain("README.md");
    }

    [Fact]
    public async Task CompliantFixtureServer_ShouldSupportPaginatedToolDiscovery()
    {
        var command = BuildFixtureCommand("compliant");
        await using var adapter = await StartFixtureServerAsync(command);

        var serverConfig = new McpServerConfig
        {
            Endpoint = command,
            Transport = "stdio"
        };

        var toolValidator = CreateToolValidator(adapter);
        var result = await toolValidator.ValidateToolDiscoveryAsync(serverConfig, new ToolTestingConfig
        {
            TestToolDiscovery = true
        }, CancellationToken.None);

        result.ToolsDiscovered.Should().Be(2);
        result.DiscoveredToolNames.Should().Contain(new[] { "list_repositories", "get_repository" });
        result.Issues.Should().Contain(issue => issue.Contains("Pagination", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task StrictSessionFixtureServer_ShouldCompletePostInitializeTransitionBeforeToolDiscovery()
    {
        var command = BuildFixtureCommand("strict-session");
        await using var adapter = await StartFixtureServerAsync(command);

        var capabilitySnapshot = await adapter.ValidateCapabilitiesAsync(command, CancellationToken.None);

        var serverConfig = new McpServerConfig
        {
            Endpoint = command,
            Transport = "stdio"
        };

        var toolValidator = CreateToolValidator(adapter);
        var result = await toolValidator.ValidateToolDiscoveryAsync(serverConfig, new ToolTestingConfig
        {
            TestToolDiscovery = true,
            CapabilitySnapshot = capabilitySnapshot
        }, CancellationToken.None);

        capabilitySnapshot.IsSuccessful.Should().BeTrue();
        capabilitySnapshot.Payload.Should().NotBeNull();
        capabilitySnapshot.Payload!.ToolListResponse.Should().NotBeNull();
        result.ToolsDiscovered.Should().Be(2);
        result.DiscoveredToolNames.Should().Contain(new[] { "list_repositories", "get_repository" });
    }

    [Fact]
    public async Task CompliantFixtureServer_ShouldReturnStructuredJsonRpcErrorsForMalformedRequests()
    {
        var command = BuildFixtureCommand("compliant");
        await using var adapter = await StartFixtureServerAsync(command);

        var result = await adapter.ValidateErrorCodesAsync(command, CancellationToken.None);

        result.Tests.Should().Contain(test => test.ExpectedErrorCode == -32700 && test.IsValid);
        result.Tests.Should().Contain(test => test.ExpectedErrorCode == -32601 && test.IsValid);
        result.Tests.Should().Contain(test =>
            test.ExpectedErrorCode == -32600 &&
            test.Name.Contains("Invalid Request", StringComparison.Ordinal) &&
            test.ActualResponse != null &&
            !string.IsNullOrWhiteSpace(test.ActualResponse.RawJson));
    }

    [Fact]
    public async Task PartialFixtureServer_ShouldSurfacePromptAndResourceFindings()
    {
        var command = BuildFixtureCommand("partial");
        await using var adapter = await StartFixtureServerAsync(command);

        var serverConfig = new McpServerConfig
        {
            Endpoint = command,
            Transport = "stdio"
        };

        var promptValidator = CreatePromptValidator(adapter);
        var resourceValidator = CreateResourceValidator(adapter);

        var promptResult = await promptValidator.ValidatePromptDiscoveryAsync(serverConfig, new PromptTestingConfig
        {
            TestPromptExecution = true
        }, CancellationToken.None);

        var resourceResult = await resourceValidator.ValidateResourceDiscoveryAsync(serverConfig, new ResourceTestingConfig
        {
            TestResourceReading = true
        }, CancellationToken.None);

        promptResult.PromptResults.Should().Contain(result => result.Findings.Any(finding => finding.RuleId == ValidationFindingRuleIds.PromptMissingName));
        promptResult.PromptResults.Should().Contain(result => result.PromptName == "broken_prompt" && result.Findings.Any(finding => finding.RuleId == ValidationFindingRuleIds.PromptMessageMissingRole));
        resourceResult.ResourceResults.Should().Contain(result => result.Findings.Any(finding => finding.RuleId == ValidationFindingRuleIds.ResourceMissingUri));
        resourceResult.ResourceResults.Should().Contain(result => result.ResourceName == "BROKEN.md" && result.Findings.Any(finding => finding.RuleId == ValidationFindingRuleIds.ResourceReadMissingTextOrBlob));
        resourceResult.Findings.Should().Contain(finding => finding.RuleId == ValidationFindingRuleIds.ResourceGuidelineMimeTypeMissing);
        resourceResult.Findings.Should().Contain(finding => finding.RuleId == ValidationFindingRuleIds.ResourceUriSchemeUnclear);
        resourceResult.Findings.Should().Contain(finding => finding.RuleId == ValidationFindingRuleIds.ResourceTemplateMissingUriTemplate);
        resourceResult.Findings.Should().Contain(finding => finding.RuleId == ValidationFindingRuleIds.ResourceTemplateDescriptionMissing);
    }

    [Fact]
    public async Task UnsafeFixtureServer_ShouldSurfaceToolSafetyFindings()
    {
        var command = BuildFixtureCommand("unsafe");
        await using var adapter = await StartFixtureServerAsync(command);

        var serverConfig = new McpServerConfig
        {
            Endpoint = command,
            Transport = "stdio"
        };

        var toolValidator = CreateToolValidator(adapter);
        var result = await toolValidator.ValidateToolDiscoveryAsync(serverConfig, new ToolTestingConfig
        {
            TestToolDiscovery = true
        }, CancellationToken.None);

        result.ToolResults.Should().Contain(tool => tool.ToolName == "delete_repository" && tool.Findings.Any(finding => finding.RuleId == ValidationFindingRuleIds.ToolGuidelineDestructiveHintMissing));
        result.ToolResults.Should().Contain(tool => tool.ToolName == "purge_repository" && tool.Findings.Any(finding => finding.RuleId == ValidationFindingRuleIds.ToolDestructiveConfirmationGuidanceMissing));
    }

    [Fact]
    public async Task StdioResponseExceedsConfiguredLimit_ShouldFailAndTerminateSession()
    {
        var command = BuildFixtureCommand("compliant");
        await using var adapter = await StartFixtureServerAsync(command);
        adapter.ConfigureExecutionPolicy(new ExecutionPolicy
        {
            MaxResponseBytes = 32
        });
        using var telemetry = ValidationObservability.BeginRun("stdio-size-limit", 2);

        var oversized = await adapter.CallAsync(command, "tools/list", null, CancellationToken.None);
        var afterRejection = await adapter.CallAsync(command, "tools/list", null, CancellationToken.None);

        oversized.IsSuccess.Should().BeFalse();
        oversized.Error.Should().Contain("configured limit of 32 bytes");
        afterRejection.IsSuccess.Should().BeFalse();
        afterRejection.Error.Should().Contain("not running");
        var metrics = telemetry.Complete(100);
        metrics.RequestsStarted.Should().Be(1);
        metrics.RequestsCompleted.Should().Be(1);
        metrics.RequestsFailed.Should().Be(1);
        metrics.TruncatedResponseCount.Should().Be(1);
    }

    [Fact]
    public async Task StdioEnvironment_ShouldExcludeAmbientSecretsAndIncludeExplicitVariables()
    {
        const string ambientName = "MCPVAL_TEST_AMBIENT_SECRET";
        const string explicitName = "MCPVAL_TEST_EXPLICIT_VALUE";
        var previousAmbientValue = Environment.GetEnvironmentVariable(ambientName);
        Environment.SetEnvironmentVariable(ambientName, "ambient-canary");

        try
        {
            var command = BuildFixtureCommand("compliant");
            await using var adapter = new StdioMcpClientAdapter(new Mock<ILogger<StdioMcpClientAdapter>>().Object);
            await adapter.StartSessionAsync(
                command,
                new Dictionary<string, string> { [explicitName] = "explicit-canary" },
                CancellationToken.None);

            var response = await adapter.CallAsync(
                command,
                "fixture/environment",
                new { names = new[] { ambientName, explicitName } },
                CancellationToken.None);

            response.IsSuccess.Should().BeTrue();
            using var document = JsonDocument.Parse(response.RawJson!);
            var variables = document.RootElement.GetProperty("result").GetProperty("variables");
            variables.GetProperty(ambientName).ValueKind.Should().Be(JsonValueKind.Null);
            variables.GetProperty(explicitName).GetString().Should().Be("explicit-canary");
        }
        finally
        {
            Environment.SetEnvironmentVariable(ambientName, previousAmbientValue);
        }
    }

    [Fact]
    public async Task StdioStartupCancellation_ShouldTerminatePartiallyStartedSession()
    {
        var command = BuildFixtureCommand("compliant");
        await using var adapter = new StdioMcpClientAdapter(new Mock<ILogger<StdioMcpClientAdapter>>().Object);
        using var cancellation = new CancellationTokenSource(TimeSpan.FromMilliseconds(50));

        var start = () => adapter.StartSessionAsync(command, cancellationToken: cancellation.Token);

        await start.Should().ThrowAsync<OperationCanceledException>();
        var response = await adapter.CallAsync(command, "ping", null, CancellationToken.None);
        response.IsSuccess.Should().BeFalse();
        response.Error.Should().Contain("not running");
    }

    [Fact]
    public async Task StdioStderrExceedsCaptureLimit_ShouldRetainBoundedMarkedTail()
    {
        var command = BuildFixtureCommand("compliant");
        await using var adapter = await StartFixtureServerAsync(command);
        var byteCount = ExecutionPolicyDefaults.DefaultMaxSubprocessOutputBytes + 4096;

        var emitted = await adapter.CallAsync(command, "fixture/stderr", new { byteCount }, CancellationToken.None);
        await Task.Delay(100);
        var probe = await adapter.SendStdioTransportProbeAsync(new StdioTransportProbeRequest
        {
            ProbeId = "stderr-preview",
            Endpoint = command,
            Kind = StdioTransportProbeKind.MessageExchange,
            RawMessage = "{\"jsonrpc\":\"2.0\",\"id\":\"stderr-preview\",\"method\":\"ping\"}",
            ResponseTimeoutMs = 1000
        });

        emitted.IsSuccess.Should().BeTrue();
        probe.IsSuccess.Should().BeTrue();
        probe.StderrPreview.Should().Contain($"bytes: {byteCount + "STDERR-TAIL".Length}");
        probe.StderrPreview.Should().Contain("truncated: true");
        probe.StderrPreview.Should().NotContain("STDERR-TAIL");
        Encoding.UTF8.GetByteCount(probe.StderrPreview!).Should().BeLessThan(128);
    }

    [Fact]
    public async Task StdioModernProtocol_ShouldEmitRequiredPerRequestMetadata()
    {
        var command = BuildFixtureCommand("compliant");
        await using var adapter = await StartFixtureServerAsync(command);
        adapter.SetProtocolVersion("2026-07-28");

        var response = await adapter.CallAsync(command, "fixture/echo", new { query = "docs" }, CancellationToken.None);

        response.IsSuccess.Should().BeTrue();
        using var document = JsonDocument.Parse(response.RawJson!);
        var parameters = document.RootElement.GetProperty("result").GetProperty("params");
        parameters.GetProperty("query").GetString().Should().Be("docs");
        var metadata = parameters.GetProperty("_meta");
        metadata.GetProperty("io.modelcontextprotocol/protocolVersion").GetString().Should().Be("2026-07-28");
        metadata.GetProperty("io.modelcontextprotocol/clientCapabilities").EnumerateObject().Should().BeEmpty();
        metadata.GetProperty("io.modelcontextprotocol/clientInfo").GetProperty("name").GetString().Should().Be("mcpval");
    }

    [Fact]
    public async Task ModernFixtureServer_ShouldAdvertiseAndInitializeWith2026_07_28()
    {
        var command = BuildFixtureCommand("modern");
        await using var adapter = await StartFixtureServerAsync(command);
        adapter.SetProtocolVersion("2026-07-28");

        var discovery = await adapter.CallAsync(command, "server/discover", null, CancellationToken.None);
        var initialize = await adapter.ValidateInitializeAsync(command, CancellationToken.None);
        var tools = await adapter.CallAsync(command, "tools/list", null, CancellationToken.None);
        var errors = await adapter.ValidateErrorCodesAsync(command, CancellationToken.None);

        discovery.IsSuccess.Should().BeTrue(discovery.Error);
        discovery.ResultType.Should().Be(McpResultType.Complete);
        using var document = JsonDocument.Parse(discovery.RawJson!);
        document.RootElement.GetProperty("result").GetProperty("supportedVersions")[0].GetString().Should().Be("2026-07-28");
        initialize.IsSuccessful.Should().BeTrue(initialize.Error);
        initialize.Payload!.ProtocolVersion.Should().Be("2026-07-28");
        initialize.Payload.ServerInfo!.Name.Should().Be("fixture-modern");
        tools.IsSuccess.Should().BeTrue(tools.Error);
        using var toolsDocument = JsonDocument.Parse(tools.RawJson!);
        var toolsResult = toolsDocument.RootElement.GetProperty("result");
        toolsResult.GetProperty("resultType").GetString().Should().Be("complete");
        toolsResult.GetProperty("cacheScope").GetString().Should().Be("public");
        toolsResult.GetProperty("ttlMs").GetInt32().Should().Be(60000);
        errors.IsCompliant.Should().BeTrue();
    }

    [Fact]
    public async Task StdioModernSubscription_ShouldAcknowledgeBeforeCorrelatedClose()
    {
        const string subscriptionId = "integration-subscription";
        var command = BuildFixtureCommand("modern");
        await using var adapter = await StartFixtureServerAsync(command);
        adapter.SetProtocolVersion("2026-07-28");
        using var telemetry = ValidationObservability.BeginRun("stdio-subscription", 2);
        var listenMessage = """
            {"jsonrpc":"2.0","id":"integration-subscription","method":"subscriptions/listen","params":{"_meta":{"io.modelcontextprotocol/protocolVersion":"2026-07-28","io.modelcontextprotocol/clientCapabilities":{}},"notifications":{}}}
            """;

        var acknowledgment = await adapter.SendStdioTransportProbeAsync(new StdioTransportProbeRequest
        {
            Endpoint = command,
            ProbeId = "subscription-listen",
            Kind = StdioTransportProbeKind.MessageExchange,
            RawMessage = listenMessage,
            ResponseTimeoutMs = 1000
        });
        var close = await adapter.SendRawJsonAsync(
            command,
            $"{{\"jsonrpc\":\"2.0\",\"method\":\"notifications/cancelled\",\"params\":{{\"requestId\":\"{subscriptionId}\"}}}}",
            CancellationToken.None);

        acknowledgment.IsSuccess.Should().BeTrue();
        using var acknowledgmentDocument = JsonDocument.Parse(acknowledgment.RawStdout!);
        acknowledgmentDocument.RootElement.GetProperty("method").GetString().Should().Be("notifications/subscriptions/acknowledged");
        acknowledgmentDocument.RootElement.GetProperty("params").GetProperty("_meta")
            .GetProperty("io.modelcontextprotocol/subscriptionId").GetString().Should().Be(subscriptionId);
        close.IsSuccess.Should().BeTrue();
        using var closeDocument = JsonDocument.Parse(close.RawJson!);
        closeDocument.RootElement.GetProperty("id").GetString().Should().Be(subscriptionId);
        closeDocument.RootElement.GetProperty("result").GetProperty("_meta")
            .GetProperty("io.modelcontextprotocol/subscriptionId").GetString().Should().Be(subscriptionId);
        var metrics = telemetry.Complete(100);
        metrics.RequestsStarted.Should().Be(2);
        metrics.RequestsCompleted.Should().Be(2);
        metrics.RequestsFailed.Should().Be(0);
        metrics.QueueTimeSampleCount.Should().Be(2);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task StdioTimeoutRecovery_ShouldRecordFailedAttemptAndObservedRecovery(bool mismatchedId)
    {
        var command = BuildFixtureCommand("compliant") + (mismatchedId ? " --mismatch-response-id" : string.Empty);
        await using var adapter = await StartFixtureServerAsync(command);
        using var telemetry = ValidationObservability.BeginRun("stdio-timeout", 2);

        var result = await adapter.ProbeTimeoutRecoveryAsync(command, CancellationToken.None);
        var metrics = telemetry.Complete(100);

        result.FailureObserved.Should().BeTrue();
        result.GracefulRecovery.Should().Be(!mismatchedId);
        metrics.RequestsStarted.Should().Be(mismatchedId ? 1 : 2);
        metrics.RequestsCompleted.Should().Be(mismatchedId ? 1 : 2);
        metrics.RequestsFailed.Should().Be(1);
        metrics.QueueTimeSampleCount.Should().Be(2);
    }

    [Fact]
    public async Task StdioZeroReadDeadline_ShouldNotConsumeBufferedResponseBytes()
    {
        await using var adapter = new StdioMcpClientAdapter(Mock.Of<ILogger<StdioMcpClientAdapter>>());
        const string line = """{"jsonrpc":"2.0","id":"pending","result":{}}""";
        using var stream = new MemoryStream(Encoding.UTF8.GetBytes(line + "\n"));
        using var reader = new StreamReader(stream);
        var read = typeof(StdioMcpClientAdapter).GetMethod("ReadLineWithTimeoutAsync",
            System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!;
        var expired = (Task<string?>)read.Invoke(adapter, [reader, TimeSpan.Zero, CancellationToken.None, false])!;
        (await expired).Should().BeNull();
        (await reader.ReadLineAsync()).Should().Be(line);
    }

    [Fact]
    public async Task StdioJsonRpcErrors_ShouldCompleteTransportAcrossStructuredAndRawPaths()
    {
        var command = BuildFixtureCommand("compliant");
        await using var adapter = await StartFixtureServerAsync(command);
        using var telemetry = ValidationObservability.BeginRun("stdio-json-rpc-errors", 3);

        var structured = await adapter.CallAsync(command, "rpc.invalid.method", null, CancellationToken.None);
        var raw = await adapter.SendRawJsonAsync(
            command,
            "{\"jsonrpc\":\"2.0\",\"id\":\"raw-error\",\"method\":\"rpc.invalid.method\"}",
            CancellationToken.None);
        var exchange = await adapter.SendStdioTransportProbeAsync(new StdioTransportProbeRequest
        {
            Endpoint = command,
            ProbeId = "exchange-error",
            Kind = StdioTransportProbeKind.MessageExchange,
            RawMessage = "{\"jsonrpc\":\"2.0\",\"id\":\"exchange-error\",\"method\":\"rpc.invalid.method\"}",
            ResponseTimeoutMs = 1000
        });
        var metrics = telemetry.Complete(100);

        structured.IsSuccess.Should().BeFalse();
        raw.RawJson.Should().Contain("\"error\"");
        exchange.RawStdout.Should().Contain("\"error\"");
        metrics.RequestsStarted.Should().Be(3);
        metrics.RequestsCompleted.Should().Be(3);
        metrics.RequestsFailed.Should().Be(0);
        metrics.ErrorRate.Should().Be(0);
    }

    private static async Task<BoundedTextReadResult> ReadIdentityReadinessAsync(System.Diagnostics.Process process, CancellationToken cancellationToken)
    {
        var buffer = new BoundedByteBuffer(1024, BoundedStreamRetention.First);
        var singleByte = new byte[1];
        while (await process.StandardOutput.BaseStream.ReadAsync(singleByte, cancellationToken) != 0)
        {
            if (singleByte[0] == (byte)'\n') break;
            if (buffer.Append(singleByte))
            {
                if (!process.HasExited) process.Kill(entireProcessTree: true);
                break;
            }
        }
        return buffer.Snapshot();
    }

    private static string BuildFixtureCommand(string profile)
    {
        var scriptPath = Path.Combine(AppContext.BaseDirectory, "Fixtures", "Servers", $"mcp-fixture-{profile}.cjs");
        if (!File.Exists(scriptPath))
        {
            throw new FileNotFoundException($"Fixture server script not found: {scriptPath}");
        }

        return $"node \"{scriptPath}\"";
    }

    private static async Task<StdioMcpClientAdapter> StartFixtureServerAsync(string command)
    {
        var adapter = new StdioMcpClientAdapter(new Mock<ILogger<StdioMcpClientAdapter>>().Object);
        await adapter.StartProcessAsync(command, null, CancellationToken.None);
        return adapter;
    }

    private static ToolValidator CreateToolValidator(IMcpHttpClient client)
    {
        var schemaRegistry = new Mock<ISchemaRegistry>();
        schemaRegistry
            .Setup(registry => registry.GetSchema(It.IsAny<Mcp.Compliance.Spec.ProtocolVersion>(), It.IsAny<string>(), It.IsAny<string>()))
            .Throws(new FileNotFoundException("Schema not found"));

        var contentSafetyAnalyzer = new Mock<IContentSafetyAnalyzer>();
        contentSafetyAnalyzer
            .Setup(analyzer => analyzer.AnalyzeTool(It.IsAny<string>()))
            .Returns(new List<ContentSafetyFinding>());

        return new ToolValidator(
            new Mock<ILogger<ToolValidator>>().Object,
            client,
            new JsonSchemaValidator(),
            schemaRegistry.Object,
            new Mock<IAuthenticationService>().Object,
            contentSafetyAnalyzer.Object,
            new ToolAiReadinessAnalyzer());
    }

    private static PromptValidator CreatePromptValidator(IMcpHttpClient client)
    {
        var contentSafetyAnalyzer = new Mock<IContentSafetyAnalyzer>();
        contentSafetyAnalyzer
            .Setup(analyzer => analyzer.AnalyzePrompt(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<int>()))
            .Returns(new List<ContentSafetyFinding>());

        return new PromptValidator(
            new Mock<ILogger<PromptValidator>>().Object,
            client,
            new JsonSchemaValidator(),
            new EmbeddedSchemaRegistry(),
            contentSafetyAnalyzer.Object);
    }

    private static ResourceValidator CreateResourceValidator(IMcpHttpClient client)
    {
        var contentSafetyAnalyzer = new Mock<IContentSafetyAnalyzer>();
        contentSafetyAnalyzer
            .Setup(analyzer => analyzer.AnalyzeResource(It.IsAny<string>(), It.IsAny<string>()))
            .Returns(new List<ContentSafetyFinding>());

        return new ResourceValidator(
            new Mock<ILogger<ResourceValidator>>().Object,
            client,
            new Mock<ISchemaValidator>().Object,
            new Mock<ISchemaRegistry>().Object,
            contentSafetyAnalyzer.Object);
    }
}