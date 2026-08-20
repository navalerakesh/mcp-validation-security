using System.Diagnostics;
using Mcp.Benchmark.CLI.Services;
using Mcp.Benchmark.CLI.Utilities;
using Mcp.Benchmark.Core.Models;
using Mcp.Benchmark.Core.Services;
using Mcp.Benchmark.Infrastructure.Scoring;
using Mcp.Benchmark.Infrastructure.Services.Reporting;

namespace Mcp.Benchmark.Tests.Unit.Performance;

public sealed class ValidatorOverheadBudgetTests
{
    private const double RegressionMultiplier = 10;

    [Fact]
    public void ProductionHostStartupAndPlanning_ShouldRemainWithinGenerousCiBudgets()
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

        var startup = Stopwatch.StartNew();
        for (var index = 0; index < 5; index++)
        {
            using var host = McpvalCliHostFactory.Create(Array.Empty<string>(), session).Build();
        }
        startup.Stop();

        var planning = Stopwatch.StartNew();
        for (var index = 0; index < 100; index++)
        {
            governance.BuildValidationPlan(session, configuration, outputDirectory: null);
        }
        planning.Stop();

        AverageMilliseconds(startup, 5).Should().BeLessThan(66.488 * RegressionMultiplier);
        AverageMilliseconds(planning, 100).Should().BeLessThan(1.198 * RegressionMultiplier);
    }

    [Fact]
    public void PureReductionScoringAndRendering_ShouldRemainWithinGenerousCiBudgets()
    {
        var result = CreateResult();
        var scoring = new SecurityFocusedScoringStrategy();
        var renderer = new MarkdownReportGenerator();

        ValidationVerdictEngine.Calculate(result);
        scoring.CalculateScore(result);
        renderer.GenerateReport(result);

        var reduction = Stopwatch.StartNew();
        for (var index = 0; index < 2_000; index++) ValidationVerdictEngine.Calculate(result);
        reduction.Stop();

        var score = Stopwatch.StartNew();
        for (var index = 0; index < 50_000; index++) scoring.CalculateScore(result);
        score.Stop();

        var rendering = Stopwatch.StartNew();
        for (var index = 0; index < 20_000; index++) renderer.GenerateReport(result);
        rendering.Stop();

        AverageMilliseconds(reduction, 2_000).Should().BeLessThan(0.137047 * RegressionMultiplier);
        AverageMilliseconds(score, 50_000).Should().BeLessThan(0.001248 * RegressionMultiplier);
        AverageMilliseconds(rendering, 20_000).Should().BeLessThan(0.004412 * RegressionMultiplier);
    }

    private static double AverageMilliseconds(Stopwatch stopwatch, int operationCount) =>
        stopwatch.Elapsed.TotalMilliseconds / operationCount;

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
        result.VerdictAssessment = ValidationVerdictEngine.Calculate(result);
        return result;
    }
}
