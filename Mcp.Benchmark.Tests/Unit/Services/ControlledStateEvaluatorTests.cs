using FluentAssertions;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.Json.Nodes;
using System.Security.Cryptography;
using Mcp.Benchmark.Core.Models;
using Mcp.Benchmark.Core.Services;
using Mcp.Benchmark.Core.Constants;
using Mcp.Benchmark.Core.Abstractions;
using Mcp.Benchmark.Infrastructure.Scenarios;
using Mcp.Benchmark.Infrastructure.Registries;
using Mcp.Benchmark.Infrastructure.Services;
using Mcp.Benchmark.Infrastructure.Validators;

namespace Mcp.Benchmark.Tests.Unit.Services;

public sealed class ControlledStateEvaluatorTests
{
    private static readonly DateTimeOffset Start = new(2026, 11, 11, 0, 0, 0, TimeSpan.Zero);
    private static readonly ControlledOperationBinding Binding = new(
        "run-1", "operation-1", "attempt-1", "request-1", "boundary-1", new string('a', 64), new string('b', 64), new string('c', 64), "actor-1", "object-1");

    [Fact]
    public void CancelledImport_ShouldNotParseOrVerifyUntrustedInput()
    {
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        var action = () => new ControlledStateEvidenceImporter().Import("malformed", Contract(), "not-a-key",
            Start.AddMinutes(1), cancellation.Token);

        action.Should().Throw<OperationCanceledException>();
    }

    [Theory]
    [InlineData("wrong-key")]
    [InlineData("tampered-state")]
    [InlineData("policy-substitution")]
    [InlineData("expired")]
    [InlineData("future")]
    [InlineData("duplicate")]
    [InlineData("unknown-field")]
    [InlineData("oversized")]
    [InlineData("wrong-curve")]
    [InlineData("audit-after-issuance")]
    [InlineData("missing-type")]
    [InlineData("missing-version")]
    [InlineData("missing-algorithm")]
    public void SignedImport_ShouldRejectUntrustedOrMalformedEvidenceWithoutEcho(string fault)
    {
        using var key = ECDsa.Create(fault == "wrong-curve" ? ECCurve.NamedCurves.nistP384 : ECCurve.NamedCurves.nistP256);
        using var otherKey = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var contract = Contract() with { AuditRequired = fault == "audit-after-issuance" };
        var document = new ControlledStateEvidenceDocument
        {
            Contract = fault == "policy-substitution" ? contract with { StateExpectation = ControlledStateExpectation.NoOpPermitted } : contract,
            Evidence = fault == "audit-after-issuance" ? Evidence() with { ObservedThrough = Start.AddSeconds(2), AuditMatched = true } : Evidence(),
            IssuedAt = fault == "future" ? Start.AddMinutes(2) : fault == "audit-after-issuance" ? Start.AddSeconds(2) : Start.AddSeconds(6),
            ExpiresAt = fault == "expired" ? Start.AddSeconds(30) : Start.AddMinutes(3),
            SigningKeyId = Convert.ToHexString(SHA256.HashData(key.ExportSubjectPublicKeyInfo())).ToLowerInvariant(), SignatureBase64 = string.Empty
        };
        document = document with { SignatureBase64 = Convert.ToBase64String(key.SignHash(
            Convert.FromHexString(document.ComputePayloadDigest()), DSASignatureFormat.IeeeP1363FixedFieldConcatenation)) };
        var options = new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };
        options.Converters.Add(new JsonStringEnumConverter(JsonNamingPolicy.CamelCase));
        var node = JsonSerializer.SerializeToNode(document, options)!;
        if (fault == "tampered-state") node["evidence"]!["stateChanged"] = false;
        if (fault == "unknown-field") node["untrusted-field"] = "signed-evidence-canary";
        if (fault == "missing-type") node.AsObject().Remove("documentType");
        if (fault == "missing-version") node.AsObject().Remove("documentSchemaVersion");
        if (fault == "missing-algorithm") node.AsObject().Remove("signatureAlgorithm");
        var json = node.ToJsonString();
        if (fault == "duplicate") json = "{\"documentType\":\"signed-evidence-canary\"," + json[1..];
        if (fault == "oversized") json += new string(' ', ExecutionPolicyDefaults.DefaultMaxResponseBytes);

