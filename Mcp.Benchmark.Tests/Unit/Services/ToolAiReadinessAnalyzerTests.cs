using System.Text.Json;
using FluentAssertions;
using Mcp.Benchmark.Core.Models;
using Mcp.Benchmark.Infrastructure.Services;

namespace Mcp.Benchmark.Tests.Unit.Services;

public class ToolAiReadinessAnalyzerTests
{
    private readonly ToolAiReadinessAnalyzer _analyzer = new();

    [Theory]
    [InlineData("anyOf")]
    [InlineData("oneOf")]
    public void AnalyzeCatalog_AlternativeGuidance_ShouldCoverEveryApplicableBranch(string applicator)
    {
      var schema = ParseJsonElement("{\"properties\":{\"mode\":{\"" + applicator + "\":[{\"type\":\"string\",\"description\":\"Select a mode.\",\"enum\":[\"read\"]},{\"type\":\"null\",\"description\":\"Use the default mode.\"}]}}}");

      var analysis = _analyzer.AnalyzeCatalog([new ToolAiReadinessTarget { Name = "fixture", InputSchema = schema }], null);

      analysis.Score.Should().Be(100);
      analysis.InspectionIssues.Should().BeEmpty();
      analysis.Findings.Should().BeEmpty();
    }

    [Fact]
    public void AnalyzeCatalog_AlternativeConstraint_ShouldNotCoverUnconstrainedNeighbor()
    {
      var schema = ParseJsonElement("{\"properties\":{\"mode\":{\"anyOf\":[{\"type\":\"string\",\"description\":\"Select read mode.\",\"enum\":[\"read\"]},{\"type\":\"string\"}]}}}");

      var analysis = _analyzer.AnalyzeCatalog([new ToolAiReadinessTarget { Name = "fixture", InputSchema = schema }], null);

      analysis.InspectionIssues.Should().BeEmpty();
      analysis.Findings.Should().Contain(finding => finding.RuleId == ValidationFindingRuleIds.AiReadinessMissingParameterDescriptions);
      analysis.Findings.Should().Contain(finding => finding.RuleId == ValidationFindingRuleIds.AiReadinessEnumCoverageMissing);
    }

    [Fact]
    public void AnalyzeCatalog_RequiredNameWithoutPropertyDefinition_ShouldRemainVisible()
    {
      var analysis = _analyzer.AnalyzeCatalog([new ToolAiReadinessTarget
      {
        Name = "fixture", InputSchema = ParseJsonElement("{\"type\":\"object\",\"required\":[\"value\"]}")
      }], null);

      analysis.Findings.Should().Contain(finding => finding.RuleId == ValidationFindingRuleIds.AiReadinessMissingParameterDescriptions);
    }

    [Fact]
    public void AnalyzeCatalog_LocalReference_ShouldPreserveEquivalentInlineSchemaAdvice()
    {
      var inline = ParseJsonElement("""
        {"type":"object","properties":{"mode":{"type":"string","description":"Operation to perform.","enum":["read","write"]}}}
        """);
      var referenced = ParseJsonElement("""
        {"type":"object","$defs":{"operation":{"type":"string","description":"Operation to perform.","enum":["read","write"]}},"properties":{"mode":{"$ref":"#/$defs/operation"}}}
        """);

      var expected = _analyzer.AnalyzeCatalog([new ToolAiReadinessTarget { Name = "fixture", InputSchema = inline }], null);
      var actual = _analyzer.AnalyzeCatalog([new ToolAiReadinessTarget { Name = "fixture", InputSchema = referenced }], null);

      actual.Score.Should().Be(expected.Score);
      actual.Findings.Select(finding => finding.RuleId).Should().Equal(expected.Findings.Select(finding => finding.RuleId));
    }

