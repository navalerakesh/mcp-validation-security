using System.Text.Json.Serialization;
using Mcp.Benchmark.Core.Constants;
using Mcp.Benchmark.Core.Models;

namespace Mcp.Benchmark.CLI.Models;

public sealed class CliResultEnvelope
{
    public string DocumentType { get; init; } = ArtifactContracts.CliResultDocumentType;

    public string DocumentSchemaVersion { get; init; } = ArtifactContracts.SchemaVersion;

    public int ExitCode { get; init; }

    public required string CommandName { get; init; }

    public required object Result { get; init; }
}

public sealed class CliErrorEnvelope
{
    public string DocumentType { get; init; } = ArtifactContracts.CliErrorDocumentType;

    public string DocumentSchemaVersion { get; init; } = ArtifactContracts.SchemaVersion;

    public required string Code { get; init; }

    public required string Category { get; init; }

    public required string Message { get; init; }

    public int ExitCode { get; init; }

    public bool Retryable { get; init; }
}

public sealed class ValidationAttestation
{
    public string DocumentType { get; init; } = ArtifactContracts.ValidationAttestationDocumentType;

    public string DocumentSchemaVersion { get; init; } = ArtifactContracts.SchemaVersion;

    public string ValidationId { get; init; } = string.Empty;

    public DateTimeOffset IssuedUtc { get; init; } = DateTimeOffset.UtcNow;

    public string SubjectFile { get; init; } = string.Empty;

    public string SubjectSha256 { get; init; } = string.Empty;

    public string Algorithm { get; init; } = "ECDSA_P256_SHA256";

    public string KeyId { get; init; } = string.Empty;

    public string PublicKeyPem { get; init; } = string.Empty;

    public string SignatureBase64 { get; init; } = string.Empty;
}

public sealed class ExecutionPlan
{
    public string ValidatorDigest { get; init; } = string.Empty;

    public string ConfigDigest { get; init; } = string.Empty;

    public string TargetDigest { get; init; } = string.Empty;

    public string CommandName { get; init; } = "validate";

    public string SessionId { get; init; } = string.Empty;

    public string Target { get; init; } = string.Empty;

    public string Transport { get; init; } = string.Empty;

    public string RequestedProtocolProfile { get; init; } = "latest";

    public string ResolvedSchemaVersion { get; init; } = string.Empty;

    public McpProtocolEra ProtocolEra { get; init; }

    public McpProtocolEraSelection ProtocolEraSelection { get; init; }

    public ExecutionMode ExecutionMode { get; init; }

    public bool DryRun { get; init; }

    public PersistenceMode PersistenceMode { get; init; }

    public RedactionLevel RedactionLevel { get; init; }

    public TraceMode TraceMode { get; init; }

    public int MaxRequests { get; init; }

    public int MaxConcurrency { get; init; }

    public int TimeoutSeconds { get; init; }

    public int MaxResponseBytes { get; init; } = ExecutionPolicyDefaults.DefaultMaxResponseBytes;

    public bool AllowPrivateAddresses { get; init; }

    public bool RequiresElevatedRiskAcknowledgement { get; init; }

    public bool ElevatedRiskAcknowledged { get; init; }

    public string? OutputDirectory { get; init; }

    public bool SessionArtifactsEnabled { get; init; }

    public bool SessionLogsEnabled { get; init; }

    public bool ModelEvaluationEnabled { get; init; }

    public IReadOnlyList<string> AllowedHosts { get; init; } = Array.Empty<string>();

    public IReadOnlyList<string> AllowedOrigins { get; init; } = Array.Empty<string>();

    public IReadOnlyList<string> SelectedClientProfiles { get; init; } = Array.Empty<string>();

    public IReadOnlyList<string> PlannedChecks { get; init; } = Array.Empty<string>();

    public IReadOnlyList<string> PlannedArtifacts { get; init; } = Array.Empty<string>();

    public IReadOnlyList<string> ValidationErrors { get; init; } = Array.Empty<string>();

    public bool IsValid => ValidationErrors.Count == 0;
}

public sealed class AuditManifest
{
    public string DocumentType { get; init; } = ArtifactContracts.AuditManifestDocumentType;

    public string DocumentSchemaVersion { get; init; } = ArtifactContracts.AuditManifestSchemaVersion;

    public DateTimeOffset TimestampUtc { get; init; } = DateTimeOffset.UtcNow;

    public string CommandName { get; init; } = "validate";

    public string ValidationId { get; init; } = string.Empty;

    public string SessionId { get; init; } = string.Empty;

    public string Target { get; init; } = string.Empty;

    public string Transport { get; init; } = string.Empty;

    public string RequestedProtocolProfile { get; init; } = "latest";

    public string ResolvedSchemaVersion { get; init; } = string.Empty;

    public McpProtocolEra ProtocolEra { get; init; }

    public McpProtocolEraSelection ProtocolEraSelection { get; init; }

    public ExecutionMode ExecutionMode { get; init; }

    public bool DryRun { get; init; }

    public PersistenceMode PersistenceMode { get; init; }

