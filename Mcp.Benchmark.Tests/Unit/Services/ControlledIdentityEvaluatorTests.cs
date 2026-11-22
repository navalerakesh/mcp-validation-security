using System.Text.Json;
using System.Text.Json.Serialization;
using System.Security.Cryptography;
using FluentAssertions;
using Mcp.Benchmark.Core.Models;
using Mcp.Benchmark.Core.Services;
using Mcp.Benchmark.Core.Utilities;
using Mcp.Benchmark.Core.Abstractions;
using Mcp.Benchmark.Core.Constants;
using Mcp.Benchmark.Infrastructure.Services;

namespace Mcp.Benchmark.Tests.Unit.Services;

public sealed class ControlledIdentityEvaluatorTests
{
    private static readonly DateTimeOffset Clock = new(2026, 11, 22, 0, 0, 0, TimeSpan.Zero);
    private static readonly ControlledOperationBinding Binding = new("run-1", "operation-control", "attempt-control", "request-control", "boundary-1",
        new string('a', 64), new string('b', 64), new string('c', 64), "actor-a", "object-a");

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void KnownViolations_ShouldSurviveMissingControlAndRetainEveryObligation(bool reverse)
    {
        var contract = Contract();
        if (reverse) contract = contract with { Cases = contract.Cases.Reverse().ToArray() };
        var result = Evaluate(contract, [Observation(Probe()) with { Decision = ControlledAuthorizationDecision.Allowed, EffectiveActorMatched = false }]);

        result.Outcome.Should().Be(ValidationOutcome.Failed);
        result.Cases.Select(entry => entry.CaseId).Should().Equal("control", "probe");
        result.Cases[0].ObservationOutcome.Should().Be(ValidationOutcome.Unavailable);
        result.Cases[1].DecisionOutcome.Should().Be(ValidationOutcome.Failed);
        result.Cases[1].EffectiveActorOutcome.Should().Be(ValidationOutcome.Failed);
        result.Cases[1].PositiveControlOutcome.Should().Be(ValidationOutcome.Inconclusive);
        result.RetryPermitted.Should().BeFalse();
    }

    [Fact]
    public void ValidMatrix_ShouldBeOrderIndependentAndReplayable()
    {
        var observations = new[] { Observation(Control()), Observation(Probe()) };
        var first = Evaluate(Contract(), observations);
        var second = Evaluate(Contract() with { Cases = Contract().Cases.Reverse().ToArray() }, observations.Reverse().ToArray());
        var restored = JsonSerializer.Deserialize<ControlledIdentityMatrixAssessment>(JsonSerializer.Serialize(first))!;

        first.Outcome.Should().Be(ValidationOutcome.Succeeded);
        JsonSerializer.Serialize(second).Should().Be(JsonSerializer.Serialize(first));
        JsonSerializer.Serialize(Evaluate(restored.AdmittedContract!, restored.RecordedObservations!)).Should().Be(JsonSerializer.Serialize(first));
    }

    [Theory]
    [InlineData(ControlledIdentityRole.ResourceSelector, null, ValidationOutcome.Succeeded)]
    [InlineData(ControlledIdentityRole.DisplayOnly, null, ValidationOutcome.Succeeded)]
    [InlineData(ControlledIdentityRole.Unknown, null, ValidationOutcome.Inconclusive)]
    [InlineData(ControlledIdentityRole.DelegationReference, true, ValidationOutcome.Succeeded)]
    [InlineData(ControlledIdentityRole.DelegationReference, false, ValidationOutcome.Failed)]
    [InlineData(ControlledIdentityRole.DelegationReference, null, ValidationOutcome.Inconclusive)]
    [InlineData(ControlledIdentityRole.ApproverAssertion, true, ValidationOutcome.Succeeded)]
    [InlineData(ControlledIdentityRole.ApproverAssertion, false, ValidationOutcome.Failed)]
    public void Roles_ShouldUseExplicitBindingEvidenceRatherThanNames(ControlledIdentityRole role, bool? matched, ValidationOutcome expected)
    {
        var probe = Probe() with { IdentityRole = role, ExpectedDecision = ControlledAuthorizationDecision.Allowed, ExpectedEffectiveActorAlias = "delegate-b" };
        var result = Evaluate(Contract() with { Cases = [Control(), probe] }, [Observation(Control()), Observation(probe) with
        {
            EffectiveActorMatched = true, DelegationBindingMatched = matched, ApprovalBindingMatched = matched
        }]);

        result.Outcome.Should().Be(expected);
        result.Cases[1].EffectiveActorOutcome.Should().Be(ValidationOutcome.Succeeded);
    }

