using Mcp.Benchmark.Core.Abstractions;
using Mcp.Benchmark.Core.Models;
using Mcp.Benchmark.Core.Services;
using Mcp.Benchmark.Core.Utilities;

namespace Mcp.Benchmark.Infrastructure.Scenarios;

public sealed class ControlledIdentityScenarioPack : IValidationScenarioPack
{
    private const string PackRevision = "1.2.0";
    private readonly IReadOnlyList<IValidationScenario> _scenarios;

    internal ControlledIdentityScenarioPack(ControlledIdentityMatrixAssessment assessment, ControlledIdentityAdmission admission)
    {
        if (assessment.AdmittedContract == null || assessment.RecordedObservations == null || assessment.SourceEvidenceSha256 == null || assessment.VerifiedObserverKeyId == null)
            throw new ArgumentException("Verified admitted identity evidence is required.", nameof(assessment));
        _scenarios = [new IdentityScenario(assessment, admission)];
    }

    public ValidationPackDescriptor Descriptor => new()
    {
        Key = new ValidationDescriptorKey("scenario-pack/controlled-identity"), Kind = ValidationPackKind.ScenarioPack,
        Revision = new ValidationRevision(PackRevision), DisplayName = "Controlled Identity Policy Evidence",
        Stability = ValidationStability.Experimental, Lane = EvaluationLane.ControlledApplication
    };

    public ValidationApplicability Applicability => new();
    public IReadOnlyList<IValidationScenario> GetScenarios() => _scenarios;

    private sealed class IdentityScenario(ControlledIdentityMatrixAssessment verified, ControlledIdentityAdmission admission) : IValidationScenario
    {
        private readonly string _instanceId = "controlled-identity-" + CanonicalJsonDigest.ComputeObject(new
        {
            verified.AdmittedContract, verified.RecordedObservations, verified.RecordedGaps, verified.AssessedAt, Admission = admission
        });

        public ValidationScenarioDescriptor Descriptor => new()
        {
            Key = new ValidationDescriptorKey(_instanceId), Revision = new ValidationRevision(PackRevision),
            DisplayName = "Controlled Identity Matrix", LayerKey = "controlled-application-identity", Lane = EvaluationLane.ControlledApplication,
            Stability = ValidationStability.Experimental, MutatesState = false
        };

        public Task<ValidationScenarioExecutionResult> ExecuteAsync(ValidationScenarioContext context, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var assessment = ControlledIdentityEvaluator.Evaluate(verified.AdmittedContract!, verified.RecordedObservations!,
                context.ValidationResult.ValidationId, context.ValidationResult.Run.TargetIdentitySha256 ?? string.Empty,
                admission.RouteIdentitySha256, admission.ObserverId, verified.AssessedAt, verified.RecordedGaps) with
            {
                SourceEvidenceSha256 = verified.SourceEvidenceSha256, VerifiedObserverKeyId = verified.VerifiedObserverKeyId
            };
            var coverage = assessment.Cases.Select(entry => Coverage($"{_instanceId}:case:{CanonicalJsonDigest.ComputeText(entry.CaseId)}", entry.Outcome,
                $"Identity case '{entry.CaseId}': observation={entry.ObservationOutcome}; decision={entry.DecisionOutcome}; actor={entry.EffectiveActorOutcome}; authentication={entry.AuthenticationBindingOutcome}; scope={entry.ScopeOutcome}; role={entry.RoleBindingOutcome}; control={entry.PositiveControlOutcome}.")).ToList();
            coverage.Add(Coverage(_instanceId, assessment.Outcome, assessment.Reason));
            if (assessment.EvidenceIntegrityOutcome != ValidationOutcome.Succeeded || coverage.Count == 0)
                coverage.Add(Coverage($"{_instanceId}:integrity", ValidationOutcome.Blocked, "Identity evidence integrity or active run/target admission is incomplete."));
            return Task.FromResult(new ValidationScenarioExecutionResult
            {
                Scenario = new ValidationScenarioResult
                {
                    ScenarioId = _instanceId, DisplayName = Descriptor.DisplayName, ControlledIdentity = assessment, Summary = assessment.Reason,
                    Status = assessment.Outcome == ValidationOutcome.Succeeded ? TestStatus.Passed : assessment.Outcome == ValidationOutcome.Failed ? TestStatus.Failed : TestStatus.Inconclusive
                },
                Coverage = coverage
            });
        }

        private static ValidationCoverageDeclaration Coverage(string scope, ValidationOutcome outcome, string reason)
        {
            var coverage = ValidationCoverageFactory.FromOutcome("controlled-application-identity", scope, outcome, reason);
            return new ValidationCoverageDeclaration
            {
                LayerId = coverage.LayerId, Scope = coverage.Scope, Status = coverage.Status, ObservedOutcome = coverage.ObservedOutcome,
                Blocker = coverage.Blocker, Confidence = coverage.Confidence, Reason = reason,
                Lane = EvaluationLane.ControlledApplication, Origin = EvidenceOrigin.DeterministicObservation
            };
        }
    }
}