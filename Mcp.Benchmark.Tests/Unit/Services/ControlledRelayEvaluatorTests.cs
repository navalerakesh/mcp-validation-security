using System.Security.Cryptography;
using System.Text.Json;
using Mcp.Benchmark.Core.Constants;
using Mcp.Benchmark.Core.Models;
using Mcp.Benchmark.Core.Services;
using Mcp.Benchmark.Core.Utilities;
using Mcp.Benchmark.Infrastructure.Services;

namespace Mcp.Benchmark.Tests.Unit.Services;

public sealed class ControlledRelayEvaluatorTests
{
    private static readonly DateTimeOffset Clock = new(2026, 11, 24, 12, 0, 0, TimeSpan.Zero);
    internal static ControlledRelayContract Contract() => new()
    {
        ScenarioId = "owned-relay", PolicyRevision = "fixture-v1", ObserverId = "owned-observer",
        MaximumDuration = TimeSpan.FromSeconds(10), MaximumEvidenceAge = TimeSpan.FromMinutes(1), MaximumObserverReads = 2,
        Boundaries = [
            new()
            {
                Binding = Binding("front", "front-request"), ProtocolVersion = "2026-07-28", Transport = "http",
                Method = "tools/call", Subject = "public-write", CapabilityDigest = new string('a',64), ResponseDigest = new string('b',64),
                ExpectedHttpStatus = 200
            },
            new()
            {
                Binding = Binding("backend", "backend-request") with { ArgumentsSha256 = new string('c',64) },
                ParentRequestId = "front-request", ProtocolVersion = "2026-07-28", Transport = "http",
                Method = "tools/call", Subject = "internal-write", CapabilityDigest = new string('d',64), ResponseDigest = new string('e',64),
                ExpectedHttpStatus = 200
            }
        ]
    };

    private static ControlledOperationBinding Binding(string boundary, string request) =>
        new("owned-run", boundary + "-operation", boundary + "-attempt", request, boundary,
            new string(boundary == "front" ? '1' : '2',64), new string('3',64), new string('4',64), "actor-a", "object-a");

    internal static ControlledRelayObservation Observation(ControlledRelayBoundaryContract expected, int index) => new()
    {
        Binding = expected.Binding, ParentRequestId = expected.ParentRequestId, ResponseRequestId = expected.Binding.RequestId, ObserverId = "owned-observer",
        ProtocolVersion = expected.ProtocolVersion, Transport = expected.Transport, Method = expected.Method, Subject = expected.Subject,
        CapabilityDigest = expected.CapabilityDigest, ResponseDigest = expected.ResponseDigest,
        HttpStatus = 200, ApplicationError = false, EffectiveActorMatched = true, AudienceMatched = true, CancellationObserved = false,
        Attempts = 1, ObserverReads = 1, StartedAt = Clock.AddSeconds(-5 + index), CompletedAt = Clock.AddSeconds(-1 - index),
        ObservedThrough = Clock, ObserverOutcome = ValidationOutcome.Succeeded, Complete = true, CleanupOutcome = ValidationOutcome.Succeeded
    };