    [Theory]
    [InlineData("version")]
    [InlineData("policy")]
    [InlineData("observer")]
    [InlineData("age")]
    [InlineData("delay")]
    [InlineData("empty")]
    [InlineData("oversized")]
    [InlineData("duplicate-case")]
    [InlineData("operation")]
    [InlineData("attempt")]
    [InlineData("request")]
    [InlineData("run")]
    [InlineData("target")]
    [InlineData("route")]
    [InlineData("digest")]
    [InlineData("alias")]
    [InlineData("role")]
    [InlineData("decision")]
    [InlineData("control-reference")]
    [InlineData("control-boundary")]
    [InlineData("control-predicate")]
    [InlineData("control-cycle")]
    public void MalformedPlans_ShouldBeBlockedWithoutRetainingUntrustedInput(string fault)
    {
        var probe = Probe();
        var contract = fault switch
        {
            "version" => Contract() with { ContractRevision = "future" },
            "policy" => Contract() with { PolicyRevision = " " },
            "observer" => Contract() with { ObserverId = "target-self-report" },
            "age" => Contract() with { MaximumEvidenceAge = TimeSpan.Zero },
            "delay" => Contract() with { MaximumObservationDelay = TimeSpan.Zero },
            "empty" => Contract() with { Cases = [] },
            "oversized" => Contract() with { Cases = Enumerable.Repeat(Control(), 65).ToArray() },
            _ => Contract()
        };
        probe = fault switch
        {
            "duplicate-case" => probe with { CaseId = "control" },
            "operation" => probe with { Binding = probe.Binding with { OperationId = Binding.OperationId } },
            "attempt" => probe with { Binding = probe.Binding with { AttemptId = Binding.AttemptId } },
            "request" => probe with { Binding = probe.Binding with { RequestId = Binding.RequestId } },
            "run" => probe with { Binding = probe.Binding with { RunId = "other-run" } },
            "target" => probe with { Binding = probe.Binding with { TargetIdentitySha256 = new string('d', 64) } },
            "route" => probe with { Binding = probe.Binding with { RouteIdentitySha256 = new string('d', 64) } },
            "digest" => probe with { Binding = probe.Binding with { ArgumentsSha256 = "invalid" } },
            "alias" => probe with { ExpectedEffectiveActorAlias = new string('x', 513) },
            "role" => probe with { IdentityRole = (ControlledIdentityRole)999 },
            "decision" => probe with { ExpectedDecision = ControlledAuthorizationDecision.Unknown },
            "control-reference" => probe with { PositiveControlCaseId = "absent-control" },
            "control-boundary" => probe with { Binding = probe.Binding with { BoundaryId = "another-boundary" } },
            "control-predicate" => probe with { ScopePredicateId = "another-policy" },
            "control-cycle" => probe with { PositiveControlCaseId = probe.CaseId },
            _ => probe
        };
        if (fault is not ("empty" or "oversized")) contract = contract with { Cases = [Control(), probe] };

        var result = Evaluate(contract, []);

        result.Outcome.Should().Be(ValidationOutcome.Blocked);
        result.AdmittedContract.Should().BeNull();
        result.RecordedObservations.Should().BeNull();
    }

