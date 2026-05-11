using System.Text.Json;
using FluentAssertions;
using Mcp.Benchmark.Core.Models;
using Mcp.Benchmark.Infrastructure.Services;

namespace Mcp.Benchmark.Tests.Unit.Services;

public class ToolAiReadinessAnalyzerTests
{
    private readonly ToolAiReadinessAnalyzer _analyzer = new();

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