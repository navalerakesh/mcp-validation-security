using Mcp.Benchmark.Core.Models;
using Mcp.Benchmark.Core.Constants;
using Mcp.Benchmark.Infrastructure.Scoring;
using FluentAssertions;
using Xunit;

namespace Mcp.Benchmark.Tests.Unit.Scoring;

public class SecurityFocusedScoringEdgeCaseTests
{
    private readonly SecurityFocusedScoringStrategy _strategy = new();

    [Fact]
    public void CalculateScore_WithOnlyProtocolAndSecurity_ShouldApplyWeights()
    {
        var result = new ValidationResult
        {
            ProtocolCompliance = new ComplianceTestResult { Status = TestStatus.Passed, Score = 100, JsonRpcCompliance = new() { RequestFormatCompliant = true, ResponseFormatCompliant = true } },
            SecurityTesting = new SecurityTestResult { Status = TestStatus.Passed, SecurityScore = 100 }
        };

        var score = _strategy.CalculateScore(result);

        score.OverallScore.Should().BeGreaterThan(0);
        score.CategoryScores.Should().ContainKey("Protocol");
        score.CategoryScores.Should().ContainKey("Security");
        score.CategoryScores.Should().ContainKey("ErrorHandling");
    }

    [Fact]
    public void CalculateScore_WithAllCategoriesPassed_ShouldBe100()
    {
        var result = BuildFullResult(TestStatus.Passed, 100, 100, 100, 100, 100);

        var score = _strategy.CalculateScore(result);

        score.OverallScore.Should().Be(100);
        score.CoverageRatio.Should().Be(1.0);
    }

    [Fact]
    public void CalculateScore_WithSecurityZero_ShouldBeLow()
    {
        var result = BuildFullResult(TestStatus.Passed, 100, 0, 100, 100, 100);

        var score = _strategy.CalculateScore(result);

        score.OverallScore.Should().BeLessThan(60);
    }

    [Fact]
    public void CalculateScore_WithJsonRpcViolations_ShouldDeduct15()
    {
        var result = BuildFullResult(TestStatus.Passed, 100, 100, 100, 100, 100);
        result.ProtocolCompliance!.JsonRpcCompliance.RequestFormatCompliant = false;

        var score = _strategy.CalculateScore(result);

        score.OverallScore.Should().BeLessThan(100);
        score.ScoringNotes.Should().Contain(n => n.Contains("JSON-RPC"));
    }

    [Fact]
    public void CalculateScore_WithSecurityBreach_OnEnterprise_ShouldBeZero()
    {
        var result = BuildFullResult(TestStatus.Passed, 100, 100, 100, 100, 100);
        result.ServerProfile = McpServerProfile.Enterprise;
        result.SecurityTesting!.AuthenticationTestResult = new AuthenticationTestResult
        {
            TestScenarios =
            {
                new AuthenticationScenario
                {
                    TestType = "No Auth",
                    Method = "tools/call",
                    StatusCode = "200",
                    IsCompliant = false,
                    IsSecure = false,
                    AssessmentScore = 0,
                    AssessmentDisposition = AuthenticationAssessmentDisposition.Insecure
                }
            }
        };

        var score = _strategy.CalculateScore(result);

        score.OverallScore.Should().Be(20);
        score.Status.Should().Be(ValidationStatus.Failed);
    }

    [Fact]
    public void CalculateScore_WithSecurityBreach_OnPublic_ShouldNotZero()
    {
        var result = BuildFullResult(TestStatus.Passed, 100, 100, 100, 100, 100);
        result.ServerProfile = McpServerProfile.Public;
        result.SecurityTesting!.AuthenticationTestResult = new AuthenticationTestResult
        {
            TestScenarios = { new AuthenticationScenario { TestType = "No Auth", StatusCode = "200", IsCompliant = false } }
        };

        var score = _strategy.CalculateScore(result);

        score.OverallScore.Should().BeGreaterThan(0);
    }

