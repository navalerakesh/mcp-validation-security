using FluentAssertions;
using Mcp.Benchmark.Core.Constants;
using Mcp.Benchmark.Core.Models;
using Mcp.Benchmark.Infrastructure.Scenarios;

namespace Mcp.Benchmark.Tests.Unit.Scenarios;

public sealed class McpAppsSecurityScenarioPackTests
{
    [Fact]
    public async Task ExecuteAsync_CompliantTool_ShouldPassWithoutFindings()
    {
        var result = CreateResult(new McpAppToolMetadata
        {
            IsPresent = true,
            ResourceUri = "ui://weather/dashboard",
            Visibility = ["model", "app"]
        });

        var execution = await ExecuteAsync(result);

        execution.Scenario.Status.Should().Be(TestStatus.Passed);
        execution.Scenario.Findings.Should().BeEmpty();
        execution.Coverage.Should().OnlyContain(item => item.Lane == EvaluationLane.ExtensionReadiness);
    }

    [Fact]
    public async Task ExecuteAsync_MalformedMetadataAndMimeType_ShouldProduceStableSpecFailures()
    {
        var result = CreateResult(new McpAppToolMetadata
        {
            IsPresent = true,
            ResourceUri = "https://example.test/app",
            Visibility = ["model", "admin"],
            UsesDeprecatedResourceUri = true,
            IsMalformed = true,
            Issues = ["metadata mismatch"]
        }, description: null);
        result.ResourceTesting = new ResourceTestResult
        {
            ResourceResults =
            [
                new IndividualResourceResult
                {
                    ResourceUri = "https://example.test/app",
                    MimeType = "text/html"
                }
            ]
        };

        var execution = await ExecuteAsync(result);

        execution.Scenario.Status.Should().Be(TestStatus.Failed);
        execution.Scenario.Findings.Select(finding => finding.RuleId).Should().Contain([
            ValidationFindingRuleIds.AppsToolMetadataMalformed,
            ValidationFindingRuleIds.AppsResourceUriInvalid,
            ValidationFindingRuleIds.AppsVisibilityInvalid,
            ValidationFindingRuleIds.AppsDeprecatedResourceUri,
            ValidationFindingRuleIds.AppsFallbackDescriptionMissing,
            ValidationFindingRuleIds.AppsResourceMimeTypeInvalid
        ]);
        execution.Scenario.Findings.Should().OnlyContain(finding => finding.ExcludedFromAggregate);
    }

    private static async Task<ValidationScenarioExecutionResult> ExecuteAsync(ValidationResult result)
    {
        var pack = new McpAppsSecurityScenarioPack();
        return await pack.GetScenarios().Single(scenario => scenario.Descriptor.Key.Value == "apps-passive-tool-linkage").ExecuteAsync(new ValidationScenarioContext
        {
            ServerConfig = result.ServerConfig,
            ApplicabilityContext = new ValidationApplicabilityContext
            {
                NegotiatedProtocolVersion = "2026-07-28",
                SchemaVersion = "2026-07-28",
                Transport = "http",
                AccessMode = "public",
                AdvertisedExtensionIds = [McpExtensionCatalog.Apps.Id]
            },
            ValidationConfiguration = result.ValidationConfig,
            ValidationResult = result
        }, CancellationToken.None);
    }

    private static ValidationResult CreateResult(McpAppToolMetadata metadata, string? description = "Get weather data") => new()
    {
        ServerConfig = new McpServerConfig { Endpoint = "https://example.test/mcp", Transport = "http" },
        ToolValidation = new ToolTestResult
        {
            ToolResults =
            [
                new IndividualToolResult
                {
                    ToolName = "weather",
                    Description = description,
                    AppMetadata = metadata
                }
            ]
        }
    };
}