    [Theory]
    [InlineData("{\"allOf\":[{\"properties\":{\"mode\":{\"type\":\"string\"}}},{\"properties\":{\"mode\":{\"description\":\"Operation to perform.\",\"enum\":[\"read\",\"write\"]}}}]}")]
    [InlineData("{\"properties\":{\"mode\":{\"allOf\":[{\"type\":\"string\"},{\"description\":\"Operation to perform.\",\"enum\":[\"read\",\"write\"]}]}}}")]
    [InlineData("{\"$defs\":{\"mode\":{\"type\":\"string\",\"enum\":[\"read\",\"write\"]}},\"properties\":{\"mode\":{\"$ref\":\"#/$defs/mode\",\"description\":\"Operation to perform.\"}}}")]
    public void AnalyzeCatalog_ConjunctiveGuidance_ShouldMatchEquivalentInlineSchema(string schema)
    {
      var expected = _analyzer.AnalyzeCatalog([new ToolAiReadinessTarget
      {
        Name = "fixture",
        InputSchema = ParseJsonElement("{\"properties\":{\"mode\":{\"type\":\"string\",\"description\":\"Operation to perform.\",\"enum\":[\"read\",\"write\"]}}}")
      }], null);
      var actual = _analyzer.AnalyzeCatalog([new ToolAiReadinessTarget { Name = "fixture", InputSchema = ParseJsonElement(schema) }], null);

      actual.Score.Should().Be(expected.Score);
      actual.AnalyzedSchemaCount.Should().Be(1);
      actual.InspectionIssues.Should().BeEmpty();
      actual.Findings.Select(finding => finding.RuleId).Should().Equal(expected.Findings.Select(finding => finding.RuleId));
    }

    [Theory]
    [InlineData("{\"allOf\":[{\"properties\":{\"value\":{\"type\":\"string\"}}},{\"properties\":{\"value\":{\"type\":\"integer\"}}}]}")]
    [InlineData("{\"properties\":{\"value\":{\"type\":\"string\"},\"value\":{\"type\":\"integer\"}}}")]
    [InlineData("{\"$defs\":{\"base\":{\"type\":\"string\"}},\"properties\":{\"value\":{\"$ref\":\"#/$defs/base\",\"type\":\"array\"}}}")]
    public void AnalyzeCatalog_AmbiguousOrConflictingPropertyClauses_ShouldRemainUnmeasured(string schema)
    {
      var analysis = _analyzer.AnalyzeCatalog([new ToolAiReadinessTarget { Name = "fixture", InputSchema = ParseJsonElement(schema) }], null);

      analysis.Score.Should().Be(-1);
      analysis.AnalyzedSchemaCount.Should().Be(0);
      analysis.InspectionIssues.Should().NotBeEmpty();
    }

    [Fact]
    public void AnalyzeCatalog_IncompleteNeighbor_ShouldRetainObservedGuidanceDefectWithoutWholeSchemaScore()
    {
      var schema = ParseJsonElement("{\"properties\":{\"known\":{\"type\":\"string\"},\"unknown\":{\"$ref\":\"#/$defs/missing\"}}}");

      var analysis = _analyzer.AnalyzeCatalog([new ToolAiReadinessTarget { Name = "fixture", InputSchema = schema }], null);

      analysis.Score.Should().Be(-1);
      analysis.InspectionIssues.Should().NotBeEmpty();
      analysis.Findings.Should().Contain(finding => finding.RuleId == ValidationFindingRuleIds.AiReadinessMissingParameterDescriptions);
    }

    [Fact]
    public void AnalyzeCatalog_LargeAnnotationCollection_ShouldStopAtInspectionBudget()
    {
      var schema = ParseJsonElement("{\"properties\":{\"value\":{\"type\":\"string\",\"examples\":[" + string.Join(",", Enumerable.Repeat("\"sample\"", 5000)) + "]}}}");

      var analysis = _analyzer.AnalyzeCatalog([new ToolAiReadinessTarget { Name = "fixture", InputSchema = schema }], null);

      analysis.Score.Should().Be(-1);
      analysis.InspectionIssues.Should().Contain(issue => issue.Status == SchemaInspectionStatus.BudgetExceeded);
    }

