using Mcp.Benchmark.Core.Abstractions;
using Mcp.Benchmark.Core.Utilities;

namespace Mcp.Benchmark.Core.Models;

public sealed record ControlledIdentityEvidenceDocument : ISignedControlledEvidence
{
    public const string Type = "mcpval.controlled-identity-evidence";
    public const string Revision = "1.0.0";
    public string DocumentType { get; init; } = Type;
    public string DocumentSchemaVersion { get; init; } = Revision;
    public required ControlledIdentityMatrixContract Contract { get; init; }
    public required IReadOnlyList<ControlledIdentityObservation> Observations { get; init; }
    public DateTimeOffset IssuedAt { get; init; }
    public DateTimeOffset ExpiresAt { get; init; }
    public string SignatureAlgorithm { get; init; } = P256ArtifactSignatures.Algorithm;
    public required string SigningKeyId { get; init; }
    public required string SignatureBase64 { get; init; }

    public string ComputePayloadDigest() => CanonicalJsonDigest.ComputeObject(new
    {
        DocumentType, DocumentSchemaVersion, Contract, Observations, IssuedAt, ExpiresAt, SignatureAlgorithm, SigningKeyId
    });
}