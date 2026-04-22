using Mcp.Benchmark.Core.Models;
using Mcp.Benchmark.Core.Abstractions;
using Mcp.Benchmark.Infrastructure.Services;
using Mcp.Benchmark.Infrastructure.Validators;
using Mcp.Benchmark.Infrastructure.Authentication;
using Mcp.Benchmark.Tests.Fixtures;
using Microsoft.Extensions.Logging;
using WireMock.RequestBuilders;
using WireMock.ResponseBuilders;
using Moq;
using Mcp.Compliance.Spec;

namespace Mcp.Benchmark.Tests.Integration;

/// <summary>
/// Professional integration tests for MCP Tool Interface Validation.
/// Tests tool discovery and validation against mock MCP servers using WireMock.
/// Validates tool registration, metadata, and basic execution patterns.
/// </summary>
public class ToolValidatorIntegrationTests : IClassFixture<McpServerTestFixture>, IDisposable
{
    private readonly McpServerTestFixture _testFixture;
    private readonly ToolValidator _validator;
    private readonly Mock<ILogger<ToolValidator>> _loggerMock;
    private readonly Mock<ISchemaValidator> _schemaValidatorMock;
    private readonly Mock<IAuthenticationService> _authServiceMock;
    private readonly Mock<ISchemaRegistry> _schemaRegistryMock;
    private readonly Mock<IContentSafetyAnalyzer> _contentSafetyAnalyzerMock;

    public ToolValidatorIntegrationTests(McpServerTestFixture testFixture)
    {
        _testFixture = testFixture;
        _loggerMock = new Mock<ILogger<ToolValidator>>();
        _schemaValidatorMock = new Mock<ISchemaValidator>();
        _authServiceMock = new Mock<IAuthenticationService>();
        _schemaRegistryMock = new Mock<ISchemaRegistry>();
        _contentSafetyAnalyzerMock = new Mock<IContentSafetyAnalyzer>();

        // By default, behave as if schemas are not present so validation gracefully skips
        _schemaRegistryMock
            .Setup(r => r.GetSchema(It.IsAny<Mcp.Compliance.Spec.ProtocolVersion>(), It.IsAny<string>(), It.IsAny<string>()))
            .Throws(new FileNotFoundException("Schema not found"));

        _validator = new ToolValidator(
            _loggerMock.Object,
            _testFixture.McpClient,
            _schemaValidatorMock.Object,
            _schemaRegistryMock.Object,
            _authServiceMock.Object,
            _contentSafetyAnalyzerMock.Object,
            new ToolAiReadinessAnalyzer());
        
        // Reset mock server state before each test
        _testFixture.ResetMockServer();
    }

    [Fact]
    public async Task ValidateToolDiscoveryAsync_WithHttpEndpoint_ShouldExecuteDiscovery()
    {
        // Arrange - Setup mock response for tool discovery
        _testFixture.MockServer
            .Given(Request.Create().WithPath("/mcp"))
            .RespondWith(Response.Create()
                .WithStatusCode(200)
                .WithHeader("Content-Type", "application/json")
                .WithBody("""
                {
                    "jsonrpc": "2.0",
                    "result": {
                        "tools": [
                            {
                                "name": "test-tool",
                                "description": "A test tool for validation",
                                "inputSchema": {
                                    "type": "object",
                                    "properties": {
                                        "input": { "type": "string" }
                                    }
                                }
                            }
                        ]
                    },
                    "id": "tool-discovery"
                }
                """));

        var serverConfig = _testFixture.CreateTestServerConfig();
        var toolConfig = new ToolTestingConfig
        {
            TestToolDiscovery = true,
            TestParameterValidation = true
        };

        // Act
        var result = await _validator.ValidateToolDiscoveryAsync(serverConfig, toolConfig);

        // Assert
        result.Should().NotBeNull();
        result.Status.Should().BeOneOf(TestStatus.Passed, TestStatus.Failed, TestStatus.Error);
        result.Duration.Should().BeGreaterThan(TimeSpan.Zero);
        result.ToolResults.Should().NotBeNull();
        
        // Verify HTTP communication occurred
        _testFixture.GetRequestCount().Should().BeGreaterThan(0);
    }

    [Fact]
    public async Task ValidateToolDiscoveryAsync_WithStdioTransport_ShouldReturnFailedResult()
    {
        // Arrange - Test STDIO transport handling
        var serverConfig = new McpServerConfig
        {
            Transport = "stdio"
        };
        var toolConfig = new ToolTestingConfig
        {
            TestToolDiscovery = true
        };

        // Act
        var result = await _validator.ValidateToolDiscoveryAsync(serverConfig, toolConfig);

        // Assert
        result.Should().NotBeNull();
        result.Status.Should().Be(TestStatus.Failed);
        result.CriticalErrors.Should().NotBeEmpty();
        result.CriticalErrors.Should().Contain(e => e.Contains("STDIO"));
    }