    [Theory]
    [InlineData("case")]
    [InlineData("binding")]
    [InlineData("observer")]
    [InlineData("predicate")]
    [InlineData("decision")]
    [InlineData("outcome")]
    [InlineData("alias")]
    [InlineData("duplicate")]
    [InlineData("oversized")]
    public void RejectedObservations_ShouldRetainAdmittedObligations(string fault)
    {
        var observation = fault switch
        {
            "case" => Observation(Probe()) with { CaseId = "unknown" },
            "binding" => Observation(Probe()) with { Binding = Probe().Binding with { RequestId = "replayed" } },
            "observer" => Observation(Probe()) with { ObserverId = "untrusted" },
            "predicate" => Observation(Probe()) with { ScopePredicateId = "other" },
            "decision" => Observation(Probe()) with { Decision = (ControlledAuthorizationDecision)999 },
            "outcome" => Observation(Probe()) with { ObserverOutcome = (ValidationOutcome)999 },
            "alias" => Observation(Probe()) with { ObserverId = "secret-canary" },
            _ => Observation(Probe())
        };
        var observations = fault == "oversized" ? Enumerable.Repeat(observation, 65).ToArray()
            : fault == "duplicate" ? new[] { observation, observation } : [observation];

        var result = Evaluate(Contract(), observations);

        result.Outcome.Should().Be(ValidationOutcome.Blocked);
        result.Cases.Select(entry => entry.CaseId).Should().Equal("control", "probe");
        result.Cases.Should().NotContain(entry => entry.Outcome == ValidationOutcome.Succeeded);
        result.EvidenceIntegrityOutcome.Should().Be(ValidationOutcome.Blocked);
        JsonSerializer.Serialize(result).Should().NotContain("secret");
    }

    [Theory]
    [InlineData("missing")]
    [InlineData("incomplete")]
    [InlineData("preconditions")]
    [InlineData("start")]
    [InlineData("end")]
    [InlineData("observation")]
    [InlineData("reversed")]
    [InlineData("early-observation")]
    [InlineData("future")]
    [InlineData("stale")]
    [InlineData("unknown-decision")]
    [InlineData("unknown-actor")]
    [InlineData("unknown-auth")]
    [InlineData("unknown-scope")]
    public void MissingEvidence_ShouldNotEarnCredit(string gap)
    {
        var observation = gap switch
        {
            "incomplete" => Observation(Probe()) with { Complete = false },
            "preconditions" => Observation(Probe()) with { PreconditionsSatisfied = null },
            "start" => Observation(Probe()) with { OperationStartedAt = null },
            "end" => Observation(Probe()) with { OperationEndedAt = null },
            "observation" => Observation(Probe()) with { ObservedAt = null },
            "reversed" => Observation(Probe()) with { OperationEndedAt = Clock.AddMinutes(-1) },
            "early-observation" => Observation(Probe()) with { ObservedAt = Clock.AddSeconds(-4) },
            "future" => Observation(Probe()) with { ObservedAt = Clock.AddSeconds(1) },
            "stale" => Observation(Probe()) with { OperationStartedAt = Clock.AddMinutes(-3), OperationEndedAt = Clock.AddMinutes(-2), ObservedAt = Clock.AddSeconds(-61) },
            "unknown-decision" => Observation(Probe()) with { Decision = ControlledAuthorizationDecision.Unknown },
            "unknown-actor" => Observation(Probe()) with { EffectiveActorMatched = null },
            "unknown-auth" => Observation(Probe()) with { AuthenticationBindingMatched = null },
            "unknown-scope" => Observation(Probe()) with { ScopeMatched = null },
            _ => Observation(Probe())
        };
        var result = Evaluate(Contract(), gap == "missing" ? [Observation(Control())] : [Observation(Control()), observation]);

        result.Outcome.Should().Be(ValidationOutcome.Inconclusive);
        result.Cases.Should().HaveCount(2);
        result.Cases[1].Outcome.Should().Be(ValidationOutcome.Inconclusive);
    }

    [Theory]
    [InlineData(ValidationOutcome.Unavailable)]
    [InlineData(ValidationOutcome.AuthRequired)]
    [InlineData(ValidationOutcome.Blocked)]
    [InlineData(ValidationOutcome.Cancelled)]
    [InlineData(ValidationOutcome.Error)]
    public void ObserverGaps_ShouldRetainTheirDistinctOutcome(ValidationOutcome outcome)
    {
        var result = Evaluate(Contract(), [Observation(Control()), Observation(Probe()) with { ObserverOutcome = outcome }]);
        result.Outcome.Should().Be(ValidationOutcome.Inconclusive);
        result.Cases[1].ObservationOutcome.Should().Be(outcome);
    }

