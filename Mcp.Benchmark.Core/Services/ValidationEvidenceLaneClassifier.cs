using Mcp.Benchmark.Core.Models;

namespace Mcp.Benchmark.Core.Services;

public static class ValidationEvidenceLaneClassifier
{
    public static bool IsBaseline(ValidationCoverageDeclaration coverage) =>
        coverage.Lane is null or EvaluationLane.Baseline;

    public static IEnumerable<ValidationCoverageDeclaration> BaselineOnly(
        IEnumerable<ValidationCoverageDeclaration> coverage) =>
        coverage.Where(IsBaseline);
}