        var action = () => new ControlledStateEvidenceImporter().Import(json, contract,
            (fault == "wrong-key" ? otherKey : key).ExportSubjectPublicKeyInfoPem(), Start.AddMinutes(1));

        action.Should().Throw<InvalidDataException>().Which.ToString().Should().NotContain("signed-evidence-canary");
    }

    [Fact]
    public void AuditMatchWithoutIndependentCompleteAudit_ShouldRemainUnknown()
    {
        var result = Evaluate(Contract() with { AuditRequired = true }, Evidence() with
        {
            AuditMatched = true, AuditObserverOutcome = ValidationOutcome.AuthRequired, AuditObservationComplete = false
        });

        result.AuditOutcome.Should().Be(ValidationOutcome.Inconclusive);
        result.StateOutcome.Should().Be(ValidationOutcome.Succeeded);
        result.Outcome.Should().Be(ValidationOutcome.Inconclusive);
    }

    [Fact]
    public void OptionalAuditIdentifier_ShouldStillRespectAdmissionBounds()
    {
        var result = Evaluate(Contract() with { AuditRequired = false }, Evidence() with { AuditPredicateId = new string('x', 513) });

        result.Outcome.Should().Be(ValidationOutcome.Blocked);
        result.RecordedEvidence.Should().BeNull();
    }

    [Theory]
    [InlineData(false, ValidationOutcome.Failed)]
    [InlineData(true, ValidationOutcome.Succeeded)]
    public async Task TrustedSignedImport_ShouldEvaluateStateInsteadOfCreditingTheSignature(bool written, ValidationOutcome expected)
    {
        using var key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var contract = Contract();
        var document = new ControlledStateEvidenceDocument
        {
            Contract = contract, Evidence = Evidence() with { StateChanged = written, AfterPredicateSatisfied = written },
            IssuedAt = Start.AddSeconds(6), ExpiresAt = Start.AddMinutes(2),
            SigningKeyId = Convert.ToHexString(SHA256.HashData(key.ExportSubjectPublicKeyInfo())).ToLowerInvariant(), SignatureBase64 = string.Empty
        };
        document = document with { SignatureBase64 = Convert.ToBase64String(key.SignHash(
            Convert.FromHexString(document.ComputePayloadDigest()), DSASignatureFormat.IeeeP1363FixedFieldConcatenation)) };
        var options = new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };
        options.Converters.Add(new JsonStringEnumConverter(JsonNamingPolicy.CamelCase));

        var result = new ControlledStateEvidenceImporter().Import(JsonSerializer.Serialize(document, options),
            contract, key.ExportSubjectPublicKeyInfoPem(), Start.AddMinutes(1));

        result.Outcome.Should().Be(expected);
        result.RetryPermitted.Should().BeFalse();
        result.SourceEvidenceSha256.Should().MatchRegex("^[a-f0-9]{64}$");
        result.VerifiedObserverKeyId.Should().Be(document.SigningKeyId);
        var pack = new ControlledStateEvidenceImporter().ImportPack(JsonSerializer.Serialize(document, options),
            contract, key.ExportSubjectPublicKeyInfoPem(), Start.AddMinutes(1));
        var validation = new ValidationResult { ValidationId = Binding.RunId };
        validation.Run.TargetIdentitySha256 = Binding.TargetIdentitySha256;
        var executed = await pack.GetScenarios().Single().ExecuteAsync(Context(validation), CancellationToken.None);
        executed.Scenario.ControlledState!.Outcome.Should().Be(expected);
        executed.Scenario.ControlledState.VerifiedObserverKeyId.Should().Be(document.SigningKeyId);
        executed.Scenario.ControlledState.SourceEvidenceSha256.Should().Be(result.SourceEvidenceSha256);
    }

    [Fact]
    public void MissingTimestamps_ShouldNotEarnStateCredit()
    {
        var result = Evaluate(Contract(), Evidence() with
        {
            BaselineAt = default, OperationStartedAt = default, OperationEndedAt = default, ObservedThrough = default
        });

        result.Outcome.Should().Be(ValidationOutcome.Inconclusive);
        result.StateOutcome.Should().Be(ValidationOutcome.Inconclusive);
    }

    [Theory]
    [InlineData("baseline")]
    [InlineData("start")]
    [InlineData("end")]
    [InlineData("watermark")]
    public void EachMissingClock_ShouldIndependentlyPreventCredit(string clock)
    {
        var evidence = clock switch
        {
            "baseline" => Evidence() with { BaselineAt = null },
            "start" => Evidence() with { OperationStartedAt = null },
            "end" => Evidence() with { OperationEndedAt = null },
            _ => Evidence() with { ObservedThrough = null }
        };

        Evaluate(Contract(), evidence).Outcome.Should().Be(ValidationOutcome.Inconclusive);
    }

    [Theory]
    [InlineData(ValidationOutcome.AuthRequired, ValidationOutcome.AuthRequired)]
    [InlineData(ValidationOutcome.Blocked, ValidationOutcome.Blocked)]
    [InlineData(ValidationOutcome.Cancelled, ValidationOutcome.Cancelled)]
    [InlineData(ValidationOutcome.Error, ValidationOutcome.Inconclusive)]
    [InlineData(ValidationOutcome.Failed, ValidationOutcome.Inconclusive)]
    public void ObserverFailure_ShouldRetainAttributionWithoutProvingTargetFailure(ValidationOutcome observer, ValidationOutcome expected)
    {
        var result = Evaluate(Contract(), Evidence() with { ObserverOutcome = observer });

        result.Outcome.Should().Be(expected);
        result.StateOutcome.Should().Be(ValidationOutcome.Inconclusive);
    }

    [Theory]
    [InlineData(ControlledApplicationOutcome.Unknown)]
    [InlineData(ControlledApplicationOutcome.Pending)]
    public void IncompleteApplicationOutcome_ShouldNotEstablishCompletedControl(ControlledApplicationOutcome application)
    {
        var result = Evaluate(Contract(), Evidence() with { ApplicationOutcome = application });

        result.StateOutcome.Should().Be(ValidationOutcome.Succeeded);
        result.ApplicationOutcome.Should().Be(ValidationOutcome.Inconclusive);
        result.Outcome.Should().Be(ValidationOutcome.Inconclusive);
    }

    [Theory]
    [InlineData("window")]
    [InlineData("baseline-age")]
    [InlineData("evidence-age")]
    [InlineData("budget")]
    [InlineData("over-budget")]
    [InlineData("negative-reads")]
    [InlineData("state-expectation")]
    [InlineData("application-expectation")]
    public void EachInvalidAdmission_ShouldIndependentlyBlockControl(string fault)
    {
        var contract = fault switch
        {
            "window" => Contract() with { ConsistencyWindow = TimeSpan.Zero },
            "baseline-age" => Contract() with { MaximumBaselineAge = TimeSpan.Zero },
            "evidence-age" => Contract() with { MaximumEvidenceAge = TimeSpan.Zero },
            "budget" => Contract() with { MaximumObserverReads = 0 },
            "state-expectation" => Contract() with { StateExpectation = (ControlledStateExpectation)999 },
            "application-expectation" => Contract() with { ExpectedApplicationOutcome = ControlledApplicationOutcome.Unknown },
            _ => Contract()
        };
        var evidence = fault == "over-budget" ? Evidence() with { ObserverReads = 4 }
            : fault == "negative-reads" ? Evidence() with { ObserverReads = -1 } : Evidence();

        Evaluate(contract, evidence).Outcome.Should().Be(ValidationOutcome.Blocked);
    }

    [Theory]
    [InlineData("missing-clock")]
    [InlineData("before-operation")]
    [InlineData("late")]
    [InlineData("incomplete")]
    public void AuditTimingAndCompleteness_ShouldBeIndependentOfHealthyState(string fault)
    {
        var evidence = Evidence() with { AuditMatched = true };
        evidence = fault switch
        {
            "missing-clock" => evidence with { AuditObservedThrough = null },
            "before-operation" => evidence with { AuditObservedThrough = Start },
            "late" => evidence with { AuditObservedThrough = Start.AddSeconds(7) },
            _ => evidence with { AuditObservationComplete = false }
        };

        var result = Evaluate(Contract() with { AuditRequired = true }, evidence);

        result.StateOutcome.Should().Be(ValidationOutcome.Succeeded);
        result.AuditOutcome.Should().Be(ValidationOutcome.Inconclusive);
        result.Outcome.Should().Be(ValidationOutcome.Inconclusive);
    }

    [Fact]
    public void DifferentOperations_ShouldHaveDifferentScenarioIdentities()
    {
        var first = new ControlledStateScenarioPack(Contract(), Evidence(), Binding, "observer-1", Start.AddMinutes(1));
        var secondBinding = Binding with { OperationId = "operation-2", AttemptId = "attempt-2" };
        var second = new ControlledStateScenarioPack(Contract() with { Binding = secondBinding },
            Evidence() with { Binding = secondBinding, StateChanged = false, AfterPredicateSatisfied = false },
            secondBinding, "observer-1", Start.AddMinutes(1));

        first.GetScenarios().Single().Descriptor.Key.Value.Should().NotBe(second.GetScenarios().Single().Descriptor.Key.Value);
    }

    [Fact]
    public void DifferentAssessmentClocks_ShouldHaveDifferentScenarioIdentities()
    {
        var fresh = new ControlledStateScenarioPack(Contract(), Evidence(), Binding, "observer-1", Start.AddMinutes(1));
        var stale = new ControlledStateScenarioPack(Contract(), Evidence(), Binding, "observer-1", Start.AddMinutes(2));

        fresh.GetScenarios().Single().Descriptor.Key.Value.Should().NotBe(stale.GetScenarios().Single().Descriptor.Key.Value);
    }

    [Fact]
    public void InvalidCleanupOutcome_ShouldNotEscapeIntoRejectedAssessment()
    {
        var result = Evaluate(Contract(), Evidence() with { CleanupOutcome = (ValidationOutcome)999 });

        result.Outcome.Should().Be(ValidationOutcome.Blocked);
        result.CleanupOutcome.Should().Be(ValidationOutcome.NotEvaluated);
        result.RecordedEvidence.Should().BeNull();
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(false, true)]
    [InlineData(true, true)]
    public async Task MultipleOperationEvidence_ShouldRetainBothOutcomesRegardlessOfOrder(bool reverse, bool staleInterpretation)
    {
        var result = new ValidationResult { ValidationId = Binding.RunId };
        result.Run.TargetIdentitySha256 = Binding.TargetIdentitySha256;
        var otherBinding = Binding with { OperationId = "operation-2", AttemptId = "attempt-2", RequestId = "request-2" };
        var packs = new[]
        {
            new ControlledStateScenarioPack(Contract(), Evidence(), Binding, "observer-1", Start.AddMinutes(1)),
            staleInterpretation
                ? new ControlledStateScenarioPack(Contract(), Evidence(), Binding, "observer-1", Start.AddMinutes(2))
                : new ControlledStateScenarioPack(Contract() with { Binding = otherBinding }, Evidence() with
            {
                Binding = otherBinding, StateChanged = false, AfterPredicateSatisfied = false
            }, otherBinding, "observer-1", Start.AddMinutes(1))
        };
        var registry = new ValidationPackRegistry<IValidationScenarioPack>(reverse ? packs.Reverse() : packs);
        var resolved = registry.Resolve(Context(result).ApplicabilityContext);
        resolved.Should().HaveCount(2);
        foreach (var pack in resolved)
        {
            var execution = await pack.GetScenarios().Single().ExecuteAsync(Context(result), CancellationToken.None);
            result.Evidence.Coverage.AddRange(execution.Coverage);
            result.Assessments.Scenarios.Add(execution.Scenario);
        }

        var claims = AssuranceAssessmentEngine.Evaluate(result, AssuranceProfileCatalog.Inventory).Claims
            .Where(claim => claim.Dimension == AssuranceDimension.ControlledApplication).ToArray();

        claims.Should().HaveCount(2);
        claims.Select(claim => claim.Outcome).Should().BeEquivalentTo([
            AssuranceClaimOutcome.Satisfied,
            staleInterpretation ? AssuranceClaimOutcome.Incomplete : AssuranceClaimOutcome.Violated]);
        result.Assessments.Scenarios.Select(scenario => scenario.ScenarioId).Should().OnlyHaveUniqueItems();
    }

    [Fact]
    public async Task Pack_ShouldBlockDifferentRunAndHonorPreCancellationWithoutMutation()
    {
        var result = new ValidationResult { ValidationId = "different-run" };
        result.Run.TargetIdentitySha256 = Binding.TargetIdentitySha256;
        var scenario = new ControlledStateScenarioPack(Contract(), Evidence(), Binding, "observer-1", Start.AddMinutes(1)).GetScenarios().Single();

        var blocked = await scenario.ExecuteAsync(Context(result), CancellationToken.None);
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        await FluentActions.Awaiting(() => scenario.ExecuteAsync(Context(result), cancellation.Token)).Should().ThrowAsync<OperationCanceledException>();

        blocked.Coverage.Single().Outcome.Should().Be(ValidationOutcome.Blocked);
        result.Assessments.Scenarios.Should().BeEmpty();
        result.Evidence.Coverage.Should().BeEmpty();
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void PersistedAssessment_ShouldRetainInputsForDeterministicReplay(bool complete)
    {
        var assessment = Evaluate(Contract() with { AuditRequired = true }, Evidence() with
        {
            AuditMatched = true, ObservationComplete = complete
        });
        var restored = JsonSerializer.Deserialize<ControlledStateAssessment>(JsonSerializer.Serialize(assessment))!;

        restored.RecordedEvidence.Should().NotBeNull();
        var replayed = ControlledStateEvaluator.Evaluate(restored.AdmittedContract!, restored.RecordedEvidence!, Binding,
            "observer-1", restored.AssessedAt);

        replayed.Should().Be(restored);
        restored.RecordedEvidence!.ObservationComplete.Should().Be(complete);
        restored.RecordedEvidence.ObserverReads.Should().Be(2);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ScenarioPack_ShouldKeepControlledEvidenceOutsideCoreConformance(bool invalidCleanup)
    {
        var result = new ValidationResult { ValidationId = Binding.RunId };
        result.Run.TargetIdentitySha256 = Binding.TargetIdentitySha256;
        var before = AssuranceAssessmentEngine.Evaluate(result, AssuranceProfileCatalog.Inventory);
        var evidence = invalidCleanup ? Evidence() with { CleanupOutcome = (ValidationOutcome)999 } : Evidence();
        var expectedState = invalidCleanup ? ValidationOutcome.Inconclusive : ValidationOutcome.Succeeded;
        var pack = new ControlledStateScenarioPack(Contract(), evidence, Binding, "observer-1", Start.AddMinutes(1));
        var scenario = pack.GetScenarios().Single();
        var context = Context(result);

        var executed = await scenario.ExecuteAsync(context, CancellationToken.None);
        result.Evidence.Coverage.AddRange(executed.Coverage);
        result.Assessments.Scenarios.Add(executed.Scenario);
        var after = AssuranceAssessmentEngine.Evaluate(result, AssuranceProfileCatalog.Inventory);

        scenario.Descriptor.MutatesState.Should().BeFalse();
        executed.Scenario.ControlledState!.StateOutcome.Should().Be(expectedState);
        executed.Coverage.Should().OnlyContain(entry => entry.Lane == EvaluationLane.ControlledApplication);
        after.Assessments.CoreConformance.RequiredClaims.Should().Be(before.Assessments.CoreConformance.RequiredClaims);
        after.Assessments.CoreConformance.SatisfiedRequiredClaims.Should().Be(before.Assessments.CoreConformance.SatisfiedRequiredClaims);
        after.EvidenceSummary.TotalDeclarations.Should().Be(before.EvidenceSummary.TotalDeclarations);
        after.EvidenceSummary.EvidenceCoverageRatio.Should().Be(before.EvidenceSummary.EvidenceCoverageRatio);
        after.CertificateEligibility.Status.Should().Be(before.CertificateEligibility.Status);
        after.Claims.Should().Contain(claim => claim.Dimension == AssuranceDimension.ControlledApplication);
        result.CloneWithoutSecrets().Assessments.Scenarios.Single().ControlledState!.StateOutcome.Should().Be(expectedState);
        result.Assurance = after;
        var options = new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };
        options.Converters.Add(new JsonStringEnumConverter(JsonNamingPolicy.CamelCase));
        var artifact = JsonSerializer.SerializeToNode(result.CloneWithoutSecrets(), options)!;
        var schemaPath = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "docs", "Schemas", "mcpval-validation-result.schema.json"));
        var validation = new JsonSchemaValidator().Validate(artifact, JsonNode.Parse(File.ReadAllText(schemaPath))!);
        validation.IsValid.Should().BeTrue(string.Join(Environment.NewLine, validation.Errors));
        if (!invalidCleanup)
        {
            foreach (var member in new[] { "admittedContract", "recordedEvidence" })
            {
                var malformed = artifact.DeepClone();
                malformed["measurements"]!["scenarios"]![0]!["controlledState"]![member]!.AsObject().Remove("preconditionId");
                new JsonSchemaValidator().Validate(malformed, JsonNode.Parse(File.ReadAllText(schemaPath))!)
                    .IsValid.Should().BeFalse($"{member} requires preconditionId");
                FluentActions.Invoking(() => malformed.Deserialize<ValidationResult>(options)).Should().Throw<JsonException>();
            }
        }
    }

    [Theory]
    [InlineData(true, ValidationOutcome.Succeeded)]
    [InlineData(false, ValidationOutcome.Failed)]
    public void SuccessfulResponse_ShouldNotSubstituteForObservedWrite(bool changed, ValidationOutcome expected)
    {
        var evidence = Evidence() with { StateChanged = changed, AfterPredicateSatisfied = changed };

        var result = Evaluate(Contract(), evidence);

        result.ApplicationOutcome.Should().Be(ValidationOutcome.Succeeded);
        result.StateOutcome.Should().Be(expected);
        result.Outcome.Should().Be(expected);
        result.RetryPermitted.Should().BeFalse();
        result.AdmittedContract!.Binding.Should().Be(Binding);
        result.RecordedEvidence!.ApplicationOutcome.Should().Be(ControlledApplicationOutcome.Succeeded);
        result.RecordedEvidence.StateChanged.Should().Be(changed);
    }

    [Fact]
    public void DeclaredNoOp_ShouldRequireAlreadySatisfiedPredicate()
    {
        var result = Evaluate(Contract() with { StateExpectation = ControlledStateExpectation.NoOpPermitted },
            Evidence() with { BeforePredicateSatisfied = true, AfterPredicateSatisfied = true, StateChanged = false });

        result.Outcome.Should().Be(ValidationOutcome.Succeeded);
    }

    [Theory]
    [InlineData(false, ValidationOutcome.Succeeded)]
    [InlineData(true, ValidationOutcome.Failed)]
    public void FailedWriteControl_ShouldDistinguishUnchangedStateFromPartialEffects(bool changed, ValidationOutcome expected)
    {
        var result = Evaluate(Contract() with
        {
            StateExpectation = ControlledStateExpectation.UnchangedRequired,
            ExpectedApplicationOutcome = ControlledApplicationOutcome.Rejected
        }, Evidence() with { ApplicationOutcome = ControlledApplicationOutcome.Rejected, StateChanged = changed, AfterPredicateSatisfied = changed });

        result.Outcome.Should().Be(expected);
        result.RetryPermitted.Should().BeFalse();
    }

    [Fact]
    public void UnavailableObserver_ShouldNeverProveDroppedWrite()
    {
        var result = Evaluate(Contract(), Evidence() with
        {
            ObserverOutcome = ValidationOutcome.Unavailable, StateChanged = false, AfterPredicateSatisfied = false
        });

        result.Outcome.Should().Be(ValidationOutcome.Inconclusive);
        result.StateOutcome.Should().Be(ValidationOutcome.Inconclusive);
    }

    [Theory]
    [InlineData("unavailable")]
    [InlineData("missing-clock")]
    [InlineData("late")]
    [InlineData("contradiction")]
    public void StateGap_ShouldPreserveIndependentRecordedFailures(string gap)
    {
        var evidence = gap switch
        {
            "unavailable" => Evidence() with { ObserverOutcome = ValidationOutcome.Unavailable },
            "missing-clock" => Evidence() with { BaselineAt = null },
            "late" => Evidence() with { ObservedThrough = Start.AddSeconds(7) },
            _ => Evidence() with { StateChanged = false }
        };
        foreach (var failure in new[] { "application", "cleanup", "cleanup-error", "audit" })
        {
            var observed = evidence with
            {
                ApplicationOutcome = failure == "application" ? ControlledApplicationOutcome.Rejected : ControlledApplicationOutcome.Succeeded,
                CleanupOutcome = failure == "cleanup" ? ValidationOutcome.Failed
                    : failure == "cleanup-error" ? ValidationOutcome.Error : ValidationOutcome.Succeeded,
                AuditMatched = failure != "audit"
            };
            var result = Evaluate(Contract() with { AuditRequired = true }, observed);

            result.Outcome.Should().Be(ValidationOutcome.Failed, $"{failure} is known despite {gap}");
            result.StateOutcome.Should().Be(ValidationOutcome.Inconclusive);
            result.ApplicationOutcome.Should().Be(failure == "application" ? ValidationOutcome.Failed : ValidationOutcome.Succeeded);
            result.AuditOutcome.Should().Be(failure == "audit" ? ValidationOutcome.Failed : ValidationOutcome.Succeeded);
            result.CleanupOutcome.Should().Be(observed.CleanupOutcome);
        }
    }

    [Theory]
    [InlineData("attempt")]
    [InlineData("run")]
    [InlineData("target")]
    [InlineData("actor")]
    [InlineData("object")]
    [InlineData("observer")]
    [InlineData("predicate")]
    public void ReplayedOrMismatchedEvidence_ShouldBeBlocked(string mismatch)
    {
        var evidence = Evidence();
        evidence = mismatch switch
        {
            "attempt" => evidence with { Binding = Binding with { AttemptId = "other-attempt" } },
            "run" => evidence with { Binding = Binding with { RunId = "other-run" } },
            "target" => evidence with { Binding = Binding with { TargetIdentitySha256 = new string('d', 64) } },
            "actor" => evidence with { Binding = Binding with { ActorAlias = "other-actor" } },
            "object" => evidence with { Binding = Binding with { ObjectAlias = "other-object" } },
            "observer" => evidence with { ObserverId = "target-self-report" },
            _ => evidence with { PredicateId = "different-predicate" }
        };

        Evaluate(Contract(), evidence).Outcome.Should().Be(ValidationOutcome.Blocked);
    }

    [Theory]
    [InlineData("late")]
    [InlineData("stale-baseline")]
    [InlineData("future")]
    [InlineData("partial")]
    [InlineData("contradiction")]
    public void UncertainObservation_ShouldNotEstablishState(string uncertainty)
    {
        var evidence = uncertainty switch
        {
            "late" => Evidence() with { ObservedThrough = Start.AddSeconds(7) },
            "stale-baseline" => Evidence() with { BaselineAt = Start.AddSeconds(-6) },
            "future" => Evidence() with { ObservedThrough = Start.AddMinutes(2) },
            "partial" => Evidence() with { ObservationComplete = false },
            _ => Evidence() with { StateChanged = false }
        };

        Evaluate(Contract(), evidence).StateOutcome.Should().Be(ValidationOutcome.Inconclusive);
    }

    [Fact]
    public void AbsentWriteBeforeWindowEnds_ShouldRemainInconclusive()
    {
        var result = Evaluate(Contract(), Evidence() with
        {
            ObservedThrough = Start.AddSeconds(5), StateChanged = false, AfterPredicateSatisfied = false
        });

        result.StateOutcome.Should().Be(ValidationOutcome.Inconclusive);
    }

    [Fact]
    public void ForbiddenObservedChange_ShouldFailBeforeWindowEnds()
    {
        var result = Evaluate(Contract() with
        {
            StateExpectation = ControlledStateExpectation.UnchangedRequired,
            ExpectedApplicationOutcome = ControlledApplicationOutcome.Rejected
        }, Evidence() with
        {
            ApplicationOutcome = ControlledApplicationOutcome.Rejected,
            ObservedThrough = Start.AddSeconds(2)
        });

        result.StateOutcome.Should().Be(ValidationOutcome.Failed);
        result.Outcome.Should().Be(ValidationOutcome.Failed);
        result.RetryPermitted.Should().BeFalse();
    }

    [Theory]
    [InlineData(ValidationOutcome.Failed, ValidationOutcome.Failed)]
    [InlineData(ValidationOutcome.Error, ValidationOutcome.Failed)]
    [InlineData(ValidationOutcome.NotEvaluated, ValidationOutcome.Inconclusive)]
    public void UnfinishedCleanup_ShouldNotEarnCompleteControlCredit(ValidationOutcome cleanup, ValidationOutcome expected)
    {
        var result = Evaluate(Contract(), Evidence() with { CleanupOutcome = cleanup });

        result.StateOutcome.Should().Be(ValidationOutcome.Succeeded);
        result.CleanupOutcome.Should().Be(cleanup);
        result.Outcome.Should().Be(expected);
    }

    [Theory]
    [InlineData("blank-policy")]
    [InlineData("blank-operation")]
    [InlineData("invalid-digest")]
    [InlineData("oversized-alias")]
    public void MalformedAdmission_ShouldNotQualify(string malformed)
    {
        var binding = malformed switch
        {
            "blank-operation" => Binding with { OperationId = " " },
            "invalid-digest" => Binding with { ArgumentsSha256 = "invalid" },
            "oversized-alias" => Binding with { ActorAlias = new string('x', 513) },
            _ => Binding
        };
        var contract = Contract() with { Binding = binding, PolicyRevision = malformed == "blank-policy" ? " " : "fixture-policy-v1" };

        var result = ControlledStateEvaluator.Evaluate(contract, Evidence() with { Binding = binding }, binding, "observer-1", Start.AddMinutes(1));

        result.Outcome.Should().Be(ValidationOutcome.Blocked);
    }

    [Theory]
    [InlineData(null, ValidationOutcome.Inconclusive)]
    [InlineData(false, ValidationOutcome.Failed)]
    [InlineData(true, ValidationOutcome.Succeeded)]
    public void RequiredAudit_ShouldRemainSeparateFromState(bool? matched, ValidationOutcome expected)
    {
        var result = Evaluate(Contract() with { AuditRequired = true }, Evidence() with { AuditMatched = matched });

        result.StateOutcome.Should().Be(ValidationOutcome.Succeeded);
        result.AuditOutcome.Should().Be(expected);
        result.Outcome.Should().Be(expected);
    }

    private static ControlledStateAssessment Evaluate(ControlledStateScenarioContract contract, ControlledStateEvidence evidence) =>
        ControlledStateEvaluator.Evaluate(contract, evidence, Binding, "observer-1", Start.AddMinutes(1));

    private static ValidationScenarioContext Context(ValidationResult result) => new()
    {
        ValidationResult = result, ValidationConfiguration = new(), ServerConfig = new(),
        ApplicabilityContext = new ValidationApplicabilityContext
        {
            NegotiatedProtocolVersion = "2025-11-25", SchemaVersion = "2025-11-25", Transport = "stdio", AccessMode = "Public"
        }
    };

    private static ControlledStateScenarioContract Contract() => new()
    {
        ScenarioId = "state-write", PolicyRevision = "fixture-policy-v1", ToolName = "fixture_write",
        ObserverId = "observer-1", PredicateId = "expected-state", PreconditionId = "fixture-ready", AuditPredicateId = "audit-operation-match", CleanupOwner = "fixture-owner", Binding = Binding,
        StateExpectation = ControlledStateExpectation.ChangeRequired, ExpectedApplicationOutcome = ControlledApplicationOutcome.Succeeded,
        ConsistencyWindow = TimeSpan.FromSeconds(5), MaximumBaselineAge = TimeSpan.FromSeconds(5), MaximumEvidenceAge = TimeSpan.FromMinutes(1), MaximumObserverReads = 3
    };

    private static ControlledStateEvidence Evidence() => new()
    {
        Binding = Binding, ObserverId = "observer-1", PredicateId = "expected-state", PreconditionId = "fixture-ready", AuditPredicateId = "audit-operation-match", BaselineAt = Start.AddSeconds(-1),
        OperationStartedAt = Start, OperationEndedAt = Start.AddSeconds(1), ObservedThrough = Start.AddSeconds(6),
        ObserverOutcome = ValidationOutcome.Succeeded, ObservationComplete = true, PreconditionsSatisfied = true,
        BeforePredicateSatisfied = false, AfterPredicateSatisfied = true, StateChanged = true,
        ApplicationOutcome = ControlledApplicationOutcome.Succeeded, ObserverReads = 2, CleanupOutcome = ValidationOutcome.NotApplicable,
        AuditObserverOutcome = ValidationOutcome.Succeeded, AuditObservationComplete = true, AuditObservedThrough = Start.AddSeconds(6)
    };
}