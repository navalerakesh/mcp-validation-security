using Mcp.Benchmark.Core.Abstractions;
using Mcp.Benchmark.Core.Constants;
using Mcp.Benchmark.Core.Models;
using Mcp.Compliance.Spec;
using Mcp.Benchmark.Infrastructure.Registries;
using Mcp.Benchmark.Infrastructure.Services;
using Mcp.Benchmark.Infrastructure.Validators;
using Microsoft.Extensions.Logging;

namespace Mcp.Benchmark.Tests.Unit;

public class ProtocolComplianceValidatorUnitTests : IDisposable
{
    private readonly Mock<ILogger<ProtocolComplianceValidator>> _loggerMock;
    private readonly Mock<IMcpHttpClient> _mcpHttpClientMock;
    private readonly IProtocolRuleRegistry _ruleRegistry;

    public ProtocolComplianceValidatorUnitTests()
    {
        _loggerMock = new Mock<ILogger<ProtocolComplianceValidator>>();
        _mcpHttpClientMock = new Mock<IMcpHttpClient>();
        _ruleRegistry = new ProtocolRuleRegistry();
        SetupHappyPathHttpClient();
    }

    [Fact]
    public void Constructor_WithValidDependencies_ShouldInitializeCorrectly()
    {
        var validator = CreateValidator();

        validator.Should().NotBeNull();
        validator.Should().BeOfType<ProtocolComplianceValidator>();
    }

    [Fact]
    public void Constructor_WithNullLogger_ShouldThrowArgumentNullException()
    {
        Action act = () => new ProtocolComplianceValidator(null!, _mcpHttpClientMock.Object, _ruleRegistry);

        act.Should().Throw<ArgumentNullException>()
            .WithParameterName("logger");
    }

    [Fact]
    public void Constructor_WithNullMcpHttpClient_ShouldThrowArgumentNullException()
    {
        Action act = () => new ProtocolComplianceValidator(_loggerMock.Object, null!, _ruleRegistry);

        act.Should().Throw<ArgumentNullException>()
            .WithParameterName("httpClient");
    }

    [Fact]
    public void Constructor_WithNullRuleRegistry_ShouldThrowArgumentNullException()
    {
        Action act = () => new ProtocolComplianceValidator(_loggerMock.Object, _mcpHttpClientMock.Object, null!);

        act.Should().Throw<ArgumentNullException>()
            .WithParameterName("ruleRegistry");
    }

    [Theory]
    [InlineData("http")]
    [InlineData("https")]
    public async Task ValidateJsonRpcComplianceAsync_WithHttpEndpoint_ShouldExecuteWithoutException(string protocol)
    {
        var validator = CreateValidator();
        var serverConfig = new McpServerConfig
        {
            Endpoint = $"{protocol}://localhost:8080/mcp",
            Transport = "http"
        };

        var result = await validator.ValidateJsonRpcComplianceAsync(serverConfig, new ProtocolComplianceConfig
        {
            TestJsonRpcCompliance = true,
            ProtocolVersion = "2024-11-05"
        });

        result.Should().NotBeNull();
        result.Status.Should().BeOneOf(TestStatus.Passed, TestStatus.Failed, TestStatus.Error);
    }

    [Fact]
    public async Task ValidateJsonRpcComplianceAsync_WithStdioTransport_ShouldReturnFailedWithMessage()
    {
        var validator = CreateValidator();
        var serverConfig = new McpServerConfig
        {
            Transport = "stdio"
        };

        var result = await validator.ValidateJsonRpcComplianceAsync(serverConfig, new ProtocolComplianceConfig());

        result.Should().NotBeNull();
        result.Status.Should().Be(TestStatus.Failed);
        result.CriticalErrors.Should().NotBeEmpty();
        result.CriticalErrors.Should().Contain(error => error.Contains("STDIO"));
    }

    [Fact]
    public async Task ValidateJsonRpcComplianceAsync_WithNullEndpoint_ShouldReturnFailedStatus()
    {
        var validator = CreateValidator();
        var serverConfig = new McpServerConfig
        {
            Endpoint = null,
            Transport = "http"
        };

        var result = await validator.ValidateJsonRpcComplianceAsync(serverConfig, new ProtocolComplianceConfig());

        result.Should().NotBeNull();
        result.Status.Should().Be(TestStatus.Failed);
        result.CriticalErrors.Should().NotBeEmpty();
        result.CriticalErrors.Should().Contain(error => error.Contains("endpoint"));
    }

