using Mcp.Benchmark.Core.Abstractions;
using Mcp.Benchmark.Core.Models;
using Mcp.Benchmark.Infrastructure.Authentication;
using Mcp.Benchmark.Infrastructure.Validators;
using Mcp.Compliance.Spec;
using Microsoft.Extensions.Logging;
using Moq;
using FluentAssertions;
using Xunit;

namespace Mcp.Benchmark.Tests.Unit.Validators;

/// <summary>
/// Comprehensive unit tests for ToolValidator covering all logic branches:
/// - Auth detection and handling
/// - Tool response structure validation
/// - AI readiness scoring
/// - LLM-friendliness grading
/// - Safe payload generation
/// </summary>
public class ToolValidatorComprehensiveTests
{
    private readonly ToolValidator _validator;
    private readonly Mock<IMcpHttpClient> _httpClient;

    public ToolValidatorComprehensiveTests()
    {
        _httpClient = new Mock<IMcpHttpClient>();
        var schemaValidator = new Mock<ISchemaValidator>();
        var schemaRegistry = new Mock<ISchemaRegistry>();
        var authService = new Mock<IAuthenticationService>();
        var contentSafety = new Mock<IContentSafetyAnalyzer>();
        contentSafety.Setup(x => x.AnalyzeTool(It.IsAny<string>())).Returns(new List<ContentSafetyFinding>());

        _validator = new ToolValidator(
            new Mock<ILogger<ToolValidator>>().Object,
            _httpClient.Object,
            schemaValidator.Object,
            schemaRegistry.Object,
            authService.Object,
            contentSafety.Object);
    }

    // ─── Auth Detection ──────────────────────────────────────────

    [Fact]
    public async Task ValidateToolDiscovery_With401AndWwwAuth_ShouldPassWithAuthDetails()
    {
        var config = new McpServerConfig { Endpoint = "https://test.com/mcp", Transport = "http" };
        _httpClient.Setup(x => x.CallAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<object>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new JsonRpcResponse
            {
                StatusCode = 401, IsSuccess = false,
                Headers = new Dictionary<string, string> { { "WWW-Authenticate", "Bearer realm=\"test\"" } }
            });

        var result = await _validator.ValidateToolDiscoveryAsync(config, new ToolTestingConfig(), CancellationToken.None);

        result.Status.Should().Be(TestStatus.Passed);
        result.AuthenticationSecurity.Should().NotBeNull();
        result.AuthenticationSecurity!.AuthenticationRequired.Should().BeTrue();
        result.AuthenticationSecurity.HasProperAuthHeaders.Should().BeTrue();
    }

    [Fact]
    public async Task ValidateToolDiscovery_With401NoWwwAuth_ShouldPassButFlagMissing()
    {
        var config = new McpServerConfig { Endpoint = "https://test.com/mcp", Transport = "http" };
        _httpClient.Setup(x => x.CallAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<object>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new JsonRpcResponse { StatusCode = 401, IsSuccess = false, Headers = new Dictionary<string, string>() });

        var result = await _validator.ValidateToolDiscoveryAsync(config, new ToolTestingConfig(), CancellationToken.None);

        result.Status.Should().Be(TestStatus.Passed);
        result.AuthenticationSecurity!.HasProperAuthHeaders.Should().BeFalse();
    }

    [Fact]
    public async Task ValidateToolDiscovery_With403_ShouldPassAsAuthEnforced()
    {
        var config = new McpServerConfig { Endpoint = "https://test.com/mcp", Transport = "http" };
        _httpClient.Setup(x => x.CallAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<object>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new JsonRpcResponse { StatusCode = 403, IsSuccess = false });

        var result = await _validator.ValidateToolDiscoveryAsync(config, new ToolTestingConfig(), CancellationToken.None);

        result.Status.Should().Be(TestStatus.Passed);
    }

    // ─── Tool Discovery (200 OK) ─────────────────────────────────

