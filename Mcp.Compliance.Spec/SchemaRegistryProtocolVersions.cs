using System;
using System.Collections.Generic;
using System.Linq;

namespace Mcp.Compliance.Spec;

/// <summary>
/// Resolves MCP protocol versions against the embedded schema registry.
/// </summary>
public static class SchemaRegistryProtocolVersions
{
    private const string LatestAlias = "latest";
    private static readonly StringComparer Comparer = StringComparer.Ordinal;
    private static readonly Lazy<ISchemaRegistry> DefaultRegistry = new(() => new EmbeddedSchemaRegistry());

    /// <summary>
    /// MCP's backwards-compatible schema fallback when a negotiated version is unavailable.
    /// </summary>
    public static ProtocolVersion BackwardCompatibilityDefault => ProtocolVersions.V2025_03_26;

    /// <summary>
    /// Lists the distinct embedded protocol versions known to the schema registry in descending order.
    /// </summary>
    public static IReadOnlyList<ProtocolVersion> GetAvailableVersions(ISchemaRegistry? schemaRegistry = null)
    {
        var registry = schemaRegistry ?? DefaultRegistry.Value;

        try
        {
            var schemas = registry.ListSchemas();
            if (schemas == null)
            {
                return Array.Empty<ProtocolVersion>();
            }

            return schemas
                .Select(s => s.Version.Value)
                .Where(v => !string.IsNullOrWhiteSpace(v))
                .Select(v => v.Trim())
                .Distinct(Comparer)
                .OrderByDescending(v => v, Comparer)
                .Select(v => new ProtocolVersion(v))
                .ToArray();
        }
        catch
        {
            return Array.Empty<ProtocolVersion>();
        }
    }

    /// <summary>
    /// Returns the newest embedded MCP protocol version.
    /// </summary>
    public static ProtocolVersion GetLatestVersion(ISchemaRegistry? schemaRegistry = null)
    {
        var versions = GetAvailableVersions(schemaRegistry);
        return versions.Count > 0 ? versions[0] : BackwardCompatibilityDefault;
    }

    /// <summary>
    /// Normalizes a user-requested protocol version for outbound initialize handshakes.
    /// Null, empty, or the "latest" alias resolve to the newest embedded schema version.
    /// </summary>
    public static string NormalizeRequestedVersion(string? requestedVersion, ISchemaRegistry? schemaRegistry = null)
    {
        if (string.IsNullOrWhiteSpace(requestedVersion))
        {
            return GetLatestVersion(schemaRegistry).Value;
        }

        var normalized = requestedVersion.Trim();
        return string.Equals(normalized, LatestAlias, StringComparison.OrdinalIgnoreCase)
            ? GetLatestVersion(schemaRegistry).Value
            : normalized;
    }

    /// <summary>
    /// Resolves a negotiated protocol version to an embedded schema bundle.
    /// Unknown versions fall back to MCP's backwards-compatible default when available.
    /// </summary>
    public static ProtocolVersion ResolveSchemaVersion(string? negotiatedVersion, ISchemaRegistry? schemaRegistry = null)
    {
        if (string.IsNullOrWhiteSpace(negotiatedVersion))
        {
            return GetFallbackSchemaVersion(schemaRegistry);
        }

        var normalized = negotiatedVersion.Trim();
        if (string.Equals(normalized, LatestAlias, StringComparison.OrdinalIgnoreCase))
        {
            return GetLatestVersion(schemaRegistry);
        }

        var versions = GetAvailableVersions(schemaRegistry);
        return versions.Any(v => string.Equals(v.Value, normalized, StringComparison.Ordinal))
            ? new ProtocolVersion(normalized)
            : GetFallbackSchemaVersion(schemaRegistry);
    }

    /// <summary>
    /// Returns whether the provided protocol version has an embedded schema bundle.
    /// </summary>
    public static bool IsAvailableVersion(string? protocolVersion, ISchemaRegistry? schemaRegistry = null)
    {
        if (string.IsNullOrWhiteSpace(protocolVersion))
        {
            return false;
        }

        var normalized = protocolVersion.Trim();
        if (string.Equals(normalized, LatestAlias, StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        return GetAvailableVersions(schemaRegistry).Any(version => string.Equals(version.Value, normalized, StringComparison.Ordinal));
    }

    /// <summary>
    /// Returns the preferred fallback schema version when the negotiated version is unavailable.
    /// </summary>
    public static ProtocolVersion GetFallbackSchemaVersion(ISchemaRegistry? schemaRegistry = null)
    {
        var versions = GetAvailableVersions(schemaRegistry);
        return versions.Any(v => string.Equals(v.Value, BackwardCompatibilityDefault.Value, StringComparison.Ordinal))
            ? BackwardCompatibilityDefault
            : (versions.Count > 0 ? versions[0] : BackwardCompatibilityDefault);
    }
}