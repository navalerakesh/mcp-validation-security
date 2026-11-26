using Mcp.Benchmark.Core.Constants;
using Mcp.Benchmark.Core.Models;
using Mcp.Benchmark.Core.Services;

namespace Mcp.Benchmark.Tests.Fixtures;

public static class ReportQualityTestData
{
    public static readonly string[] Names =
    [
        "inventory", "mixed", "interrupted", "dense", "sparse", "ci",
        "comprehensive", "comprehensive-failed", "comprehensive-missing"
    ];

    public static ValidationResult Create(string name)
    {
        if (!Names.Contains(name, StringComparer.Ordinal))
            throw new ArgumentOutOfRangeException(nameof(name));

        var result = name is "mixed" or "dense"
            ? ReportSnapshotTestData.CreateComprehensiveResult()
            : new ValidationResult();
        result.ValidationId = $"synthetic-report-{name}-001";
        result.StartTime = new DateTime(2026, 4, 20, 10, 0, 0, DateTimeKind.Utc);
        result.EndTime = result.StartTime.AddSeconds(12);
        result.ServerConfig.Endpoint = $"https://{name}.example.invalid/mcp";
        result.ServerConfig.Transport = "http";
        result.Run.ExecutionMode = name is "inventory" or "interrupted" or "sparse" ? ExecutionMode.Safe : ExecutionMode.Standard;
        result.Run.TerminationOutcome = ValidationOutcome.Succeeded;
        result.ProtocolVersion = name == "sparse" ? null : "2025-11-25";
        result.Run.SchemaVersion = result.ProtocolVersion;
        var profile = name.StartsWith("comprehensive", StringComparison.Ordinal) ? AssuranceProfileCatalog.Comprehensive
            : name == "ci" ? AssuranceProfileCatalog.Ci
            : name is "mixed" or "dense" ? AssuranceProfileCatalog.Provider : AssuranceProfileCatalog.Inventory;
        result.ValidationConfig.Assurance.Profile = profile.ProfileId;
        if (result.Run.ExecutionMode == ExecutionMode.Safe)
        {
            var categories = result.ValidationConfig.Validation.Categories;
            categories.ToolTesting.TestToolExecution = false;
            categories.ToolTesting.TestParameterValidation = false;
            categories.ResourceTesting.TestResourceReading = false;
            categories.PromptTesting.TestPromptExecution = false;
        }

        if (name == "inventory")
        {
            result.ToolValidation = new ToolTestResult
            {
                Status = TestStatus.Passed,
                ToolsDiscovered = 3,
                ToolResults = Enumerable.Range(1, 3).Select(index => new IndividualToolResult
                {
                    ToolName = $"catalog-tool-{index}",
                    Description = "Synthetic catalog entry; runtime invocation was not attempted.",
                    Status = TestStatus.Skipped
                }).ToList()
            };
            result.Evidence.Coverage.Add(ValidationCoverageFactory.FromOutcome(
                "tool-surface", "tools/list", ValidationOutcome.Succeeded, "Three synthetic catalog entries recorded."));
            result.Evidence.Coverage.Add(ValidationCoverageFactory.FromOutcome(
                "tool-surface", "tools/call", ValidationOutcome.Skipped, "Inventory only; runtime invocation was not admitted."));
        }
        if (name == "interrupted")
        {
            result.Run.TerminationOutcome = ValidationOutcome.Cancelled;
            result.CriticalErrors.Add("Synthetic run cancelled during catalog discovery; runtime behavior remains unobserved.");
            result.Evidence.Coverage.Add(ValidationCoverageFactory.FromOutcome(
                "tool-surface", "tools/list", ValidationOutcome.Inconclusive, result.CriticalErrors[0]));
        }
        if (name == "dense")
        {
            for (var index = 0; index < 36; index++)
                result.Evidence.Coverage.Add(ValidationCoverageFactory.FromOutcome(
                    "synthetic-layout", $"long-scope-{index:D3}-{new string('x', 90)}",
                    ValidationOutcome.Unavailable,
                    "Synthetic layout evidence is unavailable. Literal untrusted text: <script>alert('inert')</script> & \"quoted\"."));
        }

        result.Assurance = AssuranceAssessmentEngine.Evaluate(result, profile,
            new AssuranceEvaluationContext { ExecutionMode = result.Run.ExecutionMode });
        if (name == "ci" || name.StartsWith("comprehensive", StringComparison.Ordinal))
        {
            var baseline = System.Text.Json.JsonSerializer.Deserialize<ValidationResult>(
                File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Fixtures", "Reports", "controlled-identity-seed.json")),
                Mcp.Benchmark.CLI.Utilities.ArtifactJsonOptions.Create())
                ?? throw new InvalidDataException("The signed report fixture must contain a canonical result.");
            result.ValidationId = baseline.ValidationId;
            result.StartTime = baseline.StartTime;
            result.EndTime = baseline.EndTime;
            result.Run.TargetIdentitySha256 = baseline.Run.TargetIdentitySha256;
            result.Assessments.Scenarios.AddRange(baseline.Assessments.Scenarios);
            result.Evidence.Coverage.Add(ValidationCoverageFactory.FromOutcome(
                "protocol", "synthetic-required-evidence", ValidationOutcome.Succeeded, "Synthetic saved assessment control."));
            result.Assurance = Unit.Services.AssuranceExitCodeMapperTests.CreateEvaluation(profile,
                name == "ci" ? AssuranceClaimOutcome.Satisfied : AssuranceClaimOutcome.Violated,
                name == "comprehensive-failed" ? AssuranceRequirementLevel.Must : AssuranceRequirementLevel.May,
                omittedBaseline: name == "comprehensive-missing" ? "selection" : null, retainedBaseline: baseline);
        }
        result.OverallStatus = name == "interrupted"
            ? ValidationStatus.Cancelled : AssuranceAssessmentEngine.DetermineValidationStatus(result.Assurance);
        result.Run.TerminationOutcome = name == "interrupted" ? ValidationOutcome.Cancelled : ValidationOutcome.Succeeded;
        result.PolicyOutcome = name == "sparse" ? null
            : ValidationPolicyEvaluator.Evaluate(result, new ValidationPolicyConfig { Mode = ValidationPolicyModes.Strict });
        return result;
    }
}
