using System.Text.Json.Serialization;

namespace Mcp.Benchmark.Core.Models;

public sealed class HttpTransportProbeRequest
{
    public required string Endpoint { get; init; }

    public string Method { get; init; } = "POST";

    public string? Body { get; init; }

    public string? ContentType { get; init; } = "application/json";

    public Dictionary<string, string> Headers { get; init; } = new(StringComparer.OrdinalIgnoreCase);

    [JsonIgnore]
    public AuthenticationConfig? Authentication { get; init; }

    public bool IncludeDefaultAuthentication { get; init; } = true;

    public bool IncludeProtocolVersionHeader { get; init; } = true;

    public bool IncludeSessionIdHeader { get; init; } = true;

    public bool CaptureSessionId { get; init; }
}

public sealed class HttpTransportProbeResponse
{
    public int StatusCode { get; init; }

    public bool IsSuccess { get; init; }

    public string? Body { get; init; }

    public string? Error { get; init; }

    public string? ContentType { get; init; }

    public Dictionary<string, string> Headers { get; init; } = new(StringComparer.OrdinalIgnoreCase);

    public Dictionary<string, string> RequestHeaders { get; init; } = new(StringComparer.OrdinalIgnoreCase);

    public double? ElapsedMs { get; init; }

    public ProbeContext? ProbeContext { get; init; }

    public List<SseEventRecord> SseEvents { get; init; } = new();
}

public sealed class StreamableHttpTransportTestResult
{
    public string ProtocolVersion { get; set; } = string.Empty;

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? SessionId { get; set; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public bool? SessionIdVisibleAscii { get; set; }

    public List<StreamableHttpTransportProbeResult> Probes { get; set; } = new();

    public int MandatoryProbeCount => Probes.Count(probe => probe.Mandatory);

    public int EvaluatedMandatoryProbeCount => Probes.Count(probe => probe.Mandatory && probe.Passed.HasValue);

    public int FailedMandatoryProbeCount => Probes.Count(probe => probe.Mandatory && probe.Passed == false);

    public double MandatoryComplianceScore => EvaluatedMandatoryProbeCount == 0
        ? 0.0
        : Probes.Count(probe => probe.Mandatory && probe.Passed == true) / (double)EvaluatedMandatoryProbeCount * 100.0;
}

public sealed class StreamableHttpTransportProbeResult
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
    public string? ContentType { get; init; }

    public Dictionary<string, string> RequestHeaders { get; init; } = new(StringComparer.OrdinalIgnoreCase);

    public Dictionary<string, string> ResponseHeaders { get; init; } = new(StringComparer.OrdinalIgnoreCase);

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? BodyPreview { get; init; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public ProbeContext? ProbeContext { get; init; }
}

public sealed class SseEventRecord
{
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Id { get; init; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Event { get; init; }

    public string Data { get; init; } = string.Empty;

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public int? RetryMilliseconds { get; init; }
}