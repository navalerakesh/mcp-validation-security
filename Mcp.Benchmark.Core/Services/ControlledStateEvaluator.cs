using Mcp.Benchmark.Core.Models;

namespace Mcp.Benchmark.Core.Services;

public static class ControlledStateEvaluator
{
    public static ControlledStateAssessment Evaluate(
        ControlledStateScenarioContract contract,
        ControlledStateEvidence evidence,
        ControlledOperationBinding admittedBinding,
        string trustedIndependentObserverId,
        DateTimeOffset assessedAt)
    {
        ArgumentNullException.ThrowIfNull(contract);
        ArgumentNullException.ThrowIfNull(evidence);
        ArgumentNullException.ThrowIfNull(admittedBinding);
        var inputsAdmitted = false;
        var application = ValidationOutcome.Inconclusive;
        var audit = contract.AuditRequired ? ValidationOutcome.Inconclusive : ValidationOutcome.NotApplicable;
        if (!ValidBinding(admittedBinding) ||
            !HasValidContractIdentity(contract) ||
            evidence.AuditPredicateId != null && !ValidIdentifier(evidence.AuditPredicateId) ||
            contract.Binding != admittedBinding || evidence.Binding != admittedBinding ||
            string.IsNullOrWhiteSpace(trustedIndependentObserverId) ||
            !string.Equals(contract.ObserverId, trustedIndependentObserverId, StringComparison.Ordinal) ||
            !string.Equals(evidence.ObserverId, trustedIndependentObserverId, StringComparison.Ordinal) ||
            !string.Equals(evidence.PredicateId, contract.PredicateId, StringComparison.Ordinal) ||
            !string.Equals(evidence.PreconditionId, contract.PreconditionId, StringComparison.Ordinal) ||
            contract.AuditRequired && !string.Equals(evidence.AuditPredicateId, contract.AuditPredicateId, StringComparison.Ordinal))
            return Gap(ValidationOutcome.Blocked, "Evidence does not match the admitted operation and independent observer.");

        if (!HasValidContractExpectations(contract) ||
            !Enum.IsDefined(evidence.ApplicationOutcome) || !Enum.IsDefined(evidence.ObserverOutcome) || !Enum.IsDefined(evidence.CleanupOutcome) || !Enum.IsDefined(evidence.AuditObserverOutcome) ||
            evidence.ObserverReads < 0 || evidence.ObserverReads > contract.MaximumObserverReads)
            return Gap(ValidationOutcome.Blocked, "Scenario expectations or observation budget are not admitted.");

        inputsAdmitted = true;
        application = evidence.ApplicationOutcome switch
        {
            ControlledApplicationOutcome.Unknown or ControlledApplicationOutcome.Pending => ValidationOutcome.Inconclusive,
            ControlledApplicationOutcome.Succeeded or ControlledApplicationOutcome.Rejected =>
                evidence.ApplicationOutcome == contract.ExpectedApplicationOutcome ? ValidationOutcome.Succeeded : ValidationOutcome.Failed,
            _ => ValidationOutcome.Inconclusive
        };
        audit = !contract.AuditRequired ? ValidationOutcome.NotApplicable
            : evidence.AuditObserverOutcome != ValidationOutcome.Succeeded || !evidence.AuditObservationComplete ||
              evidence.OperationStartedAt is not { } auditStarted || evidence.OperationEndedAt is not { } auditEnded || auditEnded < auditStarted ||
              evidence.AuditObservedThrough is not { } auditThrough || auditThrough < auditEnded || auditThrough > assessedAt ||
              auditThrough - auditEnded > contract.ConsistencyWindow || assessedAt - auditThrough > contract.MaximumEvidenceAge
                ? ValidationOutcome.Inconclusive
                : evidence.AuditMatched == true ? ValidationOutcome.Succeeded
                : evidence.AuditMatched == false && auditThrough - auditEnded >= contract.ConsistencyWindow ? ValidationOutcome.Failed : ValidationOutcome.Inconclusive;
        if (evidence.BaselineAt is not { } baseline || evidence.OperationStartedAt is not { } started ||
            evidence.OperationEndedAt is not { } ended || evidence.ObservedThrough is not { } through ||
            baseline > started || started - baseline > contract.MaximumBaselineAge || ended < started ||
            through < ended || through > assessedAt || through - ended > contract.ConsistencyWindow ||
            assessedAt - through > contract.MaximumEvidenceAge)
            return Gap(ValidationOutcome.Inconclusive, "Recorded baseline or observation timing cannot establish the declared consistency window.");

        if (evidence.ObserverOutcome != ValidationOutcome.Succeeded || !evidence.ObservationComplete ||
            evidence.PreconditionsSatisfied != true || evidence.BeforePredicateSatisfied == null ||
            evidence.AfterPredicateSatisfied == null || evidence.StateChanged == null || evidence.ObserverReads == 0)
            return Gap(evidence.ObserverOutcome is ValidationOutcome.AuthRequired or ValidationOutcome.Blocked or ValidationOutcome.Cancelled
                ? evidence.ObserverOutcome : ValidationOutcome.Inconclusive,
                "Independent observation or preconditions are unavailable or incomplete.");

        if (evidence.StateChanged == false && evidence.BeforePredicateSatisfied != evidence.AfterPredicateSatisfied)
            return Gap(ValidationOutcome.Inconclusive, "Observer predicates contradict the recorded unchanged state.");

        var windowComplete = through - ended >= contract.ConsistencyWindow;
        var stateMatches = contract.StateExpectation switch
        {
            ControlledStateExpectation.ChangeRequired => evidence.StateChanged == true && evidence.AfterPredicateSatisfied == true,
            ControlledStateExpectation.NoOpPermitted => evidence.AfterPredicateSatisfied == true &&
                (evidence.StateChanged == true || evidence.BeforePredicateSatisfied == true),
            ControlledStateExpectation.UnchangedRequired => evidence.StateChanged == false,
            _ => false
        };
        var state = stateMatches
            ? contract.StateExpectation == ControlledStateExpectation.UnchangedRequired && !windowComplete
                ? ValidationOutcome.Inconclusive : ValidationOutcome.Succeeded
            : windowComplete || contract.StateExpectation == ControlledStateExpectation.UnchangedRequired && evidence.StateChanged == true
                ? ValidationOutcome.Failed : ValidationOutcome.Inconclusive;
        var outcome = new[] { application, state, audit }.Contains(ValidationOutcome.Failed) ? ValidationOutcome.Failed
            : application == ValidationOutcome.Succeeded && state == ValidationOutcome.Succeeded &&
              audit is ValidationOutcome.Succeeded or ValidationOutcome.NotApplicable ? ValidationOutcome.Succeeded : ValidationOutcome.Inconclusive;
        if (evidence.CleanupOutcome is ValidationOutcome.Failed or ValidationOutcome.Error)
            outcome = ValidationOutcome.Failed;
        else if (outcome == ValidationOutcome.Succeeded && evidence.CleanupOutcome is not (ValidationOutcome.Succeeded or ValidationOutcome.NotApplicable))
            outcome = ValidationOutcome.Inconclusive;
        return new ControlledStateAssessment
        {
            AdmittedContract = contract,
            RecordedEvidence = evidence,
            AssessedAt = assessedAt,
            Outcome = outcome, StateOutcome = state, ApplicationOutcome = application, AuditOutcome = audit,
            CleanupOutcome = evidence.CleanupOutcome,
            Reason = outcome == ValidationOutcome.Succeeded
                ? "Recorded evidence satisfies the declared application/state control; this is not a generic persistence guarantee."
                : "Recorded application, state, required audit or cleanup evidence does not establish the declared control."
        };

        ControlledStateAssessment Gap(ValidationOutcome outcome, string reason) => new()
        {
            AdmittedContract = inputsAdmitted ? contract : null,
            RecordedEvidence = inputsAdmitted ? evidence : null,
            AssessedAt = assessedAt,
            Outcome = inputsAdmitted && (application == ValidationOutcome.Failed || audit == ValidationOutcome.Failed ||
                evidence.CleanupOutcome is ValidationOutcome.Failed or ValidationOutcome.Error) ? ValidationOutcome.Failed : outcome,
            StateOutcome = ValidationOutcome.Inconclusive, ApplicationOutcome = application,
            AuditOutcome = audit,
            CleanupOutcome = Enum.IsDefined(evidence.CleanupOutcome) ? evidence.CleanupOutcome : ValidationOutcome.NotEvaluated,
            Reason = reason
        };
    }