    [Fact]
    public void AnalyzeCatalog_DeepAnnotationBelowStepBudget_ShouldStopAtDepthBound()
    {
      var nested = new string('[', 70) + "0" + new string(']', 70);
      using var document = JsonDocument.Parse("{\"properties\":{\"value\":{\"type\":\"string\",\"examples\":" + nested + "}}}",
        new JsonDocumentOptions { MaxDepth = 128 });

      var analysis = _analyzer.AnalyzeCatalog([new ToolAiReadinessTarget { Name = "fixture", InputSchema = document.RootElement }], null);

      analysis.Score.Should().Be(-1);
      analysis.InspectionIssues.Should().Contain(issue => issue.Status == SchemaInspectionStatus.BudgetExceeded);
    }

    [Fact]
    public void AnalyzeCatalog_RepeatedConjunctReference_ShouldNotMultiplyMetadataWork()
    {
      var properties = Enumerable.Range(0, 180).ToDictionary(index => $"parameter{index}",
        _ => new { type = "string", description = "Documented input value." });
      var schema = JsonSerializer.SerializeToElement(new Dictionary<string, object>
      {
        ["$defs"] = new { shared = new { properties } },
        ["allOf"] = Enumerable.Range(0, 40).Select(_ => new Dictionary<string, string> { ["$ref"] = "#/$defs/shared" }).ToArray()
      });

      var analysis = _analyzer.AnalyzeCatalog([new ToolAiReadinessTarget { Name = "fixture", InputSchema = schema }], null);

      analysis.Score.Should().Be(100);
      analysis.AnalyzedSchemaCount.Should().Be(1);
      analysis.InspectionIssues.Should().BeEmpty();
    }

    [Fact]
    public void AnalyzeCatalog_MixedCompleteAndIncompleteSchemas_ShouldNotPublishWholeCatalogScore()
    {
      var analysis = _analyzer.AnalyzeCatalog(
      [
        new ToolAiReadinessTarget { Name = "known", InputSchema = ParseJsonElement("{\"properties\":{\"value\":{\"type\":\"string\",\"description\":\"Documented input value.\"}}}") },
        new ToolAiReadinessTarget { Name = "unknown", InputSchema = ParseJsonElement("{\"$ref\":\"https://example.invalid/schema\"}") }
      ], null);

      analysis.AnalyzedSchemaCount.Should().Be(1);
      analysis.InspectionIssues.Should().ContainSingle();
      analysis.Score.Should().Be(-1);
      analysis.SummaryIssue.Should().BeNull();
    }