    [Fact]
    public async Task ValidateToolDiscovery_WithToolsFound_ShouldDiscoverAndValidate()
    {
        var config = new McpServerConfig { Endpoint = "https://test.com/mcp", Transport = "http" };
        SetupToolsList(_httpClient, "[{\"name\":\"calc\",\"description\":\"Calculator\",\"inputSchema\":{\"type\":\"object\",\"properties\":{\"a\":{\"type\":\"integer\",\"description\":\"First number\"}}}}]");
        SetupToolCall(_httpClient, 200, "{\"jsonrpc\":\"2.0\",\"result\":{\"content\":[{\"type\":\"text\",\"text\":\"42\"}],\"isError\":false},\"id\":1}");

        var result = await _validator.ValidateToolDiscoveryAsync(config, new ToolTestingConfig(), CancellationToken.None);

        result.ToolsDiscovered.Should().BeGreaterThan(0);
        result.DiscoveredToolNames.Should().Contain("calc");
    }

    [Fact]
    public async Task ValidateToolDiscovery_WithNoTools_ShouldPassWithMessage()
    {
        var config = new McpServerConfig { Endpoint = "https://test.com/mcp", Transport = "http" };
        SetupToolsList(_httpClient, "[]");

        var result = await _validator.ValidateToolDiscoveryAsync(config, new ToolTestingConfig(), CancellationToken.None);

        result.ToolsDiscovered.Should().Be(0);
        result.Status.Should().Be(TestStatus.Passed);
        result.Issues.Should().Contain(i => i.Contains("no tools", StringComparison.OrdinalIgnoreCase) || i.Contains("No tools"));
    }

    [Fact]
    public async Task ValidateToolDiscovery_WithServerError_ShouldFail()
    {
        var config = new McpServerConfig { Endpoint = "https://test.com/mcp", Transport = "http" };
        _httpClient.Setup(x => x.CallAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<object>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new JsonRpcResponse { StatusCode = 500, IsSuccess = false, Error = "Internal Error" });

        var result = await _validator.ValidateToolDiscoveryAsync(config, new ToolTestingConfig(), CancellationToken.None);

        result.Status.Should().Be(TestStatus.Failed);
    }

    // ─── Tool Call Response Structure ─────────────────────────────

    [Fact]
    public async Task ValidateToolDiscovery_WithValidToolResponse_ShouldValidateContent()
    {
        var config = new McpServerConfig { Endpoint = "https://test.com/mcp", Transport = "http" };
        SetupToolsList(_httpClient, "[{\"name\":\"echo\",\"inputSchema\":{\"type\":\"object\",\"properties\":{\"msg\":{\"type\":\"string\"}}}}]");
        SetupToolCall(_httpClient, 200, "{\"jsonrpc\":\"2.0\",\"result\":{\"content\":[{\"type\":\"text\",\"text\":\"hello\"}],\"isError\":false},\"id\":1}");

        var result = await _validator.ValidateToolDiscoveryAsync(config, new ToolTestingConfig(), CancellationToken.None);

        result.ToolResults.Should().Contain(t => t.ToolName == "echo");
    }

    [Fact]
    public async Task ValidateToolDiscovery_WithMissingContentArray_ShouldFlag()
    {
        var config = new McpServerConfig { Endpoint = "https://test.com/mcp", Transport = "http" };
        SetupToolsList(_httpClient, "[{\"name\":\"bad_tool\",\"inputSchema\":{\"type\":\"object\",\"properties\":{}}}]");
        SetupToolCall(_httpClient, 200, "{\"jsonrpc\":\"2.0\",\"result\":{\"data\":\"no content array\"},\"id\":1}");

        var result = await _validator.ValidateToolDiscoveryAsync(config, new ToolTestingConfig(), CancellationToken.None);

        result.ToolResults.Should().Contain(t => t.Issues.Any(i => i.Contains("content")));
    }

