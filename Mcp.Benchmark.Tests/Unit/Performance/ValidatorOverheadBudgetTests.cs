using Mcp.Benchmark.CLI.Services;
using Mcp.Benchmark.CLI.Utilities;
using Mcp.Benchmark.Core.Constants;
using Mcp.Benchmark.Core.Models;
using Mcp.Benchmark.Core.Services;
using Mcp.Benchmark.Infrastructure.Scoring;
using Mcp.Benchmark.Infrastructure.Services.Reporting;

namespace Mcp.Benchmark.Tests.Unit.Performance;

public sealed class ValidatorOverheadBudgetTests
{
    [Fact]
    public void ProductionHostStartupAndPlanning_ShouldRemainWithinAllocationBudgets()
    {
        var session = new CliSessionContext("performance-budget");
        var governance = new ExecutionGovernanceService();
        var configuration = new McpValidatorConfiguration
        {
            Server = new McpServerConfig { Endpoint = "https://benchmark.invalid/mcp", Transport = "http" }
        };

        using (McpvalCliHostFactory.Create(Array.Empty<string>(), session).Build())
        {
        }
        governance.BuildValidationPlan(session, configuration, outputDirectory: null);

        var startupBytes = AverageAllocatedBytes(5, () =>
        {
            using var host = McpvalCliHostFactory.Create(Array.Empty<string>(), session).Build();
        });

        var planningBytes = AverageAllocatedBytes(
            100,
            () => governance.BuildValidationPlan(session, configuration, outputDirectory: null));

        startupBytes.Should().BeLessThan(512 * 1024);
        planningBytes.Should().BeLessThan(2 * 1024 * 1024);
    }

    [Fact]
    public void PureReductionScoringAndRendering_ShouldRemainWithinAllocationBudgets()
    {
        var result = CreateResult();
        var scoring = new SecurityFocusedScoringStrategy();
        var renderer = new MarkdownReportGenerator();

        AssuranceAssessmentEngine.Evaluate(result, AssuranceProfileCatalog.Inventory);
        scoring.CalculateScore(result);
        renderer.GenerateReport(result);

        var reductionBytes = AverageAllocatedBytes(
            2_000,
            () => AssuranceAssessmentEngine.Evaluate(result, AssuranceProfileCatalog.Inventory));
        var scoringBytes = AverageAllocatedBytes(50_000, () => scoring.CalculateScore(result));
        var renderingBytes = AverageAllocatedBytes(20_000, () => renderer.GenerateReport(result));

        reductionBytes.Should().BeLessThan(16 * 1024);
        scoringBytes.Should().BeLessThan(16 * 1024);
        renderingBytes.Should().BeLessThan(64 * 1024);
    }

    private static double AverageAllocatedBytes(int operationCount, Action operation)
    {
        var before = GC.GetAllocatedBytesForCurrentThread();
        for (var index = 0; index < operationCount; index++) operation();
        return (GC.GetAllocatedBytesForCurrentThread() - before) / (double)operationCount;
    }

    private static ValidationResult CreateResult()
    {
        var result = new ValidationResult
        {
            ValidationId = "performance-budget",
            OverallStatus = ValidationStatus.Passed,
            ComplianceScore = 100,
            ServerConfig = new McpServerConfig { Endpoint = "https://benchmark.invalid/mcp", Transport = "http" },
            ValidationConfig = new McpValidatorConfiguration(),
            ProtocolCompliance = new ComplianceTestResult { Status = TestStatus.Passed, ComplianceScore = 100 },
            ToolValidation = new ToolTestResult { Status = TestStatus.Passed, Score = 100 },
            SecurityTesting = new SecurityTestResult { Status = TestStatus.Passed, SecurityScore = 100 }
        };
        result.Evidence.Coverage.Add(ValidationCoverageFactory.FromOutcome(
            "protocol-core",
            "json-rpc",
            ValidationOutcome.Succeeded));
        result.Assurance = AssuranceAssessmentEngine.Evaluate(result, AssuranceProfileCatalog.Inventory);
        return result;
    }
}
