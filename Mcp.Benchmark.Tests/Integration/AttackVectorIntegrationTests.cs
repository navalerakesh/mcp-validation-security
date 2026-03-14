using Mcp.Benchmark.Core.Abstractions;
using Mcp.Benchmark.Core.Models;
using Mcp.Benchmark.Infrastructure.Attacks;
using Microsoft.Extensions.Logging;
using Moq;
using FluentAssertions;
using Xunit;

namespace Mcp.Benchmark.Tests.Integration;

public class AttackVectorIntegrationTests
{
    private readonly Mock<IMcpHttpClient> _httpClient;
    private readonly McpServerConfig _config;

    public AttackVectorIntegrationTests()
    {
        _httpClient = new Mock<IMcpHttpClient>();
        _config = new McpServerConfig { Endpoint = "https://test.com/mcp", Transport = "http" };
    }

    // ─── JsonRpcErrorSmuggling ───────────────────────
    [Fact]
    public async Task ErrorSmuggling_WithGracefulHandling_ShouldPass()
    {
        var attack = new JsonRpcErrorSmuggling(new Mock<ILogger<JsonRpcErrorSmuggling>>().Object);
        _httpClient.Setup(x => x.CallAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<object>(), It.IsAny<AuthenticationConfig>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new JsonRpcResponse { StatusCode = 200, IsSuccess = true, RawJson = "{\"jsonrpc\":\"2.0\",\"error\":{\"code\":-32601,\"message\":\"Method not found\"},\"id\":null}" });

        var result = await attack.ExecuteAsync(_config, _httpClient.Object, CancellationToken.None);

        result.IsBlocked.Should().BeTrue();
    }

    [Fact]
    public async Task ErrorSmuggling_WithServerCrash_ShouldFail()
    {
        var attack = new JsonRpcErrorSmuggling(new Mock<ILogger<JsonRpcErrorSmuggling>>().Object);
        _httpClient.Setup(x => x.CallAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<object>(), It.IsAny<AuthenticationConfig>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new JsonRpcResponse { StatusCode = 500, IsSuccess = false, Error = "Internal Server Error" });

        var result = await attack.ExecuteAsync(_config, _httpClient.Object, CancellationToken.None);

        result.IsBlocked.Should().BeFalse();
    }

    // ─── MetadataEnumeration ─────────────────────────
    [Fact]
    public async Task MetadataEnum_WithConsistentErrors_ShouldPass()
    {
        var attack = new MetadataEnumeration(new Mock<ILogger<MetadataEnumeration>>().Object);
        _httpClient.Setup(x => x.CallAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<object>(), It.IsAny<AuthenticationConfig>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new JsonRpcResponse { StatusCode = 404, IsSuccess = false, RawJson = "{\"error\":\"not found\"}" });

        var result = await attack.ExecuteAsync(_config, _httpClient.Object, CancellationToken.None);

        result.IsBlocked.Should().BeTrue();
    }

    [Fact]
    public async Task MetadataEnum_WithDifferentStatusCodes_ShouldDetect()
    {
        var attack = new MetadataEnumeration(new Mock<ILogger<MetadataEnumeration>>().Object);
        var callCount = 0;
        _httpClient.Setup(x => x.CallAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<object>(), It.IsAny<AuthenticationConfig>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(() =>
            {
                callCount++;
                return callCount == 1
                    ? new JsonRpcResponse { StatusCode = 404, IsSuccess = false }
                    : new JsonRpcResponse { StatusCode = 403, IsSuccess = false };
            });

        var result = await attack.ExecuteAsync(_config, _httpClient.Object, CancellationToken.None);

        result.IsBlocked.Should().BeFalse();
    }

    // ─── SchemaConfusion ─────────────────────────────
    [Fact]
    public async Task SchemaConfusion_WithNoTools_ShouldStillExecute()
    {
        var attack = new SchemaConfusion(new Mock<ILogger<SchemaConfusion>>().Object);
        _httpClient.Setup(x => x.CallAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<object>(), It.IsAny<AuthenticationConfig>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new JsonRpcResponse { StatusCode = 200, IsSuccess = true, RawJson = "{\"result\":{\"tools\":[]}}" });

        var result = await attack.ExecuteAsync(_config, _httpClient.Object, CancellationToken.None);

        // SchemaConfusion uses a default "test-tool" when no tools found
        result.Should().NotBeNull();
    }

    [Fact]
    public async Task SchemaConfusion_WithCrash_ShouldFail()
    {
        var attack = new SchemaConfusion(new Mock<ILogger<SchemaConfusion>>().Object);
        _httpClient.Setup(x => x.CallAsync(It.IsAny<string>(), "tools/list", It.IsAny<object>(), It.IsAny<AuthenticationConfig>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new JsonRpcResponse
            {
                StatusCode = 200, IsSuccess = true,
                RawJson = "{\"result\":{\"tools\":[{\"name\":\"test\"}]}}"
            });
        _httpClient.Setup(x => x.CallAsync(It.IsAny<string>(), "tools/call", It.IsAny<object>(), It.IsAny<AuthenticationConfig>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new JsonRpcResponse { StatusCode = 500, IsSuccess = false });

        var result = await attack.ExecuteAsync(_config, _httpClient.Object, CancellationToken.None);

        result.IsBlocked.Should().BeFalse();
    }

    // ─── HallucinationFuzzer ─────────────────────────
    [Fact]
    public async Task HallucinationFuzzer_WithNoTools_ShouldSkip()
    {
        var attack = new HallucinationFuzzer(new Mock<ILogger<HallucinationFuzzer>>().Object);
        _httpClient.Setup(x => x.CallAsync(It.IsAny<string>(), "tools/list", It.IsAny<object>(), It.IsAny<AuthenticationConfig>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new JsonRpcResponse { StatusCode = 200, IsSuccess = true, RawJson = "{\"result\":{\"tools\":[]}}" });

        var result = await attack.ExecuteAsync(_config, _httpClient.Object, CancellationToken.None);

        result.Analysis.Should().Contain("Skipped");
    }

    [Fact]
    public async Task HallucinationFuzzer_WithTypedParam_ShouldGradeError()
    {
        var attack = new HallucinationFuzzer(new Mock<ILogger<HallucinationFuzzer>>().Object);
        _httpClient.Setup(x => x.CallAsync(It.IsAny<string>(), "tools/list", It.IsAny<object>(), It.IsAny<AuthenticationConfig>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new JsonRpcResponse
            {
                StatusCode = 200, IsSuccess = true,
                RawJson = "{\"result\":{\"tools\":[{\"name\":\"calc\",\"inputSchema\":{\"type\":\"object\",\"properties\":{\"value\":{\"type\":\"integer\"}}}}]}}"
            });
        _httpClient.Setup(x => x.CallAsync(It.IsAny<string>(), "tools/call", It.IsAny<object>(), It.IsAny<AuthenticationConfig>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new JsonRpcResponse
            {
                StatusCode = 200, IsSuccess = true,
                RawJson = "{\"jsonrpc\":\"2.0\",\"error\":{\"code\":-32602,\"message\":\"Invalid params: 'value' expected integer, got string\",\"data\":{\"param\":\"value\"}},\"id\":1}"
            });

        var result = await attack.ExecuteAsync(_config, _httpClient.Object, CancellationToken.None);

        result.Analysis.Should().Contain("Error clarity");
    }
}