    [Fact]
    public void LaterPositiveControl_ShouldNotEstablishEarlierProbe()
    {
        var result = Evaluate(Contract(), [Observation(Control()) with { ObservedAt = Clock }, Observation(Probe())]);
        result.Cases[1].PositiveControlOutcome.Should().Be(ValidationOutcome.Inconclusive);
        result.Outcome.Should().Be(ValidationOutcome.Inconclusive);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void MalformedControl_ShouldNotEraseIndependentViolationsAndShouldReplay(bool reverse)
    {
        var observations = new[]
        {
            Observation(Control()) with { Decision = (ControlledAuthorizationDecision)999, ObserverId = "single-line-secret-canary" },
            Observation(Probe()) with { Decision = ControlledAuthorizationDecision.Allowed, EffectiveActorMatched = false }
        };
        var result = Evaluate(Contract(), reverse ? observations.Reverse().ToArray() : observations);
        var restored = JsonSerializer.Deserialize<ControlledIdentityMatrixAssessment>(JsonSerializer.Serialize(result))!;

        result.Outcome.Should().Be(ValidationOutcome.Failed);
        result.EvidenceIntegrityOutcome.Should().Be(ValidationOutcome.Blocked);
        result.Cases[0].ObservationOutcome.Should().Be(ValidationOutcome.Blocked);
        result.Cases[1].DecisionOutcome.Should().Be(ValidationOutcome.Failed);
        result.Cases[1].EffectiveActorOutcome.Should().Be(ValidationOutcome.Failed);
        result.RecordedGaps.Should().Equal(new ControlledIdentityEvidenceGap("control", 1));
        JsonSerializer.Serialize(result).Should().NotContain("single-line-secret-canary");
        var replay = ControlledIdentityEvaluator.Evaluate(restored.AdmittedContract!, restored.RecordedObservations!, Binding.RunId,
            Binding.TargetIdentitySha256, Binding.RouteIdentitySha256, "observer-1", restored.AssessedAt, restored.RecordedGaps);
        JsonSerializer.Serialize(replay).Should().Be(JsonSerializer.Serialize(result));
    }

    [Theory]
    [InlineData(5, ValidationOutcome.Succeeded)]
    [InlineData(6, ValidationOutcome.Inconclusive)]
    [InlineData(86400, ValidationOutcome.Inconclusive)]
    public void RecentObservation_ShouldNotRefreshOldOperation(int delay, ValidationOutcome expected)
    {
        var control = Observation(Control());
        var ended = control.ObservedAt!.Value.AddSeconds(-delay);
        var result = Evaluate(Contract(), [control with { OperationStartedAt = ended.AddSeconds(-1), OperationEndedAt = ended }, Observation(Probe())]);

        result.Outcome.Should().Be(expected);
        result.Cases[0].Outcome.Should().Be(expected);
    }

    [Fact]
    public void DuplicateObservation_ShouldQuarantineBothCopiesRegardlessOfOrder()
    {
        var valid = Observation(Probe());
        var conflicting = valid with { EffectiveActorMatched = false };
        var first = Evaluate(Contract(), [Observation(Control()), valid, conflicting]);
        var second = Evaluate(Contract(), [conflicting, Observation(Control()), valid]);

        first.Outcome.Should().Be(ValidationOutcome.Blocked);
        first.RecordedGaps.Should().Equal(new ControlledIdentityEvidenceGap("probe", 2));
        first.RecordedObservations.Should().ContainSingle(entry => entry.CaseId == "control");
        JsonSerializer.Serialize(first).Should().Be(JsonSerializer.Serialize(second));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void SignedIdentity_ShouldPreserveActualPolicyOutcome(bool violation)
    {
        using var key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var document = SignedDocument(key, violation);
        var json = SerializeDocument(document);
        var result = new ControlledIdentityEvidenceImporter().Import(json, Contract(), Admission(), key.ExportSubjectPublicKeyInfoPem(), Clock);

        result.Outcome.Should().Be(violation ? ValidationOutcome.Failed : ValidationOutcome.Succeeded);
        result.SourceEvidenceSha256.Should().Be(CanonicalJsonDigest.ComputeText(json));
        result.VerifiedObserverKeyId.Should().Be(document.SigningKeyId);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task SignedPack_ShouldRetainCaseCoverageWithoutCoreCredit(bool wrongRun)
    {
        using var key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var pack = new ControlledIdentityEvidenceImporter().ImportPack(SerializeDocument(SignedDocument(key, true)), Contract(), Admission(), key.ExportSubjectPublicKeyInfoPem(), Clock);
        var result = new ValidationResult { ValidationId = wrongRun ? "other-run" : Binding.RunId };
        result.Run.TargetIdentitySha256 = Binding.TargetIdentitySha256;
        var before = AssuranceAssessmentEngine.Evaluate(result, AssuranceProfileCatalog.Inventory);
        var executed = await pack.GetScenarios().Single().ExecuteAsync(new ValidationScenarioContext
        {
            ValidationResult = result, ServerConfig = new(), ValidationConfiguration = new(),
            ApplicabilityContext = new ValidationApplicabilityContext { NegotiatedProtocolVersion = "2025-11-25", SchemaVersion = "2025-11-25", Transport = "http", AccessMode = "Public" }
        }, CancellationToken.None);
        result.Assessments.Scenarios.Add(executed.Scenario);
        result.Evidence.Coverage.AddRange(executed.Coverage);
        var after = AssuranceAssessmentEngine.Evaluate(result, AssuranceProfileCatalog.Inventory);

        executed.Scenario.ControlledIdentity!.Outcome.Should().Be(wrongRun ? ValidationOutcome.Blocked : ValidationOutcome.Failed);
        executed.Coverage.Should().OnlyContain(entry => entry.Lane == EvaluationLane.ControlledApplication);
        after.Assessments.CoreConformance.Should().BeEquivalentTo(before.Assessments.CoreConformance);
        after.CertificateEligibility.Should().BeEquivalentTo(before.CertificateEligibility);
        if (!wrongRun) executed.Coverage.Should().HaveCount(2);
        result.Assurance = after;
        var options = new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };
        options.Converters.Add(new JsonStringEnumConverter(JsonNamingPolicy.CamelCase));
        var artifact = JsonSerializer.SerializeToNode(result.CloneWithoutSecrets(), options)!;
        var schemaPath = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "docs", "Schemas", "mcpval-validation-result.schema.json"));
        var validation = new Mcp.Benchmark.Infrastructure.Validators.JsonSchemaValidator().Validate(artifact,
            System.Text.Json.Nodes.JsonNode.Parse(File.ReadAllText(schemaPath))!);
        validation.IsValid.Should().BeTrue(string.Join(Environment.NewLine, validation.Errors));
    }

