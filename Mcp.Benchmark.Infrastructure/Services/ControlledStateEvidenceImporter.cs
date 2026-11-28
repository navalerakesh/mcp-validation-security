using Mcp.Benchmark.Core.Models;
using Mcp.Benchmark.Core.Services;
using Mcp.Benchmark.Core.Utilities;
using Mcp.Benchmark.Infrastructure.Scenarios;

namespace Mcp.Benchmark.Infrastructure.Services;

public sealed class ControlledStateEvidenceImporter
{
    public ControlledStateScenarioPack ImportPack(
        string json, ControlledStateScenarioContract admittedContract, string trustedObserverPublicKeyPem, DateTimeOffset assessedAt,
        CancellationToken cancellationToken = default) =>
        new(Import(json, admittedContract, trustedObserverPublicKeyPem, assessedAt, cancellationToken));

    public ControlledStateAssessment Import(
        string json,
        ControlledStateScenarioContract admittedContract,
        string trustedObserverPublicKeyPem,
        DateTimeOffset assessedAt,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(admittedContract);
        var document = SignedControlledEvidenceReader.Read<ControlledStateEvidenceDocument>(json, trustedObserverPublicKeyPem,
            ControlledStateEvidenceDocument.Type, ControlledStateEvidenceDocument.Revision, assessedAt,
            candidate => candidate.Contract != null && candidate.Evidence != null && candidate.Evidence.ObservedThrough != null &&
                candidate.Evidence.ObservedThrough <= candidate.IssuedAt && !(candidate.Evidence.AuditObservedThrough > candidate.IssuedAt) &&
                CanonicalJsonDigest.ComputeObject(candidate.Contract) == CanonicalJsonDigest.ComputeObject(admittedContract), cancellationToken);
        return ControlledStateEvaluator.Evaluate(admittedContract, document.Evidence, admittedContract.Binding,
            admittedContract.ObserverId, assessedAt) with
        {
            SourceEvidenceSha256 = CanonicalJsonDigest.ComputeText(json), VerifiedObserverKeyId = document.SigningKeyId
        };
    }
}