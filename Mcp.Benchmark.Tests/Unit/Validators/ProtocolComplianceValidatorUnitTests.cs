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
    private readonly IValidationApplicabilityResolver _applicabilityResolver;
    private readonly IProtocolFeatureResolver _protocolFeatureResolver;

    public ProtocolComplianceValidatorUnitTests()
    {
        _loggerMock = new Mock<ILogger<ProtocolComplianceValidator>>();
        _mcpHttpClientMock = new Mock<IMcpHttpClient>();
        _ruleRegistry = new ProtocolRuleRegistry();
        _applicabilityResolver = new ValidationApplicabilityResolver(new EmbeddedSchemaRegistry());
        _protocolFeatureResolver = new ProtocolFeatureResolver(
            new ValidationPackRegistry<IProtocolFeaturePack>(
                new IProtocolFeaturePack[] { new BuiltInProtocolFeaturePack(new EmbeddedSchemaRegistry()) }));
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
        Action act = () => new ProtocolComplianceValidator(null!, _mcpHttpClientMock.Object, _ruleRegistry, _applicabilityResolver, _protocolFeatureResolver);

        act.Should().Throw<ArgumentNullException>()
            .WithParameterName("logger");
    }

    [Fact]
    public void Constructor_WithNullMcpHttpClient_ShouldThrowArgumentNullException()
    {
        Action act = () => new ProtocolComplianceValidator(_loggerMock.Object, null!, _ruleRegistry, _applicabilityResolver, _protocolFeatureResolver);

        act.Should().Throw<ArgumentNullException>()
            .WithParameterName("httpClient");
    }

    [Fact]
    public void Constructor_WithNullRuleRegistry_ShouldThrowArgumentNullException()
    {
        Action act = () => new ProtocolComplianceValidator(_loggerMock.Object, _mcpHttpClientMock.Object, null!, _applicabilityResolver, _protocolFeatureResolver);

        act.Should().Throw<ArgumentNullException>()
            .WithParameterName("ruleRegistry");
    }

    [Fact]
    public void Constructor_WithNullApplicabilityResolver_ShouldThrowArgumentNullException()
    {
        Action act = () => new ProtocolComplianceValidator(_loggerMock.Object, _mcpHttpClientMock.Object, _ruleRegistry, null!, _protocolFeatureResolver);

        act.Should().Throw<ArgumentNullException>()
            .WithParameterName("applicabilityResolver");
    }

    [Fact]
    public void Constructor_WithNullProtocolFeatureResolver_ShouldThrowArgumentNullException()
    {
        Action act = () => new ProtocolComplianceValidator(_loggerMock.Object, _mcpHttpClientMock.Object, _ruleRegistry, _applicabilityResolver, null!);

        act.Should().Throw<ArgumentNullException>()
            .WithParameterName("protocolFeatureResolver");
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
        result.Status.Should().BeOneOf(TestStatus.Passed, TestStatus.Failed, TestStatus.Error, TestStatus.Inconclusive, TestStatus.AuthRequired);
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
    public async Task ValidateJsonRpcComplianceAsync_WithStdioEndpoint_ShouldNotThrowWhenHttpOnlyRulesAreAbsent()
    {
        var validator = CreateValidator();
        var serverConfig = new McpServerConfig
        {
            Endpoint = "npx -y mcpval-localmcp",
            Transport = "stdio"
        };

        var result = await validator.ValidateJsonRpcComplianceAsync(serverConfig, new ProtocolComplianceConfig
        {
            TestJsonRpcCompliance = true,
            ProtocolVersion = "2024-11-05"
        });

        result.Should().NotBeNull();
        result.Status.Should().NotBe(TestStatus.Error);
        result.CriticalErrors.Should().BeEmpty();
        result.Message.Should().NotContain("Sequence contains no matching element");
    }

    [Fact]
    public async Task ValidateJsonRpcComplianceAsync_WithStdioTransport_ShouldSkipBatchProbeViolations()
    {
        _mcpHttpClientMock
            .Setup(client => client.SendRawJsonAsync(
                It.IsAny<string>(),
                It.Is<string>(raw => raw.StartsWith("[{\"jsonrpc\"", StringComparison.Ordinal)),
                It.IsAny<CancellationToken>(),
                It.IsAny<bool>()))
            .ReturnsAsync(new JsonRpcResponse
            {
                StatusCode = 500,
                IsSuccess = false,
                Error = "No batch response"
            });

        var validator = CreateValidator();
        var serverConfig = new McpServerConfig
        {
            Endpoint = "npx -y mcpval-localmcp",
            Transport = "stdio"
        };

        var result = await validator.ValidateJsonRpcComplianceAsync(serverConfig, new ProtocolComplianceConfig());

        result.Status.Should().Be(TestStatus.Passed);
        result.Violations.Should().NotContain(v => v.Description.Contains("Batch processing", StringComparison.OrdinalIgnoreCase));
        result.Findings.Should().Contain(f => f.RuleId == "MCP.GUIDELINE.PROTOCOL.BATCH_PROBE_SKIPPED");
    }

    [Fact]
    public async Task ValidateJsonRpcComplianceAsync_With2025_06_18Protocol_ShouldSkipBatchProbeViolations()
    {
        _mcpHttpClientMock
            .Setup(client => client.SendRawJsonAsync(
                It.IsAny<string>(),
                It.Is<string>(raw => raw.StartsWith("[{\"jsonrpc\"", StringComparison.Ordinal)),
                It.IsAny<CancellationToken>(),
                It.IsAny<bool>()))
            .ReturnsAsync(new JsonRpcResponse
            {
                StatusCode = 500,
                IsSuccess = false,
                Error = "Expected StartObject token"
            });

        var validator = CreateValidator();
        var serverConfig = new McpServerConfig
        {
            Endpoint = "http://localhost:8080/mcp",
            Transport = "http"
        };

        var result = await validator.ValidateJsonRpcComplianceAsync(serverConfig, new ProtocolComplianceConfig
        {
            ProtocolVersion = "2025-06-18"
        });

        result.Status.Should().Be(TestStatus.Passed);
        result.Violations.Should().NotContain(v => v.Description.Contains("Batch processing", StringComparison.OrdinalIgnoreCase));
        result.Findings.Should().Contain(f =>
            f.RuleId == "MCP.GUIDELINE.PROTOCOL.BATCH_PROBE_SKIPPED" &&
            f.Summary.Contains("active embedded schema context", StringComparison.Ordinal));
    }

    [Fact]
    public async Task ValidateJsonRpcComplianceAsync_With2025_11_25TransportFailures_ShouldReportStreamableHttpViolations()
    {
        _mcpHttpClientMock
            .Setup(client => client.SendHttpTransportProbeAsync(It.IsAny<HttpTransportProbeRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((HttpTransportProbeRequest request, CancellationToken _) =>
            {
                if (request.Body?.Contains("transport-invalid-version", StringComparison.Ordinal) == true)
                {
                    return CreateTransportProbeResponse(request, 200, "{\"jsonrpc\":\"2.0\",\"result\":{},\"id\":\"transport-invalid-version\"}");
                }

                if (request.Headers.TryGetValue("Origin", out var _origin))
                {
                    return CreateTransportProbeResponse(request, 200, "{\"jsonrpc\":\"2.0\",\"result\":{},\"id\":\"transport-invalid-origin\"}");
                }

                if (string.Equals(request.Method, "GET", StringComparison.OrdinalIgnoreCase))
                {
                    return CreateTransportProbeResponse(request, 200, "{\"jsonrpc\":\"2.0\",\"result\":{},\"id\":\"get\"}", "application/json");
                }

                if (request.Body?.Contains("notifications/initialized", StringComparison.Ordinal) == true)
                {
                    return CreateTransportProbeResponse(request, 200, "{\"jsonrpc\":\"2.0\",\"result\":{},\"id\":\"notification\"}");
                }

                return CreateTransportProbeResponse(request, 200, "{\"jsonrpc\":\"2.0\",\"result\":{},\"id\":\"transport-initialize\"}");
            });

        var result = await CreateValidator().ValidateJsonRpcComplianceAsync(
            new McpServerConfig { Endpoint = "http://localhost:8080/mcp", Transport = "http" },
            new ProtocolComplianceConfig { ProtocolVersion = "2025-11-25" });

        result.StreamableHttpTransport.Should().NotBeNull();
        result.StreamableHttpTransport!.FailedMandatoryProbeCount.Should().BeGreaterThanOrEqualTo(4);
        result.Violations.Should().Contain(v => v.CheckId == ValidationConstants.CheckIds.HttpNotificationStatus);
        result.Violations.Should().Contain(v => v.CheckId == ValidationConstants.CheckIds.HttpGetSseOrMethodNotAllowed);
        result.Violations.Should().Contain(v => v.CheckId == ValidationConstants.CheckIds.HttpInvalidProtocolVersion);
        result.Violations.Should().Contain(v => v.CheckId == ValidationConstants.CheckIds.HttpOriginValidation);
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
        result.Status.Should().BeOneOf(TestStatus.Passed, TestStatus.Failed, TestStatus.Error, TestStatus.Inconclusive, TestStatus.AuthRequired);
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
    public async Task ValidateInitializationAsync_ShouldUseResolvedFeatureVersion()
    {
        object? initializePayload = null;

        _mcpHttpClientMock
            .Setup(client => client.CallAsync(
                It.IsAny<string>(),
                ValidationConstants.Methods.Initialize,
                It.IsAny<object?>(),
                It.IsAny<CancellationToken>()))
            .Callback<string, string, object?, CancellationToken>((_, _, payload, _) => initializePayload = payload)
            .ReturnsAsync(CreateSuccessResponse("{\"jsonrpc\":\"2.0\",\"result\":{\"protocolVersion\":\"2025-11-25\",\"capabilities\":{},\"serverInfo\":{\"name\":\"fixture\",\"version\":\"1.0.0\"}},\"id\":\"init\"}"));

        var protocolFeatureResolver = new Mock<IProtocolFeatureResolver>();
        protocolFeatureResolver
            .Setup(resolver => resolver.Resolve(It.IsAny<ValidationApplicabilityContext>()))
            .Returns(new ProtocolFeatureSet
            {
                NegotiatedProtocolVersion = "2025-11-25",
                SchemaVersion = "2025-11-25",
                OptionalCapabilities = Array.Empty<string>()
            });

        var validator = new ProtocolComplianceValidator(
            _loggerMock.Object,
            _mcpHttpClientMock.Object,
            _ruleRegistry,
            _applicabilityResolver,
            protocolFeatureResolver.Object);

        await validator.ValidateInitializationAsync(
            new McpServerConfig { Endpoint = "http://localhost:8080/mcp", Transport = "http", ProtocolVersion = "2024-11-05" },
            new ProtocolComplianceConfig { ProtocolVersion = "2024-11-05" });

        var version = initializePayload!.GetType().GetProperty("protocolVersion")?.GetValue(initializePayload) as string;
        version.Should().Be("2025-11-25");
        protocolFeatureResolver.Verify(resolver => resolver.Resolve(It.IsAny<ValidationApplicabilityContext>()), Times.Once);
    }

    [Fact]
    public async Task ValidateInitializationAsync_WithUnsupportedServerProtocolVersion_ShouldReportSchemaFallback()
    {
        _mcpHttpClientMock
            .Setup(client => client.CallAsync(
                It.IsAny<string>(),
                ValidationConstants.Methods.Initialize,
                It.IsAny<object?>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(CreateSuccessResponse("{\"jsonrpc\":\"2.0\",\"result\":{\"protocolVersion\":\"2099-01-01\",\"capabilities\":{},\"serverInfo\":{\"name\":\"fixture\",\"version\":\"1.0.0\"}},\"id\":\"init\"}"));

        var result = await CreateValidator().ValidateInitializationAsync(
            new McpServerConfig { Endpoint = "http://localhost:8080/mcp", Transport = "http", ProtocolVersion = "2025-11-25" },
            new ProtocolComplianceConfig { ProtocolVersion = "2025-11-25" });

        result.Initialization.RequestedProtocolVersion.Should().Be("2025-11-25");
        result.Initialization.ServerProtocolVersion.Should().Be("2099-01-01");
        result.Initialization.SchemaVersion.Should().Be(SchemaRegistryProtocolVersions.BackwardCompatibilityDefault.Value);
        result.Initialization.SchemaVersionFallbackApplied.Should().BeTrue();
        result.Initialization.ServerProtocolVersionSupported.Should().BeFalse();
        var violation = result.Violations.Should().ContainSingle(v => v.CheckId == ValidationConstants.CheckIds.ProtocolInitializeUnsupportedProtocolVersion).Subject;
        violation.Context["requestedProtocolVersion"].Should().Be("2025-11-25");
        violation.Context["serverProtocolVersion"].Should().Be("2099-01-01");
        violation.Context["schemaVersion"].Should().Be(SchemaRegistryProtocolVersions.BackwardCompatibilityDefault.Value);
        violation.Context["fallbackApplied"].Should().Be(true);
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
            new ProtocolComplianceConfig { ProtocolVersion = "2025-03-26" });

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
            .ReturnsAsync(CreateSuccessResponse("{\"jsonrpc\":\"2.0\",\"result\":{\"capabilities\":{\"roots\":{},\"logging\":{},\"sampling\":{},\"completions\":{}}},\"id\":\"init\"}"));

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
            new ProtocolComplianceConfig { ProtocolVersion = "2025-03-26" });

        result.Findings.Should().Contain(finding => finding.RuleId == ValidationFindingRuleIds.OptionalCapabilityRootsSupported);
        result.Findings.Should().Contain(finding => finding.RuleId == ValidationFindingRuleIds.OptionalCapabilityLoggingSupported);
        result.Findings.Should().Contain(finding => finding.RuleId == ValidationFindingRuleIds.OptionalCapabilitySamplingSupported);
        result.Findings.Should().Contain(finding => finding.RuleId == ValidationFindingRuleIds.OptionalCapabilityCompletionsSupported);
        result.Findings.Should().NotContain(finding => finding.RuleId == ValidationFindingRuleIds.OptionalCapabilityLoggingSupportedButUndeclared);
        result.Findings.Should().NotContain(finding => finding.RuleId == ValidationFindingRuleIds.OptionalCapabilityCompletionsSupportedButUndeclared);
    }

    [Fact]
    public async Task ValidateJsonRpcComplianceAsync_WithClientSideCapabilities_ShouldEmitAiSafetyAdvisories()
    {
        _mcpHttpClientMock
            .Setup(client => client.CallAsync(
                It.IsAny<string>(),
                ValidationConstants.Methods.Initialize,
                It.IsAny<object?>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(CreateSuccessResponse("{\"jsonrpc\":\"2.0\",\"result\":{\"capabilities\":{\"roots\":{},\"sampling\":{\"tools\":{}},\"elicitation\":{\"url\":{}},\"tasks\":{\"list\":{},\"requests\":{\"tools\":{\"call\":{}},\"sampling\":{\"createMessage\":{}},\"elicitation\":{\"create\":{}}}}}},\"id\":\"init\"}"));

        var result = await CreateValidator().ValidateJsonRpcComplianceAsync(
            new McpServerConfig { Endpoint = "http://localhost:8080/mcp", Transport = "http" },
            new ProtocolComplianceConfig { ProtocolVersion = "2025-11-25" });

        result.Findings.Should().Contain(finding =>
            finding.RuleId == ValidationFindingRuleIds.AiClientCapabilityAdvertisedByServer &&
            finding.Metadata["capability"] == McpSpecConstants.Capabilities.Roots);
        result.Findings.Should().Contain(finding =>
            finding.RuleId == ValidationFindingRuleIds.AiClientCapabilityAdvertisedByServer &&
            finding.Metadata["capability"] == McpSpecConstants.Capabilities.Sampling);
        result.Findings.Should().Contain(finding =>
            finding.RuleId == ValidationFindingRuleIds.AiClientCapabilityAdvertisedByServer &&
            finding.Metadata["capability"] == McpSpecConstants.Capabilities.Elicitation);
        result.Findings.Should().Contain(finding => finding.RuleId == ValidationFindingRuleIds.AiRootsBoundaryAdvisory);
        result.Findings.Should().Contain(finding => finding.RuleId == ValidationFindingRuleIds.AiSamplingHumanReviewAdvisory);
        result.Findings.Should().Contain(finding => finding.RuleId == ValidationFindingRuleIds.AiElicitationConsentAdvisory);

        var taskFinding = result.Findings.Should().ContainSingle(finding => finding.RuleId == ValidationFindingRuleIds.AiTasksIsolationAdvisory).Subject;
        taskFinding.Metadata["taskCapabilities"].Should().Contain(McpSpecConstants.Capabilities.TasksRequestsToolsCall);
        taskFinding.Metadata["taskCapabilities"].Should().Contain(McpSpecConstants.Capabilities.TasksList);
    }

    [Fact]
    public async Task ValidateJsonRpcComplianceAsync_WithoutAiCapabilities_ShouldNotEmitAiSafetyAdvisories()
    {
        _mcpHttpClientMock
            .Setup(client => client.CallAsync(
                It.IsAny<string>(),
                ValidationConstants.Methods.Initialize,
                It.IsAny<object?>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(CreateSuccessResponse("{\"jsonrpc\":\"2.0\",\"result\":{\"capabilities\":{\"tools\":{}}},\"id\":\"init\"}"));

        var result = await CreateValidator().ValidateJsonRpcComplianceAsync(
            new McpServerConfig { Endpoint = "http://localhost:8080/mcp", Transport = "http" },
            new ProtocolComplianceConfig { ProtocolVersion = "2025-11-25" });

        result.Findings.Should().NotContain(finding => finding.Category == "AiSafety");
    }

    [Fact]
    public async Task ValidateJsonRpcComplianceAsync_ShouldPopulateProtocolDetailSnapshots()
    {
        var result = await CreateValidator().ValidateJsonRpcComplianceAsync(
            new McpServerConfig { Endpoint = "http://localhost:8080/mcp", Transport = "http" },
            new ProtocolComplianceConfig());

        result.NotificationHandling.NotificationFormatCorrect.Should().BeTrue();
        result.NotificationHandling.SubscriptionMechanismWorking.Should().BeNull();
        result.NotificationHandling.UnsubscriptionWorking.Should().BeNull();
        result.NotificationHandling.NotificationsReceived.Should().BeNull();
        result.MessageFormat.RequestFormatValid.Should().BeTrue();
        result.MessageFormat.ResponseFormatValid.Should().BeTrue();
        result.MessageFormat.ErrorFormatValid.Should().BeTrue();
    }

    [Fact]
    public async Task ValidateJsonRpcComplianceAsync_WithPlainText400ForUnknownMethod_ShouldFailErrorCodeCompliance()
    {
        _mcpHttpClientMock
            .Setup(client => client.ValidateErrorCodesAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new JsonRpcErrorValidationResult
            {
                Tests =
                [
                    new JsonRpcErrorTest
                    {
                        Name = "Parse Error",
                        ExpectedErrorCode = -32700,
                        IsValid = true,
                        ActualResponse = CreateSuccessResponse("{\"jsonrpc\":\"2.0\",\"error\":{\"code\":-32700},\"id\":null}")
                    },
                    new JsonRpcErrorTest
                    {
                        Name = "Invalid Request - Missing jsonrpc",
                        ExpectedErrorCode = -32600,
                        IsValid = true,
                        ActualResponse = CreateSuccessResponse("{\"jsonrpc\":\"2.0\",\"error\":{\"code\":-32600},\"id\":1}")
                    },
                    new JsonRpcErrorTest
                    {
                        Name = "Method Not Found",
                        ExpectedErrorCode = -32601,
                        IsValid = false,
                        ActualResponse = new JsonRpcResponse
                        {
                            StatusCode = 400,
                            IsSuccess = false,
                            RawJson = "JSON RPC not handled: \"nonexistent_method\" unsupported"
                        }
                    },
                    new JsonRpcErrorTest
                    {
                        Name = "Invalid Params",
                        ExpectedErrorCode = -32602,
                        IsValid = true,
                        ActualResponse = CreateSuccessResponse("{\"jsonrpc\":\"2.0\",\"error\":{\"code\":-32602},\"id\":1}")
                    }
                ],
                OverallScore = 75,
                IsCompliant = false
            });

        var result = await CreateValidator().ValidateJsonRpcComplianceAsync(
            new McpServerConfig { Endpoint = "http://localhost:8080/mcp", Transport = "http" },
            new ProtocolComplianceConfig());

        result.JsonRpcCompliance.ErrorHandlingCompliant.Should().BeFalse();
        result.Violations.Should().Contain(v => v.Description == "Error codes do not comply with JSON-RPC 2.0 standard error codes");
    }

    [Fact]
    public async Task ValidateJsonRpcComplianceAsync_WithPlainText500ForMalformedJson_ShouldFailRequestFormat()
    {
        _mcpHttpClientMock
            .Setup(client => client.ValidateErrorCodesAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new JsonRpcErrorValidationResult
            {
                Tests =
                [
                    new JsonRpcErrorTest
                    {
                        Name = "Parse Error",
                        ExpectedErrorCode = -32700,
                        IsValid = false,
                        ActualResponse = new JsonRpcResponse
                        {
                            StatusCode = 500,
                            IsSuccess = false,
                            RawJson = "Internal server error while parsing request"
                        }
                    },
                    new JsonRpcErrorTest
                    {
                        Name = "Invalid Request - Missing jsonrpc",
                        ExpectedErrorCode = -32600,
                        IsValid = true,
                        ActualResponse = CreateSuccessResponse("{\"jsonrpc\":\"2.0\",\"error\":{\"code\":-32600},\"id\":1}")
                    },
                    new JsonRpcErrorTest
                    {
                        Name = "Method Not Found",
                        ExpectedErrorCode = -32601,
                        IsValid = true,
                        ActualResponse = CreateSuccessResponse("{\"jsonrpc\":\"2.0\",\"error\":{\"code\":-32601},\"id\":1}")
                    },
                    new JsonRpcErrorTest
                    {
                        Name = "Invalid Params",
                        ExpectedErrorCode = -32602,
                        IsValid = true,
                        ActualResponse = CreateSuccessResponse("{\"jsonrpc\":\"2.0\",\"error\":{\"code\":-32602},\"id\":1}")
                    }
                ],
                OverallScore = 75,
                IsCompliant = false
            });

        var result = await CreateValidator().ValidateJsonRpcComplianceAsync(
            new McpServerConfig { Endpoint = "http://localhost:8080/mcp", Transport = "http" },
            new ProtocolComplianceConfig());

        result.MessageFormat.RequestFormatValid.Should().BeFalse();
        result.Violations.Should().Contain(v => v.Description == "Request format does not comply with JSON-RPC 2.0 specification");
    }

    [Fact]
    public async Task ValidateJsonRpcComplianceAsync_WithStdioRawProbeDrops_ShouldSkipUnobservableMalformedRequestFailures()
    {
        _mcpHttpClientMock
            .Setup(client => client.ValidateErrorCodesAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new JsonRpcErrorValidationResult
            {
                Tests =
                [
                    new JsonRpcErrorTest
                    {
                        Name = "Parse Error",
                        ExpectedErrorCode = -32700,
                        IsValid = false,
                        ActualResponse = new JsonRpcResponse
                        {
                            StatusCode = 500,
                            IsSuccess = false,
                            Error = "No response"
                        }
                    },
                    new JsonRpcErrorTest
                    {
                        Name = "Invalid Request - Missing jsonrpc",
                        ExpectedErrorCode = -32600,
                        IsValid = false,
                        ActualResponse = new JsonRpcResponse
                        {
                            StatusCode = 500,
                            IsSuccess = false,
                            Error = "No response"
                        }
                    },
                    new JsonRpcErrorTest
                    {
                        Name = "Method Not Found",
                        ExpectedErrorCode = -32601,
                        IsValid = true,
                        ActualResponse = CreateSuccessResponse("{\"jsonrpc\":\"2.0\",\"error\":{\"code\":-32601},\"id\":1}")
                    },
                    new JsonRpcErrorTest
                    {
                        Name = "Invalid Params",
                        ExpectedErrorCode = -32602,
                        IsValid = false,
                        ActualResponse = new JsonRpcResponse
                        {
                            StatusCode = 500,
                            IsSuccess = false,
                            Error = "No response"
                        }
                    }
                ],
                OverallScore = 25,
                IsCompliant = false
            });

        var result = await CreateValidator().ValidateJsonRpcComplianceAsync(
            new McpServerConfig { Endpoint = "npx -y mcpval-localmcp", Transport = "stdio" },
            new ProtocolComplianceConfig());

        result.MessageFormat.RequestFormatValid.Should().BeTrue();
        result.JsonRpcCompliance.ErrorHandlingCompliant.Should().BeTrue();
        result.Violations.Should().NotContain(v => v.Description.Contains("Parse Error", StringComparison.Ordinal));
        result.Violations.Should().NotContain(v => v.Description.Contains("Invalid Request - Missing jsonrpc", StringComparison.Ordinal));
        result.Violations.Should().NotContain(v => v.Description.Contains("Invalid Params", StringComparison.Ordinal));
        result.Violations.Should().NotContain(v => v.Description == "Request format does not comply with JSON-RPC 2.0 specification");
        result.Findings.Should().Contain(f => f.RuleId == "MCP.GUIDELINE.PROTOCOL.STDIO_RAW_ERROR_PROBE_SKIPPED");
        result.StdioTransport.Should().NotBeNull();
        result.StdioTransport!.Probes.Should().Contain(probe =>
            probe.CheckId == ValidationConstants.CheckIds.StdioParserBoundary &&
            probe.Mandatory == false &&
            probe.Passed == null);
        result.Findings.Should().Contain(f => f.RuleId == "MCP.GUIDELINE.STDIO.PARSER_BOUNDARY_PROBE_SKIPPED");
    }

    [Fact]
    public async Task ValidateJsonRpcComplianceAsync_WithStdioTransportFailures_ShouldReportTransportViolations()
    {
        _mcpHttpClientMock
            .Setup(client => client.SendStdioTransportProbeAsync(
                It.Is<StdioTransportProbeRequest>(request => request.Kind == StdioTransportProbeKind.MessageExchange),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync((StdioTransportProbeRequest request, CancellationToken _) => new StdioTransportProbeResponse
            {
                ProbeId = request.ProbeId,
                Kind = request.Kind,
                StatusCode = 200,
                IsSuccess = true,
                Executed = true,
                RawStdout = "debug: starting server"
            });

        _mcpHttpClientMock
            .Setup(client => client.SendStdioTransportProbeAsync(
                It.Is<StdioTransportProbeRequest>(request => request.Kind == StdioTransportProbeKind.ShutdownLifecycle),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync((StdioTransportProbeRequest request, CancellationToken _) => new StdioTransportProbeResponse
            {
                ProbeId = request.ProbeId,
                Kind = request.Kind,
                StatusCode = 500,
                IsSuccess = false,
                Executed = true,
                ProcessExited = false,
                Restarted = false,
                ShutdownMode = "kill"
            });

        var result = await CreateValidator().ValidateJsonRpcComplianceAsync(
            new McpServerConfig { Endpoint = "npx -y mcpval-localmcp", Transport = "stdio" },
            new ProtocolComplianceConfig { ProtocolVersion = "2025-11-25" });

        result.StdioTransport.Should().NotBeNull();
        result.StdioTransport!.FailedMandatoryProbeCount.Should().Be(2);
        result.Violations.Should().Contain(violation => violation.CheckId == ValidationConstants.CheckIds.StdioStdoutJsonRpcOnly);
        result.Violations.Should().Contain(violation => violation.CheckId == ValidationConstants.CheckIds.StdioLifecycleShutdown);
    }

    [Fact]
    public async Task ValidateJsonRpcComplianceAsync_WithUnobservableStdioTransportProbe_ShouldMarkInconclusive()
    {
        _mcpHttpClientMock
            .Setup(client => client.SendStdioTransportProbeAsync(
                It.Is<StdioTransportProbeRequest>(request => request.Kind == StdioTransportProbeKind.MessageExchange),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync((StdioTransportProbeRequest request, CancellationToken _) => new StdioTransportProbeResponse
            {
                ProbeId = request.ProbeId,
                Kind = request.Kind,
                StatusCode = -1,
                IsSuccess = false,
                Executed = false,
                Error = "No response from STDIO stdout."
            });

        var result = await CreateValidator().ValidateJsonRpcComplianceAsync(
            new McpServerConfig { Endpoint = "npx -y mcpval-localmcp", Transport = "stdio" },
            new ProtocolComplianceConfig { ProtocolVersion = "2025-11-25" });

        result.Status.Should().Be(TestStatus.Inconclusive);
        result.StdioTransport.Should().NotBeNull();
        result.StdioTransport!.Probes.Should().Contain(probe => probe.Mandatory && probe.Passed == null);
        result.Violations.Should().NotContain(violation => violation.CheckId == ValidationConstants.CheckIds.StdioStdoutJsonRpcOnly);
        result.Findings.Should().Contain(f => f.RuleId == "MCP.GUIDELINE.STDIO.TRANSPORT_PROBE_INCONCLUSIVE");
    }

    [Fact]
    public async Task ValidateJsonRpcComplianceAsync_ShouldUseDeclaredListMethodForBatchProbe()
    {
        _mcpHttpClientMock
            .Setup(client => client.CallAsync(
                It.IsAny<string>(),
                ValidationConstants.Methods.Initialize,
                It.IsAny<object?>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(CreateSuccessResponse("{\"jsonrpc\":\"2.0\",\"result\":{\"capabilities\":{\"tools\":{}}},\"id\":\"init\"}"));

        _mcpHttpClientMock
            .Setup(client => client.SendRawJsonAsync(
                It.IsAny<string>(),
                It.Is<string>(raw => raw.Contains("\"method\": \"tools/list\"", StringComparison.Ordinal)),
                It.IsAny<CancellationToken>(),
                It.IsAny<bool>()))
            .ReturnsAsync(new JsonRpcResponse
            {
                StatusCode = 200,
                IsSuccess = true,
                RawJson = "[{\"jsonrpc\":\"2.0\",\"result\":{\"tools\":[]},\"id\":1},{\"jsonrpc\":\"2.0\",\"result\":{\"tools\":[]},\"id\":2}]"
            });

        var result = await CreateValidator().ValidateJsonRpcComplianceAsync(
            new McpServerConfig { Endpoint = "http://localhost:8080/mcp", Transport = "http" },
            new ProtocolComplianceConfig { ProtocolVersion = "2025-03-26" });

        result.Violations.Should().NotContain(v => v.Description.Contains("Batch processing", StringComparison.OrdinalIgnoreCase));
        _mcpHttpClientMock.Verify(client => client.SendRawJsonAsync(
            It.IsAny<string>(),
            It.Is<string>(raw => raw.Contains("\"method\": \"tools/list\"", StringComparison.Ordinal)),
            It.IsAny<CancellationToken>(),
            It.IsAny<bool>()), Times.AtLeastOnce);
    }

    private ProtocolComplianceValidator CreateValidator() =>
        new(_loggerMock.Object, _mcpHttpClientMock.Object, _ruleRegistry, _applicabilityResolver, _protocolFeatureResolver);

    private void SetupHappyPathHttpClient()
    {
        _mcpHttpClientMock
            .Setup(client => client.ValidateErrorCodesAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new JsonRpcErrorValidationResult
            {
                IsCompliant = true,
                OverallScore = 100,
                Tests =
                [
                    new JsonRpcErrorTest
                    {
                        Name = "Parse Error",
                        ExpectedErrorCode = -32700,
                        IsValid = true,
                        ActualResponse = CreateParseErrorResponse()
                    },
                    new JsonRpcErrorTest
                    {
                        Name = "Invalid Request - Missing jsonrpc",
                        ExpectedErrorCode = -32600,
                        IsValid = true,
                        ActualResponse = CreateInvalidRequestResponse()
                    },
                    new JsonRpcErrorTest
                    {
                        Name = "Method Not Found",
                        ExpectedErrorCode = -32601,
                        IsValid = true,
                        ActualResponse = CreateMethodNotFoundResponse()
                    },
                    new JsonRpcErrorTest
                    {
                        Name = "Invalid Params",
                        ExpectedErrorCode = -32602,
                        IsValid = true,
                        ActualResponse = CreateSuccessResponse("{\"jsonrpc\":\"2.0\",\"error\":{\"code\":-32602,\"message\":\"Invalid params\"},\"id\":1}")
                    }
                ]
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
                It.Is<string>(raw => raw == "{\"jsonrpc\": \"2.0\", \"method\": \"notifications/initialized\"}"),
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

        _mcpHttpClientMock
            .Setup(client => client.SendHttpTransportProbeAsync(It.IsAny<HttpTransportProbeRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((HttpTransportProbeRequest request, CancellationToken _) => CreateDefaultTransportProbeResponse(request));

        _mcpHttpClientMock
            .Setup(client => client.SendStdioTransportProbeAsync(It.IsAny<StdioTransportProbeRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((StdioTransportProbeRequest request, CancellationToken _) => CreateDefaultStdioTransportProbeResponse(request));
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

    private static HttpTransportProbeResponse CreateDefaultTransportProbeResponse(HttpTransportProbeRequest request)
    {
        if (request.Body?.Contains("transport-invalid-version", StringComparison.Ordinal) == true)
        {
            return CreateTransportProbeResponse(request, 400, string.Empty);
        }

        if (request.Headers.ContainsKey("Origin"))
        {
            return CreateTransportProbeResponse(request, 403, string.Empty);
        }

        if (string.Equals(request.Method, "GET", StringComparison.OrdinalIgnoreCase))
        {
            return CreateTransportProbeResponse(request, 405, string.Empty);
        }

        if (request.Body?.Contains("notifications/initialized", StringComparison.Ordinal) == true)
        {
            return CreateTransportProbeResponse(request, 202, string.Empty);
        }

        return CreateTransportProbeResponse(request, 200, "{\"jsonrpc\":\"2.0\",\"result\":{},\"id\":\"transport-initialize\"}");
    }

    private static HttpTransportProbeResponse CreateTransportProbeResponse(
        HttpTransportProbeRequest request,
        int statusCode,
        string body,
        string contentType = "application/json") => new()
    {
        StatusCode = statusCode,
        IsSuccess = statusCode is >= 200 and < 300,
        Body = body,
        ContentType = contentType,
        RequestHeaders = BuildRequestHeaders(request),
        Headers = string.IsNullOrWhiteSpace(contentType)
            ? new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            : new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase) { ["Content-Type"] = contentType }
    };

    private static StdioTransportProbeResponse CreateDefaultStdioTransportProbeResponse(StdioTransportProbeRequest request)
    {
        if (request.Kind == StdioTransportProbeKind.ShutdownLifecycle)
        {
            return new StdioTransportProbeResponse
            {
                ProbeId = request.ProbeId,
                Kind = request.Kind,
                StatusCode = 200,
                IsSuccess = true,
                Executed = true,
                ProcessExited = true,
                Restarted = true,
                ShutdownMode = "stdin-close"
            };
        }

        return new StdioTransportProbeResponse
        {
            ProbeId = request.ProbeId,
            Kind = request.Kind,
            StatusCode = 200,
            IsSuccess = true,
            Executed = true,
            RawStdout = "{\"jsonrpc\":\"2.0\",\"result\":{},\"id\":\"stdio-transport-ping\"}"
        };
    }

    private static Dictionary<string, string> BuildRequestHeaders(HttpTransportProbeRequest request)
    {
        var headers = new Dictionary<string, string>(request.Headers, StringComparer.OrdinalIgnoreCase);
        if (!string.IsNullOrWhiteSpace(request.ContentType))
        {
            headers["Content-Type"] = request.ContentType!;
        }

        return headers;
    }

    [Fact]
    public async Task ValidateNotificationHandlingAsync_WithInitializedNotificationAccepted_ShouldPass()
    {
        var validator = CreateValidator();
        var serverConfig = new McpServerConfig
        {
            Endpoint = "http://localhost:8080/mcp",
            Transport = "http"
        };

        var result = await validator.ValidateNotificationHandlingAsync(serverConfig, new ProtocolComplianceConfig());

        result.Status.Should().Be(TestStatus.Passed);
        result.Violations.Should().BeEmpty();

        _mcpHttpClientMock.Verify(client => client.SendRawJsonAsync(
            It.IsAny<string>(),
            It.Is<string>(raw => raw == "{\"jsonrpc\": \"2.0\", \"method\": \"notifications/initialized\"}"),
            It.IsAny<CancellationToken>(),
            It.IsAny<bool>()), Times.AtLeastOnce);
    }

    [Fact]
    public async Task ValidateNotificationHandlingAsync_WithTransientRateLimit_ShouldSkip()
    {
        _mcpHttpClientMock
            .Setup(client => client.SendRawJsonAsync(
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<CancellationToken>(),
                It.IsAny<bool>()))
            .ReturnsAsync(new JsonRpcResponse
            {
                StatusCode = 429,
                IsSuccess = false,
                Error = "HTTP 429 Too Many Requests",
                RawJson = "too many requests"
            });

        var validator = CreateValidator();
        var serverConfig = new McpServerConfig
        {
            Endpoint = "http://localhost:8080/mcp",
            Transport = "http"
        };

        var result = await validator.ValidateNotificationHandlingAsync(serverConfig, new ProtocolComplianceConfig());

        result.Status.Should().Be(TestStatus.Skipped);
        result.Violations.Should().BeEmpty();
        result.Message.Should().Contain("transient transport pressure");
    }

    public void Dispose()
    {
        _loggerMock.Reset();
        _mcpHttpClientMock.Reset();
    }
}