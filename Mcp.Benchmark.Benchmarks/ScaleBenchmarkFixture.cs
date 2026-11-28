using Mcp.Benchmark.Core.Constants;
using Mcp.Benchmark.Core.Models;
using Mcp.Benchmark.Core.Services;

namespace Mcp.Benchmark.Benchmarks;

internal static class ScaleBenchmarkFixture
{
    public static ValidationResult CreateResult(int catalogItems)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(catalogItems);
        var claimCount = checked(catalogItems * 10);
        var result = new ValidationResult
        {
            ValidationId = "scale-fixture", StartTime = new DateTime(2026, 11, 24, 0, 0, 0, DateTimeKind.Utc),
            EndTime = new DateTime(2026, 11, 24, 0, 0, 1, DateTimeKind.Utc), OverallStatus = ValidationStatus.Passed,
            ToolValidation = new() { Status = TestStatus.Inconclusive, ToolsDiscovered = catalogItems }
        };
        for (var index = 0; index < catalogItems; index++)
            result.ToolValidation.ToolResults.Add(new()
            {
                ToolName = $"fixture-{index}", Status = TestStatus.Skipped,
                Description = "Synthetic scale fixture; <script>untrusted text</script>; no invocation."
            });
        for (var index = 0; index < claimCount; index++)
            result.Evidence.Coverage.Add(ValidationCoverageFactory.FromOutcome("tool-surface", $"fixture-obligation-{index}",
                index % 4 == 0 ? ValidationOutcome.Unavailable : index % 4 == 1 ? ValidationOutcome.Failed : ValidationOutcome.Succeeded));
        result.Assurance = AssuranceAssessmentEngine.Evaluate(result, AssuranceProfileCatalog.Inventory);
        if (result.Assurance.Claims.Count < claimCount)
            throw new InvalidOperationException("Scale fixture lost required observations.");
        return result;
    }
}