    [Fact]
    public void CalculateScore_WithLowCoverage_ShouldCapAt60()
    {
        var result = new ValidationResult
        {
            ProtocolCompliance = new ComplianceTestResult { Status = TestStatus.Passed, Score = 100, JsonRpcCompliance = new() { RequestFormatCompliant = true, ResponseFormatCompliant = true } },
            SecurityTesting = new SecurityTestResult { Status = TestStatus.Skipped },
            ToolValidation = new ToolTestResult { Status = TestStatus.Skipped },
            ResourceTesting = new ResourceTestResult { Status = TestStatus.Skipped },
            PromptTesting = new PromptTestResult { Status = TestStatus.Skipped },
            PerformanceTesting = new PerformanceTestResult { Status = TestStatus.Skipped }
        };

        var score = _strategy.CalculateScore(result);

        score.OverallScore.Should().BeLessThanOrEqualTo(ScoringConstants.LowCoverageScoreCap);
    }

    [Fact]
    public void CalculateScore_PerformanceWeightShouldBeZero()
    {
        // Performance has weight 0 now — it's informational only
        var result = BuildFullResult(TestStatus.Passed, 100, 100, 100, 100, 100);
        var resultWithFailedPerf = BuildFullResult(TestStatus.Failed, 100, 100, 100, 100, 100);

        var score1 = _strategy.CalculateScore(result);
        var score2 = _strategy.CalculateScore(resultWithFailedPerf);

        // Scores should be the same because performance has no weight
        score1.OverallScore.Should().Be(score2.OverallScore);
    }

    [Fact]
    public void CalculateScore_WithRepeatedGuidelineFindingsAcrossDifferentCatalogSizes_ShouldRemainEqual()
    {
        var fiveToolResult = BuildFullResult(TestStatus.Passed, 100, 100, 100, 100, 100);
        fiveToolResult.ToolValidation = BuildToolValidationWithGuidelineFindings(5);

        var fortyToolResult = BuildFullResult(TestStatus.Passed, 100, 100, 100, 100, 100);
        fortyToolResult.ToolValidation = BuildToolValidationWithGuidelineFindings(40);

        var fiveToolScore = _strategy.CalculateScore(fiveToolResult);
        var fortyToolScore = _strategy.CalculateScore(fortyToolResult);

        fiveToolScore.OverallScore.Should().Be(fortyToolScore.OverallScore);
        fiveToolScore.Status.Should().Be(fortyToolScore.Status);
    }

    private static ValidationResult BuildFullResult(TestStatus perfStatus, double proto, double sec, double tools, double res, double prompts)
    {
        return new ValidationResult
        {
            ProtocolCompliance = new ComplianceTestResult { Status = TestStatus.Passed, Score = proto, JsonRpcCompliance = new() { RequestFormatCompliant = true, ResponseFormatCompliant = true } },
            SecurityTesting = new SecurityTestResult { Status = TestStatus.Passed, SecurityScore = sec },
            ToolValidation = new ToolTestResult { Status = TestStatus.Passed, Score = tools, ToolsDiscovered = 1, ToolsTestPassed = 1 },
            ResourceTesting = new ResourceTestResult { Status = TestStatus.Passed, Score = res, ResourcesDiscovered = 1, ResourcesAccessible = 1 },
            PromptTesting = new PromptTestResult { Status = TestStatus.Passed, Score = prompts, PromptsDiscovered = 1, PromptsTestPassed = 1 },
            ErrorHandling = new ErrorHandlingTestResult { Status = TestStatus.Passed, Score = 100 },
            PerformanceTesting = new PerformanceTestResult { Status = perfStatus, Score = perfStatus == TestStatus.Passed ? 100 : 0 }
        };
    }

    private static ToolTestResult BuildToolValidationWithGuidelineFindings(int toolCount)
    {
        var toolResults = Enumerable.Range(1, toolCount)
            .Select(index => new IndividualToolResult
            {
                ToolName = $"tool_{index}",
                Status = TestStatus.Passed,
                Findings = new List<ValidationFinding>
                {
                    new()
                    {
                        RuleId = ValidationFindingRuleIds.ToolGuidelineDestructiveHintMissing,
                        Category = "McpGuideline",
                        Component = $"tool_{index}",
                        Severity = ValidationFindingSeverity.Low,
                        Summary = $"Tool 'tool_{index}' does not declare annotations.destructiveHint."
                    }
                }
            })
            .ToList();

        return new ToolTestResult
        {
            Status = TestStatus.Passed,
            Score = 100,
            ToolsDiscovered = toolCount,
            ToolsTestPassed = toolCount,
            ToolResults = toolResults
        };
    }
}
