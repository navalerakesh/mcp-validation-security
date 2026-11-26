using Mcp.Benchmark.Core.Models;

namespace Mcp.Benchmark.Core.Services;

public static class ControlledRelayEvaluator
{
    public static ControlledRelayAssessment Evaluate(ControlledRelayContract contract, IReadOnlyList<ControlledRelayObservation> observations,
        string runId, string targetDigest, string observerId, DateTimeOffset assessedAt, ControlledStateEvidence? effectEvidence = null)
    {
        ArgumentNullException.ThrowIfNull(contract);
        ArgumentNullException.ThrowIfNull(observations);
        var checks = new List<ControlledRelayCheck>();
        if (contract.ContractRevision != ControlledRelayContract.Revision || contract.Boundaries is not { Count: 2 } ||
            observations.Count > 2 || contract.MaximumDuration <= TimeSpan.Zero || contract.MaximumEvidenceAge <= TimeSpan.Zero ||
            contract.MaximumObserverReads <= 0 || !ControlledEvidenceAdmission.IsValidIdentifier(contract.ScenarioId) ||
            !ControlledEvidenceAdmission.IsValidIdentifier(contract.PolicyRevision) || !ControlledEvidenceAdmission.IsValidIdentifier(observerId) ||
            contract.ObserverId != observerId || !ControlledEvidenceAdmission.IsValidIdentifier(runId) ||
            !ControlledEvidenceAdmission.IsSha256(targetDigest))
            return Rejected();
        foreach (var boundary in contract.Boundaries)
            if (boundary == null || !ControlledEvidenceAdmission.IsValidBinding(boundary.Binding) || boundary.Binding.RunId != runId ||
                !new[] { boundary.ProtocolVersion, boundary.Method, boundary.Subject }.All(ControlledEvidenceAdmission.IsValidIdentifier) ||
                boundary.Transport is not ("http" or "stdio") || !ControlledEvidenceAdmission.IsSha256(boundary.CapabilityDigest) ||
                !ControlledEvidenceAdmission.IsSha256(boundary.ResponseDigest) || boundary.MaximumAttempts is <= 0 or > 100_000 ||
                boundary.Transport == "http" && boundary.ExpectedHttpStatus is not (>= 100 and <= 599) ||
                boundary.Transport == "stdio" && boundary.ExpectedHttpStatus != null)
                return Rejected();
        var upstream = contract.Boundaries[0];
        var downstream = contract.Boundaries[1];
        if (upstream.ParentRequestId != null || downstream.ParentRequestId != upstream.Binding.RequestId ||
            upstream.Binding.TargetIdentitySha256 != targetDigest || upstream.Binding.BoundaryId == downstream.Binding.BoundaryId ||
            upstream.Binding.RequestId == downstream.Binding.RequestId || upstream.Binding.OperationId == downstream.Binding.OperationId ||
            contract.EffectContract != null && (contract.EffectContract.Binding != downstream.Binding || contract.EffectContract.ObserverId != observerId ||
                !ControlledStateEvaluator.IsValidContract(contract.EffectContract)))
            return Rejected();
        var admitted = contract with { Boundaries = Array.AsReadOnly(contract.Boundaries.ToArray()) };
        var observed = new Dictionary<string, ControlledRelayObservation>(StringComparer.Ordinal);
        foreach (var item in observations)
        {
            if (item == null || !ControlledEvidenceAdmission.IsValidBinding(item.Binding) ||
                !contract.Boundaries.Any(boundary => boundary.Binding.BoundaryId == item.Binding.BoundaryId) ||
                !observed.TryAdd(item.Binding.BoundaryId, item))
                return Rejected();
        }
        long reads = 0;
        var retained = new List<ControlledRelayObservation>();
        var usableBoundaries = new HashSet<string>(StringComparer.Ordinal);
        foreach (var expected in contract.Boundaries)
        {
            var boundaryId = expected.Binding.BoundaryId;
            if (!observed.TryGetValue(boundaryId, out var item))
            {
                checks.Add(new(boundaryId, "observation", ValidationOutcome.Unavailable));
                continue;
            }
            if (item.ObserverId != observerId || !Enum.IsDefined(item.ObserverOutcome) || !Enum.IsDefined(item.CleanupOutcome) ||
                item.ParentRequestId != null && !ControlledEvidenceAdmission.IsValidIdentifier(item.ParentRequestId) ||
                item.ResponseRequestId != null && !ControlledEvidenceAdmission.IsValidIdentifier(item.ResponseRequestId) ||
                item.ObserverReads < 0 || item.Attempts < 0 ||
                !new[] { item.ProtocolVersion, item.Method, item.Subject }.All(ControlledEvidenceAdmission.IsValidIdentifier) ||
                item.Transport is not ("http" or "stdio") || !ControlledEvidenceAdmission.IsSha256(item.CapabilityDigest) ||
                !ControlledEvidenceAdmission.IsSha256(item.ResponseDigest) || item.HttpStatus is < 100 or > 599)
            {
                checks.Add(new(boundaryId, "integrity", ValidationOutcome.Blocked));
                continue;
            }
            reads += item.ObserverReads;
            retained.Add(item);
            if (item.ObserverOutcome != ValidationOutcome.Succeeded || !item.Complete || item.StartedAt == null ||
                item.CompletedAt == null || item.ObservedThrough == null || item.StartedAt > item.CompletedAt ||
                item.CompletedAt > item.ObservedThrough || item.ObservedThrough > assessedAt ||
                assessedAt - item.StartedAt > contract.MaximumEvidenceAge || item.ObserverReads == 0 || item.Attempts == 0)
            {
                checks.Add(new(boundaryId, "observation", ValidationOutcome.Inconclusive));
                continue;
            }
            usableBoundaries.Add(boundaryId);
            Check("binding", item.Binding == expected.Binding && item.ParentRequestId == expected.ParentRequestId);
            Check("response-correlation", item.ResponseRequestId == expected.Binding.RequestId, item.ResponseRequestId != null);
            Check("negotiation", item.ProtocolVersion == expected.ProtocolVersion && item.Transport == expected.Transport &&
                item.CapabilityDigest == expected.CapabilityDigest);
            Check("request-transformation", item.Method == expected.Method && item.Subject == expected.Subject);
            Check("response-transformation", item.ResponseDigest == expected.ResponseDigest && item.HttpStatus == expected.ExpectedHttpStatus &&
                item.ProtocolError == expected.ExpectedProtocolError && item.ApplicationError == expected.ExpectedApplicationError,
                item.ApplicationError.HasValue);
            Check("identity", item.EffectiveActorMatched == true && item.AudienceMatched == true,
                item.EffectiveActorMatched.HasValue && item.AudienceMatched.HasValue);
            Check("cancellation", item.CancellationObserved == expected.ExpectedCancellation, item.CancellationObserved.HasValue);
            Check("attempt-budget", item.Attempts <= expected.MaximumAttempts);
            Check("deadline", item.CompletedAt - item.StartedAt <= contract.MaximumDuration);
            checks.Add(new(boundaryId, "cleanup", item.CleanupOutcome is ValidationOutcome.Succeeded or ValidationOutcome.Failed
                ? item.CleanupOutcome : ValidationOutcome.Inconclusive));

            void Check(string id, bool passed, bool available = true) => checks.Add(new(boundaryId, id,
                !available ? ValidationOutcome.Inconclusive : passed ? ValidationOutcome.Succeeded : ValidationOutcome.Failed));
        }
        checks.Add(new(upstream.Binding.BoundaryId, "observer-budget",
            reads <= contract.MaximumObserverReads ? ValidationOutcome.Succeeded : ValidationOutcome.Inconclusive));
        if (observed.TryGetValue(upstream.Binding.BoundaryId, out var parent) &&
            observed.TryGetValue(downstream.Binding.BoundaryId, out var child))
        {
            var clocksAvailable = usableBoundaries.Contains(upstream.Binding.BoundaryId) && usableBoundaries.Contains(downstream.Binding.BoundaryId);
            checks.Add(new(downstream.Binding.BoundaryId, "parent-window", !clocksAvailable ? ValidationOutcome.Inconclusive :
                parent.StartedAt <= child.StartedAt && child.CompletedAt <= parent.CompletedAt
                    ? ValidationOutcome.Succeeded : ValidationOutcome.Failed));
        }
        ControlledStateAssessment? effect = null;
        if (contract.EffectContract is { } effectContract)
        {
            if (effectEvidence == null) checks.Add(new(downstream.Binding.BoundaryId, "state-effect", ValidationOutcome.Unavailable));
            else
            {
                effect = ControlledStateEvaluator.Evaluate(effectContract, effectEvidence, downstream.Binding, observerId, assessedAt);
                checks.Add(new(downstream.Binding.BoundaryId, "state-effect", effect.Outcome));
            }
        }
        else if (effectEvidence != null) checks.Add(new(downstream.Binding.BoundaryId, "unadmitted-effect", ValidationOutcome.Blocked));
        return new ControlledRelayAssessment
        {
            AdmittedContract = admitted, Observations = Array.AsReadOnly(retained.OrderBy(item => item.Binding.BoundaryId, StringComparer.Ordinal).ToArray()),
            Checks = checks.AsReadOnly(), Effect = effect, AssessedAt = assessedAt,
            Outcome = checks.Any(check => check.Outcome == ValidationOutcome.Failed) ? ValidationOutcome.Failed :
                checks.All(check => check.Outcome == ValidationOutcome.Succeeded) ? ValidationOutcome.Succeeded : ValidationOutcome.Inconclusive,
            Reason = "Only the two admitted boundaries and declared transformations were assessed; hidden hops and production independence are not established."
        };

        ControlledRelayAssessment Rejected() => new()
        {
            Outcome = ValidationOutcome.Blocked, Checks = [new("unavailable", "admission", ValidationOutcome.Blocked)],
            AssessedAt = assessedAt, Reason = "Relay evidence or its two-boundary admission contract is invalid or ambiguous."
        };
    }
}