    [Theory]
    [InlineData("key")]
    [InlineData("tampered")]
    [InlineData("contract")]
    [InlineData("expired")]
    [InlineData("identity")]
    [InlineData("unknown-field")]
    [InlineData("duplicate-field")]
    [InlineData("oversized")]
    [InlineData("future-observation")]
    public void SignedIdentity_ShouldRejectUntrustedEnvelopeWithoutEcho(string fault)
    {
        using var key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        using var otherKey = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var document = SignedDocument(key, false);
        if (fault == "future-observation")
        {
            document = document with { Observations = [Observation(Control()), Observation(Probe()) with { ObservedAt = Clock.AddSeconds(1) }] };
            document = document with { SignatureBase64 = Convert.ToBase64String(key.SignHash(Convert.FromHexString(document.ComputePayloadDigest()), DSASignatureFormat.IeeeP1363FixedFieldConcatenation)) };
        }
        var node = System.Text.Json.Nodes.JsonNode.Parse(SerializeDocument(document))!;
        if (fault == "tampered") node["observations"]![1]!["effectiveActorMatched"] = false;
        if (fault == "expired") node["expiresAt"] = Clock.AddSeconds(-1);
        if (fault == "identity") node.AsObject().Remove("documentType");
        if (fault == "unknown-field") node["raw-identity"] = "secret-principal-canary";
        var contract = fault == "contract" ? Contract() with { PolicyRevision = "substituted" } : Contract();
        var json = node.ToJsonString();
        if (fault == "duplicate-field") json = "{\"documentType\":\"secret-principal-canary\"," + json[1..];
        if (fault == "oversized") json += new string(' ', ExecutionPolicyDefaults.DefaultMaxResponseBytes);
        var action = () => new ControlledIdentityEvidenceImporter().Import(json, contract, Admission(),
            (fault == "key" ? otherKey : key).ExportSubjectPublicKeyInfoPem(), Clock);

        action.Should().Throw<InvalidDataException>().Which.ToString().Should().NotContain("secret-principal-canary");
    }

