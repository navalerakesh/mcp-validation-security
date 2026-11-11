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

namespace Mcp.Benchmark.Tests.Integration;

public class FixtureServerProcessIntegrationTests
{
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
            new Mock<ISchemaValidator>().Object,
            new Mock<ISchemaRegistry>().Object,
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