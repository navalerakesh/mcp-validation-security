using Mcp.Benchmark.Core.Abstractions;
using Mcp.Benchmark.Core.Models;
using Mcp.Benchmark.Infrastructure.Authentication;
using Mcp.Benchmark.Infrastructure.Services;
using Mcp.Benchmark.Infrastructure.Health;
using Microsoft.Extensions.Logging;
using ModelContextProtocol.Protocol;
using McpServerCapabilities = ModelContextProtocol.Protocol.ServerCapabilities;

namespace Mcp.Benchmark.Tests.Unit.Services;

public sealed class ValidationSessionBuilderProtocolEraTests
{
    [Fact]
    public async Task BuildAsync_ExplicitModernDiscoveryFailure_ShouldNotFallBack()
    {
        var (builder, httpClient) = CreateBuilder("2025-06-18");
        await FluentActions.Awaiting(() => builder.BuildAsync(CreateConfiguration(McpProtocolEraSelection.Modern, "2026-07-28"), CancellationToken.None))
            .Should().ThrowAsync<ValidationSessionException>().WithMessage("Modern protocol discovery failed:*");
        httpClient.Verify(client => client.ValidateInitializeAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
        httpClient.Verify(client => client.ValidateCapabilitiesAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task BuildAsync_AutoDiscoveryRejected_WithRealHealthCheck_ShouldEstablishLegacyVersion()
    {
        var (_, httpClient) = CreateBuilder("2025-06-18");
        httpClient.Setup(client => client.CallAsync(It.IsAny<string>(), "server/discover", null,
            It.IsAny<AuthenticationConfig?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Mcp.Benchmark.Core.Models.JsonRpcResponse
            {
                StatusCode = 200, IsSuccess = true,
                RawJson = """{"jsonrpc":"2.0","id":"discover","error":{"code":-32601,"message":"Method not found"}}"""
            });
        httpClient.Setup(client => client.ValidateInitializeAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new TransportResult<InitializeResult>
            {
                IsSuccessful = true, Transport = TransportMetadata.Empty,
                Payload = new InitializeResult
                {
                    ProtocolVersion = "2025-06-18", Capabilities = new McpServerCapabilities(),
                    ServerInfo = new Implementation { Name = "legacy-server", Version = "1.0.0" }
                }
            });
        var health = new HealthCheckService(httpClient.Object, Mock.Of<ILogger<HealthCheckService>>(), Mock.Of<ITelemetryService>());
        var builder = new ValidationSessionBuilder(httpClient.Object, Mock.Of<IAuthenticationService>(), health,
            Mock.Of<ILogger<ValidationSessionBuilder>>());

        var context = await builder.BuildAsync(CreateConfiguration(McpProtocolEraSelection.Auto, "2026-07-28"), CancellationToken.None);

        context.ProtocolVersion.Should().Be("2025-06-18");
        context.InitializationHandshake.Should().NotBeNull();
        context.ModernDiscovery!.IsSuccessful.Should().BeFalse();
        httpClient.Verify(client => client.ValidateInitializeAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Once);
        httpClient.Verify(client => client.CallAsync(It.IsAny<string>(), "server/discover", null,
            It.IsAny<AuthenticationConfig?>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task BuildAsync_ExplicitModernEraNegotiatesLegacy_ShouldFailWithoutCapabilityCollection()
    {
        var (builder, httpClient) = CreateBuilder("2025-11-25");
        var configuration = CreateConfiguration(McpProtocolEraSelection.Modern, "2025-11-25");

        var action = () => builder.BuildAsync(configuration, CancellationToken.None);

        await action.Should().ThrowAsync<ValidationSessionException>()
            .WithMessage("*conflicts with explicit modern era*");
        httpClient.Verify(
            client => client.ValidateCapabilitiesAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task BuildAsync_ExplicitModernEraWithValidDiscovery_ShouldSkipLegacyInitializeEvidence()
    {
        var (builder, httpClient) = CreateBuilder("2025-11-25");
        httpClient
            .Setup(client => client.CallAsync(
                It.IsAny<string>(),
                "server/discover",
                null,
                It.IsAny<AuthenticationConfig?>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Mcp.Benchmark.Core.Models.JsonRpcResponse
            {
                StatusCode = 200,
                IsSuccess = true,
                ElapsedMs = 42,
                RawJson = """
                    {
                      "jsonrpc": "2.0",
                      "id": "discover",
                      "result": {
                        "supportedVersions": ["2026-07-28"],
                                                "capabilities": {
                                                    "tools": {},
                                                    "extensions": {
                                                        "io.modelcontextprotocol/tasks": {},
                                                        "com.example/audit": {}
                                                    }
                                                },
                        "resultType": "complete",
                        "cacheScope": "public",
                        "ttlMs": 60000
                      }
                    }
                    """
            });
        var configuration = CreateConfiguration(McpProtocolEraSelection.Modern, "2026-07-28");

        var context = await builder.BuildAsync(configuration, CancellationToken.None);

        context.ProtocolVersion.Should().Be("2026-07-28");
        context.InitializationHandshake.Should().BeNull();
        context.ModernDiscovery.Should().NotBeNull();
        context.ModernDiscovery!.Payload!.IsValid.Should().BeTrue();
        context.ModernDiscovery.Payload.SupportedVersions.Should().Equal("2026-07-28");
        context.ModernDiscovery.Payload.CapabilityNames.Should().Equal("extensions", "tools");
        context.ModernDiscovery.Payload.ExtensionIds.Should().Equal("com.example/audit", "io.modelcontextprotocol/tasks");
        context.BootstrapHealth.Should().NotBeNull();
        context.BootstrapHealth!.Disposition.Should().Be(HealthCheckDisposition.Healthy);
        context.BootstrapHealth.ResponseTimeMs.Should().Be(42);
    }

    [Fact]
    public async Task BuildAsync_AutoEraNegotiatesLegacy_ShouldRecordObservedVersion()
    {
        var (builder, _) = CreateBuilder("2025-11-25");
        var configuration = CreateConfiguration(McpProtocolEraSelection.Auto, "2026-07-28");

        var context = await builder.BuildAsync(configuration, CancellationToken.None);

        context.ProtocolVersion.Should().Be("2025-11-25");
        context.EffectiveServer.ProtocolVersion.Should().Be("2025-11-25");
    }

    [Fact]
    public async Task BuildAsync_AutoEraModernBindingFailure_ShouldFallBackToLegacyInitialize()
    {
        var (builder, httpClient) = CreateBuilder("2025-06-18");
        httpClient
            .Setup(client => client.CallAsync(
                It.IsAny<string>(),
                "server/discover",
                null,
                It.IsAny<AuthenticationConfig?>(),
                It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("Required properties [result] are not present"));
        var configuration = CreateConfiguration(McpProtocolEraSelection.Auto, "2026-07-28");

        var context = await builder.BuildAsync(configuration, CancellationToken.None);

        context.ProtocolVersion.Should().Be("2025-06-18");
        context.ModernDiscovery.Should().NotBeNull();
        context.ModernDiscovery!.IsSuccessful.Should().BeFalse();
        context.ModernDiscovery.Error.Should().Be("server/discover response could not be parsed as modern discovery evidence.");
    }

    private static (ValidationSessionBuilder Builder, Mock<IMcpHttpClient> HttpClient) CreateBuilder(string negotiatedVersion)
    {
        var httpClient = new Mock<IMcpHttpClient>();
        httpClient
            .Setup(client => client.CallAsync(
                It.IsAny<string>(),
                "server/discover",
                null,
                It.IsAny<AuthenticationConfig?>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Mcp.Benchmark.Core.Models.JsonRpcResponse
            {
                StatusCode = 404,
                IsSuccess = false,
                Error = "Method not found"
            });
        httpClient
            .Setup(client => client.ValidateCapabilitiesAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new TransportResult<CapabilitySummary>
            {
                IsSuccessful = true,
                Payload = new CapabilitySummary(),
                Transport = TransportMetadata.Empty
            });
        var healthCheck = new Mock<IHealthCheckService>();
        healthCheck
            .Setup(service => service.PerformHealthCheckAsync(It.IsAny<McpServerConfig>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new HealthCheckResult
            {
                IsHealthy = true,
                ProtocolVersion = negotiatedVersion,
                InitializationDetails = new TransportResult<InitializeResult>
                {
                    IsSuccessful = true,
                    Payload = new InitializeResult
                    {
                        ProtocolVersion = negotiatedVersion,
                        ServerInfo = new Implementation { Name = "era-fixture", Version = "1.0.0" },
                        Capabilities = new McpServerCapabilities()
                    },
                    Transport = TransportMetadata.Empty
                }
            });
        var builder = new ValidationSessionBuilder(
            httpClient.Object,
            Mock.Of<IAuthenticationService>(),
            healthCheck.Object,
            Mock.Of<ILogger<ValidationSessionBuilder>>());
        return (builder, httpClient);
    }

    private static McpValidatorConfiguration CreateConfiguration(McpProtocolEraSelection selection, string requestedVersion)
    {
        return new McpValidatorConfiguration
        {
            Server = new McpServerConfig
            {
                Endpoint = "https://example.test/mcp",
                Transport = "http",
                ProtocolEra = selection,
                ProtocolVersion = requestedVersion
            }
        };
    }
}
