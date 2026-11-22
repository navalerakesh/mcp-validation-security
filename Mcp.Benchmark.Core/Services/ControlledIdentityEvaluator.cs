using Mcp.Benchmark.Core.Models;

namespace Mcp.Benchmark.Core.Services;

public static class ControlledIdentityEvaluator
{
    public static ControlledIdentityMatrixAssessment Evaluate(ControlledIdentityMatrixContract contract,
        IReadOnlyList<ControlledIdentityObservation> observations, string admittedRunId, string admittedTargetDigest,
        string admittedRouteDigest, string trustedObserverId, DateTimeOffset assessedAt,
        IReadOnlyList<ControlledIdentityEvidenceGap>? retainedGaps = null)
    {
        ArgumentNullException.ThrowIfNull(contract);
        ArgumentNullException.ThrowIfNull(observations);
        ControlledIdentityMatrixContract? admitted = null;
        if (contract.ContractRevision != ControlledIdentityMatrixContract.CurrentRevision ||
            !new[] { contract.PolicyRevision, contract.ToolName, contract.ObserverId, admittedRunId, trustedObserverId }.All(ControlledEvidenceAdmission.IsValidIdentifier) ||
            contract.ObserverId != trustedObserverId || !ControlledEvidenceAdmission.IsSha256(admittedTargetDigest) ||
            !ControlledEvidenceAdmission.IsSha256(admittedRouteDigest) || contract.MaximumEvidenceAge <= TimeSpan.Zero || contract.MaximumObservationDelay <= TimeSpan.Zero ||
            contract.Cases is not { Count: > 0 and <= ControlledIdentityMatrixContract.MaximumCases }) return Rejected();

        var cases = new Dictionary<string, ControlledIdentityCase>(StringComparer.Ordinal);
        var operations = new HashSet<string>(StringComparer.Ordinal);
        var attempts = new HashSet<string>(StringComparer.Ordinal);
        var requests = new HashSet<string>(StringComparer.Ordinal);
        foreach (var entry in contract.Cases)
        {
            if (entry == null || !new[] { entry.CaseId, entry.ScopePredicateId, entry.ExpectedEffectiveActorAlias }.All(ControlledEvidenceAdmission.IsValidIdentifier) ||
                !ControlledEvidenceAdmission.IsValidBinding(entry.Binding) || entry.Binding.RunId != admittedRunId ||
                entry.Binding.TargetIdentitySha256 != admittedTargetDigest || entry.Binding.RouteIdentitySha256 != admittedRouteDigest ||
                !cases.TryAdd(entry.CaseId, entry) || !operations.Add(entry.Binding.OperationId) || !attempts.Add(entry.Binding.AttemptId) || !requests.Add(entry.Binding.RequestId) ||
                !Enum.IsDefined(entry.IdentityRole) || entry.ExpectedDecision is not (ControlledAuthorizationDecision.Allowed or ControlledAuthorizationDecision.Denied) ||
                (entry.IsPositiveControl ? entry.ExpectedDecision != ControlledAuthorizationDecision.Allowed || entry.PositiveControlCaseId != null
                    : !ControlledEvidenceAdmission.IsValidIdentifier(entry.PositiveControlCaseId))) return Rejected();
        }
        foreach (var entry in cases.Values.Where(entry => !entry.IsPositiveControl))
        {
            if (!cases.TryGetValue(entry.PositiveControlCaseId!, out var control) || !control.IsPositiveControl ||
                control.Binding.BoundaryId != entry.Binding.BoundaryId || control.ScopePredicateId != entry.ScopePredicateId) return Rejected();
        }
        admitted = contract with { Cases = Array.AsReadOnly(cases.Values.OrderBy(entry => entry.CaseId, StringComparer.Ordinal).ToArray()) };
        if (observations.Count > ControlledIdentityMatrixContract.MaximumCases || retainedGaps?.Count > ControlledIdentityMatrixContract.MaximumCases) return Rejected();
        var recorded = new Dictionary<string, ControlledIdentityObservation>(StringComparer.Ordinal);
        var gaps = new Dictionary<string, int>(StringComparer.Ordinal);
        var unattributed = 0;
        foreach (var gap in retainedGaps ?? [])
        {
            if (gap == null || gap.RejectedObservations is <= 0 or > ControlledIdentityMatrixContract.MaximumCases ||
                gap.CaseId != null && !cases.ContainsKey(gap.CaseId)) return Rejected();
            if (gap.CaseId == null) unattributed += gap.RejectedObservations;
            else gaps[gap.CaseId] = gaps.GetValueOrDefault(gap.CaseId) + gap.RejectedObservations;
        }
        if ((long)unattributed + gaps.Values.Sum(value => (long)value) + observations.Count > ControlledIdentityMatrixContract.MaximumCases) return Rejected();
        foreach (var observation in observations)
        {
            var entry = observation != null && ControlledEvidenceAdmission.IsValidIdentifier(observation.CaseId)
                ? cases.GetValueOrDefault(observation.CaseId) : null;
            if (entry == null)
            {
                unattributed++;
                continue;
            }
            if (gaps.ContainsKey(entry.CaseId) || observation!.Binding != entry.Binding ||
                observation.ObserverId != trustedObserverId || observation.ScopePredicateId != entry.ScopePredicateId ||
                !Enum.IsDefined(observation.ObserverOutcome) || !Enum.IsDefined(observation.Decision) ||
                !recorded.TryAdd(observation.CaseId, observation))
            {
                var rejectedCount = recorded.Remove(entry.CaseId) ? 2 : 1;
                gaps[entry.CaseId] = gaps.GetValueOrDefault(entry.CaseId) + rejectedCount;
            }
        }
        var primary = admitted.Cases.ToDictionary(entry => entry.CaseId, Assess, StringComparer.Ordinal);
        var results = admitted.Cases.Select(entry =>
        {
            var assessment = primary[entry.CaseId];
            if (entry.IsPositiveControl) return assessment;
            var control = primary[entry.PositiveControlCaseId!];
            var positive = control.Outcome == ValidationOutcome.Succeeded && recorded.TryGetValue(control.CaseId, out var controlObservation) &&
                recorded.TryGetValue(entry.CaseId, out var observation) && controlObservation.ObservedAt < observation.OperationStartedAt
                ? ValidationOutcome.Succeeded : ValidationOutcome.Inconclusive;
            return assessment with { PositiveControlOutcome = positive,
                Outcome = assessment.Outcome is ValidationOutcome.Failed or ValidationOutcome.Blocked ? assessment.Outcome
                    : positive == ValidationOutcome.Succeeded ? assessment.Outcome : ValidationOutcome.Inconclusive };
        }).ToArray();
        var integrity = gaps.Count == 0 && unattributed == 0 ? ValidationOutcome.Succeeded : ValidationOutcome.Blocked;
        var outcome = Combine(results.Select(result => result.Outcome));
        var recordedGaps = gaps.OrderBy(pair => pair.Key, StringComparer.Ordinal).Select(pair => new ControlledIdentityEvidenceGap(pair.Key, pair.Value)).ToList();
        if (unattributed > 0) recordedGaps.Insert(0, new ControlledIdentityEvidenceGap(null, unattributed));
        return new ControlledIdentityMatrixAssessment
        {
            AdmittedContract = admitted, RecordedObservations = Array.AsReadOnly(recorded.Values.OrderBy(entry => entry.CaseId, StringComparer.Ordinal).ToArray()),
            RecordedGaps = recordedGaps.AsReadOnly(), EvidenceIntegrityOutcome = integrity,
            AssessedAt = assessedAt, Outcome = outcome == ValidationOutcome.Failed ? outcome : integrity == ValidationOutcome.Blocked ? ValidationOutcome.Blocked : outcome, Cases = Array.AsReadOnly(results),
            Reason = "Identity outcomes apply only to the admitted policy matrix and independent observer; they do not establish universal enforcement."
        };

        ControlledIdentityCaseAssessment Assess(ControlledIdentityCase entry)
        {
            if (gaps.ContainsKey(entry.CaseId)) return Gap(entry.CaseId, ValidationOutcome.Blocked) with { Outcome = ValidationOutcome.Blocked };
            if (!recorded.TryGetValue(entry.CaseId, out var observation)) return Gap(entry.CaseId, ValidationOutcome.Unavailable);
            if (observation.ObserverOutcome != ValidationOutcome.Succeeded) return Gap(entry.CaseId, observation.ObserverOutcome);
            if (!observation.Complete || observation.PreconditionsSatisfied != true ||
                observation.OperationStartedAt is not { } started || observation.OperationEndedAt is not { } ended || observation.ObservedAt is not { } observed ||
                ended < started || observed < ended || observed - ended > contract.MaximumObservationDelay || observed > assessedAt || assessedAt - observed > contract.MaximumEvidenceAge)
                return Gap(entry.CaseId, ValidationOutcome.Inconclusive);
            var decision = observation.Decision == ControlledAuthorizationDecision.Unknown ? ValidationOutcome.Inconclusive
                : Predicate(observation.Decision == entry.ExpectedDecision);
            var actor = Predicate(observation.EffectiveActorMatched);
            var authentication = Predicate(observation.AuthenticationBindingMatched);
            var scope = Predicate(observation.ScopeMatched);
            var role = entry.IdentityRole switch
            {
                ControlledIdentityRole.Unknown => ValidationOutcome.Inconclusive,
                ControlledIdentityRole.DelegationReference => Predicate(observation.DelegationBindingMatched),
                ControlledIdentityRole.ApproverAssertion => Predicate(observation.ApprovalBindingMatched),
                _ => ValidationOutcome.NotApplicable
            };
            return new ControlledIdentityCaseAssessment
            {
                CaseId = entry.CaseId, Outcome = Combine([decision, actor, authentication, scope, role]), ObservationOutcome = ValidationOutcome.Succeeded,
                DecisionOutcome = decision, EffectiveActorOutcome = actor, AuthenticationBindingOutcome = authentication, ScopeOutcome = scope,
                RoleBindingOutcome = role, PositiveControlOutcome = ValidationOutcome.NotApplicable
            };
        }

        ControlledIdentityMatrixAssessment Rejected() => new()
        {
            AdmittedContract = admitted, AssessedAt = assessedAt, Outcome = ValidationOutcome.Blocked,
            Cases = admitted == null ? [] : Array.AsReadOnly(admitted.Cases.Select(entry => Gap(entry.CaseId, ValidationOutcome.Blocked) with { Outcome = ValidationOutcome.Blocked }).ToArray()),
            Reason = "Identity evidence does not match the admitted bounded matrix."
        };
    }

    private static ValidationOutcome Predicate(bool? value) => value == true ? ValidationOutcome.Succeeded : value == false ? ValidationOutcome.Failed : ValidationOutcome.Inconclusive;

    private static ValidationOutcome Combine(IEnumerable<ValidationOutcome> outcomes)
    {
        var values = outcomes.ToArray();
        return values.Contains(ValidationOutcome.Failed) ? ValidationOutcome.Failed
            : values.All(value => value is ValidationOutcome.Succeeded or ValidationOutcome.NotApplicable) ? ValidationOutcome.Succeeded : ValidationOutcome.Inconclusive;
    }

    private static ControlledIdentityCaseAssessment Gap(string caseId, ValidationOutcome observed) => new()
    {
        CaseId = caseId, Outcome = ValidationOutcome.Inconclusive, ObservationOutcome = observed, DecisionOutcome = ValidationOutcome.Inconclusive,
        EffectiveActorOutcome = ValidationOutcome.Inconclusive, AuthenticationBindingOutcome = ValidationOutcome.Inconclusive,
        ScopeOutcome = ValidationOutcome.Inconclusive, RoleBindingOutcome = ValidationOutcome.Inconclusive, PositiveControlOutcome = ValidationOutcome.NotApplicable
    };
}