    [Theory]
    [InlineData("{\"properties\":{\"value\":{\"$ref\":\"#/$defs/missing\"}}}", SchemaInspectionStatus.UnresolvedReference)]
    [InlineData("{\"$defs\":{\"loop\":{\"$ref\":\"#/$defs/loop\"}},\"properties\":{\"value\":{\"$ref\":\"#/$defs/loop\"}}}", SchemaInspectionStatus.CyclicReference)]
    [InlineData("{\"properties\":{\"value\":{\"$ref\":\"https://example.invalid/schema\"}}}", SchemaInspectionStatus.Unsupported)]
    [InlineData("{\"if\":{\"type\":\"object\"},\"then\":{\"properties\":{\"value\":{\"type\":\"string\"}}}}", SchemaInspectionStatus.Unsupported)]
    [InlineData("{\"properties\":{\"value\":{\"type\":\"string\"}},\"patternProperties\":{\"value\":{\"description\":\"Conditional guidance\"}}}", SchemaInspectionStatus.Unsupported)]
    [InlineData("{\"properties\":{\"value\":{\"type\":\"string\"}},\"dependentSchemas\":{\"value\":{\"properties\":{\"value\":{\"description\":\"Dependent guidance\"}}}}}", SchemaInspectionStatus.Unsupported)]
    [InlineData("{\"$schema\":\"https://example.invalid/dialect\",\"properties\":{\"value\":{\"type\":\"string\"}}}", SchemaInspectionStatus.Unsupported)]
    [InlineData("{\"data\":{\"type\":\"string\"},\"properties\":{\"value\":{\"$ref\":\"#/data\"}}}", SchemaInspectionStatus.Unsupported)]
    [InlineData("{\"$defs\":{\"nested\":{\"$id\":\"https://example.invalid/other\",\"$defs\":{\"value\":{\"type\":\"string\"}}}},\"properties\":{\"value\":{\"$ref\":\"#/$defs/nested/$defs/value\"}}}", SchemaInspectionStatus.Unsupported)]
    [InlineData("{\"properties\":{\"value\":{\"$ref\":\"#/$defs/value\",\"description\":\"Sibling annotation\"}}}", SchemaInspectionStatus.UnresolvedReference)]
    public void AnalyzeCatalog_UnresolvedInspection_ShouldNotEarnOrInventSchemaScore(string schema, SchemaInspectionStatus status)
    {
      var analysis = _analyzer.AnalyzeCatalog([new ToolAiReadinessTarget { Name = "fixture", InputSchema = ParseJsonElement(schema) }],
        new string('x', 1024 * 1024));

      analysis.AnalyzedSchemaCount.Should().Be(0);
      analysis.InspectionIssues.Should().Contain(issue => issue.Status == status);
      analysis.Score.Should().Be(-1);
      analysis.Findings.Should().NotContain(finding => finding.RuleId == ValidationFindingRuleIds.AiReadinessMissingParameterDescriptions);
    }

    [Theory]
    [InlineData("true")]
    [InlineData("false")]
    [InlineData("{}")]
    [InlineData("{\"type\":[\"string\",\"null\"]}")]
    [InlineData("{\"type\":[\"array\",\"null\"],\"items\":true}")]
    [InlineData("{\"type\":\"array\",\"items\":false}")]
    [InlineData("{\"type\":\"array\",\"items\":{\"type\":\"string\"}}")]
    public void AnalyzeCatalog_WithValidSchemaAlternatives_ShouldNotThrowOrRequireMinimumItems(string propertySchema)
    {
      var schema = ParseJsonElement("{\"type\":\"object\",\"properties\":{\"value\":" + propertySchema + "},\"required\":[\"value\"]}");
      var analysis = _analyzer.AnalyzeCatalog([new ToolAiReadinessTarget { Name = "fixture", InputSchema = schema }], null);

      analysis.Findings.Should().NotContain(finding => finding.RuleId == ValidationFindingRuleIds.AiReadinessRequiredArraySchema);
    }

    [Theory]
    [InlineData("value", true)]
    [InlineData("Value", false)]
    public void AnalyzeCatalog_ShouldMatchRequiredNamesExactly(string requiredName, bool expectsArrayAdvice)
    {
      var schema = ParseJsonElement("{\"properties\":{\"value\":{\"type\":\"array\"}},\"required\":[\"" + requiredName + "\"]}");
      var analysis = _analyzer.AnalyzeCatalog([new ToolAiReadinessTarget { Name = "fixture", InputSchema = schema }], null);

      analysis.Findings.Any(finding => finding.RuleId == ValidationFindingRuleIds.AiReadinessRequiredArraySchema)
        .Should().Be(expectsArrayAdvice);
    }