    public RedactionLevel RedactionLevel { get; init; }

    public TraceMode TraceMode { get; init; }

    public bool ModelEvaluationEnabled { get; init; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? ModelEvaluationStatus { get; init; }

    public bool AllowPrivateAddresses { get; init; }

    public int MaxRequests { get; init; }

    public int MaxConcurrency { get; init; }

    public int TimeoutSeconds { get; init; }

    public int MaxResponseBytes { get; init; } = ExecutionPolicyDefaults.DefaultMaxResponseBytes;

    public IReadOnlyList<string> AllowedHosts { get; init; } = Array.Empty<string>();

    public IReadOnlyList<string> AllowedOrigins { get; init; } = Array.Empty<string>();

    public IReadOnlyList<string> PlannedChecks { get; init; } = Array.Empty<string>();

    public IReadOnlyList<string> ExecutedChecks { get; init; } = Array.Empty<string>();

    public IReadOnlyList<string> ArtifactPaths { get; init; } = Array.Empty<string>();

    public AuditDigestSet Digests { get; init; } = new();

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public EvidenceCoverageSummary? EvidenceCompleteness { get; init; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public ValidationStatus? OverallStatus { get; init; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public ValidationVerdict? BaselineVerdict { get; init; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public ValidationVerdict? ProtocolVerdict { get; init; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public ValidationVerdict? CoverageVerdict { get; init; }
}

public sealed class AuditDigestSet
{
    public string ValidatorSha256 { get; init; } = string.Empty;

    public string RuleCatalogSha256 { get; init; } = string.Empty;

    public string ProfileCatalogSha256 { get; init; } = string.Empty;

    public string ConfigSha256 { get; init; } = string.Empty;

    public string TargetIdentitySha256 { get; init; } = string.Empty;

    public IReadOnlyDictionary<string, string> ArtifactsSha256 { get; init; } = new SortedDictionary<string, string>(StringComparer.Ordinal);
}

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum ModelEvaluationArtifactStatus
{
    Skipped,
    Completed,
    Failed
}

public sealed class ModelEvaluationArtifact
{
    public string DocumentType { get; init; } = ArtifactContracts.ModelEvaluationDocumentType;

    public string DocumentSchemaVersion { get; init; } = ArtifactContracts.ModelEvaluationSchemaVersion;

    public DateTimeOffset TimestampUtc { get; init; } = DateTimeOffset.UtcNow;

    public string ValidationId { get; init; } = string.Empty;

    public string SessionId { get; init; } = string.Empty;

    public string Provider { get; init; } = string.Empty;

    public string? Model { get; init; }

    public string? PromptSet { get; init; }

    public ModelEvaluationArtifactStatus Status { get; init; }

    public ModelEvaluationCost Cost { get; init; } = new();

    public string Summary { get; init; } = string.Empty;

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public ValidationVerdict? BaselineVerdict { get; init; }

    public IReadOnlyList<string> AdvisoryNotes { get; init; } = Array.Empty<string>();

    public IReadOnlyList<ModelEvaluationFindingLink> RelatedDeterministicFindings { get; init; } = Array.Empty<ModelEvaluationFindingLink>();
}

public sealed class ModelEvaluationCost
{
    public const string CurrentVersion = "1.0.0";

    public string ContractVersion { get; init; } = CurrentVersion;

    public long InputTokens { get; init; }

    public long OutputTokens { get; init; }

    public decimal Amount { get; init; }

    public string Currency { get; init; } = "USD";

    public bool Estimated { get; init; }
}

public sealed class ModelEvaluationInput
{
    public string ValidationId { get; init; } = string.Empty;

    public ValidationVerdict BaselineVerdict { get; init; } = ValidationVerdict.Unknown;

    public IReadOnlyList<ModelEvaluationDecisionInput> BlockingDecisions { get; init; } = Array.Empty<ModelEvaluationDecisionInput>();

    public IReadOnlyList<ModelEvaluationClientInput> ClientProfiles { get; init; } = Array.Empty<ModelEvaluationClientInput>();

    public IReadOnlyList<ModelEvaluationFindingInput> AiFindings { get; init; } = Array.Empty<ModelEvaluationFindingInput>();
}

public sealed record ModelEvaluationDecisionInput(
    string DecisionId,
    string? RuleId,
    GateOutcome Gate,
    ValidationFindingSeverity Severity,
    string Category,
    string Component);

public sealed record ModelEvaluationClientInput(string ProfileId, ClientProfileCompatibilityStatus Status);

public sealed record ModelEvaluationFindingInput(
    string RuleId,
    string Category,
    string Component,
    ValidationFindingSeverity Severity,
    string EvidenceKind);

public sealed class ModelEvaluationFindingLink
{
    public string RuleId { get; init; } = string.Empty;

    public string Category { get; init; } = string.Empty;

    public string Component { get; init; } = string.Empty;

    public string EvidenceKind { get; init; } = string.Empty;

    public string Summary { get; init; } = string.Empty;

    public string? Recommendation { get; init; }
}