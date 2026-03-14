using System.Threading;

namespace Mcp.Benchmark.Core.Abstractions;

/// <summary>
/// Provides a host-agnostic way to persist transient validation artifacts (JSON payloads, telemetry snapshots, etc.).
/// </summary>
public interface ISessionArtifactStore
{
    /// <summary>
    /// Saves the payload as a JSON artifact and throws if the operation fails.
    /// </summary>
    /// <param name="artifactName">Logical artifact identifier (will be sanitized for file names).</param>
    /// <param name="payload">Serializable payload object.</param>
    /// <param name="cancellationToken">Optional cancellation token.</param>
    /// <returns>Absolute path or identifier of the stored artifact.</returns>
    string SaveJson(string artifactName, object payload, CancellationToken cancellationToken = default);

    /// <summary>
    /// Attempts to save the payload as a JSON artifact without throwing. Returns null if persistence fails.
    /// </summary>
    /// <param name="artifactName">Logical artifact identifier.</param>
    /// <param name="payload">Serializable payload object.</param>
    /// <param name="cancellationToken">Optional cancellation token.</param>
    /// <returns>Absolute path/identifier when successful; otherwise null.</returns>
    string? TrySaveJson(string artifactName, object payload, CancellationToken cancellationToken = default);
}
