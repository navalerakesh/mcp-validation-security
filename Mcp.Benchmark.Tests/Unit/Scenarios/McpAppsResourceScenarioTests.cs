using FluentAssertions;
using Mcp.Benchmark.Core.Abstractions;
using Mcp.Benchmark.Core.Constants;
using Mcp.Benchmark.Core.Models;
using Mcp.Benchmark.Infrastructure.Scenarios;
using Moq;

namespace Mcp.Benchmark.Tests.Unit.Scenarios;

public sealed class McpAppsResourceScenarioTests
{
    [Fact]
    public async Task ExecuteAsync_SafeMode_ShouldNotReadLinkedResource()
    {
        var client = new Mock<IMcpHttpClient>(MockBehavior.Strict);
        var execution = await ExecuteAsync(client.Object, ExecutionMode.Safe);

        execution.Scenario.Status.Should().Be(TestStatus.Skipped);
        client.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task ExecuteAsync_StandardMode_ShouldReadAndRetainDigestOnly()
    {
        var client = new Mock<IMcpHttpClient>();
        client.Setup(instance => instance.CallAsync(
                "https://example.test/mcp",
                McpSpecConstants.ResourceMethods.Read,
                It.IsAny<object>(),
                It.IsAny<AuthenticationConfig?>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new JsonRpcResponse
            {
                IsSuccess = true,
                RawJson = """
                    {"result":{"contents":[{"uri":"ui://weather/dashboard","mimeType":"text/html;profile=mcp-app","text":"<!doctype html><html></html>"}]}}
                    """
            });

        var execution = await ExecuteAsync(client.Object, ExecutionMode.Standard);

        execution.Scenario.Status.Should().Be(TestStatus.Passed);
        execution.Observations.Should().ContainSingle();
        execution.Observations.Single().Metadata["contentSha256"].Should().MatchRegex("^[a-f0-9]{64}$");
        execution.Observations.Single().RedactedPayloadPreview.Should().BeNull();
    }

    private static async Task<ValidationScenarioExecutionResult> ExecuteAsync(
        IMcpHttpClient client,
        ExecutionMode mode)
    {
        var result = new ValidationResult
        {
            ServerConfig = new McpServerConfig { Endpoint = "https://example.test/mcp", Transport = "http" },
            ToolValidation = new ToolTestResult
            {
                ToolResults =
                [
                    new IndividualToolResult
                    {
                        ToolName = "weather",
                        AppMetadata = new McpAppToolMetadata
                        {
                            IsPresent = true,
                            ResourceUri = "ui://weather/dashboard"
                        }
                    }
                ]
            }
        };
        result.ValidationConfig.Execution = new ExecutionPolicy { Mode = mode };
        var scenario = new McpAppsSecurityScenarioPack(client).GetScenarios()
            .Single(item => item.Descriptor.Key.Value == "apps-linked-resource-security");
        return await scenario.ExecuteAsync(new ValidationScenarioContext
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
}