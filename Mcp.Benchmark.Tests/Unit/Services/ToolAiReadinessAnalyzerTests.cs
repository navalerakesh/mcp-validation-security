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
        assessment.SupportingIssues.Should().BeEmpty();
    }

    private static JsonElement ParseJsonElement(string json)
    {
        using var document = JsonDocument.Parse(json);
        return document.RootElement.Clone();
    }
}