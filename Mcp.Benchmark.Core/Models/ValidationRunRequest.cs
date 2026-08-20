using System.Text.Json;

namespace Mcp.Benchmark.Core.Models;

public sealed class ValidationRunRequest
{
    private readonly byte[] _configurationUtf8;

    private ValidationRunRequest(McpValidatorConfiguration configuration, string? requestId)
    {
        RequestId = string.IsNullOrWhiteSpace(requestId) ? Guid.NewGuid().ToString("N") : requestId;
        Target = configuration.Server.Endpoint ?? string.Empty;
        Transport = configuration.Server.Transport;
        OperationPolicy = OperationPolicySnapshot.From(configuration.Execution ?? new ExecutionPolicy());
        _configurationUtf8 = JsonSerializer.SerializeToUtf8Bytes(configuration);
    }

    public string RequestId { get; }

    public string Target { get; }

    public string Transport { get; }

    public OperationPolicySnapshot OperationPolicy { get; }

    public static ValidationRunRequest Capture(McpValidatorConfiguration configuration, string? requestId = null)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        ArgumentNullException.ThrowIfNull(configuration.Server);
        return new ValidationRunRequest(configuration, requestId);
    }

    public McpValidatorConfiguration CreateConfiguration()
    {
        return JsonSerializer.Deserialize<McpValidatorConfiguration>(_configurationUtf8)
            ?? throw new InvalidOperationException("The captured validation configuration could not be materialized.");
    }
}