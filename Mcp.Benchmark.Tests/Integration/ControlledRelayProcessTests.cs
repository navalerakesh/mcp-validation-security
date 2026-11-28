using System.Diagnostics;
using System.Net;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Xml.Linq;
using Mcp.Benchmark.Core.Constants;
using Mcp.Benchmark.Core.Models;
using Mcp.Benchmark.Core.Services;
using Mcp.Benchmark.Core.Utilities;
using Mcp.Benchmark.Infrastructure.Services;
using Mcp.Benchmark.Infrastructure.Services.Reporting;
using Mcp.Benchmark.Infrastructure.Utilities;

namespace Mcp.Benchmark.Tests.Integration;

public sealed class ControlledRelayProcessTests
{
    [Theory]
    [InlineData("positive", ValidationOutcome.Succeeded)]
    [InlineData("wrong-route", ValidationOutcome.Failed)]
    [InlineData("wrong-target", ValidationOutcome.Failed)]
    [InlineData("wrong-correlation", ValidationOutcome.Failed)]
    [InlineData("wrong-response-id", ValidationOutcome.Failed)]
    [InlineData("wrong-identity", ValidationOutcome.Failed)]
    [InlineData("replay", ValidationOutcome.Failed)]
    [InlineData("late-response", ValidationOutcome.Failed)]
    [InlineData("client-cancel", ValidationOutcome.Inconclusive)]
    [InlineData("response-lost", ValidationOutcome.Inconclusive)]
    [InlineData("observer-unavailable", ValidationOutcome.Inconclusive)]
    public async Task OwnedRelay_ShouldObserveBothBoundariesAndRejectIndependentFaults(string mode, ValidationOutcome expected)
    {
        var credentials = new { front = Convert.ToHexString(RandomNumberGenerator.GetBytes(24)),
            backend = Convert.ToHexString(RandomNumberGenerator.GetBytes(24)), observer = Convert.ToHexString(RandomNumberGenerator.GetBytes(24)) };
        var start = new ProcessStartInfo("node") { RedirectStandardInput = true, RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false };
        start.ArgumentList.Add(Path.Combine(AppContext.BaseDirectory, "Fixtures", "Servers", "mcp-fixture-relay.cjs"));
        start.ArgumentList.Add(mode);
        start.Environment.Clear();
        foreach (var name in new[] { "PATH", "SYSTEMROOT", "WINDIR", "PATHEXT" })
            if (Environment.GetEnvironmentVariable(name) is { } value) start.Environment[name] = value;
        start.Environment["MCPVAL_RELAY_CREDENTIALS"] = JsonSerializer.Serialize(credentials);
        using var process = Process.Start(start)!;
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(15));
        var stderr = BoundedStreamReader.ReadUtf8Async(process.StandardError.BaseStream, 4096,
            BoundedStreamRetention.First, () => process.Kill(entireProcessTree: true), deadline.Token);
        JsonElement? observed = null;
        const int maximumObserverSnapshots = 32;
        var observerSnapshots = 0;
        ControlledRelayContract? contract = null;
        try
        {
            var ready = await process.StandardOutput.ReadLineAsync(deadline.Token);
            ready.Should().NotBeNull();
            ready!.Length.Should().BeLessThan(1024);
            using var ports = JsonDocument.Parse(ready);
            var front = $"http://127.0.0.1:{ports.RootElement.GetProperty("front").GetInt32()}/mcp";
            var backend = $"http://127.0.0.1:{ports.RootElement.GetProperty("backend").GetInt32()}/mcp";
            var run = Guid.NewGuid().ToString("N");
            var upstream = Boundary("front", "front-request", front, "public-write", 2, null, "saved");
            var downstream = Boundary("backend", "backend-0", backend, "internal-write", 4, "front-request", "backend-saved");
            contract = new()
            {
                ScenarioId = "owned-relay", PolicyRevision = "fixture-v1", ObserverId = "out-of-band",
                Boundaries = [upstream, downstream], MaximumDuration = mode == "late-response" ? TimeSpan.FromMilliseconds(10) : TimeSpan.FromSeconds(10),
                MaximumEvidenceAge = TimeSpan.FromMinutes(1), MaximumObserverReads = maximumObserverSnapshots * 2,
                EffectContract = new()
                {
                    ScenarioId = "relay-effect", PolicyRevision = "fixture-v1", ToolName = "internal-write", ObserverId = "out-of-band",
                    PredicateId = "counter-is-four", PreconditionId = "counter-is-zero", CleanupOwner = "fixture-process",
                    Binding = downstream.Binding, StateExpectation = ControlledStateExpectation.ChangeRequired,
                    ExpectedApplicationOutcome = ControlledApplicationOutcome.Succeeded, ConsistencyWindow = TimeSpan.FromSeconds(10),
                    MaximumBaselineAge = TimeSpan.FromMinutes(1), MaximumEvidenceAge = TimeSpan.FromMinutes(1), MaximumObserverReads = maximumObserverSnapshots
                }
            };
            using var client = new HttpClient(new HttpClientHandler { AllowAutoRedirect = false }) { Timeout = TimeSpan.FromSeconds(5) };
            var baseline = await ObserveSnapshot();
            var baselineAt = DateTimeOffset.UtcNow;
            using var request = new HttpRequestMessage(HttpMethod.Post, front)
            {
                Content = new StringContent(JsonSerializer.Serialize(new
                {
                    jsonrpc = "2.0", id = "front-request", method = "tools/call",
                    @params = new { name = "public-write", arguments = new { value = 2 }, _meta = new Dictionary<string, object>
                    { ["io.modelcontextprotocol/protocolVersion"] = "2026-07-28", ["io.modelcontextprotocol/clientCapabilities"] = new { } } }
                }), Encoding.UTF8, "application/json")
            };
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", credentials.front);
            if (mode == "client-cancel")
            {
                using var cancel = CancellationTokenSource.CreateLinkedTokenSource(deadline.Token);
                var pending = client.SendAsync(request, cancel.Token);
                try
                {
                    await ObserveUntil(snapshot => snapshot.GetProperty("front").EnumerateArray()
                        .Any(item => item.TryGetProperty("waitingForCancellation", out var waiting) && waiting.GetBoolean()));
                    cancel.Cancel();
                    var completed = async () => { using var response = await pending; };
                    await completed.Should().ThrowAsync<OperationCanceledException>();
                    observed = await ObserveUntil(snapshot => snapshot.GetProperty("front").EnumerateArray()
                        .Any(item => item.TryGetProperty("completedAt", out _)));
                }
                finally
                {
                    cancel.Cancel();
                    try { using var response = await pending; }
                    catch (OperationCanceledException) when (cancel.IsCancellationRequested) { }
                }
            }
            else
            {
                try { using var response = await client.SendAsync(request, deadline.Token); }
                catch (HttpRequestException) when (mode == "response-lost") { }
                observed = await ObserveSnapshot();
            }
            process.StandardInput.Close();
            await process.WaitForExitAsync(deadline.Token);
            process.ExitCode.Should().Be(0);
            var assessedAt = DateTimeOffset.UtcNow;
            var observations = observed is { } snapshot
                ? new[] { Observe("front", upstream), Observe("backend", downstream) } : Array.Empty<ControlledRelayObservation>();
            var effect = observed is { } capturedState ? new ControlledStateEvidence
            {
                Binding = observations[1].Binding, ObserverId = contract.ObserverId, PredicateId = "counter-is-four", PreconditionId = "counter-is-zero",
                BaselineAt = baselineAt, OperationStartedAt = observations[1].StartedAt, OperationEndedAt = observations[1].CompletedAt,
                ObservedThrough = assessedAt, ObserverOutcome = ValidationOutcome.Succeeded, ObservationComplete = true,
                PreconditionsSatisfied = baseline?.GetProperty("value").GetInt32() == 0,
                BeforePredicateSatisfied = baseline?.GetProperty("value").GetInt32() == 4, AfterPredicateSatisfied = capturedState.GetProperty("value").GetInt32() == 4,
                StateChanged = capturedState.GetProperty("value").GetInt32() != baseline?.GetProperty("value").GetInt32(),
                ApplicationOutcome = observations[1].HttpStatus == 200 ? ControlledApplicationOutcome.Succeeded : ControlledApplicationOutcome.Rejected,
                ObserverReads = observerSnapshots, CleanupOutcome = ValidationOutcome.Succeeded
            } : null;
            using var key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
            var document = new ControlledRelayEvidenceDocument
            {
                Contract = contract, Observations = observations, EffectEvidence = effect, IssuedAt = assessedAt,
                ExpiresAt = assessedAt.AddMinutes(1), SigningKeyId = CanonicalJsonDigest.ComputeBytes(key.ExportSubjectPublicKeyInfo()), SignatureBase64 = ""
            };
            document = document with { SignatureBase64 = Convert.ToBase64String(key.SignHash(Convert.FromHexString(document.ComputePayloadDigest()),
                DSASignatureFormat.IeeeP1363FixedFieldConcatenation)) };
            var pack = new ControlledRelayEvidenceImporter().ImportPack(JsonSerializer.Serialize(document, OfficialConformanceContract.CreateJsonOptions()),
                contract, key.ExportSubjectPublicKeyInfoPem(), run, upstream.Binding.TargetIdentitySha256, assessedAt);
            var result = new ValidationResult { ValidationId = run };
            result.Run.TargetIdentitySha256 = upstream.Binding.TargetIdentitySha256;
            var execution = await pack.GetScenarios().Single().ExecuteAsync(new()
            {
                ValidationResult = result, ValidationConfiguration = new(), ServerConfig = new(),
                ApplicabilityContext = new() { NegotiatedProtocolVersion = "2026-07-28", SchemaVersion = "2026-07-28", Transport = "http", AccessMode = "Public" }
            }, deadline.Token);
            execution.Scenario.ControlledRelay!.Outcome.Should().Be(expected,
                string.Join("; ", execution.Scenario.ControlledRelay.Checks.Where(check => check.Outcome != ValidationOutcome.Succeeded)));
            if (mode == "client-cancel")
            {
                observations[0].CancellationObserved.Should().BeTrue();
                effect!.StateChanged.Should().BeTrue("caller cancellation is not rollback");
                execution.Scenario.ControlledRelay.Effect!.Outcome.Should().Be(ValidationOutcome.Succeeded);
            }
            result.Assessments.Scenarios.Add(execution.Scenario);
            result.Evidence.Coverage.AddRange(execution.Coverage);
            result.Assurance = AssuranceAssessmentEngine.Evaluate(result, AssuranceProfileCatalog.Inventory);
            var renderer = new ValidationReportRenderer();
            var markdown = new MarkdownReportGenerator().GenerateReport(result);
            var html = renderer.GenerateHtmlReport(result, new(), verbose: true);
            var xml = XDocument.Parse(renderer.GenerateXmlReport(result, verbose: true));
            var junit = XDocument.Parse(renderer.GenerateJunitReport(result));
            using var sarif = JsonDocument.Parse(renderer.GenerateSarifReport(result));
            var sarifClaims = sarif.RootElement.GetProperty("runs")[0].GetProperty("results").EnumerateArray()
                .Where(item => item.GetProperty("properties").TryGetProperty("claimId", out _))
                .ToDictionary(item => item.GetProperty("properties").GetProperty("claimId").GetString()!,
                    item => item.GetProperty("properties").GetProperty("outcome").GetString());
            foreach (var claim in result.Assurance.Claims)
            {
                markdown.Should().Contain(claim.ClaimId);
                html.Should().Contain($"data-claim-id=\"{claim.ClaimId}\" data-claim-outcome=\"{claim.Outcome}\"");
                xml.Descendants("Claim").Single(item => item.Attribute("id")?.Value == claim.ClaimId)
                    .Attribute("outcome")!.Value.Should().Be(claim.Outcome.ToString());
                var testCase = junit.Descendants("testcase").Single(item => item.Attribute("name")?.Value == claim.ClaimId);
                if (claim.Outcome == AssuranceClaimOutcome.Violated) testCase.Element("failure").Should().NotBeNull();
                else testCase.Element("failure").Should().BeNull();
                if (claim.Outcome is not (AssuranceClaimOutcome.Satisfied or AssuranceClaimOutcome.Violated or AssuranceClaimOutcome.Error))
                    testCase.Element("skipped").Should().NotBeNull();
                if (claim.Outcome is AssuranceClaimOutcome.Satisfied or AssuranceClaimOutcome.NotApplicable)
                    sarifClaims.Should().NotContainKey(claim.ClaimId);
                else sarifClaims.Should().ContainKey(claim.ClaimId).WhoseValue.Should().Be(claim.Outcome.ToString());
            }
            var canonical = JsonSerializer.Serialize(result.CloneWithoutSecrets());
            foreach (var credential in new[] { credentials.front, credentials.backend, credentials.observer })
                foreach (var artifact in new[] { canonical, markdown, html, xml.ToString(), junit.ToString(), sarif.RootElement.GetRawText() })
                    artifact.Should().NotContain(credential);

            ControlledRelayBoundaryContract Boundary(string id, string requestId, string target, string subject, int argument, string? parent, string text) => new()
            {
                Binding = new(run, id + "-operation", id + "-attempt", requestId, id, CanonicalJsonDigest.ComputeText(target),
                    CanonicalJsonDigest.ComputeText(target), CanonicalJsonDigest.ComputeObject(new { value = argument }), "actor-a", "counter"),
                ParentRequestId = parent, ProtocolVersion = "2026-07-28", Transport = "http", Method = "tools/call", Subject = subject,
                CapabilityDigest = CanonicalJsonDigest.ComputeObject(new { }),
                ResponseDigest = CanonicalJsonDigest.ComputeObject(new { resultType = "complete", content = new[] { new { type = "text", text } } }),
                ExpectedHttpStatus = 200
            };
            ControlledRelayObservation Observe(string boundary, ControlledRelayBoundaryContract expectedBoundary)
            {
                var entries = observed!.Value.GetProperty(boundary);
                var first = entries[0];
                var last = entries[entries.GetArrayLength() - 1];
                var wire = first.GetProperty("request");
                var parameters = wire.GetProperty("params");
                return new()
                {
                    Binding = expectedBoundary.Binding with { RequestId = wire.GetProperty("id").GetString()!,
                        TargetIdentitySha256 = CanonicalJsonDigest.ComputeText(first.GetProperty("endpoint").GetString()!),
                        RouteIdentitySha256 = CanonicalJsonDigest.ComputeText(first.GetProperty("endpoint").GetString()!),
                        ArgumentsSha256 = CanonicalJsonDigest.ComputeObject(parameters.GetProperty("arguments")) },
                    ParentRequestId = boundary == "backend" ? parameters.GetProperty("_meta").GetProperty("fixture.parentRequestId").GetString() : null,
                    ResponseRequestId = last.GetProperty("responseRequestId").GetString(),
                    ObserverId = contract.ObserverId, ProtocolVersion = parameters.GetProperty("_meta").GetProperty("io.modelcontextprotocol/protocolVersion").GetString()!,
                    Transport = "http", Method = wire.GetProperty("method").GetString()!, Subject = parameters.GetProperty("name").GetString()!,
                    CapabilityDigest = CanonicalJsonDigest.ComputeObject(parameters.GetProperty("_meta").GetProperty("io.modelcontextprotocol/clientCapabilities")),
                    ResponseDigest = CanonicalJsonDigest.ComputeObject(last.GetProperty("response")),
                    HttpStatus = last.GetProperty("httpStatus").ValueKind == JsonValueKind.Null ? null : last.GetProperty("httpStatus").GetInt32(),
                    ApplicationError = false, EffectiveActorMatched = entries.EnumerateArray().All(item => item.GetProperty("identityMatched").GetBoolean()),
                    AudienceMatched = entries.EnumerateArray().All(item => item.GetProperty("identityMatched").GetBoolean()),
                    Attempts = entries.GetArrayLength(), ObserverReads = observerSnapshots,
                    CancellationObserved = first.TryGetProperty("cancellationObserved", out var cancelled) && cancelled.GetBoolean(),
                    StartedAt = first.GetProperty("startedAt").GetDateTimeOffset(), CompletedAt = last.GetProperty("completedAt").GetDateTimeOffset(),
                    ObservedThrough = assessedAt, ObserverOutcome = ValidationOutcome.Succeeded,
                    Complete = last.GetProperty("response").ValueKind != JsonValueKind.Null, CleanupOutcome = ValidationOutcome.Succeeded
                };
            }
            async Task<JsonElement?> ObserveSnapshot()
            {
                (++observerSnapshots).Should().BeLessThanOrEqualTo(maximumObserverSnapshots);
                using var observerRequest = new HttpRequestMessage(HttpMethod.Get, front.Replace("/mcp", "/observer", StringComparison.Ordinal));
                observerRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", credentials.observer);
                using var observerResponse = await client.SendAsync(observerRequest, deadline.Token);
                if (mode == "observer-unavailable")
                {
                    observerResponse.StatusCode.Should().Be(HttpStatusCode.ServiceUnavailable);
                    return null;
                }
                observerResponse.EnsureSuccessStatusCode();
                var bytes = await observerResponse.Content.ReadAsByteArrayAsync(deadline.Token);
                bytes.Length.Should().BeLessThan(8192);
                using var captured = JsonDocument.Parse(bytes);
                return captured.RootElement.Clone();
            }
            async Task<JsonElement> ObserveUntil(Func<JsonElement, bool> predicate)
            {
                while (observerSnapshots < maximumObserverSnapshots)
                {
                    var snapshot = await ObserveSnapshot();
                    snapshot.Should().NotBeNull();
                    if (predicate(snapshot!.Value)) return snapshot.Value;
                    await Task.Delay(TimeSpan.FromMilliseconds(10), deadline.Token);
                }
                throw new TimeoutException("Owned relay observer budget exhausted before the required event.");
            }
        }
        finally
        {
            if (!process.HasExited) { process.Kill(entireProcessTree: true); await process.WaitForExitAsync(); }
            var errors = await stderr;
            errors.IsTruncated.Should().BeFalse();
            errors.Text.Should().BeEmpty();
        }
    }
}