    internal static bool IsValidContract(ControlledStateScenarioContract contract) =>
        ControlledEvidenceAdmission.IsValidBinding(contract.Binding) && HasValidContractIdentity(contract) && HasValidContractExpectations(contract);

    private static bool HasValidContractIdentity(ControlledStateScenarioContract contract) =>
        new[] { contract.ScenarioId, contract.PolicyRevision, contract.ToolName, contract.ObserverId, contract.PredicateId, contract.PreconditionId, contract.CleanupOwner }
            .All(ValidIdentifier) &&
        (!contract.AuditRequired || ValidIdentifier(contract.AuditPredicateId)) &&
        (contract.AuditPredicateId == null || ValidIdentifier(contract.AuditPredicateId));

    private static bool HasValidContractExpectations(ControlledStateScenarioContract contract) =>
        contract.ContractRevision == ControlledStateScenarioContract.CurrentRevision &&
        Enum.IsDefined(contract.StateExpectation) && contract.StateExpectation != ControlledStateExpectation.Unspecified &&
        contract.ExpectedApplicationOutcome is ControlledApplicationOutcome.Succeeded or ControlledApplicationOutcome.Rejected &&
        contract.ConsistencyWindow > TimeSpan.Zero && contract.MaximumBaselineAge > TimeSpan.Zero &&
        contract.MaximumEvidenceAge > TimeSpan.Zero && contract.MaximumObserverReads > 0;

    private static bool ValidBinding(ControlledOperationBinding binding) => ControlledEvidenceAdmission.IsValidBinding(binding);

    private static bool ValidIdentifier(string? value) => ControlledEvidenceAdmission.IsValidIdentifier(value);
}