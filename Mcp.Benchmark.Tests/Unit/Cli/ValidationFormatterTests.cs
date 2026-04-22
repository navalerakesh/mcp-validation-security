using System.IO;
using FluentAssertions;
using Mcp.Benchmark.CLI.Services.Formatters;
using Mcp.Benchmark.Core.Models;
using Mcp.Benchmark.Core.Resources;

namespace Mcp.Benchmark.Tests.Unit.Cli;

public class ValidationFormatterTests
{
    [Fact]
    public void DisplayResults_WithAuthenticationLimitedSkip_ShouldShowAuthenticationLimitationsNote()
    {
        var result = CreateMinimalResult();
        result.PerformanceTesting = new PerformanceTestResult
        {
            Status = TestStatus.Skipped,
            Message = "Server requires authentication — performance testing skipped",
            Findings = new List<ValidationFinding>
            {
                new()
                {
                    RuleId = ValidationFindingRuleIds.PerformanceAuthRequiredAdvisory,
                    Category = "Performance",
                    Component = "load-testing",
                    Severity = ValidationFindingSeverity.Info,
                    Summary = "Performance score is advisory because the endpoint requires authentication before a synthetic load probe can be run safely."
                }
            }
        };

        var output = CaptureOutput(() => ValidationFormatter.DisplayResults(result, showDetails: false, useColors: false, verbose: false));

        output.Should().Contain(Strings.Auth_Limitations_Title);
        output.Should().Contain("Skipped Components: Performance");
    }

    [Fact]
    public void DisplayResults_WithPublicAdvisoryPerformanceSkip_ShouldNotShowAuthenticationLimitationsNote()
    {
        var result = CreateMinimalResult();
        result.PerformanceTesting = new PerformanceTestResult
        {
            Status = TestStatus.Skipped,
            Message = "Public remote synthetic load probe did not capture measurements before timeout/cancellation; results are reported as advisory and excluded from pass/fail decisions.",
            Findings = new List<ValidationFinding>
            {
                new()
                {
                    RuleId = ValidationFindingRuleIds.PerformancePublicRemoteTimeoutAdvisory,
                    Category = "Performance",
                    Component = "load-testing",
                    Severity = ValidationFindingSeverity.Info,
                    Summary = "Public remote synthetic load probe did not capture any measurements before timing out or being cancelled, so the performance result is treated as advisory rather than a readiness failure."
                }
            }
        };

        var output = CaptureOutput(() => ValidationFormatter.DisplayResults(result, showDetails: false, useColors: false, verbose: false));

        output.Should().NotContain(Strings.Auth_Limitations_Title);
        output.Should().NotContain("Skipped Components: Performance");
    }

    [Fact]
    public void DisplayResults_WithPublicProfileAuthScenario_ShouldShowCanonicalExpectedBehavior()
    {
        var result = CreateMinimalResult();
        result.SecurityTesting = new SecurityTestResult
        {
            Status = TestStatus.Passed,
            SecurityScore = 100,
            AuthenticationTestResult = new AuthenticationTestResult
            {
                ComplianceScore = 100,
                TestScenarios = new List<AuthenticationScenario>
                {
                    new()
                    {
                        ScenarioName = "No Auth - initialize",
                        TestType = "No Auth",
                        Method = "initialize",
                        ExpectedBehavior = "Informational (public profile)",
                        ActualBehavior = "Discovery metadata returned",
                        StatusCode = "200",
                        Analysis = "INFO (Public profile): COMPATIBLE: Discovery metadata was exposed without authentication.",
                        IsCompliant = true
                    }
                }
            }
        };

        var output = CaptureOutput(() => ValidationFormatter.DisplayResults(result, showDetails: false, useColors: false, verbose: false));

        output.Should().Contain("Informational (public pro");
        output.Should().Contain("Discovery metad...");
        output.Should().NotContain("400/401 + WWW-Auth");
    }

    [Fact]
    public void DisplayResults_WithTimedOutPerformanceAndNoMeasurements_ShouldShowUnavailableScore()
    {
        var result = CreateMinimalResult();
        result.PerformanceTesting = new PerformanceTestResult
        {
            Status = TestStatus.Failed,
            Message = "Operation timed out or was cancelled"
        };

        var output = CaptureOutput(() => ValidationFormatter.DisplayResults(result, showDetails: true, useColors: false, verbose: false));

        output.Should().Contain("Performance     FAILED       Unavailable");
        output.Should().Contain("Score: Unavailable");
        output.Should().NotContain("Score: 0.0%");
    }

    private static ValidationResult CreateMinimalResult()
    {
        return new ValidationResult
        {
            ValidationId = "formatter-test",
            OverallStatus = ValidationStatus.Passed,
            ComplianceScore = 85,
            ServerConfig = new McpServerConfig
            {
                Endpoint = "https://example.test/mcp",
                Transport = "http"
            }
        };
    }

    private static string CaptureOutput(Action action)
    {
        var originalOut = Console.Out;
        using var writer = new StringWriter();

        Console.SetOut(writer);
        try
        {
            action();
        }
        finally
        {
            Console.SetOut(originalOut);
        }

        return writer.ToString();
    }
}