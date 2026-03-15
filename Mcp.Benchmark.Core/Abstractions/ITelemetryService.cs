using System;
using System.Collections.Generic;

namespace Mcp.Benchmark.Core.Abstractions;

/// <summary>
/// Abstraction for telemetry and observability.
/// Allows for pluggable implementations (e.g., Application Insights, OpenTelemetry, Console)
/// without coupling core logic to specific telemetry SDKs.
/// </summary>
public interface ITelemetryService
{
    /// <summary>
    /// Tracks a discrete event (e.g., "ValidationStarted", "AttackSimulated").
    /// </summary>
    void TrackEvent(string eventName, IDictionary<string, string>? properties = null, IDictionary<string, double>? metrics = null);

    /// <summary>
    /// Tracks an exception or error.
    /// </summary>
    void TrackException(Exception exception, IDictionary<string, string>? properties = null);

    /// <summary>
    /// Tracks a numeric metric (e.g., "ResponseTime", "ComplianceScore").
    /// </summary>
    void TrackMetric(string name, double value, IDictionary<string, string>? properties = null);

    /// <summary>
    /// Tracks a dependency call (e.g., HTTP request to MCP server).
    /// </summary>
    void TrackDependency(string dependencyName, string commandName, DateTimeOffset startTime, TimeSpan duration, bool success);
}
