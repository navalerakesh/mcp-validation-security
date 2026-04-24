using System.Text.Json.Serialization;

namespace Mcp.Benchmark.Core.Models;

/// <summary>
/// Safe-by-default execution modes for validator runs.
/// </summary>
[JsonConverter(typeof(JsonStringEnumConverter))]
public enum ExecutionMode
{
    Safe,
    Standard,
    Elevated
}

/// <summary>
/// Persistence policy for operational artifacts produced by the CLI.
/// </summary>
[JsonConverter(typeof(JsonStringEnumConverter))]
public enum PersistenceMode
{
    Ephemeral,
    ExplicitOutput,
    Session
}

/// <summary>
/// Trace capture mode for operational diagnostics.
/// </summary>
[JsonConverter(typeof(JsonStringEnumConverter))]
public enum TraceMode
{
    Off,
    Redacted,
    Full
}

/// <summary>
/// Redaction strength applied to operational logs and artifacts.
/// </summary>
[JsonConverter(typeof(JsonStringEnumConverter))]
public enum RedactionLevel
{
    Strict,
    Standard
}

/// <summary>
/// Shared execution policy enforced by the CLI and transport surfaces.
/// </summary>
public sealed class ExecutionPolicy
{
    /// <summary>
    /// Gets or sets the execution mode.
    /// </summary>
    [JsonPropertyName("mode")]
    public ExecutionMode Mode { get; set; } = ExecutionMode.Safe;

    /// <summary>
    /// Gets or sets whether the run should stop after printing the pre-flight plan.
    /// </summary>
    [JsonPropertyName("dryRun")]
    public bool DryRun { get; set; }

    /// <summary>
    /// Gets or sets the allowed destination hosts for outbound HTTP requests.
    /// </summary>
    [JsonPropertyName("allowedHosts")]
    public List<string> AllowedHosts { get; set; } = new();

    /// <summary>
    /// Gets or sets whether private or loopback addresses may be contacted.
    /// </summary>
    [JsonPropertyName("allowPrivateAddresses")]
    public bool AllowPrivateAddresses { get; set; }

    /// <summary>
    /// Gets or sets the maximum number of outbound requests allowed for the run.
    /// </summary>
    [JsonPropertyName("maxRequests")]
    public int MaxRequests { get; set; } = 100;

    /// <summary>
    /// Gets or sets the maximum concurrency allowed for the run.
    /// </summary>
    [JsonPropertyName("maxConcurrency")]
    public int MaxConcurrency { get; set; } = 4;

    /// <summary>
    /// Gets or sets the request timeout budget in seconds.
    /// </summary>
    [JsonPropertyName("timeoutSeconds")]
    public int TimeoutSeconds { get; set; } = 120;

    /// <summary>
    /// Gets or sets the operational persistence mode.
    /// </summary>
    [JsonPropertyName("persistenceMode")]
    public PersistenceMode PersistenceMode { get; set; } = PersistenceMode.Ephemeral;

    /// <summary>
    /// Gets or sets the redaction level for logs and operational artifacts.
    /// </summary>
    [JsonPropertyName("redactLevel")]
    public RedactionLevel RedactLevel { get; set; } = RedactionLevel.Strict;

    /// <summary>
    /// Gets or sets the trace capture mode.
    /// </summary>
    [JsonPropertyName("traceMode")]
    public TraceMode TraceMode { get; set; } = TraceMode.Off;

    /// <summary>
    /// Gets or sets whether the operator explicitly acknowledged elevated-risk execution.
    /// </summary>
    [JsonPropertyName("confirmElevatedRisk")]
    public bool ConfirmElevatedRisk { get; set; }

    /// <summary>
    /// Creates a detached copy of the execution policy.
    /// </summary>
    public ExecutionPolicy Clone()
    {
        return new ExecutionPolicy
        {
            Mode = Mode,
            DryRun = DryRun,
            AllowedHosts = new List<string>(AllowedHosts),
            AllowPrivateAddresses = AllowPrivateAddresses,
            MaxRequests = MaxRequests,
            MaxConcurrency = MaxConcurrency,
            TimeoutSeconds = TimeoutSeconds,
            PersistenceMode = PersistenceMode,
            RedactLevel = RedactLevel,
            TraceMode = TraceMode,
            ConfirmElevatedRisk = ConfirmElevatedRisk
        };
    }
}

/// <summary>
/// Optional evaluation overlays that must remain separate from deterministic validation results.
/// </summary>
public sealed class EvaluationPolicy
{
    /// <summary>
    /// Gets or sets the optional experimental model evaluation configuration.
    /// </summary>
    [JsonPropertyName("modelEvaluation")]
    public ModelEvaluationPolicy ModelEvaluation { get; set; } = new();

    /// <summary>
    /// Creates a detached copy of the evaluation policy.
    /// </summary>
    public EvaluationPolicy Clone()
    {
        return new EvaluationPolicy
        {
            ModelEvaluation = ModelEvaluation.Clone()
        };
    }
}

/// <summary>
/// Provider-neutral configuration for experimental model evaluation.
/// </summary>
public sealed class ModelEvaluationPolicy
{
    /// <summary>
    /// Gets or sets whether model evaluation is enabled.
    /// </summary>
    [JsonPropertyName("enabled")]
    public bool Enabled { get; set; }

    /// <summary>
    /// Gets or sets the provider identifier.
    /// </summary>
    [JsonPropertyName("provider")]
    public string Provider { get; set; } = "none";

    /// <summary>
    /// Gets or sets the model identifier when a provider is configured.
    /// </summary>
    [JsonPropertyName("model")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Model { get; set; }

    /// <summary>
    /// Gets or sets the prompt or rubric set identifier used for the evaluation.
    /// </summary>
    [JsonPropertyName("promptSet")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? PromptSet { get; set; }

    /// <summary>
    /// Gets or sets provider-specific metadata required by the experimental lane.
    /// </summary>
    [JsonPropertyName("metadata")]
    public Dictionary<string, string> Metadata { get; set; } = new();

    /// <summary>
    /// Creates a detached copy of the model evaluation policy.
    /// </summary>
    public ModelEvaluationPolicy Clone()
    {
        return new ModelEvaluationPolicy
        {
            Enabled = Enabled,
            Provider = Provider,
            Model = Model,
            PromptSet = PromptSet,
            Metadata = new Dictionary<string, string>(Metadata)
        };
    }
}