using Mcp.Benchmark.Core.Models;

namespace Mcp.Benchmark.Core.Constants;

public static class AssuranceProfileCatalog
{
    public const string Revision = "2026-11-05.1";

    public static AssuranceProfileManifest Inventory { get; } = new()
    {
        ProfileId = "inventory",
        Revision = Revision,
        MinimumExecutionMode = ExecutionMode.Safe,
        MinimumEvidenceCoverageRatio = 0,
        MinimumEvidenceConfidenceRatio = 0,
        MinimumRepeatedRunConsistency = 0,
        CertificateEligible = false
    };

    public static AssuranceProfileManifest Ci { get; } = new()
    {
        ProfileId = "ci",
        Revision = Revision,
        MinimumExecutionMode = ExecutionMode.Standard,
        MinimumEvidenceCoverageRatio = 0.8,
        MinimumEvidenceConfidenceRatio = 0.8,
        MinimumRepeatedRunConsistency = 0,
        CertificateEligible = false
    };

    public static AssuranceProfileManifest Provider { get; } = new()
    {
        ProfileId = "provider",
        Revision = Revision,
        MinimumExecutionMode = ExecutionMode.Standard,
        MinimumEvidenceCoverageRatio = 0.9,
        MinimumEvidenceConfidenceRatio = 0.9,
        MinimumRepeatedRunConsistency = 0.99,
        RequiresOfficialConformance = true,
        RequiresSignedAttestation = true,
        RequiresRepeatedRuns = true,
        CertificateEligible = true
    };

    public static AssuranceProfileManifest Resolve(string? profileId) => profileId?.Trim().ToLowerInvariant() switch
    {
        null or "" or "inventory" => Inventory,
        "ci" => Ci,
        "provider" => Provider,
        _ => throw new ArgumentException($"Unknown assurance profile '{profileId}'. Supported profiles: inventory, ci, provider.", nameof(profileId))
    };
}