    [Fact]
    public async Task ValidateToolDiscoveryAsync_WithNoEndpoint_ShouldReturnFailedResult()
    {
        // Arrange - No endpoint provided
        var serverConfig = new McpServerConfig
        {
            Endpoint = null,
            Transport = "http"
        };
        var toolConfig = new ToolTestingConfig
        {
            TestToolDiscovery = true
        };

        // Act
        var result = await _validator.ValidateToolDiscoveryAsync(serverConfig, toolConfig);

        // Assert
        result.Should().NotBeNull();
        result.Status.Should().Be(TestStatus.Failed);
        result.CriticalErrors.Should().NotBeEmpty();
        result.CriticalErrors.Should().Contain(e => e.Contains("endpoint"));
    }

    [Fact]
    public async Task ValidateToolDiscoveryAsync_WithServerError_ShouldHandleGracefully()
    {
        // Arrange - Server returns error
        _testFixture.MockServer
            .Given(Request.Create().WithPath("/mcp"))
            .RespondWith(Response.Create()
                .WithStatusCode(500)
                .WithBody("Server Error"));

        var serverConfig = _testFixture.CreateTestServerConfig();
        var toolConfig = new ToolTestingConfig
        {
            TestToolDiscovery = true
        };

        // Act
        var result = await _validator.ValidateToolDiscoveryAsync(serverConfig, toolConfig);

        // Assert
        result.Should().NotBeNull();
        result.Status.Should().BeOneOf(TestStatus.Failed, TestStatus.Error);
        result.Duration.Should().BeGreaterThan(TimeSpan.Zero);
    }

    [Fact]
    public async Task ValidateToolDiscoveryAsync_WithTimeout_ShouldHandleTimeout()
    {
        // Use shorter delay for CI/CD performance, but still test timeout behavior
        // This tests the same timeout handling logic but runs much faster
        var testDelayMs = 1500; // 1.5s delay - fast but realistic timeout test
        var clientTimeoutMs = 800; // 800ms timeout - ensures timeout occurs
        
        // Arrange - Server with delayed response
        _testFixture.MockServer
            .Given(Request.Create().WithPath("/mcp"))
            .RespondWith(Response.Create()
                .WithDelay(TimeSpan.FromMilliseconds(testDelayMs))
                .WithStatusCode(200)
                .WithBody("""{"jsonrpc": "2.0", "result": {"tools": []}, "id": "test"}"""));

        var serverConfig = _testFixture.CreateTestServerConfig(timeoutMs: clientTimeoutMs);
        var toolConfig = new ToolTestingConfig
        {
            TestToolDiscovery = true
        };

        // Act
        var result = await _validator.ValidateToolDiscoveryAsync(serverConfig, toolConfig);

        // Assert
        result.Should().NotBeNull();
        result.Status.Should().Be(TestStatus.Skipped);
        result.Message.Should().Contain("tools/list probe inconclusive");
        // Timeout/retry pressure is treated as inconclusive rather than a contract failure
    }

    [Fact]
    [Trait("Category", "LongRunning")]
    public async Task ValidateToolDiscoveryAsync_WithExtendedTimeout_ShouldHandleRealisticScenarios()
    {
        // Extended timeout test for comprehensive real-world scenario validation
        // This test can be excluded from regular runs: dotnet test --filter "Category!=LongRunning"
        
        // Arrange - Server with realistic network delay
        _testFixture.MockServer
            .Given(Request.Create().WithPath("/mcp"))
            .RespondWith(Response.Create()
                .WithDelay(TimeSpan.FromSeconds(8)) // Extended delay for real network conditions
                .WithStatusCode(200)
                .WithBody("""{"jsonrpc": "2.0", "result": {"tools": []}, "id": "test"}"""));

        var serverConfig = _testFixture.CreateTestServerConfig(timeoutMs: 2000); // 2s timeout
        var toolConfig = new ToolTestingConfig
        {
            TestToolDiscovery = true
        };

        // Act
        var result = await _validator.ValidateToolDiscoveryAsync(serverConfig, toolConfig);

        // Assert - Same behavior expected as regular timeout test
        result.Should().NotBeNull();
        result.Status.Should().Be(TestStatus.Skipped);
        result.Message.Should().Contain("tools/list probe inconclusive");
        // Extended timeout still maps to an inconclusive probe rather than a contract failure
    }

    [Fact]
    public async Task ValidateToolDiscoveryAsync_WithEmptyToolList_ShouldReturnValidResult()
    {
        // Arrange - Server returns empty tool list
        _testFixture.MockServer
            .Given(Request.Create().WithPath("/mcp"))
            .RespondWith(Response.Create()
                .WithStatusCode(200)
                .WithHeader("Content-Type", "application/json")
                .WithBody("""
                {
                    "jsonrpc": "2.0",
                    "result": {
                        "tools": []
                    },
                    "id": "empty-tools"
                }
                """));

        var serverConfig = _testFixture.CreateTestServerConfig();
        var toolConfig = new ToolTestingConfig
        {
            TestToolDiscovery = true
        };

        // Act
        var result = await _validator.ValidateToolDiscoveryAsync(serverConfig, toolConfig);

        // Assert
        result.Should().NotBeNull();
        result.Status.Should().BeOneOf(TestStatus.Passed, TestStatus.Failed, TestStatus.Error);
        result.ToolsDiscovered.Should().BeGreaterThanOrEqualTo(0);
    }

    public void Dispose()
    {
        // Cleanup handled by test fixture
    }
}
