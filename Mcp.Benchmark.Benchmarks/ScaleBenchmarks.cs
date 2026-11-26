using BenchmarkDotNet.Attributes;
using Mcp.Benchmark.CLI.Services;
using Mcp.Benchmark.CLI.Utilities;
using Mcp.Benchmark.Core;
using Mcp.Benchmark.Core.Constants;
using Mcp.Benchmark.Core.Models;
using Mcp.Benchmark.Core.Services;
using Mcp.Benchmark.Infrastructure.Services.Reporting;
using Mcp.Benchmark.Infrastructure.Validators;
using System.Text.Json.Nodes;

namespace Mcp.Benchmark.Benchmarks;

[MemoryDiagnoser]
public class ScaleBenchmarks
{
    [Params(10, 100, 1000)]
    public int CatalogItems { get; set; }
    private readonly JsonSchemaValidator _schema = new();
    private readonly MarkdownReportGenerator _markdown = new();
    private readonly ExecutionGovernanceService _governance = new();
    private readonly CliSessionContext _session = new("scale-fixture");
    private JsonNode[] _arguments = [];
    private JsonNode _inputSchema = null!;
    private ValidationResult _result = null!;
    private McpValidatorConfiguration _configuration = null!;

    [GlobalSetup]
    public void Setup()
    {
        _inputSchema = JsonNode.Parse("""{"type":"object","properties":{"name":{"type":"string"},"kind":{"enum":["simple","composed"]}},"required":["name"],"if":{"properties":{"kind":{"const":"composed"}}},"then":{"required":["kind"]}}""")!;
        _arguments = Enumerable.Range(0, CatalogItems).Select(i => JsonNode.Parse($$"""{"name":"fixture-{{i}}","kind":"composed"}""")!).ToArray();
        _configuration = new() { Assurance = new() { Profile = "inventory" }, Server = new() { Endpoint = "https://benchmark.invalid/mcp" } };
        _result = new()
        {
            ValidationId = "scale-fixture", StartTime = new DateTime(2026, 11, 24, 0, 0, 0, DateTimeKind.Utc),
            EndTime = new DateTime(2026, 11, 24, 0, 0, 1, DateTimeKind.Utc), OverallStatus = ValidationStatus.Passed,
            ToolValidation = new() { Status = TestStatus.Inconclusive, ToolsDiscovered = CatalogItems }
        };
        for (var index = 0; index < CatalogItems; index++)
            _result.ToolValidation.ToolResults.Add(new()
            {
                ToolName = $"fixture-{index}", Status = TestStatus.Skipped,
                Description = "Synthetic scale fixture; <script>untrusted text</script>; no invocation."
            });
        for (var index = 0; index < CatalogItems * 10; index++)
            _result.Evidence.Coverage.Add(ValidationCoverageFactory.FromOutcome("tool-surface", $"fixture-obligation-{index}",
                index % 4 == 0 ? ValidationOutcome.Unavailable : index % 4 == 1 ? ValidationOutcome.Failed : ValidationOutcome.Succeeded));
        _result.Assurance = AssuranceAssessmentEngine.Evaluate(_result, AssuranceProfileCatalog.Inventory);
        if (_result.Assurance.Claims.Count < CatalogItems * 10) throw new InvalidOperationException("Scale fixture lost required observations.");
    }

    [Benchmark]
    public object ConfigPlanning() => _governance.BuildValidationPlan(_session, _configuration, null);

    [Benchmark]
    public int SchemaCatalog()
    {
        var valid = 0;
        foreach (var arguments in _arguments) if (_schema.Validate(arguments, _inputSchema).IsValid) valid++;
        if (valid != CatalogItems) throw new InvalidOperationException("Schema workload produced an unexpected outcome.");
        return valid;
    }

    [Benchmark]
    public AssuranceEvaluation EvidenceReduction() => AssuranceAssessmentEngine.Evaluate(_result, AssuranceProfileCatalog.Inventory);

    [Benchmark]
    public string ReportRendering() => _markdown.GenerateReport(_result);
}
