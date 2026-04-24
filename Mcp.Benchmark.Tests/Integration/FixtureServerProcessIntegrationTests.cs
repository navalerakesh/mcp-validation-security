using Mcp.Benchmark.Core.Abstractions;
using Mcp.Benchmark.Core.Models;
using Mcp.Benchmark.Infrastructure.Authentication;
using Mcp.Benchmark.Infrastructure.Http;
using Mcp.Benchmark.Infrastructure.Services;
using Mcp.Benchmark.Infrastructure.Validators;
using Mcp.Compliance.Spec;

namespace Mcp.Benchmark.Tests.Integration;

public class FixtureServerProcessIntegrationTests
{
    [Fact]
    public async Task CompliantFixtureServer_ShouldSupportInitializeAndCapabilityListing()
    {
        var command = BuildFixtureCommand("compliant");
        await using var adapter = await StartFixtureServerAsync(command);

        var initialize = await adapter.ValidateInitializeAsync(command, CancellationToken.None);
        var toolsList = await adapter.CallAsync(command, "tools/list", null, CancellationToken.None);
        var promptsList = await adapter.CallAsync(command, "prompts/list", null, CancellationToken.None);
        var resourcesList = await adapter.CallAsync(command, "resources/list", null, CancellationToken.None);

        initialize.IsSuccessful.Should().BeTrue();
        initialize.Payload.Should().NotBeNull();
        toolsList.IsSuccess.Should().BeTrue();
        toolsList.RawJson.Should().Contain("list_repositories");
        promptsList.IsSuccess.Should().BeTrue();
        promptsList.RawJson.Should().Contain("code_review");
        resourcesList.IsSuccess.Should().BeTrue();
        resourcesList.RawJson.Should().Contain("README.md");
    }

