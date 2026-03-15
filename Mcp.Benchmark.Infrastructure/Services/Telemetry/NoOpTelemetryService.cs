using Mcp.Benchmark.Core.Abstractions;

namespace Mcp.Benchmark.Infrastructure.Services.Telemetry;

/// <summary>
/// Default no-op implementation of telemetry service.
/// Used when no specific telemetry provider is configured.
/// </summary>
public class NoOpTelemetryService : ITelemetryService
{
    public void TrackEvent(string eventName, IDictionary<string, string>? properties = null, IDictionary<string, double>? metrics = null)
    {
        // No-op
    }

    public void TrackException(Exception exception, IDictionary<string, string>? properties = null)
    {
        // No-op
    }

    public void TrackMetric(string name, double value, IDictionary<string, string>? properties = null)
    {
        // No-op
    }

    public void TrackDependency(string dependencyName, string commandName, DateTimeOffset startTime, TimeSpan duration, bool success)
    {
        // No-op
    }
}