    [Theory]
    [InlineData("duration")]
    [InlineData("age")]
    [InlineData("reads")]
    [InlineData("null-boundary")]
    [InlineData("binding")]
    [InlineData("protocol")]
    [InlineData("method")]
    [InlineData("subject")]
    [InlineData("zero-attempts")]
    [InlineData("too-many-attempts")]
    [InlineData("upstream-parent")]
    [InlineData("missing-parent")]
    [InlineData("wrong-root-target")]
    [InlineData("same-boundary")]
    [InlineData("same-request")]
    [InlineData("same-operation")]
    [InlineData("effect-binding")]
    [InlineData("effect-observer")]
    public void ContractAdmission_ShouldRejectEachIndependentInvalidExpectation(string fault)
    {
        var contract = Contract();
        var first = contract.Boundaries[0];
        var second = contract.Boundaries[1];
        switch (fault)
        {
            case "duration": contract = contract with { MaximumDuration = TimeSpan.Zero }; break;
            case "age": contract = contract with { MaximumEvidenceAge = TimeSpan.Zero }; break;
            case "reads": contract = contract with { MaximumObserverReads = 0 }; break;
            case "null-boundary": second = null!; break;
            case "binding": second = second with { Binding = second.Binding with { ActorAlias = "" } }; break;
            case "protocol": second = second with { ProtocolVersion = "" }; break;
            case "method": second = second with { Method = "" }; break;
            case "subject": second = second with { Subject = "" }; break;
            case "zero-attempts": second = second with { MaximumAttempts = 0 }; break;
            case "too-many-attempts": second = second with { MaximumAttempts = 100_001 }; break;
            case "upstream-parent": first = first with { ParentRequestId = "unexpected" }; break;
            case "missing-parent": second = second with { ParentRequestId = null }; break;
            case "wrong-root-target": first = first with { Binding = first.Binding with { TargetIdentitySha256 = new string('9', 64) } }; break;
            case "same-boundary": second = second with { Binding = second.Binding with { BoundaryId = first.Binding.BoundaryId } }; break;
            case "same-request": second = second with { Binding = second.Binding with { RequestId = first.Binding.RequestId } }; break;
            case "same-operation": second = second with { Binding = second.Binding with { OperationId = first.Binding.OperationId } }; break;
            case "effect-binding": contract = contract with { EffectContract = EffectContract(contract) with { Binding = first.Binding } }; break;
            case "effect-observer": contract = contract with { EffectContract = EffectContract(contract) with { ObserverId = "different-observer" } }; break;
        }
        contract = contract with { Boundaries = [first, second] };
        var assessment = ControlledRelayEvaluator.Evaluate(contract, [], "owned-run", new string('1', 64), "owned-observer", Clock);
        assessment.Outcome.Should().Be(ValidationOutcome.Blocked);
        assessment.AdmittedContract.Should().BeNull();
        assessment.Observations.Should().BeNull();
        assessment.Reason.Should().NotBeNullOrWhiteSpace();
    }

    [Theory]
    [InlineData("http", 100)]
    [InlineData("http", 599)]
    [InlineData("stdio", null)]
    public void BoundaryAdmission_ShouldPreserveValidTransportAndAttemptLimits(string transport, int? status)
    {
        var contract = Contract();
        contract = contract with { Boundaries = contract.Boundaries.Select(boundary => boundary with
            { Transport = transport, ExpectedHttpStatus = status, MaximumAttempts = 100_000 }).ToArray() };
        var observations = contract.Boundaries.Select((boundary, index) => Observation(boundary, index) with
            { HttpStatus = status, Complete = transport == "stdio" }).ToArray();
        var assessment = ControlledRelayEvaluator.Evaluate(contract, observations, "owned-run", new string('1', 64), "owned-observer", Clock);
        assessment.AdmittedContract.Should().NotBeNull();
        assessment.Observations.Should().HaveCount(2).And.OnlyContain(item => item.HttpStatus == status);
        assessment.Checks.Should().NotContain(check => check.CheckId == "integrity");
        assessment.Outcome.Should().Be(transport == "stdio" ? ValidationOutcome.Succeeded : ValidationOutcome.Inconclusive);
    }

    [Theory]
    [InlineData("http", null)]
    [InlineData("http", 99)]
    [InlineData("http", 600)]
    [InlineData("stdio", 200)]
    public void BoundaryAdmission_ShouldRejectIncompatibleHttpStatus(string transport, int? status)
    {
        var contract = Contract();
        contract = contract with { Boundaries = [contract.Boundaries[0],
            contract.Boundaries[1] with { Transport = transport, ExpectedHttpStatus = status }] };
        ControlledRelayEvaluator.Evaluate(contract, [], "owned-run", new string('1', 64), "owned-observer", Clock)
            .AdmittedContract.Should().BeNull();
    }