    [Fact]
    public async Task CompliantFixtureServer_ShouldSupportPaginatedToolDiscovery()
    {
        var command = BuildFixtureCommand("compliant");
        await using var adapter = await StartFixtureServerAsync(command);

        var serverConfig = new McpServerConfig
        {
            Endpoint = command,
            Transport = "stdio"
        };

        var toolValidator = CreateToolValidator(adapter);
        var result = await toolValidator.ValidateToolDiscoveryAsync(serverConfig, new ToolTestingConfig
        {
            TestToolDiscovery = true
        }, CancellationToken.None);

        result.ToolsDiscovered.Should().Be(2);
        result.DiscoveredToolNames.Should().Contain(new[] { "list_repositories", "get_repository" });
        result.Issues.Should().Contain(issue => issue.Contains("Pagination", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task StrictSessionFixtureServer_ShouldCompletePostInitializeTransitionBeforeToolDiscovery()
    {
        var command = BuildFixtureCommand("strict-session");
        await using var adapter = await StartFixtureServerAsync(command);

        var capabilitySnapshot = await adapter.ValidateCapabilitiesAsync(command, CancellationToken.None);

        var serverConfig = new McpServerConfig
        {
            Endpoint = command,
            Transport = "stdio"
        };

        var toolValidator = CreateToolValidator(adapter);
        var result = await toolValidator.ValidateToolDiscoveryAsync(serverConfig, new ToolTestingConfig
        {
            TestToolDiscovery = true,
            CapabilitySnapshot = capabilitySnapshot
        }, CancellationToken.None);

        capabilitySnapshot.IsSuccessful.Should().BeTrue();
        capabilitySnapshot.Payload.Should().NotBeNull();
        capabilitySnapshot.Payload!.ToolListResponse.Should().NotBeNull();
        result.ToolsDiscovered.Should().Be(2);
        result.DiscoveredToolNames.Should().Contain(new[] { "list_repositories", "get_repository" });
    }

    [Fact]
    public async Task PartialFixtureServer_ShouldSurfacePromptAndResourceFindings()
    {
        var command = BuildFixtureCommand("partial");
        await using var adapter = await StartFixtureServerAsync(command);

        var serverConfig = new McpServerConfig
        {
            Endpoint = command,
            Transport = "stdio"
        };

        var promptValidator = CreatePromptValidator(adapter);
        var resourceValidator = CreateResourceValidator(adapter);

        var promptResult = await promptValidator.ValidatePromptDiscoveryAsync(serverConfig, new PromptTestingConfig
        {
            TestPromptExecution = true
        }, CancellationToken.None);

        var resourceResult = await resourceValidator.ValidateResourceDiscoveryAsync(serverConfig, new ResourceTestingConfig
        {
            TestResourceReading = true
        }, CancellationToken.None);

        promptResult.PromptResults.Should().Contain(result => result.Findings.Any(finding => finding.RuleId == ValidationFindingRuleIds.PromptMissingName));
        promptResult.PromptResults.Should().Contain(result => result.PromptName == "broken_prompt" && result.Findings.Any(finding => finding.RuleId == ValidationFindingRuleIds.PromptMessageMissingRole));
        resourceResult.ResourceResults.Should().Contain(result => result.Findings.Any(finding => finding.RuleId == ValidationFindingRuleIds.ResourceMissingUri));
        resourceResult.ResourceResults.Should().Contain(result => result.ResourceName == "BROKEN.md" && result.Findings.Any(finding => finding.RuleId == ValidationFindingRuleIds.ResourceReadMissingTextOrBlob));
        resourceResult.Findings.Should().Contain(finding => finding.RuleId == ValidationFindingRuleIds.ResourceGuidelineMimeTypeMissing);
        resourceResult.Findings.Should().Contain(finding => finding.RuleId == ValidationFindingRuleIds.ResourceUriSchemeUnclear);
        resourceResult.Findings.Should().Contain(finding => finding.RuleId == ValidationFindingRuleIds.ResourceTemplateMissingUriTemplate);
        resourceResult.Findings.Should().Contain(finding => finding.RuleId == ValidationFindingRuleIds.ResourceTemplateDescriptionMissing);
    }

    [Fact]
    public async Task UnsafeFixtureServer_ShouldSurfaceToolSafetyFindings()
    {
        var command = BuildFixtureCommand("unsafe");
        await using var adapter = await StartFixtureServerAsync(command);

        var serverConfig = new McpServerConfig
        {
            Endpoint = command,
            Transport = "stdio"
        };

        var toolValidator = CreateToolValidator(adapter);
        var result = await toolValidator.ValidateToolDiscoveryAsync(serverConfig, new ToolTestingConfig
        {
            TestToolDiscovery = true
        }, CancellationToken.None);

        result.ToolResults.Should().Contain(tool => tool.ToolName == "delete_repository" && tool.Findings.Any(finding => finding.RuleId == ValidationFindingRuleIds.ToolGuidelineDestructiveHintMissing));
        result.ToolResults.Should().Contain(tool => tool.ToolName == "purge_repository" && tool.Findings.Any(finding => finding.RuleId == ValidationFindingRuleIds.ToolDestructiveConfirmationGuidanceMissing));
    }

    private static string BuildFixtureCommand(string profile)
    {
        var scriptPath = Path.Combine(AppContext.BaseDirectory, "Fixtures", "Servers", $"mcp-fixture-{profile}.cjs");
        if (!File.Exists(scriptPath))
        {
            throw new FileNotFoundException($"Fixture server script not found: {scriptPath}");
        }

        return $"node \"{scriptPath}\"";
    }

    private static async Task<StdioMcpClientAdapter> StartFixtureServerAsync(string command)
    {
        var adapter = new StdioMcpClientAdapter(new Mock<ILogger<StdioMcpClientAdapter>>().Object);
        await adapter.StartProcessAsync(command, null, CancellationToken.None);
        return adapter;
    }

    private static ToolValidator CreateToolValidator(IMcpHttpClient client)
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
            client,
            new Mock<ISchemaValidator>().Object,
            schemaRegistry.Object,
            new Mock<IAuthenticationService>().Object,
            contentSafetyAnalyzer.Object,
            new ToolAiReadinessAnalyzer());
    }

    private static PromptValidator CreatePromptValidator(IMcpHttpClient client)
    {
        var contentSafetyAnalyzer = new Mock<IContentSafetyAnalyzer>();
        contentSafetyAnalyzer
            .Setup(analyzer => analyzer.AnalyzePrompt(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<int>()))
            .Returns(new List<ContentSafetyFinding>());

        return new PromptValidator(
            new Mock<ILogger<PromptValidator>>().Object,
            client,
            new Mock<ISchemaValidator>().Object,
            new Mock<ISchemaRegistry>().Object,
            contentSafetyAnalyzer.Object);
    }

    private static ResourceValidator CreateResourceValidator(IMcpHttpClient client)
    {
        var contentSafetyAnalyzer = new Mock<IContentSafetyAnalyzer>();
        contentSafetyAnalyzer
            .Setup(analyzer => analyzer.AnalyzeResource(It.IsAny<string>(), It.IsAny<string>()))
            .Returns(new List<ContentSafetyFinding>());

        return new ResourceValidator(
            new Mock<ILogger<ResourceValidator>>().Object,
            client,
            new Mock<ISchemaValidator>().Object,
            new Mock<ISchemaRegistry>().Object,
            contentSafetyAnalyzer.Object);
    }
}