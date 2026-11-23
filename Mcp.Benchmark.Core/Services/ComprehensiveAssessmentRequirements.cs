using Mcp.Benchmark.Core.Constants;
using Mcp.Benchmark.Core.Models;

namespace Mcp.Benchmark.Core.Services;

public static class ComprehensiveAssessmentRequirements
{
    public static IReadOnlyList<string> GetPreflightErrors(McpValidatorConfiguration configuration, bool conformanceEvidenceProvided, bool planningOnly = false)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        if (AssuranceProfileCatalog.Resolve(configuration.Assurance?.Profile).ProfileId != AssuranceProfileCatalog.Comprehensive.ProfileId || planningOnly) return [];
        var errors = new List<string>();
        if (configuration.Execution?.DryRun == true)
            errors.Add("A dry-run plan cannot be submitted to the validation execution API.");
        if (configuration.Execution == null || !Enum.IsDefined(configuration.Execution.Mode) ||
            configuration.Execution.Mode < AssuranceProfileCatalog.Comprehensive.MinimumExecutionMode)
            errors.Add("Comprehensive assessment requires explicit standard or elevated execution admission. Use --assurance inventory for Safe discovery.");
        if (!conformanceEvidenceProvided)
            errors.Add("Comprehensive assessment requires pinned official conformance evidence (--conformance-results).");
        var categories = configuration.Validation?.Categories;
        if (categories == null)
            errors.Add("Comprehensive assessment requires all validation categories.");
        else
        {
            foreach (var categoryProperty in typeof(ValidationScenarios).GetProperties())
            {
                var category = categoryProperty.GetValue(categories);
                if (category == null)
                {
                    errors.Add($"Comprehensive assessment requires category {categoryProperty.Name}.");
                    continue;
                }
                if (category is PerformanceTestingConfig { TestMemoryUsage: true })
                    errors.Add("Target-memory assessment requires an operator-owned observer; this build has no target-memory observer admission contract.");
                if (category is ToolTestingConfig tools)
                {
                    try { ToolScenarioInputs.CapturePositiveScenarios(tools); }
                    catch (ArgumentException exception) { errors.Add($"ToolTesting.CustomScenarios cannot be executed: {exception.Message}"); }
                }
                if (category is PerformanceTestingConfig { CustomScenarios.Count: > 0 })
                    errors.Add($"{categoryProperty.Name}.CustomScenarios cannot be executed by this build; comprehensive assessment cannot silently omit explicit requests.");
                if (category is SecurityTestingConfig security)
                {
                    if (security.TestBufferOverflow)
                        errors.Add("Buffer-overflow assessment requires an explicitly admitted isolated stress harness, which is unavailable in this build.");
                    if (security.TestResourceExhaustion)
                        errors.Add("Resource-exhaustion assessment requires an explicitly admitted isolated stress harness, which is unavailable in this build.");
                }
                foreach (var flag in category.GetType().GetProperties().Where(property => property.PropertyType == typeof(bool) && property.Name.StartsWith("Test", StringComparison.Ordinal)))
                {
                    if (category is PerformanceTestingConfig && flag.Name == nameof(PerformanceTestingConfig.TestMemoryUsage)) continue;
                    if (category is SecurityTestingConfig && flag.Name is nameof(SecurityTestingConfig.TestBufferOverflow) or nameof(SecurityTestingConfig.TestResourceExhaustion)) continue;
                    if (flag.GetValue(category) is false)
                        errors.Add($"Comprehensive assessment cannot omit {categoryProperty.Name}.{flag.Name}; select an explicitly narrower profile instead.");
                }
            }
        }
        return errors;
    }
}