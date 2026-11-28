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
    private readonly ValidationReportRenderer _html = new();
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
        _result = ScaleBenchmarkFixture.CreateResult(CatalogItems);
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

    [Benchmark]
    public string HtmlReportRendering() => _html.GenerateHtmlReport(_result, _configuration.Reporting, verbose: true);
}
