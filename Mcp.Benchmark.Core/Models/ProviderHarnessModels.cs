namespace Mcp.Benchmark.Core.Models;

public enum ProviderAuthorizationVerificationStatus
{
    Verified,
    Unverified,
    Invalid
}

public enum ProviderHarnessStatus
{
    Verified,
    Incomplete,
    Invalid
}

public sealed class ProviderAuthorizationGrant
{
    public required string GrantId { get; init; }

    public required string Authority { get; init; }

    public required string TargetIdentitySha256 { get; init; }

    public required DateTimeOffset ValidFromUtc { get; init; }

    public required DateTimeOffset ExpiresAtUtc { get; init; }

    public IReadOnlyList<ExecutionMode> AllowedExecutionModes { get; init; } = Array.Empty<ExecutionMode>();

    public IReadOnlyList<string> AllowedProbeClasses { get; init; } = Array.Empty<string>();

    public required string GrantDocumentSha256 { get; init; }

    public ProviderAuthorizationVerificationStatus VerificationStatus { get; init; }
}

public sealed class ProviderHarnessRunReceipt
{
    public required string RunId { get; init; }

    public required string ResultSha256 { get; init; }

    public required string ExecutionPlanSha256 { get; init; }

    public required DateTimeOffset StartedAtUtc { get; init; }

    public required DateTimeOffset EndedAtUtc { get; init; }

    public required CoreConformanceStatus CoreConformance { get; init; }

    public required SecurityAssuranceStatus SecurityAssurance { get; init; }

    public required OperationalReadinessStatus OperationalReadiness { get; init; }

    public IReadOnlyList<string> NonSuccessClaimIds { get; init; } = Array.Empty<string>();
}

public sealed class ProviderHarnessManifest
{
    public string DocumentType { get; init; } = Constants.ArtifactContracts.ProviderHarnessDocumentType;

    public string DocumentSchemaVersion { get; init; } = Constants.ArtifactContracts.ProviderHarnessSchemaVersion;

    public required string ProviderId { get; init; }

    public required string TargetIdentitySha256 { get; init; }

    public required string ProtocolRevision { get; init; }

    public required ProviderAuthorizationGrant Authorization { get; init; }

    public int RequiredRunCount { get; init; } = 3;

    public double MinimumConsistency { get; init; } = 0.99;

    public IReadOnlyList<ProviderHarnessRunReceipt> Runs { get; init; } = Array.Empty<ProviderHarnessRunReceipt>();
}

public sealed class ProviderHarnessAssessment
{
    public ProviderHarnessStatus Status { get; init; } = ProviderHarnessStatus.Incomplete;

    public bool AuthorizationVerified { get; init; }

    public required string TargetIdentitySha256 { get; init; }

    public int AcceptedRunCount { get; init; }

    public double RepeatedRunConsistency { get; init; }

    public IReadOnlyList<string> Reasons { get; init; } = Array.Empty<string>();
}