    [Fact]
    public void CancelledImport_ShouldNotReadUntrustedEnvelope()
    {
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        var action = () => new ControlledIdentityEvidenceImporter().Import("malformed", Contract(), Admission(), "not-a-key", Clock, cancellation.Token);
        action.Should().Throw<OperationCanceledException>();
    }

    [Theory]
    [InlineData(64, ValidationOutcome.Succeeded)]
    [InlineData(65, ValidationOutcome.Blocked)]
    public void MatrixCaseLimit_ShouldAdmitExactBoundary(int count, ValidationOutcome expected)
    {
        var cases = new List<ControlledIdentityCase> { Control() };
        for (var index = 1; index < count; index++) cases.Add(Probe() with
        {
            CaseId = $"probe-{index}", Binding = Probe().Binding with { OperationId = $"operation-{index}", AttemptId = $"attempt-{index}", RequestId = $"request-{index}" }
        });
        var result = Evaluate(Contract() with { Cases = cases }, cases.Select(Observation).ToArray());
        result.Outcome.Should().Be(expected);
        if (expected == ValidationOutcome.Succeeded) result.Cases.Should().HaveCount(count);
    }

    [Theory]
    [InlineData(512, ValidationOutcome.Succeeded)]
    [InlineData(513, ValidationOutcome.Blocked)]
    public void IdentifierLimit_ShouldAdmitExactBoundary(int length, ValidationOutcome expected)
    {
        var probe = Probe() with { CaseId = new string('x', length), ExpectedEffectiveActorAlias = new string('y', length) };
        Evaluate(Contract() with { Cases = [Control(), probe] }, [Observation(Control()), Observation(probe)]).Outcome.Should().Be(expected);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void IndependentAuthOrScopeMismatch_ShouldFailDespiteUnknownDecision(bool authentication)
    {
        var observed = Observation(Probe()) with
        {
            Decision = ControlledAuthorizationDecision.Unknown, AuthenticationBindingMatched = !authentication, ScopeMatched = authentication
        };
        var result = Evaluate(Contract(), [observed]);
        result.Outcome.Should().Be(ValidationOutcome.Failed);
        result.Cases[1].DecisionOutcome.Should().Be(ValidationOutcome.Inconclusive);
        result.Cases[1].PositiveControlOutcome.Should().Be(ValidationOutcome.Inconclusive);
    }

    private static ControlledIdentityAdmission Admission() => new(Binding.RunId, Binding.TargetIdentitySha256, Binding.RouteIdentitySha256, "observer-1");

    [Theory]
    [InlineData("A", "a")]
    [InlineData("probe.a", "probe_a")]
    public async Task CaseScopes_ShouldSurviveProductionAggregationAndClaimNormalization(string controlId, string probeId)
    {
        var control = Control() with { CaseId = controlId };
        var probe = Probe() with { CaseId = probeId, PositiveControlCaseId = controlId };
        var contract = Contract() with { Cases = [control, probe] };
        using var key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var document = SignedDocument(key, false) with
        {
            Contract = contract, Observations = [Observation(control), Observation(probe) with { EffectiveActorMatched = false }]
        };
        document = document with { SignatureBase64 = Convert.ToBase64String(key.SignHash(Convert.FromHexString(document.ComputePayloadDigest()), DSASignatureFormat.IeeeP1363FixedFieldConcatenation)) };
        var pack = new ControlledIdentityEvidenceImporter().ImportPack(SerializeDocument(document), contract, Admission(), key.ExportSubjectPublicKeyInfoPem(), Clock);
        var result = new ValidationResult { ValidationId = Binding.RunId };
        result.Run.TargetIdentitySha256 = Binding.TargetIdentitySha256;
        var executed = await pack.GetScenarios().Single().ExecuteAsync(new ValidationScenarioContext
        {
            ValidationResult = result, ServerConfig = new(), ValidationConfiguration = new(),
            ApplicabilityContext = new ValidationApplicabilityContext { NegotiatedProtocolVersion = "2025-11-25", SchemaVersion = "2025-11-25", Transport = "http", AccessMode = "Public" }
        }, CancellationToken.None);
        typeof(McpValidatorService).GetMethod("AddCoverageDeclarations", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static)!
            .Invoke(null, [result.Evidence.Coverage, executed.Coverage]);
        var claims = AssuranceAssessmentEngine.Evaluate(result, AssuranceProfileCatalog.Inventory).Claims
            .Where(claim => claim.Dimension == AssuranceDimension.ControlledApplication).ToArray();

        result.Evidence.Coverage.Should().HaveCount(2);
        claims.Should().HaveCount(2);
        claims.Select(claim => claim.Outcome).Should().BeEquivalentTo([AssuranceClaimOutcome.Satisfied, AssuranceClaimOutcome.Violated]);
    }

    private static ControlledIdentityEvidenceDocument SignedDocument(ECDsa key, bool violation)
    {
        var document = new ControlledIdentityEvidenceDocument
        {
            Contract = Contract(), Observations = [Observation(Control()), Observation(Probe()) with { EffectiveActorMatched = !violation }],
            IssuedAt = Clock, ExpiresAt = Clock.AddMinutes(1), SigningKeyId = CanonicalJsonDigest.ComputeBytes(key.ExportSubjectPublicKeyInfo()), SignatureBase64 = string.Empty
        };
        return document with { SignatureBase64 = Convert.ToBase64String(key.SignHash(Convert.FromHexString(document.ComputePayloadDigest()),
            DSASignatureFormat.IeeeP1363FixedFieldConcatenation)) };
    }

    private static string SerializeDocument(ControlledIdentityEvidenceDocument document)
    {
        var options = new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };
        options.Converters.Add(new JsonStringEnumConverter(JsonNamingPolicy.CamelCase));
        return JsonSerializer.Serialize(document, options);
    }