    [Fact]
    public async Task ValidateJsonRpcComplianceAsync_WithValidConfig_ShouldSetCorrectTimestamps()
    {
        var validator = CreateValidator();
        var serverConfig = new McpServerConfig
        {
            Endpoint = "http://localhost:8080/mcp",
            Transport = "http"
        };

        var result = await validator.ValidateJsonRpcComplianceAsync(serverConfig, new ProtocolComplianceConfig());

        result.Should().NotBeNull();
        result.Duration.Should().BeGreaterThan(TimeSpan.Zero);
        result.Duration.Should().BeLessThan(TimeSpan.FromMinutes(1));
    }

    [Theory]
    [InlineData("2024-11-05")]
    [InlineData("1.0.0")]
    [InlineData("latest")]
    public async Task ValidateJsonRpcComplianceAsync_WithDifferentProtocolVersions_ShouldProcessCorrectly(string protocolVersion)
    {
        var validator = CreateValidator();
        var serverConfig = new McpServerConfig
        {
            Endpoint = "http://localhost:8080/mcp",
            Transport = "http"
        };

        var result = await validator.ValidateJsonRpcComplianceAsync(serverConfig, new ProtocolComplianceConfig
        {
            TestJsonRpcCompliance = true,
            ProtocolVersion = protocolVersion
        });

        result.Should().NotBeNull();
        result.Status.Should().BeOneOf(TestStatus.Passed, TestStatus.Failed, TestStatus.Error);
    }

    [Fact]
    public async Task ValidateJsonRpcComplianceAsync_WithLatestAlias_ShouldAdvertiseNewestEmbeddedVersion()
    {
        object? initializePayload = null;

        _mcpHttpClientMock
            .Setup(client => client.CallAsync(
                It.IsAny<string>(),
                ValidationConstants.Methods.Initialize,
                It.IsAny<object?>(),
                It.IsAny<CancellationToken>()))
            .Callback<string, string, object?, CancellationToken>((_, _, payload, _) => initializePayload = payload)
            .ReturnsAsync(CreateSuccessResponse("{\"jsonrpc\":\"2.0\",\"result\":{\"capabilities\":{}},\"id\":\"init\"}"));

        await CreateValidator().ValidateJsonRpcComplianceAsync(
            new McpServerConfig { Endpoint = "http://localhost:8080/mcp", Transport = "http" },
            new ProtocolComplianceConfig { ProtocolVersion = "latest" });

        initializePayload.Should().NotBeNull();
        var version = initializePayload!.GetType().GetProperty("protocolVersion")?.GetValue(initializePayload) as string;
        version.Should().Be(SchemaRegistryProtocolVersions.GetLatestVersion().Value);
    }

