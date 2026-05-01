using System.Text.Json.Serialization;
using Mcp.Benchmark.Core.Models;

namespace Mcp.Benchmark.CLI.Models;

public sealed class ExecutionPlan
{
    public string CommandName { get; init; } = "validate";

    public string SessionId { get; init; } = string.Empty;

    public string Target { get; init; } = string.Empty;

    public string Transport { get; init; } = string.Empty;

    public ExecutionMode ExecutionMode { get; init; }

    public bool DryRun { get; init; }

    public PersistenceMode PersistenceMode { get; init; }

    public RedactionLevel RedactionLevel { get; init; }

    public TraceMode TraceMode { get; init; }

    public int MaxRequests { get; init; }

    public int MaxConcurrency { get; init; }

    public int TimeoutSeconds { get; init; }

    public bool AllowPrivateAddresses { get; init; }

    public bool RequiresElevatedRiskAcknowledgement { get; init; }

    public bool ElevatedRiskAcknowledged { get; init; }

    public string? OutputDirectory { get; init; }

    public bool SessionArtifactsEnabled { get; init; }

    public bool SessionLogsEnabled { get; init; }

    public bool ModelEvaluationEnabled { get; init; }

    public IReadOnlyList<string> AllowedHosts { get; init; } = Array.Empty<string>();

    public IReadOnlyList<string> SelectedClientProfiles { get; init; } = Array.Empty<string>();

    public IReadOnlyList<string> PlannedChecks { get; init; } = Array.Empty<string>();

    public IReadOnlyList<string> PlannedArtifacts { get; init; } = Array.Empty<string>();

    public IReadOnlyList<string> ValidationErrors { get; init; } = Array.Empty<string>();

    public bool IsValid => ValidationErrors.Count == 0;
}

public sealed class AuditManifest
{
    public DateTimeOffset TimestampUtc { get; init; } = DateTimeOffset.UtcNow;

    public string CommandName { get; init; } = "validate";

    public string ValidationId { get; init; } = string.Empty;

    public string SessionId { get; init; } = string.Empty;

    public string Target { get; init; } = string.Empty;

    public string Transport { get; init; } = string.Empty;

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

    public IReadOnlyList<string> AllowedHosts { get; init; } = Array.Empty<string>();

    public IReadOnlyList<string> PlannedChecks { get; init; } = Array.Empty<string>();

    public IReadOnlyList<string> ExecutedChecks { get; init; } = Array.Empty<string>();

    public IReadOnlyList<string> ArtifactPaths { get; init; } = Array.Empty<string>();

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public ValidationStatus? OverallStatus { get; init; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public ValidationVerdict? BaselineVerdict { get; init; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public ValidationVerdict? ProtocolVerdict { get; init; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public ValidationVerdict? CoverageVerdict { get; init; }
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
    public DateTimeOffset TimestampUtc { get; init; } = DateTimeOffset.UtcNow;

    public string ValidationId { get; init; } = string.Empty;

    public string SessionId { get; init; } = string.Empty;

    public string Provider { get; init; } = string.Empty;

    public string? Model { get; init; }

    public string? PromptSet { get; init; }

    public ModelEvaluationArtifactStatus Status { get; init; }

    public string Summary { get; init; } = string.Empty;

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public ValidationVerdict? BaselineVerdict { get; init; }

    public IReadOnlyList<string> AdvisoryNotes { get; init; } = Array.Empty<string>();

    public IReadOnlyList<ModelEvaluationFindingLink> RelatedDeterministicFindings { get; init; } = Array.Empty<ModelEvaluationFindingLink>();
}

public sealed class ModelEvaluationFindingLink
{
    public string RuleId { get; init; } = string.Empty;

    public string Category { get; init; } = string.Empty;

    public string Component { get; init; } = string.Empty;

    public string EvidenceKind { get; init; } = string.Empty;

    public string Summary { get; init; } = string.Empty;

    public string? Recommendation { get; init; }
}