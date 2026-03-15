using System.Text.Json.Serialization;

namespace Mcp.Benchmark.Core.Models;

/// <summary>
/// Represents a MCP specification profile (versioned test suite).
/// </summary>
public class SpecProfile
{
    /// <summary>
    /// Profile identifier (e.g., "2025-06-18", "2025-11-25", "latest").
    /// </summary>
    [JsonPropertyName("id")]
    public string Id { get; set; } = "2025-06-18";

    /// <summary>
    /// Human-friendly label.
    /// </summary>
    [JsonPropertyName("label")]
    public string Label { get; set; } = "MCP Spec 2025-06-18";

    /// <summary>
    /// Optional description or release notes.
    /// </summary>
    [JsonPropertyName("description")]
    public string? Description { get; set; }
        = "Baseline MCP profile (June 2025).";

    /// <summary>
    /// Link to spec tag or changelog.
    /// </summary>
    [JsonPropertyName("specUrl")]
    public string? SpecUrl { get; set; }
        = "https://modelcontextprotocol.io/specification/2025-06-18";

    /// <summary>
    /// Checks included in this profile (stable IDs).
    /// </summary>
    [JsonPropertyName("includedChecks")]
    public List<string> IncludedChecks { get; set; } = new();

    /// <summary>
    /// Checks explicitly excluded/NA for this profile.
    /// </summary>
    [JsonPropertyName("excludedChecks")]
    public List<string> ExcludedChecks { get; set; } = new();
}
