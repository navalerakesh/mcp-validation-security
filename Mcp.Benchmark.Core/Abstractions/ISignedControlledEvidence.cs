namespace Mcp.Benchmark.Core.Abstractions;

public interface ISignedControlledEvidence
{
    DateTimeOffset IssuedAt { get; }
    DateTimeOffset ExpiresAt { get; }
    string SigningKeyId { get; }
    string SignatureBase64 { get; }
    string ComputePayloadDigest();
}