    [Theory]
    [InlineData("observer")]
    [InlineData("observer-outcome")]
    [InlineData("cleanup-outcome")]
    [InlineData("parent")]
    [InlineData("response-id")]
    [InlineData("reads")]
    [InlineData("attempts")]
    [InlineData("protocol")]
    [InlineData("method")]
    [InlineData("subject")]
    [InlineData("transport")]
    [InlineData("capabilities")]
    [InlineData("response")]
    [InlineData("low-status")]
    [InlineData("high-status")]
    public void ObservationIntegrity_ShouldRejectMalformedRowsWithoutPersistingThem(string fault)
    {
        var contract = Contract();
        var bad = Observation(contract.Boundaries[1], 1);
        bad = fault switch
        {
            "observer" => bad with { ObserverId = "different-observer" },
            "observer-outcome" => bad with { ObserverOutcome = (ValidationOutcome)999 },
            "cleanup-outcome" => bad with { CleanupOutcome = (ValidationOutcome)999 },
            "parent" => bad with { ParentRequestId = "" },
            "response-id" => bad with { ResponseRequestId = "" },
            "reads" => bad with { ObserverReads = -1 },
            "attempts" => bad with { Attempts = -1 },
            "protocol" => bad with { ProtocolVersion = "" },
            "method" => bad with { Method = "" },
            "subject" => bad with { Subject = "" },
            "transport" => bad with { Transport = "" },
            "capabilities" => bad with { CapabilityDigest = "invalid" },
            "response" => bad with { ResponseDigest = "invalid" },
            "low-status" => bad with { HttpStatus = 99 },
            _ => bad with { HttpStatus = 600 }
        };
        var assessment = ControlledRelayEvaluator.Evaluate(contract, [Observation(contract.Boundaries[0], 0), bad],
            "owned-run", new string('1', 64), "owned-observer", Clock);
        assessment.Outcome.Should().Be(ValidationOutcome.Inconclusive);
        assessment.Checks.Should().Contain(new ControlledRelayCheck("backend", "integrity", ValidationOutcome.Blocked));
        assessment.Observations.Should().ContainSingle().Which.Binding.BoundaryId.Should().Be("front");
    }

    [Theory]
    [InlineData("reads")]
    [InlineData("attempts")]
    [InlineData("end")]
    [InlineData("watermark")]
    [InlineData("start-after-end")]
    [InlineData("end-after-watermark")]
    [InlineData("future-watermark")]
    public void IncompleteObservation_ShouldRemainDistinctFromMalformedOrSuccessfulEvidence(string missing)
    {
        var contract = Contract();
        var observation = Observation(contract.Boundaries[1], 1);
        observation = missing switch
        {
            "reads" => observation with { ObserverReads = 0 },
            "attempts" => observation with { Attempts = 0 },
            "end" => observation with { CompletedAt = null },
            "watermark" => observation with { ObservedThrough = null },
            "start-after-end" => observation with { StartedAt = Clock },
            "end-after-watermark" => observation with { CompletedAt = Clock.AddTicks(1) },
            _ => observation with { ObservedThrough = Clock.AddTicks(1) }
        };
        var assessment = ControlledRelayEvaluator.Evaluate(contract, [Observation(contract.Boundaries[0], 0), observation],
            "owned-run", new string('1', 64), "owned-observer", Clock);
        assessment.Outcome.Should().Be(ValidationOutcome.Inconclusive);
        assessment.Checks.Should().Contain(new ControlledRelayCheck("backend", "observation", ValidationOutcome.Inconclusive));
        assessment.Checks.Should().NotContain(check => check.CheckId == "integrity");
        assessment.Observations.Should().HaveCount(2);
    }

    [Fact]
    public void ExactTemporalLimits_ShouldAdmitEqualParentWindowsAndPreserveStableEvidenceOrder()
    {
        var contract = Contract() with { MaximumDuration = TimeSpan.FromSeconds(10), MaximumEvidenceAge = TimeSpan.FromSeconds(10) };
        var observations = contract.Boundaries.Select((boundary, index) => Observation(boundary, index) with
            { StartedAt = Clock.AddSeconds(-10), CompletedAt = Clock, ObservedThrough = Clock }).ToArray();
        var assessment = ControlledRelayEvaluator.Evaluate(contract, observations, "owned-run", new string('1', 64), "owned-observer", Clock);
        assessment.Outcome.Should().Be(ValidationOutcome.Succeeded);
        assessment.Checks.Should().OnlyContain(check => check.Outcome == ValidationOutcome.Succeeded);
        assessment.Observations!.Select(item => item.Binding.BoundaryId).Should().Equal("backend", "front");
        assessment.Reason.Should().Contain("hidden");
    }