    [Fact]
    public async Task ValidateToolDiscovery_WithJsonRpcErrorResponse_ShouldPassAsCompliant()
    {
        var config = new McpServerConfig { Endpoint = "https://test.com/mcp", Transport = "http" };
        SetupToolsList(_httpClient, "[{\"name\":\"strict_tool\",\"inputSchema\":{\"type\":\"object\",\"properties\":{\"id\":{\"type\":\"integer\"}}}}]");
        SetupToolCall(_httpClient, 200, "{\"jsonrpc\":\"2.0\",\"error\":{\"code\":-32602,\"message\":\"Invalid params: 'id' must be positive integer\",\"data\":{\"param\":\"id\"}},\"id\":1}");

        var result = await _validator.ValidateToolDiscoveryAsync(config, new ToolTestingConfig(), CancellationToken.None);

        var toolResult = result.ToolResults.FirstOrDefault(t => t.ToolName == "strict_tool");
        toolResult.Should().NotBeNull();
        toolResult!.Status.Should().Be(TestStatus.Passed);
        toolResult.Issues.Should().Contain(i => i.Contains("correctly validated input"));
        // Should also have LLM-friendliness grade
        toolResult.Issues.Should().Contain(i => i.Contains("LLM-Friendliness"));
    }

    [Fact]
    public async Task ValidateToolDiscovery_With500OnToolCall_ShouldFailTool()
    {
        var config = new McpServerConfig { Endpoint = "https://test.com/mcp", Transport = "http" };
        SetupToolsList(_httpClient, "[{\"name\":\"crash_tool\",\"inputSchema\":{\"type\":\"object\",\"properties\":{}}}]");
        SetupToolCall(_httpClient, 500, null);

        var result = await _validator.ValidateToolDiscoveryAsync(config, new ToolTestingConfig(), CancellationToken.None);

        var toolResult = result.ToolResults.FirstOrDefault(t => t.ToolName == "crash_tool");
        toolResult!.Status.Should().Be(TestStatus.Failed);
        toolResult.Issues.Should().Contain(i => i.Contains("crashed") || i.Contains("500"));
    }

