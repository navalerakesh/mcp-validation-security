using Mcp.Benchmark.Core.Abstractions;
using Mcp.Benchmark.Core.Constants;
using Mcp.Benchmark.Core.Models;
using Mcp.Benchmark.Infrastructure.Authentication;
using Mcp.Benchmark.Infrastructure.Services;
using Mcp.Benchmark.Infrastructure.Validators;
using Mcp.Benchmark.Tests.Fixtures;
using Mcp.Compliance.Spec;

namespace Mcp.Benchmark.Tests.Integration;

public class FixtureProfileRegressionTests
{
    [Fact]
    public async Task CompliantProfile_ShouldRemainHealthyAcrossToolPromptAndResourceValidators()
    {
        var serverConfig = CreateServerConfig();

        var toolClient = new Mock<IMcpHttpClient>();
        toolClient
            .Setup(client => client.CallAsync(serverConfig.Endpoint!, ValidationConstants.Methods.ToolsList, null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(McpFixtureProfiles.CreateToolsListResponse(McpFixtureProfileKind.Compliant));

        var toolValidator = CreateToolValidator(toolClient);
        var toolResult = await toolValidator.ValidateToolDiscoveryAsync(serverConfig, new ToolTestingConfig
        {
            TestToolDiscovery = true
        });

        toolResult.ToolsDiscovered.Should().Be(1);
        toolResult.ToolResults.Should().ContainSingle();
        toolResult.ToolResults[0].MetadataValid.Should().BeTrue();
        toolResult.ToolResults[0].Findings.Select(finding => finding.RuleId).Should().NotContain(new[]
        {
            ValidationFindingRuleIds.ToolGuidelineDisplayTitleMissing,
            ValidationFindingRuleIds.ToolGuidelineReadOnlyHintMissing,
            ValidationFindingRuleIds.ToolGuidelineDestructiveHintMissing,
            ValidationFindingRuleIds.ToolGuidelineOpenWorldHintMissing,
            ValidationFindingRuleIds.ToolGuidelineIdempotentHintMissing,
            ValidationFindingRuleIds.AiReadinessMissingParameterDescriptions
        });

        var promptClient = new Mock<IMcpHttpClient>();
        promptClient
            .Setup(client => client.CallAsync(serverConfig.Endpoint!, ValidationConstants.Methods.PromptsList, null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(McpFixtureProfiles.CreatePromptsListResponse(McpFixtureProfileKind.Compliant));
        promptClient
            .Setup(client => client.CallAsync(serverConfig.Endpoint!, ValidationConstants.Methods.PromptsGet, It.IsAny<object>(), It.IsAny<AuthenticationConfig>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(McpFixtureProfiles.CreatePromptGetResponse(McpFixtureProfileKind.Compliant));

        var promptValidator = CreatePromptValidator(promptClient);
        var promptResult = await promptValidator.ValidatePromptDiscoveryAsync(serverConfig, new PromptTestingConfig
        {
            TestPromptExecution = true
        }, CancellationToken.None);

        promptResult.PromptsDiscovered.Should().Be(1);
        promptResult.PromptResults.Should().ContainSingle();
        promptResult.PromptResults[0].MetadataValid.Should().BeTrue();
        promptResult.PromptResults[0].ExecutionSuccessful.Should().BeTrue();

        var resourceClient = new Mock<IMcpHttpClient>();
        resourceClient
            .Setup(client => client.CallAsync(serverConfig.Endpoint!, ValidationConstants.Methods.ResourcesList, null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(McpFixtureProfiles.CreateResourcesListResponse(McpFixtureProfileKind.Compliant));
        resourceClient
            .Setup(client => client.CallAsync(serverConfig.Endpoint!, ValidationConstants.Methods.ResourcesRead, It.IsAny<object>(), It.IsAny<AuthenticationConfig>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(McpFixtureProfiles.CreateResourceReadResponse(McpFixtureProfileKind.Compliant));

        var resourceValidator = CreateResourceValidator(resourceClient);
        var resourceResult = await resourceValidator.ValidateResourceDiscoveryAsync(serverConfig, new ResourceTestingConfig
        {
            TestResourceReading = true
        }, CancellationToken.None);

        resourceResult.ResourcesDiscovered.Should().Be(1);
        resourceResult.ResourceResults.Should().ContainSingle();
        resourceResult.ResourceResults[0].MetadataValid.Should().BeTrue();
        resourceResult.ResourceResults[0].AccessSuccessful.Should().BeTrue();
    }

    [Fact]
    public async Task PartiallyCompliantProfile_ShouldExposeStableMetadataFailures()
    {
        var serverConfig = CreateServerConfig();

        var promptClient = new Mock<IMcpHttpClient>();
        promptClient
            .Setup(client => client.CallAsync(serverConfig.Endpoint!, ValidationConstants.Methods.PromptsList, null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(McpFixtureProfiles.CreatePromptsListResponse(McpFixtureProfileKind.PartiallyCompliant));

        var promptValidator = CreatePromptValidator(promptClient);
        var promptResult = await promptValidator.ValidatePromptDiscoveryAsync(serverConfig, new PromptTestingConfig
        {
            TestPromptExecution = false
        }, CancellationToken.None);

        promptResult.PromptResults.Should().ContainSingle();
        promptResult.PromptResults[0].Findings.Should().Contain(finding => finding.RuleId == ValidationFindingRuleIds.PromptMissingName);

        var resourceClient = new Mock<IMcpHttpClient>();
        resourceClient
            .Setup(client => client.CallAsync(serverConfig.Endpoint!, ValidationConstants.Methods.ResourcesList, null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(McpFixtureProfiles.CreateResourcesListResponse(McpFixtureProfileKind.PartiallyCompliant));

        var resourceValidator = CreateResourceValidator(resourceClient);
        var resourceResult = await resourceValidator.ValidateResourceDiscoveryAsync(serverConfig, new ResourceTestingConfig
        {
            TestResourceReading = false
        }, CancellationToken.None);

        resourceResult.ResourceResults.Should().ContainSingle();
        resourceResult.ResourceResults[0].Findings.Should().Contain(finding => finding.RuleId == ValidationFindingRuleIds.ResourceMissingUri);
    }

    [Fact]
    public async Task UnsafeProfile_ShouldExposeToolSafetyRegressionSignals()
    {
        var serverConfig = CreateServerConfig();
        var toolClient = new Mock<IMcpHttpClient>();
        toolClient
            .Setup(client => client.CallAsync(serverConfig.Endpoint!, ValidationConstants.Methods.ToolsList, null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(McpFixtureProfiles.CreateToolsListResponse(McpFixtureProfileKind.Unsafe));

        var toolValidator = CreateToolValidator(toolClient);
        var toolResult = await toolValidator.ValidateToolDiscoveryAsync(serverConfig, new ToolTestingConfig
        {
            TestToolDiscovery = true
        });

        toolResult.ToolResults.Should().HaveCount(2);
        toolResult.ToolResults.Should().Contain(result => result.ToolName == "delete_repository" && result.Findings.Any(finding => finding.RuleId == ValidationFindingRuleIds.ToolGuidelineDestructiveHintMissing));
        toolResult.ToolResults.Should().Contain(result => result.ToolName == "purge_repository" && result.Findings.Any(finding => finding.RuleId == ValidationFindingRuleIds.ToolDestructiveConfirmationGuidanceMissing));
        toolResult.AiReadinessFindings.Should().Contain(finding => finding.RuleId == ValidationFindingRuleIds.AiReadinessMissingParameterDescriptions);
    }

    private static McpServerConfig CreateServerConfig()
    {
        return new McpServerConfig
        {
            Endpoint = "https://fixture.example.test/mcp",
            Transport = "http"
        };
    }

    private static ToolValidator CreateToolValidator(Mock<IMcpHttpClient> httpClient)
    {
        var schemaRegistry = new Mock<ISchemaRegistry>();
        schemaRegistry
            .Setup(registry => registry.GetSchema(It.IsAny<Mcp.Compliance.Spec.ProtocolVersion>(), It.IsAny<string>(), It.IsAny<string>()))
            .Throws(new FileNotFoundException("Schema not found"));

        var contentSafetyAnalyzer = new Mock<IContentSafetyAnalyzer>();
        contentSafetyAnalyzer
            .Setup(analyzer => analyzer.AnalyzeTool(It.IsAny<string>()))
            .Returns(new List<ContentSafetyFinding>());

        return new ToolValidator(
            new Mock<ILogger<ToolValidator>>().Object,
            httpClient.Object,
            new Mock<ISchemaValidator>().Object,
            schemaRegistry.Object,
            new Mock<IAuthenticationService>().Object,
            contentSafetyAnalyzer.Object,
            new ToolAiReadinessAnalyzer());
    }

    private static PromptValidator CreatePromptValidator(Mock<IMcpHttpClient> httpClient)
    {
        var contentSafetyAnalyzer = new Mock<IContentSafetyAnalyzer>();
        contentSafetyAnalyzer
            .Setup(analyzer => analyzer.AnalyzePrompt(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<int>()))
            .Returns(new List<ContentSafetyFinding>());

        return new PromptValidator(
            new Mock<ILogger<PromptValidator>>().Object,
            httpClient.Object,
            new Mock<ISchemaValidator>().Object,
            new Mock<ISchemaRegistry>().Object,
            contentSafetyAnalyzer.Object);
    }

    private static ResourceValidator CreateResourceValidator(Mock<IMcpHttpClient> httpClient)
    {
        var contentSafetyAnalyzer = new Mock<IContentSafetyAnalyzer>();
        contentSafetyAnalyzer
            .Setup(analyzer => analyzer.AnalyzeResource(It.IsAny<string>(), It.IsAny<string>()))
            .Returns(new List<ContentSafetyFinding>());

        return new ResourceValidator(
            new Mock<ILogger<ResourceValidator>>().Object,
            httpClient.Object,
            new Mock<ISchemaValidator>().Object,
            new Mock<ISchemaRegistry>().Object,
            contentSafetyAnalyzer.Object);
    }
}