    [Fact]
    public void MissingHopAndEffect_ShouldRemainExplicitNonCreditChecks()
    {
        var contract = Contract();
        var missingHop = ControlledRelayEvaluator.Evaluate(contract, [Observation(contract.Boundaries[0], 0)],
            "owned-run", new string('1', 64), "owned-observer", Clock);
        missingHop.Checks.Should().Contain(new ControlledRelayCheck("backend", "observation", ValidationOutcome.Unavailable));
        var effect = EffectContract(contract);
        var missingEffect = ControlledRelayEvaluator.Evaluate(contract with { EffectContract = effect }, contract.Boundaries.Select(Observation).ToArray(),
            "owned-run", new string('1', 64), "owned-observer", Clock);
        missingEffect.Checks.Should().Contain(new ControlledRelayCheck("backend", "state-effect", ValidationOutcome.Unavailable));
        missingEffect.Outcome.Should().Be(ValidationOutcome.Inconclusive);
        var evidence = new ControlledStateEvidence
        {
            Binding = effect.Binding, ObserverId = effect.ObserverId, PredicateId = effect.PredicateId, PreconditionId = effect.PreconditionId
        };
        var unadmitted = ControlledRelayEvaluator.Evaluate(contract, contract.Boundaries.Select(Observation).ToArray(),
            "owned-run", new string('1', 64), "owned-observer", Clock, evidence);
        unadmitted.Checks.Should().Contain(new ControlledRelayCheck("backend", "unadmitted-effect", ValidationOutcome.Blocked));
        unadmitted.Outcome.Should().Be(ValidationOutcome.Inconclusive);
        var supplied = ControlledRelayEvaluator.Evaluate(contract with { EffectContract = effect }, contract.Boundaries.Select(Observation).ToArray(),
            "owned-run", new string('1', 64), "owned-observer", Clock, evidence);
        supplied.Checks.Should().Contain(new ControlledRelayCheck("backend", "state-effect", supplied.Effect!.Outcome));
        supplied.Outcome.Should().NotBe(ValidationOutcome.Succeeded);
    }

    [Fact]
    public void ObservationAdmission_ShouldRejectMissingArgumentsAndUnknownBoundaryBindings()
    {
        var contract = Contract();
        FluentActions.Invoking(() => ControlledRelayEvaluator.Evaluate(null!, [], "owned-run", new string('1', 64), "owned-observer", Clock))
            .Should().Throw<ArgumentNullException>();
        FluentActions.Invoking(() => ControlledRelayEvaluator.Evaluate(contract, null!, "owned-run", new string('1', 64), "owned-observer", Clock))
            .Should().Throw<ArgumentNullException>();
        var good = Observation(contract.Boundaries[0], 0);
        foreach (var bad in new ControlledRelayObservation?[]
        {
            null, good with { Binding = good.Binding with { ActorAlias = "" } },
            good with { Binding = good.Binding with { BoundaryId = "unobserved-hop" } }
        })
            ControlledRelayEvaluator.Evaluate(contract, [bad!], "owned-run", new string('1', 64), "owned-observer", Clock)
                .AdmittedContract.Should().BeNull();
    }

    private static ControlledStateScenarioContract EffectContract(ControlledRelayContract contract) => new()
    {
        ScenarioId = "effect", PolicyRevision = "fixture-v1", ToolName = "internal-write", ObserverId = contract.ObserverId,
        PredicateId = "changed", PreconditionId = "ready", CleanupOwner = "fixture", Binding = contract.Boundaries[1].Binding,
        StateExpectation = ControlledStateExpectation.ChangeRequired, ExpectedApplicationOutcome = ControlledApplicationOutcome.Succeeded,
        ConsistencyWindow = TimeSpan.FromSeconds(5), MaximumBaselineAge = TimeSpan.FromMinutes(1),
        MaximumEvidenceAge = TimeSpan.FromMinutes(1), MaximumObserverReads = 2
    };

