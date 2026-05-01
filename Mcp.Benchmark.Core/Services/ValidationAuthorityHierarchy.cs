using Mcp.Benchmark.Core.Models;

namespace Mcp.Benchmark.Core.Services;

public static class ValidationAuthorityHierarchy
{
    public const string Legend = "Authority order: Spec blocking and warnings, then Guideline, Heuristic, and Operational signals.";

    public static int GetSortOrder(ValidationRuleSource source)
    {
        return source switch
        {
            ValidationRuleSource.Spec => 0,
            ValidationRuleSource.Guideline => 1,
            ValidationRuleSource.Heuristic => 2,
            _ => 3
        };
    }

    public static string GetDisplayLabel(ValidationRuleSource source)
    {
        return source switch
        {
            ValidationRuleSource.Spec => "Spec",
            ValidationRuleSource.Guideline => "Guideline",
            ValidationRuleSource.Heuristic => "Heuristic",
            _ => "Operational"
        };
    }

    public static string GetMachineLabel(ValidationRuleSource source)
    {
        return source switch
        {
            ValidationRuleSource.Spec => "spec",
            ValidationRuleSource.Guideline => "guideline",
            ValidationRuleSource.Heuristic => "heuristic",
            _ => "operational"
        };
    }

    public static string FormatTag(ValidationRuleSource source) => $"[{GetDisplayLabel(source)}]";

    public static string GetDescription(ValidationRuleSource source)
    {
        return source switch
        {
            ValidationRuleSource.Spec => "Normative MCP specification requirement; fix before lower-authority findings.",
            ValidationRuleSource.Guideline => "MCP guidance or recommended practice; fix after spec issues and before heuristics.",
            ValidationRuleSource.Heuristic => "Validator advisory or AI-safety heuristic; review after spec and guideline items.",
            _ => "Operational validator/runtime signal; review after protocol, guidance, and heuristic findings."
        };
    }
}
