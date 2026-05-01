using System.Text.Json;
using System.Text.Json.Nodes;
using System.Xml;
using System.Xml.Linq;
using FsCheck;
using FsCheck.Xunit;
using Mcp.Benchmark.Core.Models;
using Mcp.Benchmark.Core.Services;
using Mcp.Benchmark.Infrastructure.Services.Reporting;
using Mcp.Benchmark.Infrastructure.Validators;

namespace Mcp.Benchmark.Tests.Unit.Fuzzing;

public class ParserAndReportFuzzTests
{
    private readonly JsonSchemaValidator _schemaValidator = new();
    private readonly ValidationReportRenderer _reportRenderer = new();

    [Property(MaxTest = 150)]
    public void SseEventStreamParser_ShouldNeverThrowForGeneratedStreams(string? eventStream)
    {
        var content = ToSafeText(eventStream, maxLength: 512);

        var action = () => SseEventStreamParser.Parse(content);

        action.Should().NotThrow();
        var records = SseEventStreamParser.Parse(content);
        records.Count.Should().BeLessThanOrEqualTo(content.Count(character => character == '\n') + 1);
    }

    [Property(MaxTest = 100)]
    public void SseEventStreamParser_ShouldPreserveGeneratedDataLineOrder(NonEmptyString firstLine, NonEmptyString secondLine)
    {
        var first = ToSseDataValue(firstLine.Get);
        var second = ToSseDataValue(secondLine.Get);
        var content = $"event: message\nid: fuzz-id\ndata: {first}\ndata: {second}\n\n";

        var records = SseEventStreamParser.Parse(content);

        records.Should().ContainSingle();
        records[0].Event.Should().Be("message");
        records[0].Id.Should().Be("fuzz-id");
        records[0].Data.Should().Be($"{first}\n{second}");
    }

    [Property(MaxTest = 100)]
    public void JsonSchemaValidator_StringMaxLength_ShouldMatchGeneratedTextLength(string? value, NonNegativeInt maxLengthSeed)
    {
        var text = ToSafeText(value, maxLength: 128);
        var maxLength = maxLengthSeed.Get % 129;
        var schema = new JsonObject
        {
            ["type"] = "string",
            ["maxLength"] = maxLength
        };

        var result = _schemaValidator.Validate(JsonValue.Create(text)!, schema);

        result.IsValid.Should().Be(text.Length <= maxLength);
    }

    [Property(MaxTest = 75)]
    public void MachineReportRenderers_ShouldEmitParseableOutputsForGeneratedFindingText(string? seed, NonNegativeInt severitySeed)
    {
        var text = ToSafeText(seed, maxLength: 96);
        var token = ToRuleToken(seed);
        var severity = PickSeverity(severitySeed.Get);
        var result = new ValidationResult
        {
            ValidationId = $"validation-{token.ToLowerInvariant()}",
            ServerConfig = new McpServerConfig
            {
                Endpoint = $"https://example.test/{token.ToLowerInvariant()}",
                Transport = "http"
            },
            OverallStatus = ValidationStatus.Failed,
            ComplianceScore = 50,
            ToolValidation = new ToolTestResult
            {
                Status = TestStatus.Failed,
                Findings = new List<ValidationFinding>
                {
                    new()
                    {
                        RuleId = $"MCP.FUZZ.{token}",
                        Category = "Fuzzing",
                        Component = $"component-{token.ToLowerInvariant()}",
                        Severity = severity,
                        Summary = text,
                        Recommendation = $"Review generated input {token}."
                    }
                }
            }
        };

        using var sarif = JsonDocument.Parse(_reportRenderer.GenerateSarifReport(result));
        var sarifResult = sarif.RootElement.GetProperty("runs")[0].GetProperty("results")[0];
        sarifResult.GetProperty("properties").GetProperty("ruleIdSource").GetString().Should().Be("explicit");
        sarifResult.GetProperty("ruleId").GetString().Should().Be($"MCP.FUZZ.{token}");

        XDocument.Parse(_reportRenderer.GenerateXmlReport(result, verbose: true)).Root.Should().NotBeNull();
        XDocument.Parse(_reportRenderer.GenerateJunitReport(result)).Root.Should().NotBeNull();
    }

    private static ValidationFindingSeverity PickSeverity(int seed)
    {
        var values = new[]
        {
            ValidationFindingSeverity.Info,
            ValidationFindingSeverity.Low,
            ValidationFindingSeverity.Medium,
            ValidationFindingSeverity.High,
            ValidationFindingSeverity.Critical
        };

        return values[seed % values.Length];
    }

    private static string ToSseDataValue(string value)
    {
        var text = ToSafeText(value, maxLength: 128)
            .Replace("\r", " ", StringComparison.Ordinal)
            .Replace("\n", " ", StringComparison.Ordinal);
        return string.IsNullOrWhiteSpace(text) ? "data" : text;
    }

    private static string ToSafeText(string? value, int maxLength)
    {
        if (string.IsNullOrEmpty(value))
        {
            return "fuzz";
        }

        var chars = value
            .Where(character => XmlConvert.IsXmlChar(character) && !char.IsSurrogate(character))
            .Take(maxLength)
            .ToArray();

        var text = new string(chars).Trim();
        return string.IsNullOrWhiteSpace(text) ? "fuzz" : text;
    }

    private static string ToRuleToken(string? value)
    {
        if (string.IsNullOrEmpty(value))
        {
            return "VALUE";
        }

        var chars = value
            .Where(char.IsLetterOrDigit)
            .Select(char.ToUpperInvariant)
            .Take(32)
            .ToArray();

        return chars.Length == 0 ? "VALUE" : new string(chars);
    }
}
