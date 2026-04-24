using Mcp.Benchmark.Core.Models;
using Mcp.Benchmark.Core.Abstractions;
using Mcp.Compliance.Spec;
using Mcp.Benchmark.Infrastructure.Services;
using Mcp.Benchmark.Infrastructure.Validators;
using Mcp.Benchmark.Tests.Fixtures;
using Microsoft.Extensions.Logging;
using WireMock.RequestBuilders;
using WireMock.ResponseBuilders;

using Mcp.Benchmark.Infrastructure.Registries;

namespace Mcp.Benchmark.Tests.Integration;

/// <summary>
/// Professional integration tests for MCP Protocol Compliance Validation.
/// Tests actual HTTP communication with mock MCP servers using WireMock.
/// Validates real JSON-RPC 2.0 compliance and MCP protocol adherence patterns.
/// </summary>
public class ProtocolComplianceValidatorIntegrationTests : IClassFixture<McpServerTestFixture>, IDisposable
{
    private readonly McpServerTestFixture _testFixture;
    private readonly ProtocolComplianceValidator _validator;
    private readonly Mock<ILogger<ProtocolComplianceValidator>> _loggerMock;
    private readonly IProtocolRuleRegistry _ruleRegistry;
    private readonly IValidationApplicabilityResolver _applicabilityResolver;
    private readonly IProtocolFeatureResolver _protocolFeatureResolver;

    public ProtocolComplianceValidatorIntegrationTests(McpServerTestFixture testFixture)
    {
        _testFixture = testFixture;
        _loggerMock = new Mock<ILogger<ProtocolComplianceValidator>>();
        _ruleRegistry = new ProtocolRuleRegistry();
        _applicabilityResolver = new ValidationApplicabilityResolver(new EmbeddedSchemaRegistry());
        _protocolFeatureResolver = new ProtocolFeatureResolver(
            new ValidationPackRegistry<IProtocolFeaturePack>(
                new IProtocolFeaturePack[] { new BuiltInProtocolFeaturePack(new EmbeddedSchemaRegistry()) }));
        _validator = new ProtocolComplianceValidator(
            _loggerMock.Object,
            _testFixture.McpClient,
            _ruleRegistry,
            _applicabilityResolver,
            _protocolFeatureResolver);
        
        // Reset mock server state before each test
        _testFixture.ResetMockServer();
    }

    [Fact]
    public async Task ValidateJsonRpcComplianceAsync_WithHttpEndpoint_ShouldExecuteValidation()
    {
        // Arrange - Setup a basic mock response
        _testFixture.MockServer
            .Given(Request.Create().WithPath("/mcp"))
            .RespondWith(Response.Create()
                .WithStatusCode(200)
                .WithHeader("Content-Type", "application/json")
                .WithBody("""
                {
                    "jsonrpc": "2.0",
                    "result": {
                        "protocolVersion": "2024-11-05",
                        "capabilities": {
                            "tools": {}
                        }
                    },
                    "id": "test-request"
                }
                """));

        var serverConfig = _testFixture.CreateTestServerConfig();
        var protocolConfig = new ProtocolComplianceConfig
        {
            TestJsonRpcCompliance = true,
            ProtocolVersion = "2024-11-05"
        };

        // Act
        var result = await _validator.ValidateJsonRpcComplianceAsync(serverConfig, protocolConfig);

        // Assert
        result.Should().NotBeNull();
        result.Status.Should().BeOneOf(TestStatus.Passed, TestStatus.Failed, TestStatus.Error);
        result.Duration.Should().BeGreaterThan(TimeSpan.Zero);
        result.JsonRpcCompliance.Should().NotBeNull();
        
        // Verify that HTTP communication occurred
        _testFixture.GetRequestCount().Should().BeGreaterThan(0);
    }

    [Fact]
    public async Task ValidateJsonRpcComplianceAsync_WithStdioTransport_ShouldReturnFailedResult()
    {
        // Arrange - Test STDIO transport handling
        var serverConfig = new McpServerConfig
        {
            Transport = "stdio"
        };
        var protocolConfig = new ProtocolComplianceConfig
        {
            TestJsonRpcCompliance = true,
            ProtocolVersion = "2024-11-05"
        };

        // Act
        var result = await _validator.ValidateJsonRpcComplianceAsync(serverConfig, protocolConfig);

        // Assert
        result.Should().NotBeNull();
        result.Status.Should().Be(TestStatus.Failed);
        result.CriticalErrors.Should().NotBeEmpty();
        result.CriticalErrors.Should().Contain(e => e.Contains("STDIO"));
    }

    [Fact]
    public async Task ValidateJsonRpcComplianceAsync_WithServerError_ShouldHandleGracefully()
    {
        // Arrange - Server returns HTTP error
        _testFixture.MockServer
            .Given(Request.Create().WithPath("/mcp"))
            .RespondWith(Response.Create()
                .WithStatusCode(500)
                .WithBody("Internal Server Error"));

        var serverConfig = _testFixture.CreateTestServerConfig();
        var protocolConfig = new ProtocolComplianceConfig
        {
            TestJsonRpcCompliance = true,
            ProtocolVersion = "2024-11-05"
        };

        // Act
        var result = await _validator.ValidateJsonRpcComplianceAsync(serverConfig, protocolConfig);

        // Assert
        result.Should().NotBeNull();
        result.Status.Should().BeOneOf(TestStatus.Failed, TestStatus.Error);
        result.Duration.Should().BeGreaterThan(TimeSpan.Zero);
    }

