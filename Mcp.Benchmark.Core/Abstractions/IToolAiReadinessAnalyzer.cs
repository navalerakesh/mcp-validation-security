using Mcp.Benchmark.Core.Models;

namespace Mcp.Benchmark.Core.Abstractions;

/// <summary>
/// Evaluates MCP tool metadata for AI-agent usability heuristics.
/// </summary>
public interface IToolAiReadinessAnalyzer
{
    ToolAiReadinessAnalysis AnalyzeCatalog(IReadOnlyCollection<ToolAiReadinessTarget> tools, string? rawJson, long? totalPayloadChars = null);

    ToolErrorAiReadinessAssessment AnalyzeErrorResponse(string toolName, string rawJson, int errorCode, string errorMessage);
}