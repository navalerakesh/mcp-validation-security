using Mcp.Benchmark.Core.Abstractions;
using Mcp.Benchmark.Core.Models;
using Mcp.Benchmark.Core.Services;
using Mcp.Benchmark.Core.Utilities;

namespace Mcp.Benchmark.Infrastructure.Scenarios;

public sealed class ControlledStateScenarioPack : IValidationScenarioPack
{
    private const string LayerKey = "controlled-application-state";
    private readonly IReadOnlyList<IValidationScenario> _scenarios;

    public ControlledStateScenarioPack(
        ControlledStateScenarioContract contract,
        ControlledStateEvidence evidence,
        ControlledOperationBinding admittedBinding,
        string trustedIndependentObserverId,
        DateTimeOffset assessedAt)
    {
        ArgumentNullException.ThrowIfNull(contract);
        ArgumentNullException.ThrowIfNull(evidence);
        ArgumentNullException.ThrowIfNull(admittedBinding);
        _scenarios = [new StateScenario(contract, evidence, admittedBinding, trustedIndependentObserverId, assessedAt)];
    }

    internal ControlledStateScenarioPack(ControlledStateAssessment verifiedAssessment)
    {
        ArgumentNullException.ThrowIfNull(verifiedAssessment);
        if (verifiedAssessment.AdmittedContract == null || verifiedAssessment.RecordedEvidence == null ||
            verifiedAssessment.SourceEvidenceSha256 == null || verifiedAssessment.VerifiedObserverKeyId == null)
            throw new ArgumentException("Verified controlled-state evidence is required.", nameof(verifiedAssessment));
        var contract = verifiedAssessment.AdmittedContract;
        _scenarios = [new StateScenario(contract, verifiedAssessment.RecordedEvidence, contract.Binding,
            contract.ObserverId, verifiedAssessment.AssessedAt, verifiedAssessment)];
    }

    public ValidationPackDescriptor Descriptor => new()
    {
        Key = new ValidationDescriptorKey("scenario-pack/controlled-application-state"),
        Lane = EvaluationLane.ControlledApplication,
        Kind = ValidationPackKind.ScenarioPack,
        Revision = new ValidationRevision(ControlledStateScenarioContract.CurrentRevision),
        DisplayName = "Controlled Application State Evidence",
        Stability = ValidationStability.Experimental
    };

    public ValidationApplicability Applicability => new();

    public IReadOnlyList<IValidationScenario> GetScenarios() => _scenarios;

    private sealed class StateScenario(
        ControlledStateScenarioContract contract,
        ControlledStateEvidence evidence,
        ControlledOperationBinding admittedBinding,
        string trustedIndependentObserverId,
        DateTimeOffset assessedAt,
        ControlledStateAssessment? verifiedAssessment = null) : IValidationScenario
    {
        private readonly string _instanceId =
            ControlledStateEvaluator.Evaluate(contract, evidence, admittedBinding, trustedIndependentObserverId, assessedAt).AdmittedContract == null
                ? "controlled-state-rejected"
                : "controlled-state-" + CanonicalJsonDigest.ComputeObject(new { Contract = contract, Evidence = evidence, AssessedAt = assessedAt });

        public ValidationScenarioDescriptor Descriptor => new()
        {
            Key = new ValidationDescriptorKey(_instanceId),
            Lane = EvaluationLane.ControlledApplication,
            Revision = new ValidationRevision(ControlledStateScenarioContract.CurrentRevision),
            DisplayName = "Controlled State Reconciliation",
            LayerKey = LayerKey,
            Stability = ValidationStability.Experimental,
            MutatesState = false
        };

        public Task<ValidationScenarioExecutionResult> ExecuteAsync(ValidationScenarioContext context, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var currentBinding = admittedBinding with
            {
                RunId = context.ValidationResult.ValidationId,
                TargetIdentitySha256 = context.ValidationResult.Run.TargetIdentitySha256 ?? string.Empty
            };
            var assessment = ControlledStateEvaluator.Evaluate(contract, evidence, currentBinding, trustedIndependentObserverId, assessedAt) with
            {
                SourceEvidenceSha256 = verifiedAssessment?.SourceEvidenceSha256,
                VerifiedObserverKeyId = verifiedAssessment?.VerifiedObserverKeyId
            };
            var status = assessment.Outcome switch
            {
                ValidationOutcome.Succeeded => TestStatus.Passed,
                ValidationOutcome.Failed => TestStatus.Failed,
                ValidationOutcome.AuthRequired => TestStatus.AuthRequired,
                ValidationOutcome.Cancelled => TestStatus.Cancelled,
                _ => TestStatus.Inconclusive
            };
            var coverage = ValidationCoverageFactory.FromOutcome(LayerKey, Descriptor.Key.Value, assessment.Outcome, assessment.Reason);
            return Task.FromResult(new ValidationScenarioExecutionResult
            {
                Scenario = new ValidationScenarioResult
                {
                    ScenarioId = Descriptor.Key.Value, DisplayName = Descriptor.DisplayName,
                    Status = status, Summary = assessment.Reason, ControlledState = assessment
                },
                Coverage =
                [
                    new ValidationCoverageDeclaration
                    {
                        LayerId = coverage.LayerId, Scope = coverage.Scope, Status = coverage.Status,
                        ObservedOutcome = coverage.ObservedOutcome, Blocker = coverage.Blocker,
                        Confidence = coverage.Confidence, Reason = assessment.Reason,
                        Lane = EvaluationLane.ControlledApplication, Origin = EvidenceOrigin.DeterministicObservation
                    }
                ]
            });
        }
    }
}