    [Fact]
    public void AnalyzeCatalog_WithUnderspecifiedSchema_ShouldEmitReadinessFindings()
    {
        var analysis = _analyzer.AnalyzeCatalog(
        [
            new ToolAiReadinessTarget
            {
                Name = "workflow_tool",
                InputSchema = ParseJsonElement("""
                {
                  "type": "object",
                  "required": ["targets", "mode", "callbackUrl"],
                  "properties": {
                    "targets": { "type": "array", "description": "Targets to process" },
                    "mode": { "type": "string", "description": "Execution mode" },
                    "callbackUrl": { "type": "string", "description": "Webhook URL for completion notices" }
                  }
                }
                """)
            }
        ],
        rawJson: new string('x', 40000),
        totalPayloadChars: 40000);

        analysis.Score.Should().BeLessThan(100);
        analysis.EstimatedTokenCount.Should().Be(10000);
        analysis.SummaryIssue.Should().Contain("AI Readiness Score");
        analysis.Findings.Should().Contain(f => f.RuleId == ValidationFindingRuleIds.AiReadinessRequiredArraySchema);
        analysis.Findings.Should().Contain(f => f.RuleId == ValidationFindingRuleIds.AiReadinessEnumCoverageMissing);
        analysis.Findings.Should().Contain(f => f.RuleId == ValidationFindingRuleIds.AiReadinessFormatHintMissing);
        analysis.Findings.Should().Contain(f => f.RuleId == ValidationFindingRuleIds.AiReadinessTokenBudgetWarning);
        analysis.Findings.Where(f => f.RuleId.StartsWith("AI.TOOL.SCHEMA.", StringComparison.Ordinal) && !f.RuleId.Contains("TOKEN_BUDGET", StringComparison.Ordinal))
          .Should().OnlyContain(f => f.Metadata[AiReadinessEvidenceKinds.MetadataKey] == AiReadinessEvidenceKinds.DeterministicSchemaHeuristic);
        analysis.Findings.Single(f => f.RuleId == ValidationFindingRuleIds.AiReadinessTokenBudgetWarning)
          .Metadata.Should().Contain(AiReadinessEvidenceKinds.MetadataKey, AiReadinessEvidenceKinds.DeterministicPayloadHeuristic);
    }

    [Fact]
    public void AnalyzeCatalog_WithMarkerSubstringsInFreeformDescriptions_ShouldNotRequireEnums()
    {
        var analysis = _analyzer.AnalyzeCatalog(
        [
            new ToolAiReadinessTarget
            {
                Name = "query-docs",
                InputSchema = ParseJsonElement("""
                {
                  "type": "object",
                  "properties": {
                    "libraryId": {
                      "type": "string",
                      "description": "Exact compatible library ID in /org/project format."
                    },
                    "query": {
                      "type": "string",
                      "description": "Question scoped to a single concept."
                    },
                    "language": {
                      "type": "string",
                      "description": "Programming language such as Python or TypeScript."
                    }
                  }
                }
                """)
            }
        ],
        rawJson: null);

        analysis.Findings.Should().NotContain(finding =>
            finding.RuleId == ValidationFindingRuleIds.AiReadinessEnumCoverageMissing);
    }

    [Fact]
    public void AnalyzeCatalog_WithConstraintWordsOnlyInDescriptions_ShouldNotInferSchemaIntent()
    {
        var analysis = _analyzer.AnalyzeCatalog(
        [
            new ToolAiReadinessTarget
            {
                Name = "search-records",
                InputSchema = ParseJsonElement("""
                {
                  "type": "object",
                  "properties": {
                    "owner": {
                      "type": "string",
                      "description": "Returns org-level fields for the account owner."
                    },
                    "query": {
                      "type": "string",
                      "description": "Scope by type, author email, or commit date in the query language."
                    }
                  }
                }
                """)
            }
        ],
        rawJson: null);

        analysis.Findings.Should().NotContain(finding =>
            finding.RuleId == ValidationFindingRuleIds.AiReadinessEnumCoverageMissing ||
            finding.RuleId == ValidationFindingRuleIds.AiReadinessFormatHintMissing);
    }

    [Fact]
    public void AnalyzeCatalog_WithMethodParameter_ShouldRecommendExplicitChoices()
    {
        var analysis = _analyzer.AnalyzeCatalog(
        [
            new ToolAiReadinessTarget
            {
                Name = "change-record",
                InputSchema = ParseJsonElement("""
                {
                  "type": "object",
                  "properties": {
                    "method": {
                      "type": "string",
                      "description": "Operation to perform."
                    }
                  }
                }
                """)
            }
        ],
        rawJson: null);

        analysis.Findings.Should().ContainSingle(finding =>
            finding.RuleId == ValidationFindingRuleIds.AiReadinessEnumCoverageMissing);
    }