    [Theory]
    [InlineData("positive", ValidationOutcome.Succeeded)]
    [InlineData("missing-hop", ValidationOutcome.Inconclusive)]
    [InlineData("observer-outage", ValidationOutcome.Inconclusive)]
    [InlineData("incomplete", ValidationOutcome.Inconclusive)]
    [InlineData("wrong-target", ValidationOutcome.Failed)]
    [InlineData("wrong-parent", ValidationOutcome.Failed)]
    [InlineData("wrong-response-id", ValidationOutcome.Failed)]
    [InlineData("missing-response-id", ValidationOutcome.Inconclusive)]
    [InlineData("wrong-protocol", ValidationOutcome.Failed)]
    [InlineData("capability-widening", ValidationOutcome.Failed)]
    [InlineData("wrong-subject", ValidationOutcome.Failed)]
    [InlineData("wrong-response", ValidationOutcome.Failed)]
    [InlineData("http-failure", ValidationOutcome.Failed)]
    [InlineData("protocol-error", ValidationOutcome.Failed)]
    [InlineData("application-error", ValidationOutcome.Failed)]
    [InlineData("actor-mismatch", ValidationOutcome.Failed)]
    [InlineData("audience-mismatch", ValidationOutcome.Failed)]
    [InlineData("unknown-actor", ValidationOutcome.Inconclusive)]
    [InlineData("unexpected-cancel", ValidationOutcome.Failed)]
    [InlineData("replay", ValidationOutcome.Failed)]
    [InlineData("observer-budget", ValidationOutcome.Inconclusive)]
    [InlineData("late-child", ValidationOutcome.Failed)]
    [InlineData("stale", ValidationOutcome.Inconclusive)]
    [InlineData("missing-clock", ValidationOutcome.Inconclusive)]
    [InlineData("cleanup-failed", ValidationOutcome.Failed)]
    [InlineData("cleanup-unknown", ValidationOutcome.Inconclusive)]
    public void Evaluate_ShouldRequireBothBoundariesAndIndependentExpectedTransformations(string variant, ValidationOutcome expected)
    {
        var contract = Contract();
        var upstream = Observation(contract.Boundaries[0],0);
        var downstream = Observation(contract.Boundaries[1],1);
        downstream = variant switch
        {
            "observer-outage" => downstream with { ObserverOutcome = ValidationOutcome.Unavailable },
            "incomplete" => downstream with { Complete = false },
            "wrong-target" => downstream with { Binding = downstream.Binding with { TargetIdentitySha256 = new string('9',64) } },
            "wrong-parent" => downstream with { ParentRequestId = "unrelated" },
            "wrong-response-id" => downstream with { ResponseRequestId = "unrelated" },
            "missing-response-id" => downstream with { ResponseRequestId = null },
            "wrong-protocol" => downstream with { ProtocolVersion = "2025-11-25" },
            "capability-widening" => downstream with { CapabilityDigest = new string('f',64) },
            "wrong-subject" => downstream with { Subject = "unapproved-write" },
            "wrong-response" => downstream with { ResponseDigest = new string('f',64) },
            "http-failure" => downstream with { HttpStatus = 503 },
            "protocol-error" => downstream with { ProtocolError = -32603 },
            "application-error" => downstream with { ApplicationError = true },
            "actor-mismatch" => downstream with { EffectiveActorMatched = false },
            "audience-mismatch" => downstream with { AudienceMatched = false },
            "unknown-actor" => downstream with { EffectiveActorMatched = null },
            "unexpected-cancel" => downstream with { CancellationObserved = true },
            "replay" => downstream with { Attempts = 2 },
            "observer-budget" => downstream with { ObserverReads = 2 },
            "late-child" => downstream with { CompletedAt = Clock },
            "stale" => downstream with { StartedAt = Clock.AddMinutes(-2) },
            "missing-clock" => downstream with { StartedAt = null },
            "cleanup-failed" => downstream with { CleanupOutcome = ValidationOutcome.Failed },
            "cleanup-unknown" => downstream with { CleanupOutcome = ValidationOutcome.NotEvaluated },
            _ => downstream
        };
        var assessment = ControlledRelayEvaluator.Evaluate(contract, variant == "missing-hop" ? [upstream] : [upstream,downstream],
            "owned-run", new string('1',64), "owned-observer", Clock);
        assessment.Outcome.Should().Be(expected, string.Join("; ", assessment.Checks.Where(check => check.Outcome != ValidationOutcome.Succeeded)));
        assessment.Checks.Should().NotBeEmpty();
        if (variant == "positive")
        {
            contract.Boundaries[0].Binding.RequestId.Should().NotBe(contract.Boundaries[1].Binding.RequestId);
            contract.Boundaries[0].Binding.ArgumentsSha256.Should().NotBe(contract.Boundaries[1].Binding.ArgumentsSha256);
            assessment.Checks.Should().OnlyContain(check => check.Outcome == ValidationOutcome.Succeeded);
        }
    }