    [Fact]
    public async Task ValidateToolDiscovery_WithTimeoutOnToolCall_ShouldPassAsInfraIssue()
    {
        var config = new McpServerConfig { Endpoint = "https://test.com/mcp", Transport = "http" };
        SetupToolsList(_httpClient, "[{\"name\":\"slow_tool\",\"inputSchema\":{\"type\":\"object\",\"properties\":{}}}]");
        _httpClient.Setup(x => x.CallAsync(It.IsAny<string>(), "tools/call", It.IsAny<object>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new JsonRpcResponse { StatusCode = -1, IsSuccess = false, Error = "Timeout" });

        var result = await _validator.ValidateToolDiscoveryAsync(config, new ToolTestingConfig(), CancellationToken.None);

        var toolResult = result.ToolResults.FirstOrDefault(t => t.ToolName == "slow_tool");
        toolResult!.Status.Should().Be(TestStatus.Passed);
        toolResult.Issues.Should().Contain(i => i.Contains("Network/timeout"));
    }

    // ─── AI Readiness Scoring ────────────────────────────────────

    [Fact]
    public async Task ValidateToolDiscovery_WithUndescribedParams_ShouldPenalizeAiScore()
    {
        var config = new McpServerConfig { Endpoint = "https://test.com/mcp", Transport = "http" };
        // Tool with params that have NO descriptions
        SetupToolsList(_httpClient, "[{\"name\":\"vague_tool\",\"inputSchema\":{\"type\":\"object\",\"properties\":{\"x\":{\"type\":\"string\"},\"y\":{\"type\":\"string\"}}}}]");
        SetupToolCall(_httpClient, 400, null);

        var result = await _validator.ValidateToolDiscoveryAsync(config, new ToolTestingConfig(), CancellationToken.None);

        result.AiReadinessScore.Should().BeLessThan(100);
        result.AiReadinessIssues.Should().Contain(i => i.Contains("lack descriptions"));
        result.AiReadinessIssues.Should().Contain(i => i.Contains("no enum/pattern"));
    }

    [Fact]
    public async Task ValidateToolDiscovery_WithWellDescribedParams_ShouldScoreHigh()
    {
        var config = new McpServerConfig { Endpoint = "https://test.com/mcp", Transport = "http" };
        SetupToolsList(_httpClient, "[{\"name\":\"good_tool\",\"inputSchema\":{\"type\":\"object\",\"properties\":{\"color\":{\"type\":\"string\",\"description\":\"The color\",\"enum\":[\"red\",\"blue\"]}}}}]");
        SetupToolCall(_httpClient, 200, "{\"jsonrpc\":\"2.0\",\"result\":{\"content\":[{\"type\":\"text\",\"text\":\"ok\"}]},\"id\":1}");

        var result = await _validator.ValidateToolDiscoveryAsync(config, new ToolTestingConfig(), CancellationToken.None);

        result.AiReadinessScore.Should().BeGreaterThanOrEqualTo(80);
    }

    // ─── Tool Execution Standalone ───────────────────────────────

    [Fact]
    public async Task ValidateToolExecution_WithAuth_ShouldSkip()
    {
        var config = new McpServerConfig { Endpoint = "https://test.com/mcp", Transport = "http" };
        _httpClient.Setup(x => x.CallAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<object>(), It.IsAny<AuthenticationConfig>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new JsonRpcResponse { StatusCode = 401, IsSuccess = false });

        var result = await _validator.ValidateToolExecutionAsync(config, new ToolTestingConfig(), CancellationToken.None);

        result.Status.Should().Be(TestStatus.Skipped);
    }

    [Fact]
    public async Task ValidateToolExecution_WithTools_ShouldCallEach()
    {
        var config = new McpServerConfig { Endpoint = "https://test.com/mcp", Transport = "http" };
        _httpClient.Setup(x => x.CallAsync(It.IsAny<string>(), "tools/list", It.IsAny<object>(), It.IsAny<AuthenticationConfig>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new JsonRpcResponse
            {
                StatusCode = 200, IsSuccess = true,
                RawJson = "{\"jsonrpc\":\"2.0\",\"result\":{\"tools\":[{\"name\":\"t1\"},{\"name\":\"t2\"}]},\"id\":1}"
            });
        _httpClient.Setup(x => x.CallAsync(It.IsAny<string>(), "tools/call", It.IsAny<object>(), It.IsAny<AuthenticationConfig>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new JsonRpcResponse { StatusCode = 200, IsSuccess = true, RawJson = "{\"jsonrpc\":\"2.0\",\"result\":{\"content\":[{\"type\":\"text\",\"text\":\"ok\"}]},\"id\":1}" });

        var result = await _validator.ValidateToolExecutionAsync(config, new ToolTestingConfig(), CancellationToken.None);

        result.ToolsDiscovered.Should().Be(2);
        result.ToolsTestPassed.Should().Be(2);
    }

    // ─── Stdio Transport ─────────────────────────────────────────

    [Fact]
    public async Task ValidateToolDiscovery_WithStdio_ShouldAcceptCommand()
    {
        var config = new McpServerConfig { Endpoint = "npx test-server", Transport = "stdio" };
        SetupToolsList(_httpClient, "[{\"name\":\"stdio_tool\"}]");
        SetupToolCall(_httpClient, 200, "{\"jsonrpc\":\"2.0\",\"result\":{\"content\":[{\"type\":\"text\",\"text\":\"ok\"}]},\"id\":1}");

        var result = await _validator.ValidateToolDiscoveryAsync(config, new ToolTestingConfig(), CancellationToken.None);

        result.Should().NotBeNull();
    }

    // ─── Helpers ─────────────────────────────────────────────────

    private static void SetupToolsList(Mock<IMcpHttpClient> mock, string toolsJson)
    {
        mock.Setup(x => x.CallAsync(It.IsAny<string>(), "tools/list", It.IsAny<object>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new JsonRpcResponse
            {
                StatusCode = 200, IsSuccess = true,
                RawJson = $"{{\"jsonrpc\":\"2.0\",\"result\":{{\"tools\":{toolsJson}}},\"id\":1}}"
            });
    }

    private static void SetupToolCall(Mock<IMcpHttpClient> mock, int statusCode, string? rawJson)
    {
        mock.Setup(x => x.CallAsync(It.IsAny<string>(), "tools/call", It.IsAny<object>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new JsonRpcResponse
            {
                StatusCode = statusCode,
                IsSuccess = statusCode == 200,
                RawJson = rawJson,
                Error = statusCode >= 400 ? "Error" : null
            });
    }
}