    [Fact]
    public void AnalyzeCatalog_WithSinceAndUntilParameters_ShouldRecommendFormatHints()
    {
        var analysis = _analyzer.AnalyzeCatalog(
        [
            new ToolAiReadinessTarget
            {
                Name = "list-records",
                InputSchema = ParseJsonElement("""
                {
                  "type": "object",
                  "properties": {
                    "since": { "type": "string", "description": "Lower timestamp bound." },
                    "until": { "type": "string", "description": "Upper timestamp bound." }
                  }
                }
                """)
            }
        ],
        rawJson: null);

        analysis.Findings.Should().ContainSingle(finding =>
            finding.RuleId == ValidationFindingRuleIds.AiReadinessFormatHintMissing &&
            finding.Summary.Contains("2/2", StringComparison.Ordinal));
    }

    [Fact]
    public void AnalyzeErrorResponse_WithStructuredHelpfulError_ShouldGradeAsProLlm()
    {
        var assessment = _analyzer.AnalyzeErrorResponse(
            "strict_tool",
            """
            {
              "jsonrpc": "2.0",
              "error": {
                "code": -32602,
                "message": "Invalid params: argument 'id' must be a positive integer",
                "data": { "param": "id", "expectedType": "integer" }
              },
              "id": 1
            }
            """,
            -32602,
            "Invalid params: argument 'id' must be a positive integer");

        assessment.Finding.RuleId.Should().Be(ValidationFindingRuleIds.ToolLlmFriendliness);
        assessment.Finding.Severity.Should().Be(ValidationFindingSeverity.Info);
        assessment.Finding.Summary.Should().Contain("Pro-LLM");
        assessment.Finding.Metadata.Should().Contain(AiReadinessEvidenceKinds.MetadataKey, AiReadinessEvidenceKinds.DeterministicErrorHeuristic);
        assessment.Finding.Metadata.Should().Contain(AiReadinessEvidenceKinds.ModelEvaluationImpactKey, AiReadinessEvidenceKinds.NotMeasuredModelImpact);
        assessment.SupportingIssues.Should().BeEmpty();
    }

    [Fact]
    public void AnalyzeErrorResponse_WithUpstreamApiPassThrough_ShouldExcludeFromLlmAverage()
    {
        // Validator's synthetic input ("test-check/test-check") triggered the upstream
        // GitHub REST API to return 404. The MCP tool faithfully passed through the
        // upstream HTTP status and URL — that is informative for an LLM and is not
        // evidence of an Anti-LLM tool. Such findings must be marked excluded.
        var assessment = _analyzer.AnalyzeErrorResponse(
            "delete_file",
            """
            {
              "jsonrpc": "2.0",
              "error": {
                "code": 0,
                "message": "failed to get branch reference: GET https://api.github.com/repos/test-check/test-check/git/ref/heads/test-check: 404 Not Found []"
              },
              "id": 1
            }
            """,
            errorCode: 0,
            errorMessage: "failed to get branch reference: GET https://api.github.com/repos/test-check/test-check/git/ref/heads/test-check: 404 Not Found []");

        assessment.Finding.RuleId.Should().Be(ValidationFindingRuleIds.ToolLlmFriendliness);
        assessment.Finding.Severity.Should().Be(ValidationFindingSeverity.Info);
        assessment.Finding.Metadata.Should().ContainKey("excludedFromLlmAverage")
            .WhoseValue.Should().Be("true");
        assessment.Finding.Metadata.Should().ContainKey("exclusionReason")
            .WhoseValue.Should().Be("upstream-http-pass-through");
        assessment.Finding.Summary.Should().Contain("not scored");
    }

    private static JsonElement ParseJsonElement(string json)
    {
        using var document = JsonDocument.Parse(json);
        return document.RootElement.Clone();
    }
}