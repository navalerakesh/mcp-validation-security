using FluentAssertions;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Mcp.Benchmark.Core.Abstractions;
using Mcp.Benchmark.Core.Constants;
using Mcp.Benchmark.Core.Models;
using Mcp.Benchmark.Infrastructure.Scenarios;
using Moq;

namespace Mcp.Benchmark.Tests.Unit.Scenarios;

public sealed class McpSkillsPreviewScenarioPackTests
{
    [Fact]
    public async Task ExecuteAsync_PaginatedCatalog_ShouldCollectAllPagesWithinBound()
    {
        var client = new Mock<IMcpHttpClient>();
        client.SetupSequence(instance => instance.CallAsync(
                "https://example.test/mcp",
                McpSpecConstants.ExtensionMethods.SkillsList,
                It.IsAny<object?>(),
                It.IsAny<AuthenticationConfig?>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(Response(CreatePage("alpha", nextCursor: "page-2")))
            .ReturnsAsync(Response(CreatePage("beta", nextCursor: null)));
        var result = new ValidationResult
        {
            ServerConfig = new McpServerConfig { Endpoint = "https://example.test/mcp", Transport = "http" }
        };
        var pack = new McpSkillsPreviewScenarioPack(client.Object);

        var execution = await pack.GetScenarios()
          .Single(scenario => scenario.Descriptor.Key.Value == "skills-passive-catalog")
          .ExecuteAsync(new ValidationScenarioContext
        {
            ServerConfig = result.ServerConfig,
            ApplicabilityContext = new ValidationApplicabilityContext
            {
                NegotiatedProtocolVersion = "2026-07-28",
                SchemaVersion = "2026-07-28",
                Transport = "http",
                AccessMode = "public",
                AdvertisedCapabilities = [McpSpecConstants.Capabilities.Resources],
                AdvertisedExtensionIds = [McpExtensionCatalog.Skills.Id]
            },
            ValidationConfiguration = result.ValidationConfig,
            ValidationResult = result
        }, CancellationToken.None);

        execution.Scenario.Status.Should().Be(TestStatus.Passed);
        execution.Scenario.Summary.Should().Contain("2 Skills catalog entries");
        execution.Observations.Should().HaveCount(2);
        client.Verify(instance => instance.CallAsync(
            "https://example.test/mcp",
            McpSpecConstants.ExtensionMethods.SkillsList,
            It.IsAny<object?>(),
            It.IsAny<AuthenticationConfig?>(),
            It.IsAny<CancellationToken>()), Times.Exactly(2));
    }

    [Fact]
    public async Task ContentScenario_WithoutExplicitStandardAuthorization_ShouldNotContactServer()
    {
      var client = new Mock<IMcpHttpClient>(MockBehavior.Strict);

      var execution = await ExecuteContentScenarioAsync(client.Object, ExecutionMode.Safe, selected: false);

      execution.Scenario.Status.Should().Be(TestStatus.Skipped);
      client.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task ContentScenario_WithExplicitStandardAuthorization_ShouldVerifyDigestWithoutPersistingContent()
    {
      const string content = "---\nname: alpha\ndescription: Alpha skill\n---\nUse the alpha workflow.\n";
      var bytes = Encoding.UTF8.GetBytes(content);
      var digest = Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
      var client = new Mock<IMcpHttpClient>();
      client.Setup(instance => instance.CallAsync(
          "https://example.test/mcp",
          McpSpecConstants.ExtensionMethods.SkillsList,
          It.IsAny<object?>(),
          It.IsAny<AuthenticationConfig?>(),
          It.IsAny<CancellationToken>()))
        .ReturnsAsync(Response(CreateManifestPage("alpha", digest, bytes.Length)));
      client.Setup(instance => instance.CallAsync(
          "https://example.test/mcp",
          McpSpecConstants.ResourceMethods.Read,
          It.IsAny<object>(),
          It.IsAny<AuthenticationConfig?>(),
          It.IsAny<CancellationToken>()))
        .ReturnsAsync(Response(JsonSerializer.Serialize(new
        {
          result = new
          {
            contents = new[]
            {
              new { uri = "skill://alpha/SKILL.md", mimeType = "text/markdown", text = content }
            }
          }
        })));

      var execution = await ExecuteContentScenarioAsync(client.Object, ExecutionMode.Standard, selected: true);

      execution.Scenario.Status.Should().Be(TestStatus.Passed);
      execution.Observations.Should().ContainSingle();
      execution.Observations.Single().Metadata["contentSha256"].Should().Be(digest);
      JsonSerializer.Serialize(execution).Should().NotContain("Use the alpha workflow");
    }

    [Fact]
    public async Task ContentScenario_ResourceTransportFailure_ShouldBecomeExtensionFinding()
    {
      const string content = "---\nname: alpha\ndescription: Alpha skill\n---\nBody\n";
      var bytes = Encoding.UTF8.GetBytes(content);
      var digest = Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
      var client = new Mock<IMcpHttpClient>();
      client.Setup(instance => instance.CallAsync(
          "https://example.test/mcp",
          McpSpecConstants.ExtensionMethods.SkillsList,
          It.IsAny<object?>(),
          It.IsAny<AuthenticationConfig?>(),
          It.IsAny<CancellationToken>()))
        .ReturnsAsync(Response(CreateManifestPage("alpha", digest, bytes.Length)));
      client.Setup(instance => instance.CallAsync(
          "https://example.test/mcp",
          McpSpecConstants.ResourceMethods.Read,
          It.IsAny<object>(),
          It.IsAny<AuthenticationConfig?>(),
          It.IsAny<CancellationToken>()))
        .ThrowsAsync(new HttpRequestException("target unavailable"));

      var execution = await ExecuteContentScenarioAsync(client.Object, ExecutionMode.Standard, selected: true);

      execution.Scenario.Status.Should().Be(TestStatus.Failed);
      execution.Scenario.Findings.Should().ContainSingle(finding =>
        finding.RuleId == ValidationFindingRuleIds.SkillsContentIntegrityMismatch &&
        finding.ExcludedFromAggregate);
    }

    private static JsonRpcResponse Response(string json) => new()
    {
        IsSuccess = true,
        StatusCode = 200,
        RawJson = json
    };

    private static async Task<ValidationScenarioExecutionResult> ExecuteContentScenarioAsync(
      IMcpHttpClient client,
      ExecutionMode mode,
      bool selected)
    {
      var result = new ValidationResult
      {
        ServerConfig = new McpServerConfig { Endpoint = "https://example.test/mcp", Transport = "http" }
      };
      result.ValidationConfig.Execution = new ExecutionPolicy { Mode = mode };
      var scenario = new McpSkillsPreviewScenarioPack(client).GetScenarios()
        .Single(item => item.Descriptor.Key.Value == "skills-authorized-content");
      return await scenario.ExecuteAsync(new ValidationScenarioContext
      {
        ServerConfig = result.ServerConfig,
        ApplicabilityContext = new ValidationApplicabilityContext
        {
          NegotiatedProtocolVersion = "2026-07-28",
          SchemaVersion = "2026-07-28",
          Transport = "http",
          AccessMode = "public",
          AdvertisedCapabilities = [McpSpecConstants.Capabilities.Resources],
          AdvertisedExtensionIds = [McpExtensionCatalog.Skills.Id],
          SelectedExtensionIds = selected ? [McpExtensionCatalog.Skills.Id] : []
        },
        ValidationConfiguration = result.ValidationConfig,
        ValidationResult = result
      }, CancellationToken.None);
    }

    private static string CreateManifestPage(string name, string digest, int size) => JsonSerializer.Serialize(new
    {
      jsonrpc = "2.0",
      id = 1,
      result = new
      {
        resultType = "complete",
        skills = new[]
        {
          new
          {
            uri = $"skill://{name}/SKILL.md",
            frontmatter = new { name, description = "Alpha skill" },
            resources = new[]
            {
              new { uri = $"skill://{name}/SKILL.md", digest = $"sha256:{digest}", size }
            }
          }
        },
        ttlMs = 300000,
        cacheScope = "public"
      }
    });

    private static string CreatePage(string name, string? nextCursor) => $$"""
        {
          "jsonrpc": "2.0",
          "id": 1,
          "result": {
            "resultType": "complete",
            "skills": [{
              "uri": "skill://{{name}}/SKILL.md",
              "frontmatter": { "name": "{{name}}", "description": "{{name}} skill" },
              "resources": [{
                "uri": "skill://{{name}}/SKILL.md",
                "digest": "sha256:aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa",
                "size": 10
              }]
            }],
            "ttlMs": 300000,
            "cacheScope": "public"{{(nextCursor == null ? string.Empty : $",\n    \"nextCursor\": \"{nextCursor}\"")}}
          }
        }
        """;
}