using System;
using Mcp.Benchmark.Core.Models;
using Mcp.Benchmark.Infrastructure.Scoring;
using Xunit;
using FluentAssertions;

namespace Mcp.Benchmark.Tests.Unit.Scoring;

public class SecurityFocusedScoringStrategyTests
{
    private readonly SecurityFocusedScoringStrategy _strategy;

    public SecurityFocusedScoringStrategyTests()
    {
        _strategy = new SecurityFocusedScoringStrategy();
    }

    [Fact]
    public void CalculateScore_Should_Return_Zero_When_No_Results()
    {
        // Arrange
        var results = new ValidationResult();

        // Act
        var score = _strategy.CalculateScore(results);

        // Assert
        score.OverallScore.Should().Be(0);
        score.Status.Should().Be(ValidationStatus.Failed);
    }

    [Fact]
    public void CalculateScore_Should_Prioritize_Security_Failures()
    {
        // Arrange
        var results = new ValidationResult();
        
        // Add a passing protocol test
        results.ProtocolCompliance = new ComplianceTestResult 
        { 
            Status = TestStatus.Passed,
            Score = 100
        };

        // Add a failing security test
        results.SecurityTesting = new SecurityTestResult 
        { 
            Status = TestStatus.Failed,
            Score = 0,
            SecurityScore = 0
        };

        // Act
        var score = _strategy.CalculateScore(results);

        // Assert
        // Security failures should heavily impact the score in this strategy
        score.OverallScore.Should().BeLessThan(50); 
        score.Status.Should().Be(ValidationStatus.Failed);
    }

    [Fact]
    public void CalculateScore_Should_Apply_CoverageMultiplier_When_CategoriesSkipped()
    {
        // Arrange
        var results = BuildBaselineResult(McpServerProfile.Authenticated);
        results.SecurityTesting!.AuthenticationTestResult = null; // Remove auth scenarios for this test

        // Act
        var score = _strategy.CalculateScore(results);

        // Assert
        // Protocol (0.35) + Security (0.45) = 0.80 included (Tools/Resources/Prompts skipped, Performance weight=0)
        score.CoverageRatio.Should().BeApproximately(0.80, 0.01);
        score.OverallScore.Should().BeApproximately(80.0, 0.01);
        score.Status.Should().Be(ValidationStatus.Passed);
    }

    [Fact]
    public void CalculateScore_Should_Treat_AuthBreaches_As_Critical_For_Strict_Profiles()
    {
        // Arrange
        var results = BuildBaselineResult(McpServerProfile.Enterprise);

        // Act
        var score = _strategy.CalculateScore(results);

        // Assert
        score.OverallScore.Should().Be(0);
        score.Status.Should().Be(ValidationStatus.Failed);
        score.ScoringNotes.Should().Contain(note => note.Contains("CRITICAL", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void CalculateScore_Should_Downgrade_AuthBreaches_For_Public_Profile()
    {
        // Arrange
        var results = BuildBaselineResult(McpServerProfile.Public);

        // Act
        var score = _strategy.CalculateScore(results);

        // Assert
        score.Status.Should().Be(ValidationStatus.Passed);
        score.OverallScore.Should().BeGreaterThan(0);
        score.ScoringNotes.Should().Contain(note => note.Contains("INFO", StringComparison.OrdinalIgnoreCase));
    }

    private static ValidationResult BuildBaselineResult(McpServerProfile profile)
    {
        return new ValidationResult
        {
            ServerProfile = profile,
            ProtocolCompliance = new ComplianceTestResult
            {
                Status = TestStatus.Passed,
                Score = 100,
                JsonRpcCompliance = new JsonRpcComplianceResult
                {
                    RequestFormatCompliant = true,
                    ResponseFormatCompliant = true
                }
            },
            SecurityTesting = new SecurityTestResult
            {
                Status = TestStatus.Passed,
                SecurityScore = 100,
                AuthenticationTestResult = new AuthenticationTestResult
                {
                    TestScenarios =
                    {
                        new AuthenticationScenario
                        {
                            TestType = "No Auth",
                            StatusCode = "200",
                            IsCompliant = false
                        }
                    }
                }
            },
            ToolValidation = new ToolTestResult
            {
                Status = TestStatus.Skipped,
                Score = 0
            },
            ResourceTesting = new ResourceTestResult
            {
                Status = TestStatus.Skipped,
                Score = 0
            },
            PromptTesting = new PromptTestResult
            {
                Status = TestStatus.Skipped,
                Score = 0
            },
            PerformanceTesting = new PerformanceTestResult
            {
                Status = TestStatus.Skipped,
                Score = 0
            }
        };
    }
}
