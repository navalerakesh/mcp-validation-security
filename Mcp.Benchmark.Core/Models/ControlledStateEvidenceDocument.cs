using Mcp.Benchmark.Core.Utilities;

namespace Mcp.Benchmark.Core.Models;

public sealed record ControlledStateEvidenceDocument
{
    public const string Type = "mcpval.controlled-state-evidence";
    public const string Revision = "1.0.0";
    public string DocumentType { get; init; } = Type;
    public string DocumentSchemaVersion { get; init; } = Revision;
    public required ControlledStateScenarioContract Contract { get; init; }
    public required ControlledStateEvidence Evidence { get; init; }
    public DateTimeOffset IssuedAt { get; init; }
    public DateTimeOffset ExpiresAt { get; init; }
    public string SignatureAlgorithm { get; init; } = P256ArtifactSignatures.Algorithm;
    public required string SigningKeyId { get; init; }
    public required string SignatureBase64 { get; init; }

    public string ComputePayloadDigest() => CanonicalJsonDigest.ComputeObject(new
    {
        DocumentType, DocumentSchemaVersion, Contract, Evidence, IssuedAt, ExpiresAt, SignatureAlgorithm, SigningKeyId
    });
}