    [Theory]
    [InlineData("duplicate")]
    [InlineData("extra")]
    [InlineData("wrong-run")]
    [InlineData("wrong-key")]
    [InlineData("tampered")]
    [InlineData("expired")]
    public void SignedImport_ShouldRejectUntrustedOrAmbiguousRelayEvidence(string variant)
    {
        using var key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var contract = Contract();
        var observations = contract.Boundaries.Select(Observation).ToArray();
        if (variant == "duplicate") observations = [observations[0], observations[0]];
        if (variant == "extra") observations = [..observations, observations[0]];
        var document = Sign(key, contract, observations);
        if (variant == "tampered") document = document with { Observations = [observations[0]] };
        if (variant == "expired") document = document with { ExpiresAt = Clock };
        using var otherKey = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var import = () => new ControlledRelayEvidenceImporter().ImportPack(
            JsonSerializer.Serialize(document, OfficialConformanceContract.CreateJsonOptions()), contract,
            variant == "wrong-key" ? otherKey.ExportSubjectPublicKeyInfoPem() : key.ExportSubjectPublicKeyInfoPem(),
            variant == "wrong-run" ? "unrelated" : "owned-run", new string('1',64), Clock);
        import.Should().Throw<InvalidDataException>();
    }

    [Fact]
    public async Task SignedRelayPack_ShouldRetainNeutralEvidenceWithoutCoreCreditAndBindActiveRun()
    {
        using var key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var contract = Contract();
        var document = Sign(key,contract,contract.Boundaries.Select(Observation).ToArray());
        var pack = new ControlledRelayEvidenceImporter().ImportPack(JsonSerializer.Serialize(document,OfficialConformanceContract.CreateJsonOptions()),
            contract,key.ExportSubjectPublicKeyInfoPem(),"owned-run",new string('1',64),Clock);
        foreach (var wrongRun in new[] { false,true })
        {
            var result = new ValidationResult { ValidationId = wrongRun ? "unrelated" : "owned-run", StartTime = Clock.UtcDateTime };
            result.Run.TargetIdentitySha256 = new string('1',64);
            var before = AssuranceAssessmentEngine.Evaluate(result,AssuranceProfileCatalog.Inventory);
            var execution = await pack.GetScenarios().Single().ExecuteAsync(new()
            {
                ValidationResult = result, ValidationConfiguration = new(), ServerConfig = new(),
                ApplicabilityContext = new() { NegotiatedProtocolVersion = "2026-07-28", SchemaVersion = "2026-07-28", Transport = "http", AccessMode = "Public" }
            },CancellationToken.None);
            execution.Scenario.Status.Should().Be(wrongRun ? TestStatus.Inconclusive : TestStatus.Passed);
            execution.Coverage.Should().OnlyContain(coverage => coverage.Lane == EvaluationLane.ControlledApplication);
            result.Assessments.Scenarios.Add(execution.Scenario);
            result.Evidence.Coverage.AddRange(execution.Coverage);
            var after = AssuranceAssessmentEngine.Evaluate(result,AssuranceProfileCatalog.Inventory);
            after.Assessments.CoreConformance.Should().BeEquivalentTo(before.Assessments.CoreConformance);
            result.CloneWithoutSecrets().Assessments.Scenarios.Single().ControlledRelay!.Checks.Should().BeEquivalentTo(execution.Scenario.ControlledRelay!.Checks);
            var schemaPath = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "docs", "Schemas", "mcpval-validation-result.schema.json"));
            var canonical = System.Text.Json.Nodes.JsonNode.Parse(JsonSerializer.Serialize(result.CloneWithoutSecrets(), OfficialConformanceContract.CreateJsonOptions()))!;
            var validation = new Mcp.Benchmark.Infrastructure.Validators.JsonSchemaValidator().Validate(canonical,
                System.Text.Json.Nodes.JsonNode.Parse(File.ReadAllText(schemaPath))!);
            validation.IsValid.Should().BeTrue(string.Join("; ", validation.Errors));
        }

    }

    [Theory]
    [InlineData("boundary-count")]
    [InlineData("oversized-alias")]
    [InlineData("effect-alias")]
    [InlineData("effect-revision")]
    [InlineData("effect-expectation")]
    public void Import_ShouldBoundCallerContractBeforeParsingOrHashingEvidence(string variant)
    {
        var contract = Contract();
        if (variant.StartsWith("effect-", StringComparison.Ordinal))
        {
            var effect = new ControlledStateScenarioContract
            {
                ScenarioId = "effect", PolicyRevision = "fixture-v1", ToolName = "internal-write", ObserverId = contract.ObserverId,
                PredicateId = "changed", PreconditionId = "ready", CleanupOwner = "fixture", Binding = contract.Boundaries[1].Binding,
                StateExpectation = ControlledStateExpectation.ChangeRequired, ExpectedApplicationOutcome = ControlledApplicationOutcome.Succeeded,
                ConsistencyWindow = TimeSpan.FromSeconds(1), MaximumBaselineAge = TimeSpan.FromSeconds(1),
                MaximumEvidenceAge = TimeSpan.FromSeconds(1), MaximumObserverReads = 1
            };
            contract = contract with { EffectContract = variant switch
            {
                "effect-alias" => effect with { PredicateId = new string('x', ControlledStateScenarioContract.MaximumIdentifierCharacters + 1) },
                "effect-revision" => effect with { ContractRevision = "unadmitted" },
                _ => effect with { StateExpectation = ControlledStateExpectation.Unspecified }
            }};
        }
        else contract = variant == "boundary-count"
            ? contract with { Boundaries = [.. contract.Boundaries, contract.Boundaries[0]] }
            : contract with { ScenarioId = new string('x', ControlledStateScenarioContract.MaximumIdentifierCharacters + 1) };
        var import = () => new ControlledRelayEvidenceImporter().ImportPack("not-json", contract, "not-a-key",
            "owned-run", new string('1', 64), Clock);
        import.Should().Throw<InvalidDataException>().WithMessage("Relay contract did not match*");
    }

    internal static ControlledRelayEvidenceDocument Sign(ECDsa key, ControlledRelayContract contract, IReadOnlyList<ControlledRelayObservation> observations)
    {
        var document = new ControlledRelayEvidenceDocument
        {
            Contract = contract, Observations = observations, IssuedAt = Clock, ExpiresAt = Clock.AddMinutes(1),
            SigningKeyId = CanonicalJsonDigest.ComputeBytes(key.ExportSubjectPublicKeyInfo()), SignatureBase64 = ""
        };
        return document with { SignatureBase64 = Convert.ToBase64String(key.SignHash(Convert.FromHexString(document.ComputePayloadDigest()),
            DSASignatureFormat.IeeeP1363FixedFieldConcatenation)) };
    }

    [Theory]
    [InlineData("fresh", TestStatus.Passed)]
    [InlineData("expired", TestStatus.Inconclusive)]
    [InlineData("stale", TestStatus.Inconclusive)]
    [InlineData("before-issued", TestStatus.Inconclusive)]
    public async Task RelayPack_ShouldRecheckSignedValidityAndFreshnessAtTheRecordedRunClock(string variant, TestStatus expected)
    {
        using var key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var contract = Contract();
        var document = Sign(key, contract, contract.Boundaries.Select(Observation).ToArray());
        if (variant == "stale")
        {
            document = document with { ExpiresAt = Clock.AddMinutes(3) };
            document = document with { SignatureBase64 = Convert.ToBase64String(key.SignHash(
                Convert.FromHexString(document.ComputePayloadDigest()), DSASignatureFormat.IeeeP1363FixedFieldConcatenation)) };
        }
        var pack = new ControlledRelayEvidenceImporter().ImportPack(JsonSerializer.Serialize(document, OfficialConformanceContract.CreateJsonOptions()),
            contract, key.ExportSubjectPublicKeyInfoPem(), "owned-run", new string('1',64), Clock);
        var at = variant switch { "expired" => Clock.AddMinutes(1), "stale" => Clock.AddMinutes(2),
            "before-issued" => Clock.AddTicks(-1), _ => Clock };
        var result = new ValidationResult { ValidationId = "owned-run", StartTime = at.UtcDateTime };
        result.Run.TargetIdentitySha256 = new string('1',64);
        var execution = await pack.GetScenarios().Single().ExecuteAsync(new()
        {
            ValidationResult = result, ValidationConfiguration = new(), ServerConfig = new(),
            ApplicabilityContext = new() { NegotiatedProtocolVersion = "2026-07-28", SchemaVersion = "2026-07-28", Transport = "http", AccessMode = "Public" }
        }, CancellationToken.None);
        execution.Scenario.Status.Should().Be(expected);
        execution.Scenario.ControlledRelay!.AssessedAt.Should().Be(at);
        if (variant != "fresh") execution.Coverage.Should().Contain(coverage => coverage.Status != ValidationCoverageStatus.Covered);
        if (variant is "expired" or "before-issued") execution.Coverage.Should().NotContain(coverage => coverage.ObservedOutcome == ValidationOutcome.Succeeded);
    }

    [Theory]
    [InlineData(true, true, false)]
    [InlineData(false, true, true)]
    [InlineData(true, false, false)]
    public async Task RelaySemanticIdentity_ShouldDistinguishRealRegressionFromFreshRunProvenance(bool beforeFailed, bool afterFailed, bool regression)
    {
        var before = await Run("first", Clock, beforeFailed);
        var after = await Run("second", Clock.AddMinutes(2), afterFailed);
        before.Assessments.Scenarios.Single().ScenarioId.Should().Be(after.Assessments.Scenarios.Single().ScenarioId);
        before.Assessments.Scenarios.Single().ControlledRelay!.SourceEvidenceSha256.Should()
            .NotBe(after.Assessments.Scenarios.Single().ControlledRelay!.SourceEvidenceSha256);
        var comparison = ValidationBaselineEvaluator.Compare(before, after);
        comparison.IsRegression.Should().Be(regression);
        if (beforeFailed == afterFailed)
        {
            comparison.NewNonSuccessClaims.Should().BeEmpty();
            comparison.ResolvedNonSuccessClaimIds.Should().BeEmpty();
        }
        else if (afterFailed) comparison.NewNonSuccessClaims.Should().NotBeEmpty();
        else comparison.ResolvedNonSuccessClaimIds.Should().NotBeEmpty();

        static async Task<ValidationResult> Run(string run, DateTimeOffset clock, bool failed)
        {
            using var key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
            var original = Contract();
            var boundaries = original.Boundaries.Select((boundary,index) => boundary with
            {
                Binding = boundary.Binding with { RunId = run, OperationId = run + "-operation-" + index,
                    AttemptId = run + "-attempt-" + index, RequestId = run + "-request-" + index },
                ParentRequestId = index == 0 ? null : run + "-request-0"
            }).ToArray();
            var contract = original with { Boundaries = boundaries };
            var observations = boundaries.Select((boundary,index) => Observation(boundary,index) with
            {
                StartedAt = clock.AddSeconds(-5 + index), CompletedAt = clock.AddSeconds(-1-index), ObservedThrough = clock,
                EffectiveActorMatched = !(failed && index == 1)
            }).ToArray();
            var document = Sign(key,contract,observations) with { IssuedAt = clock, ExpiresAt = clock.AddMinutes(1) };
            document = document with { SignatureBase64 = Convert.ToBase64String(key.SignHash(
                Convert.FromHexString(document.ComputePayloadDigest()), DSASignatureFormat.IeeeP1363FixedFieldConcatenation)) };
            var pack = new ControlledRelayEvidenceImporter().ImportPack(JsonSerializer.Serialize(document,OfficialConformanceContract.CreateJsonOptions()),
                contract,key.ExportSubjectPublicKeyInfoPem(),run,new string('1',64),clock);
            var result = new ValidationResult { ValidationId = run, StartTime = clock.UtcDateTime, OverallStatus = ValidationStatus.Passed };
            result.Run.TargetIdentitySha256 = new string('1',64);
            var execution = await pack.GetScenarios().Single().ExecuteAsync(new()
            {
                ValidationResult = result, ValidationConfiguration = new(), ServerConfig = new(),
                ApplicabilityContext = new() { NegotiatedProtocolVersion = "2026-07-28", SchemaVersion = "2026-07-28", Transport = "http", AccessMode = "Public" }
            },CancellationToken.None);
            result.Assessments.Scenarios.Add(execution.Scenario);
            result.Evidence.Coverage.AddRange(execution.Coverage);
            result.Assurance = AssuranceAssessmentEngine.Evaluate(result,AssuranceProfileCatalog.Inventory);
            return result;
        }
    }
}
