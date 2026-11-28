using System.Text.Json;
using Mcp.Benchmark.Core.Models;
using Mcp.Benchmark.Infrastructure.Services;
using Microsoft.Extensions.Logging.Abstractions;

namespace Mcp.Benchmark.Tests.Fixtures;

internal static class RuleQualityCorpus
{
    public static IReadOnlyList<(RuleCalibrationSample Expected, RuleCalibrationSample Observed)> ObserveSystemImpact()
    {
        using var document = JsonDocument.Parse(File.ReadAllText(
            Path.Combine(AppContext.BaseDirectory, "Fixtures", "Calibration", "rule-quality-v2.json")));
        var options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
        var analyzer = new ContentSafetyAnalyzer(NullLogger<ContentSafetyAnalyzer>.Instance);
        var samples = new List<(RuleCalibrationSample, RuleCalibrationSample)>();
        foreach (var element in document.RootElement.EnumerateArray())
        {
            var expected = element.Deserialize<RuleCalibrationSample>(options)
                ?? throw new InvalidDataException("Rule-quality fixture sample is missing.");
            var toolName = element.GetProperty("toolName").GetString();
            if (string.IsNullOrWhiteSpace(toolName))
                throw new InvalidDataException("Rule-quality fixture requires an explicit tool name.");
            var observed = analyzer.AnalyzeTool(toolName).Any(finding => finding.Axis == ContentRiskAxis.SystemImpact);
            samples.Add((expected, new RuleCalibrationSample
            {
                SampleId = expected.SampleId,
                RuleId = expected.RuleId,
                ExpectedPositive = expected.ExpectedPositive,
                ObservedPositive = observed,
                ProtocolRevision = expected.ProtocolRevision,
                Transport = expected.Transport,
                Partition = expected.Partition,
                SourceGroupId = expected.SourceGroupId,
                ScenarioFamily = expected.ScenarioFamily,
                Notes = expected.Notes
            }));
        }
        return samples;
    }
}
