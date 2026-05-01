using System.Text.Json.Serialization;

namespace Mcp.Benchmark.Core.Models;

public enum StdioTransportProbeKind
{
    MessageExchange = 0,
    ShutdownLifecycle = 1
}

public sealed class StdioTransportProbeRequest
{
    public required string Endpoint { get; init; }

    public required string ProbeId { get; init; }

    public StdioTransportProbeKind Kind { get; init; } = StdioTransportProbeKind.MessageExchange;

    public string? RawMessage { get; init; }

    public int ResponseTimeoutMs { get; init; } = 10_000;
}

public sealed class StdioTransportProbeResponse
{
    public required string ProbeId { get; init; }

    public StdioTransportProbeKind Kind { get; init; }

    public int StatusCode { get; init; }

    public bool IsSuccess { get; init; }

    public bool Executed { get; init; }

    public string? RawStdout { get; init; }

    public string? StderrPreview { get; init; }

    public string? Error { get; init; }

    public double? ElapsedMs { get; init; }

    public bool? ProcessExited { get; init; }

    public bool? Restarted { get; init; }

    public string? ShutdownMode { get; init; }

    public ProbeContext? ProbeContext { get; init; }

    public Dictionary<string, string> Metadata { get; init; } = new(StringComparer.OrdinalIgnoreCase);
}

public sealed class StdioTransportTestResult
{
    public string ProtocolVersion { get; set; } = string.Empty;

    public List<StdioTransportProbeResult> Probes { get; set; } = new();

    public int MandatoryProbeCount => Probes.Count(probe => probe.Mandatory);

    public int EvaluatedMandatoryProbeCount => Probes.Count(probe => probe.Mandatory && probe.Passed.HasValue);

    public int FailedMandatoryProbeCount => Probes.Count(probe => probe.Mandatory && probe.Passed == false);

    public double MandatoryComplianceScore => EvaluatedMandatoryProbeCount == 0
        ? 0.0
        : Probes.Count(probe => probe.Mandatory && probe.Passed == true) / (double)EvaluatedMandatoryProbeCount * 100.0;
}

public sealed class StdioTransportProbeResult
{
    public required string ProbeId { get; init; }

    public required string CheckId { get; init; }

    public bool Mandatory { get; init; } = true;

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public bool? Passed { get; init; }

    public ViolationSeverity Severity { get; init; } = ViolationSeverity.High;

    public required string Requirement { get; init; }

    public required string Expected { get; init; }

    public required string Actual { get; init; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public int? StatusCode { get; init; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? StdoutPreview { get; init; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? StderrPreview { get; init; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public ProbeContext? ProbeContext { get; init; }

    public Dictionary<string, string> Metadata { get; init; } = new(StringComparer.OrdinalIgnoreCase);
}