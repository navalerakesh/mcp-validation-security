using System.Text.Json.Serialization;
using ModelContextProtocol.Protocol;

namespace Mcp.Benchmark.Core.Models;

public class JsonRpcRequest
{
    [JsonPropertyName("jsonrpc")]
    public string JsonRpc { get; set; } = "2.0";
    
    [JsonPropertyName("method")]
    public string Method { get; set; } = "";
    
    [JsonPropertyName("params")]
    public object? Params { get; set; }
    
    [JsonPropertyName("id")]
    public string Id { get; set; } = "";
}

public class JsonRpcResponse
{
    public int StatusCode { get; set; }
    public bool IsSuccess { get; set; }
    public string? RawJson { get; set; }
    public string? Error { get; set; }
    public Dictionary<string, string> Headers { get; set; } = new();

    /// <summary>
    /// Gets or sets the time taken by the underlying HTTP request/response
    /// cycle in milliseconds, excluding client-side backoff delays and
    /// scheduling overhead. May be null when timing is unavailable.
    /// </summary>
    public double? ElapsedMs { get; set; }
}

public class JsonRpcErrorTest
{
    public string Name { get; set; } = "";
    public string? Payload { get; set; }
    public int ExpectedErrorCode { get; set; }
    public JsonRpcResponse? ActualResponse { get; set; }
    public bool IsValid { get; set; }
}

public class JsonRpcErrorValidationResult
{
    public List<JsonRpcErrorTest> Tests { get; set; } = new();
    public double OverallScore { get; set; }
    public bool IsCompliant { get; set; }
}

public class TransportResilienceProbeResult
{
    public string ProbeId { get; set; } = string.Empty;

    public string DisplayName { get; set; } = string.Empty;

    public bool Executed { get; set; }

    public bool FailureObserved { get; set; }

    public bool GracefulRecovery { get; set; }

    public string? ExpectedOutcome { get; set; }

    public string? ActualOutcome { get; set; }

    public JsonRpcResponse? FailureResponse { get; set; }

    public JsonRpcResponse? RecoveryResponse { get; set; }

    public double FailureElapsedMs { get; set; }

    public double RecoveryElapsedMs { get; set; }

    public List<string> Notes { get; set; } = new();

    public bool HandledCorrectly => Executed && FailureObserved && GracefulRecovery;
}

