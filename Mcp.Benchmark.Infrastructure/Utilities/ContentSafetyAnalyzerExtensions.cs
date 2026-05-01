using Mcp.Benchmark.Core.Abstractions;
using Mcp.Benchmark.Core.Models;

namespace Mcp.Benchmark.Infrastructure.Utilities;

internal static class ContentSafetyAnalyzerExtensions
{
    public static IReadOnlyList<ContentSafetyFinding> AnalyzeTool(
        this IContentSafetyAnalyzer analyzer,
        string toolName,
        ContentSafetyAnalysisContext context)
    {
        if (analyzer is IContextualContentSafetyAnalyzer contextualAnalyzer)
        {
            return contextualAnalyzer.AnalyzeTool(toolName, context);
        }

        return analyzer.AnalyzeTool(toolName);
    }

    public static IReadOnlyList<ContentSafetyFinding> AnalyzeResource(
        this IContentSafetyAnalyzer analyzer,
        string? resourceName,
        string resourceUri,
        ContentSafetyAnalysisContext context)
    {
        if (analyzer is IContextualContentSafetyAnalyzer contextualAnalyzer)
        {
            return contextualAnalyzer.AnalyzeResource(resourceName, resourceUri, context);
        }

        return analyzer.AnalyzeResource(resourceName, resourceUri);
    }

    public static IReadOnlyList<ContentSafetyFinding> AnalyzePrompt(
        this IContentSafetyAnalyzer analyzer,
        string promptName,
        string? description,
        int argumentsCount,
        ContentSafetyAnalysisContext context)
    {
        if (analyzer is IContextualContentSafetyAnalyzer contextualAnalyzer)
        {
            return contextualAnalyzer.AnalyzePrompt(promptName, description, argumentsCount, context);
        }

        return analyzer.AnalyzePrompt(promptName, description, argumentsCount);
    }
}
