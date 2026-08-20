using BenchmarkDotNet.Attributes;
using Mcp.Benchmark.CLI.Services;
using Mcp.Benchmark.CLI.Utilities;
using Mcp.Benchmark.Core.Models;
using Mcp.Benchmark.Core.Services;
using Mcp.Benchmark.Infrastructure.Scoring;
using Mcp.Benchmark.Infrastructure.Services.Reporting;
using Microsoft.Extensions.Hosting;

namespace Mcp.Benchmark.Benchmarks;

[MemoryDiagnoser]
public class ValidatorBenchmarks
{
    private readonly McpValidatorConfiguration _configuration = new()
    {
        Server = new McpServerConfig
        {
            Endpoint = "https://benchmark.invalid/mcp",
            Transport = "http"
        }
    };

    private readonly ExecutionGovernanceService _governance = new();
    private readonly CliSessionContext _session = new("benchmark-session");
    private readonly SecurityFocusedScoringStrategy _scoring = new();
    private readonly MarkdownReportGenerator _markdown = new();
    private ValidationResult _result = null!;

    [GlobalSetup]
    public void Setup()
    {
        _result = new ValidationResult
        {
            ValidationId = "benchmark-validation",
            OverallStatus = ValidationStatus.Passed,
            ComplianceScore = 100,
            ServerConfig = _configuration.Server.CloneWithoutSecrets(),
            ValidationConfig = _configuration.CloneWithoutSecrets(),
            ProtocolCompliance = new ComplianceTestResult { Status = TestStatus.Passed, ComplianceScore = 100 },
            ToolValidation = new ToolTestResult { Status = TestStatus.Passed, Score = 100 },
            SecurityTesting = new SecurityTestResult { Status = TestStatus.Passed, SecurityScore = 100 }
        };
        _result.Evidence.Coverage.Add(ValidationCoverageFactory.FromOutcome(
            "protocol-core",
            "json-rpc",
            ValidationOutcome.Succeeded));
        _result.VerdictAssessment = ValidationVerdictEngine.Calculate(_result);
    }

    [Benchmark(Baseline = true)]
    public McpValidatorConfiguration ConfigurationCapture() =>
        _configuration.CloneWithoutSecrets();

    [Benchmark]
    public void HostStartup()
    {
        using var host = McpvalCliHostFactory.Create(Array.Empty<string>(), _session).Build();
    }

    [Benchmark]
    public object Planning() =>
        _governance.BuildValidationPlan(_session, _configuration.CloneWithoutSecrets(), outputDirectory: null);

    [Benchmark]
    public VerdictAssessment VerdictReduction() =>
        ValidationVerdictEngine.Calculate(_result);

    [Benchmark]
    public ScoringResult Scoring() =>
        _scoring.CalculateScore(_result);

    [Benchmark]
    public string MarkdownRendering() =>
        _markdown.GenerateReport(_result);
}