    [Fact]
    public async Task ValidateJsonRpcComplianceAsync_WithTimeout_ShouldHandleTimeout()
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
                .WithBody("""{"jsonrpc": "2.0", "result": {}, "id": "test"}"""));

        var serverConfig = _testFixture.CreateTestServerConfig(timeoutMs: clientTimeoutMs);
        var protocolConfig = new ProtocolComplianceConfig
        {
            TestJsonRpcCompliance = true,
            ProtocolVersion = "2024-11-05"
        };

        // Act
        var result = await _validator.ValidateJsonRpcComplianceAsync(serverConfig, protocolConfig);

        // Assert
        result.Should().NotBeNull();
        result.Status.Should().BeOneOf(TestStatus.Failed, TestStatus.Error);
        // Should handle timeout gracefully without throwing exceptions
    }

    [Fact]
    [Trait("Category", "LongRunning")]
    public async Task ValidateJsonRpcComplianceAsync_WithExtendedTimeout_ShouldHandleRealisticScenarios()
    {
        // Extended timeout test for comprehensive real-world scenario validation
        // This test can be excluded from regular runs: dotnet test --filter "Category!=LongRunning"
        
        // Arrange - Server with realistic network delay
        _testFixture.MockServer
            .Given(Request.Create().WithPath("/mcp"))
            .RespondWith(Response.Create()
                .WithDelay(TimeSpan.FromSeconds(8)) // Extended delay for real network conditions
                .WithStatusCode(200)
                .WithBody("""{"jsonrpc": "2.0", "result": {}, "id": "test"}"""));

        var serverConfig = _testFixture.CreateTestServerConfig(timeoutMs: 2000); // 2s timeout
        var protocolConfig = new ProtocolComplianceConfig
        {
            TestJsonRpcCompliance = true,
            ProtocolVersion = "2024-11-05"
        };

        // Act
        var result = await _validator.ValidateJsonRpcComplianceAsync(serverConfig, protocolConfig);

        // Assert - Same behavior expected as regular timeout test
        result.Should().NotBeNull();
        result.Status.Should().BeOneOf(TestStatus.Failed, TestStatus.Error);
        // Should handle extended timeout gracefully
    }

    [Theory]
    [InlineData("2024-11-05")]
    [InlineData("1.0.0")]
    public async Task ValidateJsonRpcComplianceAsync_WithDifferentProtocolVersions_ShouldProcess(
        string protocolVersion)
    {
        // Arrange
        _testFixture.MockServer
            .Given(Request.Create().WithPath("/mcp"))
            .RespondWith(Response.Create()
                .WithStatusCode(200)
                .WithHeader("Content-Type", "application/json")
                .WithBody($@"{{
                    ""jsonrpc"": ""2.0"",
                    ""result"": {{
                        ""protocolVersion"": ""{protocolVersion}"",
                        ""capabilities"": {{}}
                    }},
                    ""id"": ""version-test""
                }}"));

        var serverConfig = _testFixture.CreateTestServerConfig();
        var protocolConfig = new ProtocolComplianceConfig
        {
            TestJsonRpcCompliance = true,
            ProtocolVersion = protocolVersion
        };

        // Act
        var result = await _validator.ValidateJsonRpcComplianceAsync(serverConfig, protocolConfig);

        // Assert
        result.Should().NotBeNull();
        result.Status.Should().BeOneOf(TestStatus.Passed, TestStatus.Failed, TestStatus.Error);
        result.Duration.Should().BeGreaterThan(TimeSpan.Zero);
    }

    [Fact]
    public async Task ValidateJsonRpcComplianceAsync_WithInvalidJson_ShouldDetectCompliance()
    {
        // Arrange - Invalid JSON response
        _testFixture.MockServer
            .Given(Request.Create().WithPath("/mcp"))
            .RespondWith(Response.Create()
                .WithStatusCode(200)
                .WithHeader("Content-Type", "application/json")
                .WithBody("{ invalid json }"));

        var serverConfig = _testFixture.CreateTestServerConfig();
        var protocolConfig = new ProtocolComplianceConfig
        {
            TestJsonRpcCompliance = true,
            ProtocolVersion = "2024-11-05"
        };

        // Act
        var result = await _validator.ValidateJsonRpcComplianceAsync(serverConfig, protocolConfig);

        // Assert
        result.Should().NotBeNull();
        result.Status.Should().BeOneOf(TestStatus.Failed, TestStatus.Error);
        result.JsonRpcCompliance.Should().NotBeNull();
    }

    public void Dispose()
    {
        // Cleanup handled by test fixture
    }
}
