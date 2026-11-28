using Mcp.Benchmark.Core.Models;
using Mcp.Benchmark.Core.Utilities;

namespace Mcp.Benchmark.Core.Services;

public static class ControlledIdentityEvidenceVerifier
{
    public static ControlledIdentityMatrixAssessment Verify(string json, ControlledIdentityMatrixContract admittedContract,
        ControlledIdentityAdmission admission, string trustedObserverPublicKeyPem, DateTimeOffset assessedAt, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(admittedContract);
        ArgumentNullException.ThrowIfNull(admission);
        cancellationToken.ThrowIfCancellationRequested();
        if (admittedContract.Cases is not { Count: > 0 and <= ControlledIdentityMatrixContract.MaximumCases })
            throw new InvalidDataException("Identity matrix admission is invalid.");
        var snapshot = admittedContract with { Cases = Array.AsReadOnly(admittedContract.Cases.ToArray()) };
        if (ControlledIdentityEvaluator.Evaluate(snapshot, [], admission.RunId, admission.TargetIdentitySha256,
                admission.RouteIdentitySha256, admission.ObserverId, assessedAt).AdmittedContract == null)
            throw new InvalidDataException("Identity matrix admission is invalid.");
        var document = SignedControlledEvidenceReader.Read<ControlledIdentityEvidenceDocument>(json, trustedObserverPublicKeyPem,
            ControlledIdentityEvidenceDocument.Type, ControlledIdentityEvidenceDocument.Revision, assessedAt,
            candidate => candidate.Contract?.Cases is { Count: > 0 and <= ControlledIdentityMatrixContract.MaximumCases } &&
                candidate.DocumentSchemaVersion == candidate.Contract.ContractRevision &&
                candidate.Observations is { Count: <= ControlledIdentityMatrixContract.MaximumCases } &&
                candidate.Observations.All(observation => observation != null && !(observation.ObservedAt > candidate.IssuedAt)) &&
                CanonicalJsonDigest.ComputeObject(candidate.Contract) == CanonicalJsonDigest.ComputeObject(snapshot), cancellationToken,
            ControlledIdentityEvidenceDocument.LegacyRevision);
        return ControlledIdentityEvaluator.Evaluate(snapshot, document.Observations, admission.RunId,
            admission.TargetIdentitySha256, admission.RouteIdentitySha256, admission.ObserverId, assessedAt) with
        {
            SourceEvidenceSha256 = CanonicalJsonDigest.ComputeText(json), VerifiedObserverKeyId = document.SigningKeyId
        };
    }
}