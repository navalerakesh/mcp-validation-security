using Mcp.Benchmark.Core.Abstractions;
using Mcp.Benchmark.Core.Models;
using Mcp.Benchmark.Core.Services;
using Mcp.Benchmark.Core.Utilities;

namespace Mcp.Benchmark.Infrastructure.Scenarios;

public sealed class ControlledRelayScenarioPack : IValidationScenarioPack
{
    private readonly IReadOnlyList<IValidationScenario> _scenarios;

    internal ControlledRelayScenarioPack(ControlledRelayAssessment assessment, ControlledStateEvidence? effect, string runId, string targetDigest,
        DateTimeOffset issuedAt, DateTimeOffset expiresAt)
    {
        _scenarios = [new RelayScenario(assessment, effect, runId, targetDigest, issuedAt, expiresAt)];
    }

    public ValidationPackDescriptor Descriptor => new()
    {
        Key = new("scenario-pack/controlled-relay"), Kind = ValidationPackKind.ScenarioPack,
        Revision = new(ControlledRelayContract.Revision), DisplayName = "Controlled Two-Boundary Relay Evidence",
        Stability = ValidationStability.Experimental, Lane = EvaluationLane.ControlledApplication
    };
    public ValidationApplicability Applicability => new();
    public IReadOnlyList<IValidationScenario> GetScenarios() => _scenarios;

    private sealed class RelayScenario(ControlledRelayAssessment verified, ControlledStateEvidence? effect, string runId, string targetDigest,
        DateTimeOffset issuedAt, DateTimeOffset expiresAt) : IValidationScenario
    {
        private readonly string _identity = "controlled-relay-" + CanonicalJsonDigest.ComputeObject(new
        {
            verified.AdmittedContract!.ScenarioId, verified.AdmittedContract.PolicyRevision, verified.AdmittedContract.ContractRevision
        });
        public ValidationScenarioDescriptor Descriptor => new()
        {
            Key = new(_identity), LayerKey = "controlled-application-relay", Lane = EvaluationLane.ControlledApplication,
            Revision = new(ControlledRelayContract.Revision), DisplayName = "Two-Boundary Relay", Stability = ValidationStability.Experimental
        };

        public Task<ValidationScenarioExecutionResult> ExecuteAsync(ValidationScenarioContext context, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var assessedAt = ValidationAssessmentClock.GetTimestamp(context.ValidationResult);
            var assessment = assessedAt < issuedAt || assessedAt >= expiresAt
                ? new ControlledRelayAssessment
                {
                    Outcome = ValidationOutcome.Blocked, Checks = [new("unavailable", "signed-validity", ValidationOutcome.Blocked)],
                    AssessedAt = assessedAt, Reason = "Signed relay evidence is outside its admitted validity interval."
                }
                : ControlledRelayEvaluator.Evaluate(verified.AdmittedContract!, verified.Observations!,
                context.ValidationResult.ValidationId, context.ValidationResult.Run.TargetIdentitySha256 ?? "",
                verified.AdmittedContract!.ObserverId, assessedAt, effect);
            assessment = assessment with
            {
                SourceEvidenceSha256 = verified.SourceEvidenceSha256, VerifiedObserverKeyId = verified.VerifiedObserverKeyId
            };
            if (context.ValidationResult.ValidationId != runId || context.ValidationResult.Run.TargetIdentitySha256 != targetDigest)
                assessment = assessment with { Outcome = ValidationOutcome.Blocked };
            var coverage = assessment.Checks.Select(check => Coverage(
                $"{_identity}:{CanonicalJsonDigest.ComputeObject(new { check.BoundaryId, check.CheckId })}", check.Outcome,
                $"Relay boundary '{check.BoundaryId}': {check.CheckId} is {check.Outcome}.")).ToList();
            coverage.Add(Coverage(_identity, assessment.Outcome));
            return Task.FromResult(new ValidationScenarioExecutionResult
            {
                Scenario = new()
                {
                    ScenarioId = _identity, DisplayName = Descriptor.DisplayName, ControlledRelay = assessment, Summary = assessment.Reason,
                    Status = assessment.Outcome == ValidationOutcome.Succeeded ? TestStatus.Passed :
                        assessment.Outcome == ValidationOutcome.Failed ? TestStatus.Failed : TestStatus.Inconclusive
                },
                Coverage = coverage
            });
        }

        private ValidationCoverageDeclaration Coverage(string scope, ValidationOutcome outcome, string? reason = null)
        {
            var value = ValidationCoverageFactory.FromOutcome(Descriptor.LayerKey, scope, outcome);
            return new()
            {
                LayerId = value.LayerId, Scope = value.Scope, Status = value.Status, ObservedOutcome = value.ObservedOutcome,
                Confidence = value.Confidence, Blocker = value.Blocker, Lane = EvaluationLane.ControlledApplication,
                Origin = EvidenceOrigin.DeterministicObservation, Reason = reason ?? "Admitted two-boundary relay evidence only."
            };
        }
    }
}
