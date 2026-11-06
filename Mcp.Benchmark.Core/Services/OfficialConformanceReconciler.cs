using Mcp.Benchmark.Core.Models;

namespace Mcp.Benchmark.Core.Services;

public static class OfficialConformanceReconciler
{
    public static OfficialConformanceReconciliationResult Reconcile(
        OfficialConformanceAssessment assessment,
        IReadOnlyList<AssuranceClaim>? nativeClaims)
    {
        ArgumentNullException.ThrowIfNull(assessment);
        if (nativeClaims == null)
        {
            return OfficialConformanceReconciliationResult.Empty;
        }

        var nativeFailures = nativeClaims
            .Where(claim => claim.Outcome is AssuranceClaimOutcome.ReviewRequired or AssuranceClaimOutcome.Violated or AssuranceClaimOutcome.Error)
            .Select(claim => (Claim: claim, RuleId: claim.Metadata.GetValueOrDefault("ruleId")))
            .Where(item => !string.IsNullOrWhiteSpace(item.RuleId))
            .GroupBy(item => item.RuleId!, StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => group.First().Claim, StringComparer.Ordinal);
        var conflicts = new List<OfficialConformanceConflict>();
        var decisions = new List<DecisionRecord>();

        foreach (var scenario in assessment.Scenarios)
        {
            foreach (var check in scenario.Checks.Where(check => check.Status == OfficialConformanceCheckStatus.Success))
            {
                if (!nativeFailures.TryGetValue(check.Id, out var nativeFailure))
                {
                    continue;
                }

                var officialEvidenceId = $"official-conformance/{scenario.ScenarioId}/{check.Id}";
                var conflictId = $"evidence-conflict/{scenario.ScenarioId}/{check.Id}";
                var relatedEvidence = nativeFailure.EvidenceIds
                    .Append(officialEvidenceId)
                    .Distinct(StringComparer.Ordinal)
                    .OrderBy(value => value, StringComparer.Ordinal)
                    .ToArray();
                conflicts.Add(new OfficialConformanceConflict
                {
                    ConflictId = conflictId,
                    Component = scenario.ScenarioId,
                    Summary = $"Official check '{check.Id}' succeeded while native claim '{nativeFailure.ClaimId}' requires review or rejection.",
                    EvidenceIds = relatedEvidence
                });
                decisions.Add(new DecisionRecord
                {
                    DecisionId = $"external:{conflictId}",
                    RelatedEvidenceIds = relatedEvidence,
                    RuleId = check.Id,
                    RuleRevision = assessment.RequirementSet.Revision,
                    Lane = EvaluationLane.ExternalConformance,
                    Authority = ValidationRuleSource.Spec,
                    Origin = EvidenceOrigin.ExternalConformanceCheck,
                    Gate = GateOutcome.ReviewRequired,
                    Severity = ValidationFindingSeverity.High,
                    Category = "EvidenceConflict",
                    Component = scenario.ScenarioId,
                    Summary = "Official conformance and native validation evidence disagree; manual review is required.",
                    SpecReference = check.SpecReferences.Select(reference => reference.Url).FirstOrDefault(url => !string.IsNullOrWhiteSpace(url)),
                    ImpactAreas = [ImpactArea.CoverageIntegrity, ImpactArea.ProtocolInteroperability],
                    Metadata = new Dictionary<string, string>(StringComparer.Ordinal)
                    {
                        ["officialCheckId"] = check.Id,
                        ["nativeClaimId"] = nativeFailure.ClaimId
                    }
                });
            }
        }

        return new OfficialConformanceReconciliationResult(conflicts, decisions);
    }
}

public sealed record OfficialConformanceReconciliationResult(
    IReadOnlyList<OfficialConformanceConflict> Conflicts,
    IReadOnlyList<DecisionRecord> Decisions)
{
    public static OfficialConformanceReconciliationResult Empty { get; } = new(
        Array.Empty<OfficialConformanceConflict>(),
        Array.Empty<DecisionRecord>());
}