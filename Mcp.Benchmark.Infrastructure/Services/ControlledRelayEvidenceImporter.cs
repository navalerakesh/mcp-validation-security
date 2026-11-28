using Mcp.Benchmark.Core.Models;
using Mcp.Benchmark.Core.Services;
using Mcp.Benchmark.Core.Utilities;
using Mcp.Benchmark.Infrastructure.Scenarios;

namespace Mcp.Benchmark.Infrastructure.Services;

public sealed class ControlledRelayEvidenceImporter
{
    public ControlledRelayScenarioPack ImportPack(string json, ControlledRelayContract contract, string trustedObserverKey,
        string runId, string targetDigest, DateTimeOffset assessedAt, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(contract);
        contract = ControlledRelayEvaluator.Evaluate(contract, [], runId, targetDigest, contract.ObserverId, assessedAt).AdmittedContract
            ?? throw new InvalidDataException("Relay contract did not match the admitted run and target.");
        var document = SignedControlledEvidenceReader.Read<ControlledRelayEvidenceDocument>(json, trustedObserverKey,
            ControlledRelayEvidenceDocument.Type, ControlledRelayContract.Revision, assessedAt,
            candidate => candidate.Contract != null && candidate.Observations is { Count: <= 2 } &&
                CanonicalJsonDigest.ComputeObject(candidate.Contract) == CanonicalJsonDigest.ComputeObject(contract) &&
                candidate.Observations.All(item => item != null && item.ObservedThrough <= candidate.IssuedAt) &&
                (candidate.EffectEvidence == null || candidate.EffectEvidence.ObservedThrough <= candidate.IssuedAt &&
                    !(candidate.EffectEvidence.AuditObservedThrough > candidate.IssuedAt)), cancellationToken);
        var assessment = ControlledRelayEvaluator.Evaluate(contract, document.Observations, runId, targetDigest, contract.ObserverId,
            assessedAt, document.EffectEvidence) with
        {
            SourceEvidenceSha256 = CanonicalJsonDigest.ComputeText(json), VerifiedObserverKeyId = document.SigningKeyId
        };
        if (assessment.AdmittedContract == null) throw new InvalidDataException("Relay contract did not match the admitted run and target.");
        return new ControlledRelayScenarioPack(assessment, document.EffectEvidence, runId, targetDigest, document.IssuedAt, document.ExpiresAt);
    }
}