    private static ControlledIdentityCase Control() => new()
    {
        CaseId = "control", Binding = Binding, ScopePredicateId = "object-policy", ExpectedEffectiveActorAlias = "actor-a",
        ExpectedDecision = ControlledAuthorizationDecision.Allowed, IdentityRole = ControlledIdentityRole.CallerAssertion, IsPositiveControl = true
    };

    private static ControlledIdentityCase Probe() => Control() with
    {
        CaseId = "probe", Binding = Binding with { OperationId = "operation-probe", AttemptId = "attempt-probe", RequestId = "request-probe", ObjectAlias = "object-b" },
        ExpectedDecision = ControlledAuthorizationDecision.Denied, IsPositiveControl = false, PositiveControlCaseId = "control"
    };

    private static ControlledIdentityMatrixContract Contract() => new()
    {
        PolicyRevision = "fixture-policy-v1", ToolName = "owned-action", ObserverId = "observer-1", Cases = [Control(), Probe()],
        MaximumEvidenceAge = TimeSpan.FromMinutes(1), MaximumObservationDelay = TimeSpan.FromSeconds(5)
    };

    private static ControlledIdentityObservation Observation(ControlledIdentityCase entry) => new()
    {
        CaseId = entry.CaseId, Binding = entry.Binding, ObserverId = "observer-1", ScopePredicateId = entry.ScopePredicateId,
        OperationStartedAt = Clock.AddSeconds(entry.IsPositiveControl ? -20 : -5), OperationEndedAt = Clock.AddSeconds(entry.IsPositiveControl ? -15 : -3),
        ObservedAt = Clock.AddSeconds(entry.IsPositiveControl ? -10 : -1), Complete = true, PreconditionsSatisfied = true,
        ObserverOutcome = ValidationOutcome.Succeeded, Decision = entry.ExpectedDecision, EffectiveActorMatched = true,
        AuthenticationBindingMatched = true, ScopeMatched = true
    };

    private static ControlledIdentityMatrixAssessment Evaluate(ControlledIdentityMatrixContract contract, IReadOnlyList<ControlledIdentityObservation> observations) =>
        ControlledIdentityEvaluator.Evaluate(contract, observations, Binding.RunId, Binding.TargetIdentitySha256, Binding.RouteIdentitySha256, "observer-1", Clock);
}