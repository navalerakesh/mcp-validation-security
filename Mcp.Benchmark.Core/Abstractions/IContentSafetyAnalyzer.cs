using Mcp.Benchmark.Core.Models;

namespace Mcp.Benchmark.Core.Abstractions;

/// <summary>
/// Analyzes MCP tools, resources, and prompts using static metadata
/// (names, descriptions, URIs, argument shapes) to identify potential
/// content and behavior risks. This analyzer is intentionally
/// server-agnostic and does not perform live content execution.
/// </summary>
public interface IContentSafetyAnalyzer
{
    /// <summary>
    /// Performs a static, metadata-only risk assessment for a tool.
    /// </summary>
    /// <param name="toolName">Logical tool name as exposed via tools/list.</param>
    /// <returns>Zero or more content safety findings for the tool.</returns>
    IReadOnlyList<ContentSafetyFinding> AnalyzeTool(string toolName);

    /// <summary>
    /// Performs a static, metadata-only risk assessment for a resource.
    /// </summary>
    /// <param name="resourceName">Logical resource name, if available.</param>
    /// <param name="resourceUri">Resource URI or identifier.</param>
    /// <returns>Zero or more content safety findings for the resource.</returns>
    IReadOnlyList<ContentSafetyFinding> AnalyzeResource(string? resourceName, string resourceUri);

    /// <summary>
    /// Performs a static, metadata-only risk assessment for a prompt.
    /// </summary>
    /// <param name="promptName">Logical prompt name.</param>
    /// <param name="description">Optional human description.</param>
    /// <param name="argumentsCount">Number of declared arguments.</param>
    /// <returns>Zero or more content safety findings for the prompt.</returns>
    IReadOnlyList<ContentSafetyFinding> AnalyzePrompt(string promptName, string? description, int argumentsCount);
}