    [Fact]
    public async Task ValidateJsonRpcComplianceAsync_WithDeclaredCapabilitiesWithoutMethods_ShouldEmitMismatchFindings()
    {
        _mcpHttpClientMock
            .Setup(client => client.CallAsync(
                It.IsAny<string>(),
                ValidationConstants.Methods.Initialize,
                It.IsAny<object?>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(CreateSuccessResponse("{\"jsonrpc\":\"2.0\",\"result\":{\"capabilities\":{\"logging\":{},\"completions\":{}}},\"id\":\"init\"}"));

        _mcpHttpClientMock
            .Setup(client => client.CallAsync(
                It.IsAny<string>(),
                ValidationConstants.Methods.LoggingSetLevel,
                It.IsAny<object?>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(CreateMethodNotFoundResponse());

        _mcpHttpClientMock
            .Setup(client => client.CallAsync(
                It.IsAny<string>(),
                ValidationConstants.Methods.CompletionComplete,
                It.IsAny<object?>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(CreateMethodNotFoundResponse());

        var result = await CreateValidator().ValidateJsonRpcComplianceAsync(
            new McpServerConfig { Endpoint = "http://localhost:8080/mcp", Transport = "http" },
            new ProtocolComplianceConfig());

        result.Findings.Should().Contain(finding => finding.RuleId == ValidationFindingRuleIds.OptionalCapabilityLoggingDeclaredButUnsupported);
        result.Findings.Should().Contain(finding => finding.RuleId == ValidationFindingRuleIds.OptionalCapabilityCompletionsDeclaredButUnsupported);
    }

    [Fact]
    public async Task ValidateJsonRpcComplianceAsync_WithSupportedOptionalMethods_ShouldEmitStructuredSupportFindings()
    {
        _mcpHttpClientMock
            .Setup(client => client.CallAsync(
                It.IsAny<string>(),
                ValidationConstants.Methods.Initialize,
                It.IsAny<object?>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(CreateSuccessResponse("{\"jsonrpc\":\"2.0\",\"result\":{\"capabilities\":{}},\"id\":\"init\"}"));

        _mcpHttpClientMock
            .Setup(client => client.CallAsync(
                It.IsAny<string>(),
                ValidationConstants.Methods.RootsList,
                It.IsAny<object?>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(CreateSuccessResponse("{\"jsonrpc\":\"2.0\",\"result\":{\"roots\":[]},\"id\":\"roots\"}"));

        _mcpHttpClientMock
            .Setup(client => client.CallAsync(
                It.IsAny<string>(),
                ValidationConstants.Methods.LoggingSetLevel,
                It.IsAny<object?>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(CreateSuccessResponse("{\"jsonrpc\":\"2.0\",\"result\":{},\"id\":\"logging\"}"));

        _mcpHttpClientMock
            .Setup(client => client.CallAsync(
                It.IsAny<string>(),
                ValidationConstants.Methods.SamplingCreateMessage,
                It.IsAny<object?>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(CreateSuccessResponse("{\"jsonrpc\":\"2.0\",\"result\":{},\"id\":\"sampling\"}"));

        _mcpHttpClientMock
            .Setup(client => client.CallAsync(
                It.IsAny<string>(),
                ValidationConstants.Methods.CompletionComplete,
                It.IsAny<object?>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(CreateSuccessResponse("{\"jsonrpc\":\"2.0\",\"result\":{\"completion\":{\"values\":[]}},\"id\":\"complete\"}"));

        var result = await CreateValidator().ValidateJsonRpcComplianceAsync(
            new McpServerConfig { Endpoint = "http://localhost:8080/mcp", Transport = "http" },
            new ProtocolComplianceConfig());

        result.Findings.Should().Contain(finding => finding.RuleId == ValidationFindingRuleIds.OptionalCapabilityRootsSupported);
        result.Findings.Should().Contain(finding => finding.RuleId == ValidationFindingRuleIds.OptionalCapabilityLoggingSupported);
        result.Findings.Should().Contain(finding => finding.RuleId == ValidationFindingRuleIds.OptionalCapabilitySamplingSupported);
        result.Findings.Should().Contain(finding => finding.RuleId == ValidationFindingRuleIds.OptionalCapabilityCompletionsSupported);
        result.Findings.Should().Contain(finding => finding.RuleId == ValidationFindingRuleIds.OptionalCapabilityLoggingSupportedButUndeclared);
        result.Findings.Should().Contain(finding => finding.RuleId == ValidationFindingRuleIds.OptionalCapabilityCompletionsSupportedButUndeclared);
    }

    private ProtocolComplianceValidator CreateValidator() =>
        new(_loggerMock.Object, _mcpHttpClientMock.Object, _ruleRegistry);

    private void SetupHappyPathHttpClient()
    {
        _mcpHttpClientMock
            .Setup(client => client.ValidateErrorCodesAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new JsonRpcErrorValidationResult
            {
                IsCompliant = true,
                OverallScore = 100,
                Tests = [new JsonRpcErrorTest { Name = "method not found", IsValid = true }]
            });

        _mcpHttpClientMock
            .Setup(client => client.CallAsync(
                It.IsAny<string>(),
                ValidationConstants.Methods.Ping,
                It.IsAny<object?>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(CreateSuccessResponse("{\"jsonrpc\":\"2.0\",\"result\":{},\"id\":\"ping\"}"));

        _mcpHttpClientMock
            .Setup(client => client.CallAsync(
                It.IsAny<string>(),
                "non_existent_method",
                It.IsAny<object?>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(CreateMethodNotFoundResponse());

        _mcpHttpClientMock
            .Setup(client => client.CallAsync(
                It.IsAny<string>(),
                ValidationConstants.Methods.RootsList,
                It.IsAny<object?>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(CreateMethodNotFoundResponse());

        _mcpHttpClientMock
            .Setup(client => client.CallAsync(
                It.IsAny<string>(),
                ValidationConstants.Methods.LoggingSetLevel,
                It.IsAny<object?>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(CreateMethodNotFoundResponse());

        _mcpHttpClientMock
            .Setup(client => client.CallAsync(
                It.IsAny<string>(),
                ValidationConstants.Methods.SamplingCreateMessage,
                It.IsAny<object?>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(CreateMethodNotFoundResponse());

        _mcpHttpClientMock
            .Setup(client => client.CallAsync(
                It.IsAny<string>(),
                ValidationConstants.Methods.CompletionComplete,
                It.IsAny<object?>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(CreateMethodNotFoundResponse());

        _mcpHttpClientMock
            .Setup(client => client.CallAsync(
                It.IsAny<string>(),
                ValidationConstants.Methods.Initialize,
                It.IsAny<object?>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(CreateSuccessResponse("{\"jsonrpc\":\"2.0\",\"result\":{\"capabilities\":{}},\"id\":\"init\"}"));

        _mcpHttpClientMock
            .Setup(client => client.SendRawJsonAsync(
                It.IsAny<string>(),
                It.Is<string>(raw => raw == "{ invalid json }"),
                It.IsAny<CancellationToken>(),
                It.IsAny<bool>()))
            .ReturnsAsync(CreateParseErrorResponse());

        _mcpHttpClientMock
            .Setup(client => client.SendRawJsonAsync(
                It.IsAny<string>(),
                It.Is<string>(raw => raw.StartsWith("[{\"jsonrpc\"", StringComparison.Ordinal)),
                It.IsAny<CancellationToken>(),
                It.IsAny<bool>()))
            .ReturnsAsync(new JsonRpcResponse
            {
                StatusCode = 200,
                IsSuccess = true,
                RawJson = "[{\"jsonrpc\":\"2.0\",\"result\":{},\"id\":1},{\"jsonrpc\":\"2.0\",\"result\":{},\"id\":2}]"
            });

        _mcpHttpClientMock
            .Setup(client => client.SendRawJsonAsync(
                It.IsAny<string>(),
                It.Is<string>(raw => raw == "{\"jsonrpc\": \"2.0\", \"method\": \"ping\"}"),
                It.IsAny<CancellationToken>(),
                It.IsAny<bool>()))
            .ReturnsAsync(new JsonRpcResponse
            {
                StatusCode = 204,
                IsSuccess = true,
                RawJson = string.Empty
            });

        _mcpHttpClientMock
            .Setup(client => client.SendRawJsonAsync(
                It.IsAny<string>(),
                It.Is<string>(raw => raw == "{\"JsonRpc\": \"2.0\", \"method\": \"ping\", \"id\": 1}"),
                It.IsAny<CancellationToken>(),
                It.IsAny<bool>()))
            .ReturnsAsync(CreateInvalidRequestResponse());

        _mcpHttpClientMock
            .Setup(client => client.SendAsync(It.IsAny<string>(), It.IsAny<HttpContent>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new HttpResponseMessage(System.Net.HttpStatusCode.UnsupportedMediaType));
    }

    private static JsonRpcResponse CreateSuccessResponse(string rawJson) => new()
    {
        StatusCode = 200,
        IsSuccess = true,
        RawJson = rawJson
    };

    private static JsonRpcResponse CreateMethodNotFoundResponse() => new()
    {
        StatusCode = 200,
        IsSuccess = true,
        RawJson = "{\"jsonrpc\":\"2.0\",\"error\":{\"code\":-32601,\"message\":\"Method not found\"},\"id\":\"missing\"}"
    };

    private static JsonRpcResponse CreateParseErrorResponse() => new()
    {
        StatusCode = 200,
        IsSuccess = true,
        RawJson = "{\"jsonrpc\":\"2.0\",\"error\":{\"code\":-32700,\"message\":\"Parse error\"},\"id\":null}"
    };

    private static JsonRpcResponse CreateInvalidRequestResponse() => new()
    {
        StatusCode = 200,
        IsSuccess = true,
        RawJson = "{\"jsonrpc\":\"2.0\",\"error\":{\"code\":-32600,\"message\":\"Invalid Request\"},\"id\":1}"
    };

    public void Dispose()
    {
        _loggerMock.Reset();
        _mcpHttpClientMock.Reset();
    }
}