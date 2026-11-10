using Mcp.Benchmark.Core.Abstractions;
using Mcp.Benchmark.Core.Constants;
using Mcp.Benchmark.Core.Models;
using Mcp.Benchmark.Core.Services;
using Mcp.Benchmark.Infrastructure.Services.Reporting;
using Mcp.Benchmark.Infrastructure.Authentication;
using Mcp.Benchmark.Infrastructure.Services;
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
    private readonly Mock<IAuthenticationService> _authService;

    public ToolValidatorComprehensiveTests()
    {
        _httpClient = new Mock<IMcpHttpClient>();
        var schemaRegistry = new Mock<ISchemaRegistry>();
        _authService = new Mock<IAuthenticationService>();
        var contentSafety = new Mock<IContentSafetyAnalyzer>();
        contentSafety.Setup(x => x.AnalyzeTool(It.IsAny<string>())).Returns(new List<ContentSafetyFinding>());

        _validator = new ToolValidator(
            new Mock<ILogger<ToolValidator>>().Object,
            _httpClient.Object,
            new JsonSchemaValidator(),
            schemaRegistry.Object,
            _authService.Object,
            contentSafety.Object,
            new ToolAiReadinessAnalyzer());
    }

    [Theory]
    [InlineData("")]
    [InlineData(" ")]
    public async Task ValidateToolDiscovery_ShouldTreatPresentCursorAsOpaqueEvenWhenEmptyOrWhitespace(string cursor)
    {
        _httpClient.Setup(client => client.CallAsync(It.IsAny<string>(), "tools/list", It.IsAny<object?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new JsonRpcResponse { IsSuccess = true, StatusCode = 200,
                RawJson = "{\"result\":{\"tools\":[{\"name\":\"first\",\"inputSchema\":{\"type\":\"object\"}}],\"nextCursor\":" + System.Text.Json.JsonSerializer.Serialize(cursor) + "}}" });
        _httpClient.Setup(client => client.CallAsync(It.IsAny<string>(), "tools/list", It.IsAny<object?>(), It.IsAny<AuthenticationConfig?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new JsonRpcResponse { IsSuccess = true, StatusCode = 200,
                RawJson = "{\"result\":{\"tools\":[{\"name\":\"last\",\"inputSchema\":{\"type\":\"object\"}}]}}" });

        var result = await _validator.ValidateToolDiscoveryAsync(new McpServerConfig { Endpoint = "https://example.invalid/mcp" },
            new ToolTestingConfig { TestToolExecution = false, TestParameterValidation = false });

        result.DiscoveredToolNames.Should().Equal("first", "last");
        result.CatalogEnumeration!.PageAttempts.Should().Be(2);
        result.CatalogEnumeration.IsComplete.Should().BeTrue();
    }

    [Fact]
    public async Task ValidateToolDiscovery_AuthRequiredContinuation_ShouldRetainDistinctOutcome()
    {
        _httpClient.Setup(client => client.CallAsync(It.IsAny<string>(), "tools/list", It.IsAny<object?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new JsonRpcResponse { IsSuccess = true, StatusCode = 200,
                RawJson = "{\"result\":{\"tools\":[{\"name\":\"retained\",\"inputSchema\":{\"type\":\"object\"}}],\"nextCursor\":\"next\"}}" });
        _httpClient.Setup(client => client.CallAsync(It.IsAny<string>(), "tools/list", It.IsAny<object?>(), It.IsAny<AuthenticationConfig?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new JsonRpcResponse { IsSuccess = false, StatusCode = 401 });

        var result = await _validator.ValidateToolDiscoveryAsync(new McpServerConfig { Endpoint = "https://example.invalid/mcp" },
            new ToolTestingConfig { TestToolExecution = false, TestParameterValidation = false });

        result.DiscoveredToolNames.Should().Equal("retained");
        result.Status.Should().Be(TestStatus.AuthRequired);
        result.AiReadinessScore.Should().Be(-1);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ValidateToolDiscovery_ContinuationBudget_ShouldStopAndNotPersistCursor(bool oversizedCursor)
    {
        var cursor = oversizedCursor ? new string('x', CatalogEnumerationLimits.MaximumCursorCharacters + 1) : "cursor-canary-0";
        _httpClient.Setup(client => client.CallAsync(It.IsAny<string>(), "tools/list", It.IsAny<object?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new JsonRpcResponse { IsSuccess = true, StatusCode = 200,
                RawJson = "{\"result\":{\"tools\":[],\"nextCursor\":" + System.Text.Json.JsonSerializer.Serialize(cursor) + "}}" });
        var continuations = 0;
        _httpClient.Setup(client => client.CallAsync(It.IsAny<string>(), "tools/list", It.IsAny<object?>(), It.IsAny<AuthenticationConfig?>(), It.IsAny<CancellationToken>()))
            .Returns(() => Task.FromResult(new JsonRpcResponse { IsSuccess = true, StatusCode = 200,
                RawJson = "{\"result\":{\"tools\":[],\"nextCursor\":\"cursor-canary-" + ++continuations + "\"}}" }));

        var result = await _validator.ValidateToolDiscoveryAsync(new McpServerConfig { Endpoint = "https://example.invalid/mcp" },
            new ToolTestingConfig { TestToolExecution = false, TestParameterValidation = false });

        result.CatalogEnumeration!.Termination.Should().Be(CatalogEnumerationTermination.BudgetExceeded);
        result.CatalogEnumeration.PageAttempts.Should().Be(oversizedCursor ? 1 : CatalogEnumerationLimits.MaximumPages);
        continuations.Should().Be(oversizedCursor ? 0 : CatalogEnumerationLimits.MaximumPages - 1);
        result.Status.Should().Be(TestStatus.Inconclusive);
        result.CatalogEnumeration.IsComplete.Should().BeFalse();
        System.Text.Json.JsonSerializer.Serialize(result).Should().NotContain("cursor-canary");
    }

    [Fact]
    public async Task ValidateToolExecution_ShouldGenerateRequiredLocalReferenceConstant()
    {
        SetupToolsList(_httpClient, """
            [{"name":"fixture","description":"Reads a test value.","inputSchema":{"type":"object","$defs":{"value":{"type":["string","null"],"const":"permitted"}},"properties":{"value":{"$ref":"#/$defs/value"}},"required":["value"]}}]
            """, authenticated: true);
        object? captured = null;
        _httpClient.Setup(client => client.CallAsync(It.IsAny<string>(), "tools/call", It.IsAny<object?>(),
                It.IsAny<AuthenticationConfig?>(), It.IsAny<CancellationToken>()))
            .Callback<string, string, object?, AuthenticationConfig?, CancellationToken>((_, _, parameters, _, _) => captured = parameters)
            .ReturnsAsync(new JsonRpcResponse { IsSuccess = true, StatusCode = 200 });

        var result = await _validator.ValidateToolExecutionAsync(new McpServerConfig { Endpoint = "https://example.invalid/mcp" }, new ToolTestingConfig());

        result.Status.Should().Be(TestStatus.Passed);
        captured.Should().NotBeNull();
        System.Text.Json.JsonSerializer.SerializeToElement(captured).GetProperty("arguments").GetProperty("value").GetString().Should().Be("permitted");
    }

    [Fact]
    public async Task ValidateToolExecution_UnsatisfiableInput_ShouldNotDispatchSyntheticCall()
    {
        SetupToolsList(_httpClient, """
            [{"name":"fixture","inputSchema":{"type":"object","properties":{"value":false},"required":["value"]}}]
            """, authenticated: true);

        var result = await _validator.ValidateToolExecutionAsync(new McpServerConfig { Endpoint = "https://example.invalid/mcp" }, new ToolTestingConfig());

        result.ToolResults.Should().ContainSingle();
        result.ToolResults[0].Status.Should().Be(TestStatus.Inconclusive);
        result.ToolResults[0].ExecutionSuccessful.Should().BeFalse();
        _httpClient.Verify(client => client.CallAsync(It.IsAny<string>(), "tools/call", It.IsAny<object?>(),
            It.IsAny<AuthenticationConfig?>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task ValidateToolExecution_OversizedConstant_ShouldNotCloneAndDispatchLargeCandidate()
    {
        var tool = new
        {
            name = "fixture",
            inputSchema = new
            {
                type = "object",
                properties = new { value = new Dictionary<string, object> { ["const"] = new string('x', 100000) } },
                required = new[] { "value" }
            }
        };
        SetupToolsList(_httpClient, System.Text.Json.JsonSerializer.Serialize(new[] { tool }), authenticated: true);

        var result = await _validator.ValidateToolExecutionAsync(new McpServerConfig { Endpoint = "https://example.invalid/mcp" }, new ToolTestingConfig());

        result.ToolResults.Should().ContainSingle();
        result.ToolResults[0].Status.Should().Be(TestStatus.Inconclusive);
        _httpClient.Verify(client => client.CallAsync(It.IsAny<string>(), "tools/call", It.IsAny<object?>(),
            It.IsAny<AuthenticationConfig?>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task ValidateToolExecution_MissingInputSchema_ShouldNotTreatCompatibilityDefaultAsEvidence()
    {
        SetupToolsList(_httpClient, "[{\"name\":\"fixture\"}]", authenticated: true);
        _httpClient.Setup(client => client.CallAsync(It.IsAny<string>(), "tools/call", It.IsAny<object?>(), It.IsAny<AuthenticationConfig?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new JsonRpcResponse { IsSuccess = true, StatusCode = 200 });

        var result = await _validator.ValidateToolExecutionAsync(new McpServerConfig { Endpoint = "https://example.invalid/mcp" }, new ToolTestingConfig());

        result.ToolResults.Should().ContainSingle();
        result.ToolResults[0].Status.Should().Be(TestStatus.Inconclusive);
        _httpClient.Verify(client => client.CallAsync(It.IsAny<string>(), "tools/call", It.IsAny<object?>(),
            It.IsAny<AuthenticationConfig?>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Theory]
    [InlineData("{\"type\":\"object\",\"$defs\":{\"unused\":{\"$ref\":\"#/$defs/unused\"}}}")]
    [InlineData("{\"type\":\"object\",\"$defs\":{\"unused\":{\"anyOf\":[true,true]}}}")]
    [InlineData("{\"type\":\"object\",\"$defs\":{\"unused\":{\"type\":\"string\",\"pattern\":\"(a+)+$\"}}}")]
    public async Task ValidateToolExecution_UnsupportedValidationDependency_ShouldNotDispatch(string schema)
    {
        SetupToolsList(_httpClient, "[{\"name\":\"fixture\",\"inputSchema\":" + schema + "}]", authenticated: true);

        var result = await _validator.ValidateToolExecutionAsync(new McpServerConfig { Endpoint = "https://example.invalid/mcp" }, new ToolTestingConfig());

        result.Status.Should().Be(TestStatus.Inconclusive);
        _httpClient.Verify(client => client.CallAsync(It.IsAny<string>(), "tools/call", It.IsAny<object?>(),
            It.IsAny<AuthenticationConfig?>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Theory]
    [InlineData(16, true)]
    [InlineData(17, false)]
    public async Task ValidateToolExecution_RepeatedConstants_ShouldRespectAggregateCandidateBudget(int count, bool expectedCall)
    {
        var properties = Enumerable.Range(0, count).ToDictionary(index => $"value{index}",
            _ => new Dictionary<string, string> { ["$ref"] = "#/$defs/value" });
        var schema = new Dictionary<string, object>
        {
            ["type"] = "object",
            ["$defs"] = new { value = new Dictionary<string, string> { ["const"] = new string('x', 4000) } },
            ["properties"] = properties,
            ["required"] = properties.Keys.ToArray()
        };
        SetupToolsList(_httpClient, System.Text.Json.JsonSerializer.Serialize(new[] { new { name = "fixture", inputSchema = schema } }), authenticated: true);
        object? captured = null;
        _httpClient.Setup(client => client.CallAsync(It.IsAny<string>(), "tools/call", It.IsAny<object?>(), It.IsAny<AuthenticationConfig?>(), It.IsAny<CancellationToken>()))
            .Callback<string, string, object?, AuthenticationConfig?, CancellationToken>((_, _, parameters, _, _) => captured = parameters)
            .ReturnsAsync(new JsonRpcResponse { IsSuccess = true, StatusCode = 200 });

        var result = await _validator.ValidateToolExecutionAsync(new McpServerConfig { Endpoint = "https://example.invalid/mcp" }, new ToolTestingConfig());

        result.Status.Should().Be(expectedCall ? TestStatus.Passed : TestStatus.Inconclusive);
        if (expectedCall)
        {
            captured.Should().NotBeNull();
            System.Text.Json.JsonSerializer.SerializeToElement(captured).GetProperty("arguments").GetRawText().Length.Should().BeLessThanOrEqualTo(64 * 1024);
        }
        else captured.Should().BeNull();
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ValidateToolDiscovery_WhenLaterPageIsCancelled_ShouldRetainEarlierInventory(bool malformedEntry)
    {
        using var cancellation = new CancellationTokenSource();
        _httpClient.Setup(client => client.CallAsync(It.IsAny<string>(), "tools/list", It.IsAny<object?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new JsonRpcResponse
            {
                IsSuccess = true,
                StatusCode = 200,
                RawJson = malformedEntry
                    ? """{"jsonrpc":"2.0","id":1,"result":{"tools":[{"name":"retained","inputSchema":{"type":"object"}},null],"nextCursor":"next"}}"""
                    : """{"jsonrpc":"2.0","id":1,"result":{"tools":[{"name":"retained","inputSchema":{"type":"object"}}],"nextCursor":"next"}}"""
            });
        _httpClient.Setup(client => client.CallAsync(It.IsAny<string>(), "tools/list", It.IsAny<object?>(),
                It.IsAny<AuthenticationConfig?>(), It.IsAny<CancellationToken>()))
            .Returns(() =>
            {
                cancellation.Cancel();
                return Task.FromCanceled<JsonRpcResponse>(cancellation.Token);
            });

        var result = await _validator.ValidateToolDiscoveryAsync(
            new McpServerConfig { Endpoint = "https://example.invalid/mcp", Transport = "http" },
            new ToolTestingConfig { TestToolExecution = false, TestParameterValidation = false }, cancellation.Token);

        result.Status.Should().Be(TestStatus.Cancelled);
        result.ToolsDiscovered.Should().Be(1);
        result.DiscoveredToolNames.Should().Equal("retained");
        result.ToolsTestPassed.Should().Be(0);
        result.ToolsTestFailed.Should().Be(malformedEntry ? 1 : 0);
        result.CatalogEnumeration.Should().NotBeNull();
        result.CatalogEnumeration!.RawItemsObserved.Should().Be(malformedEntry ? 2 : 1);
        result.CatalogEnumeration.RecoveredItems.Should().Be(1);
        result.CatalogEnumeration.RejectedItems.Should().Be(malformedEntry ? 1 : 0);
        result.CatalogEnumeration.PageAttempts.Should().Be(2);
        result.CatalogEnumeration.ParsedPages.Should().Be(1);
        result.CatalogEnumeration.IsComplete.Should().BeFalse();
        if (malformedEntry)
        {
            result.Issues.Should().Contain(issue => issue.Contains("item rejected", StringComparison.Ordinal));
            result.Findings.Should().Contain(finding => finding.RuleId == ValidationFindingRuleIds.ToolListItemInvalid);
            var canonical = new ValidationResult { ToolValidation = result, OverallStatus = ValidationStatus.Cancelled };
            canonical.Assurance = AssuranceAssessmentEngine.Evaluate(canonical, AssuranceProfileCatalog.Resolve("inventory"), new AssuranceEvaluationContext());
            canonical.Assurance.Claims.Should().Contain(claim => claim.Outcome == AssuranceClaimOutcome.Violated &&
                claim.Metadata.ContainsKey("ruleId") && claim.Metadata["ruleId"] == ValidationFindingRuleIds.ToolListItemInvalid);
            new ValidationReportRenderer().GenerateSarifReport(canonical).Should().Contain(ValidationFindingRuleIds.ToolListItemInvalid);
        }
    }

    [Fact]
    public async Task ValidateToolDiscovery_WithEmptyFirstPageAndFailedNextPage_ShouldNotPassCatalog()
    {
        _httpClient.Setup(client => client.CallAsync(It.IsAny<string>(), "tools/list", It.IsAny<object?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new JsonRpcResponse
            {
                IsSuccess = true, StatusCode = 200,
                RawJson = """{"jsonrpc":"2.0","id":1,"result":{"tools":[],"nextCursor":"next"}}"""
            });
        _httpClient.Setup(client => client.CallAsync(It.IsAny<string>(), "tools/list", It.IsAny<object?>(),
                It.IsAny<AuthenticationConfig?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new JsonRpcResponse { IsSuccess = false, StatusCode = 500 });

        var result = await _validator.ValidateToolDiscoveryAsync(
            new McpServerConfig { Endpoint = "https://example.invalid/mcp", Transport = "http" },
            new ToolTestingConfig { TestToolExecution = false, TestParameterValidation = false });

        result.Status.Should().Be(TestStatus.Inconclusive);
        result.ToolsDiscovered.Should().Be(0);
        result.ToolsTestPassed.Should().Be(0);
        result.Issues.Should().Contain(issue => issue.Contains("page 2 failed", StringComparison.Ordinal));
    }

    [Fact]
    public async Task ValidateToolDiscovery_ShouldRetainTypedIncompleteSchemaInspectionInCanonicalEvidence()
    {
        SetupToolsList(_httpClient, """
            [{"name":"referenced","inputSchema":{"type":"object","properties":{"value":{"$ref":"#/$defs/missing"}}}}]
            """);

        var result = await _validator.ValidateToolDiscoveryAsync(
            new McpServerConfig { Endpoint = "https://example.invalid/mcp", Transport = "http" },
            new ToolTestingConfig { TestToolExecution = false, TestParameterValidation = false });

        result.DiscoveredToolNames.Should().Equal("referenced");
        result.AiReadinessScore.Should().Be(-1);
        result.AiReadinessAnalyzedSchemaCount.Should().Be(0);
        result.AiReadinessInspectionIssues.Should().ContainSingle(issue => issue.Status == SchemaInspectionStatus.UnresolvedReference);
        var canonical = new ValidationResult { ToolValidation = result };
        var json = System.Text.Json.JsonSerializer.Serialize(canonical);
        var restored = System.Text.Json.JsonSerializer.Deserialize<ValidationResult>(json)!;
        restored.ToolValidation.Should().NotBeNull();
        restored.ToolValidation!.AiReadinessInspectionIssues.Should().ContainSingle(issue => issue.Status == SchemaInspectionStatus.UnresolvedReference);
    }

    [Fact]
    public async Task ValidateToolDiscovery_WithMalformedMiddleItem_ShouldRetainNeighborsAndFailCatalog()
    {
        SetupToolsList(_httpClient, """
            [{"name":"first","inputSchema":{"type":"object"}},
             {"name":"malformed","description":42,"inputSchema":{"type":"object"}},
             {"name":"last","inputSchema":{"type":"object"}}]
            """);

        var result = await _validator.ValidateToolDiscoveryAsync(
            new McpServerConfig { Endpoint = "https://example.invalid/mcp", Transport = "http" },
            new ToolTestingConfig { TestToolExecution = false, TestParameterValidation = false });

        result.DiscoveredToolNames.Should().Equal("first", "last");
        result.Status.Should().Be(TestStatus.Failed);
        result.ToolsTestFailed.Should().BeGreaterThan(0);
        result.Issues.Should().Contain(issue => issue.Contains("item rejected", StringComparison.Ordinal));
    }

    [Fact]
    public async Task ValidateToolDiscovery_ShouldSeparateMalformedPageFromRejectedItemCounts()
    {
        _httpClient.Setup(client => client.CallAsync(It.IsAny<string>(), "tools/list", It.IsAny<object?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new JsonRpcResponse
            {
                IsSuccess = true, StatusCode = 200,
                RawJson = """{"jsonrpc":"2.0","id":1,"result":{"tools":[{"name":"retained","inputSchema":{"type":"object"}}],"nextCursor":"next"}}"""
            });
        _httpClient.Setup(client => client.CallAsync(It.IsAny<string>(), "tools/list", It.IsAny<object?>(),
                It.IsAny<AuthenticationConfig?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new JsonRpcResponse { IsSuccess = true, StatusCode = 200, RawJson = "{invalid" });

        var result = await _validator.ValidateToolDiscoveryAsync(new McpServerConfig { Endpoint = "https://example.invalid/mcp" },
            new ToolTestingConfig { TestToolExecution = false, TestParameterValidation = false });

        result.CatalogEnumeration.Should().NotBeNull();
        result.CatalogEnumeration!.RawItemsObserved.Should().Be(1);
        result.CatalogEnumeration.RecoveredItems.Should().Be(1);
        result.CatalogEnumeration.RejectedItems.Should().Be(0);
        result.CatalogEnumeration.RejectedPages.Should().Be(1);
        result.CatalogEnumeration.ParsedPages.Should().Be(1);
        result.CatalogEnumeration.IsComplete.Should().BeFalse();
        result.ToolsTestFailed.Should().Be(0);
        result.Status.Should().Be(TestStatus.Inconclusive);
        result.AiReadinessScore.Should().Be(-1);
    }

    [Theory]
    [InlineData("[null]")]
    [InlineData("[{\"name\":\"invalid\",\"annotations\":null}]")]
    [InlineData("[{\"name\":\"invalid\",\"annotations\":{\"readOnlyHint\":\"true\"}}]")]
    [InlineData("[{\"description\":\"missing name\"}]")]
    public async Task ValidateToolDiscovery_WithOnlyRejectedItems_ShouldNotReportEmptyCatalogPass(string tools)
    {
        SetupToolsList(_httpClient, tools);

        var result = await _validator.ValidateToolDiscoveryAsync(
            new McpServerConfig { Endpoint = "https://example.invalid/mcp", Transport = "http" },
            new ToolTestingConfig { TestToolExecution = false, TestParameterValidation = false });

        result.Status.Should().Be(TestStatus.Failed);
        result.ToolsTestPassed.Should().Be(0);
        result.ToolsTestFailed.Should().BeGreaterThan(0);
    }

    [Fact]
    public async Task ValidateToolDiscovery_WhenAdvisoryAnalysisThrows_ShouldRetainCollectedTool()
    {
        SetupToolsList(_httpClient, "[{\"name\":\"retained\",\"inputSchema\":{\"type\":\"object\",\"properties\":{}}}]");
        var analyzer = new Mock<IToolAiReadinessAnalyzer>();
        analyzer.Setup(service => service.AnalyzeCatalog(It.IsAny<IReadOnlyCollection<ToolAiReadinessTarget>>(),
                It.IsAny<string?>(), It.IsAny<long?>()))
            .Throws(new InvalidOperationException("late advisory failure"));
        var safety = new Mock<IContentSafetyAnalyzer>();
        safety.Setup(service => service.AnalyzeTool(It.IsAny<string>())).Returns(new List<ContentSafetyFinding>());
        var validator = new ToolValidator(new Mock<ILogger<ToolValidator>>().Object, _httpClient.Object,
            new JsonSchemaValidator(), new Mock<ISchemaRegistry>().Object, _authService.Object, safety.Object, analyzer.Object);

        var result = await validator.ValidateToolDiscoveryAsync(
            new McpServerConfig { Endpoint = "https://example.invalid/mcp", Transport = "http" },
            new ToolTestingConfig { TestToolExecution = false, TestParameterValidation = false });

        result.Status.Should().Be(TestStatus.Error);
        result.ToolResults.Should().Contain(tool => tool.ToolName == "retained");
        analyzer.Verify(service => service.AnalyzeCatalog(It.IsAny<IReadOnlyCollection<ToolAiReadinessTarget>>(),
            It.IsAny<string?>(), It.IsAny<long?>()), Times.Once);
    }

    [Fact]
    public async Task ValidateToolDiscovery_WhenToolsCapabilityIsNotAdvertised_ShouldSkipToolProbes()
    {
        var config = new McpServerConfig { Endpoint = "https://test.com/mcp", Transport = "http" };
        var capabilitySnapshot = new TransportResult<CapabilitySummary>
        {
            IsSuccessful = true,
            Payload = new CapabilitySummary
            {
                CapabilityDeclarationsAvailable = true,
                AdvertisedCapabilities = Array.Empty<string>()
            }
        };

        var result = await _validator.ValidateToolDiscoveryAsync(
            config,
            new ToolTestingConfig { CapabilitySnapshot = capabilitySnapshot },
            CancellationToken.None);

        result.Status.Should().Be(TestStatus.Skipped);
        result.Score.Should().Be(100.0);
        result.Issues.Should().Contain("Tools capability was not advertised during initialize; tools/list and tools/call probes were skipped.");

        _httpClient.Verify(x => x.CallAsync(
            It.IsAny<string>(),
            "tools/list",
            It.IsAny<object?>(),
            It.IsAny<CancellationToken>()),
            Times.Never);
    }

    // ─── Auth Detection ──────────────────────────────────────────

    [Fact]
    public async Task ValidateToolDiscovery_With401AndWwwAuth_ShouldReturnAuthRequiredWithAuthDetails()
    {
        var config = new McpServerConfig { Endpoint = "https://test.com/mcp", Transport = "http" };
        _httpClient.Setup(x => x.CallAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<object>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new JsonRpcResponse
            {
                StatusCode = 401, IsSuccess = false,
                Headers = new Dictionary<string, string> { { "WWW-Authenticate", "Bearer realm=\"test\"" } }
            });

        var result = await _validator.ValidateToolDiscoveryAsync(config, new ToolTestingConfig(), CancellationToken.None);

        result.Status.Should().Be(TestStatus.AuthRequired);
        result.AuthenticationSecurity.Should().NotBeNull();
        result.AuthenticationSecurity!.AuthenticationRequired.Should().BeTrue();
        result.AuthenticationSecurity.HasProperAuthHeaders.Should().BeTrue();
    }

    [Fact]
    public async Task ValidateToolDiscovery_With401NoWwwAuth_ShouldReturnAuthRequiredAndFlagMissingHeader()
    {
        var config = new McpServerConfig { Endpoint = "https://test.com/mcp", Transport = "http" };
        _httpClient.Setup(x => x.CallAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<object>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new JsonRpcResponse { StatusCode = 401, IsSuccess = false, Headers = new Dictionary<string, string>() });

        var result = await _validator.ValidateToolDiscoveryAsync(config, new ToolTestingConfig(), CancellationToken.None);

        result.Status.Should().Be(TestStatus.AuthRequired);
        result.AuthenticationSecurity!.HasProperAuthHeaders.Should().BeFalse();
    }

    [Fact]
    public async Task ValidateToolDiscovery_With403_ShouldReturnAuthRequired()
    {
        var config = new McpServerConfig { Endpoint = "https://test.com/mcp", Transport = "http" };
        _httpClient.Setup(x => x.CallAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<object>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new JsonRpcResponse { StatusCode = 403, IsSuccess = false });

        var result = await _validator.ValidateToolDiscoveryAsync(config, new ToolTestingConfig(), CancellationToken.None);

        result.Status.Should().Be(TestStatus.AuthRequired);
    }

    [Fact]
    public async Task ValidateToolDiscovery_WhenCredentialAcquisitionReturnsNoToken_ShouldRemainAuthCoverage()
    {
        const string metadataUrl = "https://test.com/.well-known/oauth-protected-resource";
        var config = new McpServerConfig
        {
            Endpoint = "https://test.com/mcp",
            Transport = "http",
            Authentication = new AuthenticationConfig { AllowInteractive = true }
        };
        _httpClient.Setup(client => client.CallAsync(
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<object>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new JsonRpcResponse
            {
                StatusCode = 401,
                IsSuccess = false,
                Headers = new Dictionary<string, string>
                {
                    ["WWW-Authenticate"] = $"Bearer resource_metadata=\"{metadataUrl}\""
                }
            });
        _httpClient.Setup(client => client.GetStringAsync(metadataUrl, It.IsAny<CancellationToken>()))
            .ReturnsAsync("""
                {
                  "resource": "https://test.com/mcp",
                  "authorization_servers": ["https://login.example.com"],
                  "scopes_supported": ["tools:read"]
                }
                """);
        _authService.Setup(service => service.GetTokenAsync(
                It.IsAny<AuthMetadata>(),
                It.IsAny<CancellationToken>(),
                It.IsAny<bool>(),
                It.IsAny<string?>(),
                It.IsAny<string?>()))
            .ReturnsAsync((string?)null);

        var result = await _validator.ValidateToolDiscoveryAsync(config, new ToolTestingConfig(), CancellationToken.None);

        result.Status.Should().Be(TestStatus.AuthRequired);
        result.ToolResults.Should().OnlyContain(tool => tool.Status != TestStatus.Failed);
        result.Issues.Should().Contain(issue => issue.Contains("requires authentication", StringComparison.OrdinalIgnoreCase));
        _authService.Verify(service => service.GetTokenAsync(
            It.IsAny<AuthMetadata>(),
            It.IsAny<CancellationToken>(),
            It.IsAny<bool>(),
            It.IsAny<string?>(),
            It.IsAny<string?>()), Times.Once);
    }

    // ─── Tool Discovery (200 OK) ─────────────────────────────────

    [Fact]
    public async Task ValidateToolDiscovery_WithExecutionDisabled_ShouldNotCallAnyTool()
    {
        var config = new McpServerConfig { Endpoint = "https://test.com/mcp", Transport = "http" };
        _httpClient
            .Setup(x => x.CallAsync(config.Endpoint, ValidationConstants.Methods.ToolsList, null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new JsonRpcResponse
            {
                StatusCode = 200,
                IsSuccess = true,
                RawJson = "{\"jsonrpc\":\"2.0\",\"result\":{\"tools\":[{\"name\":\"mutating-tool\",\"inputSchema\":{\"type\":\"object\"}}]},\"id\":1}"
            });

        var result = await _validator.ValidateToolDiscoveryAsync(
            config,
            new ToolTestingConfig
            {
                TestToolExecution = false,
                TestParameterValidation = false
            },
            CancellationToken.None);

        result.ToolResults.Should().ContainSingle(tool => tool.ToolName == "mutating-tool");
        result.ToolResults.Single(tool => tool.ToolName == "mutating-tool").ExecutionSuccessful.Should().BeFalse();
        _httpClient.Verify(x => x.CallAsync(
            It.IsAny<string>(),
            ValidationConstants.Methods.ToolsCall,
            It.IsAny<object?>(),
            It.IsAny<CancellationToken>()),
            Times.Never);
    }

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
    public async Task ValidateToolDiscovery_WithPublicProfile_ShouldContextualizeContentSafetyFindings()
    {
        var config = new McpServerConfig
        {
            Endpoint = "https://test.com/mcp",
            Transport = "http",
            Profile = McpServerProfile.Public
        };
        SetupToolsList(_httpClient, "[{\"name\":\"update_customer_record\",\"description\":\"Updates a customer record\",\"inputSchema\":{\"type\":\"object\",\"properties\":{}}}]");
        SetupToolCall(_httpClient, 400, null);

        var validator = new ToolValidator(
            new Mock<ILogger<ToolValidator>>().Object,
            _httpClient.Object,
            new JsonSchemaValidator(),
            new Mock<ISchemaRegistry>().Object,
            new Mock<IAuthenticationService>().Object,
            new ContentSafetyAnalyzer(new Mock<ILogger<ContentSafetyAnalyzer>>().Object),
            new ToolAiReadinessAnalyzer());

        var result = await validator.ValidateToolDiscoveryAsync(config, new ToolTestingConfig(), CancellationToken.None);

        var finding = result.ContentSafetyFindings.Should().ContainSingle(f => f.ItemName == "update_customer_record").Subject;
        finding.RiskLevel.Should().Be(ContentRiskLevel.High);
        finding.Context.Should().ContainKey("contextProfile").WhoseValue.Should().Be("PublicUnauthenticated");
        finding.Context.Should().ContainKey("baseRiskLevel").WhoseValue.Should().Be("Medium");
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

    [Fact]
    public async Task ValidateToolDiscovery_WithRateLimitedToolsList_ShouldReturnInconclusiveAsTransientProbe()
    {
        var config = new McpServerConfig { Endpoint = "https://test.com/mcp", Transport = "http" };
        _httpClient.Setup(x => x.CallAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<object>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new JsonRpcResponse { StatusCode = 429, IsSuccess = false, Error = "HTTP 429 Too Many Requests", RawJson = "too many requests" });

        var result = await _validator.ValidateToolDiscoveryAsync(config, new ToolTestingConfig(), CancellationToken.None);

        result.Status.Should().Be(TestStatus.Inconclusive);
        result.ToolsTestFailed.Should().Be(0);
        result.Message.Should().Contain("tools/list probe inconclusive");
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
    public async Task ValidateToolDiscovery_WithoutIsErrorField_ShouldTreatResponseAsCompliant()
    {
        var config = new McpServerConfig { Endpoint = "https://test.com/mcp", Transport = "http" };
        SetupToolsList(_httpClient, "[{\"name\":\"echo\",\"inputSchema\":{\"type\":\"object\",\"properties\":{\"msg\":{\"type\":\"string\"}}}}]");
        SetupToolCall(_httpClient, 200, "{\"jsonrpc\":\"2.0\",\"result\":{\"content\":[{\"type\":\"text\",\"text\":\"hello\"}]},\"id\":1}");

        var result = await _validator.ValidateToolDiscoveryAsync(config, new ToolTestingConfig(), CancellationToken.None);

        var toolResult = result.ToolResults.Single(t => t.ToolName == "echo");
        toolResult.Status.Should().Be(TestStatus.Passed);
        toolResult.Findings.Should().NotContain(f => f.RuleId == ValidationFindingRuleIds.ToolCallMissingIsError);
    }

    [Fact]
    public async Task ValidateToolDiscovery_WithMissingContentArray_ShouldFlag()
    {
        var config = new McpServerConfig { Endpoint = "https://test.com/mcp", Transport = "http" };
        SetupToolsList(_httpClient, "[{\"name\":\"bad_tool\",\"inputSchema\":{\"type\":\"object\",\"properties\":{}}}]");
        SetupToolCall(_httpClient, 200, "{\"jsonrpc\":\"2.0\",\"result\":{\"data\":\"no content array\"},\"id\":1}");

        var result = await _validator.ValidateToolDiscoveryAsync(config, new ToolTestingConfig(), CancellationToken.None);

        result.ToolResults.Should().Contain(t => t.Issues.Any(i => i.Contains("content")));
        result.ToolResults.Should().Contain(t => t.Findings.Any(f => f.RuleId == ValidationFindingRuleIds.ToolCallMissingContentArray));
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
        toolResult.Findings.Should().Contain(f => f.RuleId == ValidationFindingRuleIds.ToolLlmFriendliness);
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
    public async Task ValidateToolDiscovery_WithTimeoutOnToolCall_ShouldRemainInconclusiveWithoutPassCredit()
    {
        var config = new McpServerConfig { Endpoint = "https://test.com/mcp", Transport = "http" };
        SetupToolsList(_httpClient, "[{\"name\":\"slow_tool\",\"inputSchema\":{\"type\":\"object\",\"properties\":{}}}]");
        _httpClient.Setup(x => x.CallAsync(It.IsAny<string>(), "tools/call", It.IsAny<object>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new JsonRpcResponse { StatusCode = -1, IsSuccess = false, Error = "Timeout" });

        var result = await _validator.ValidateToolDiscoveryAsync(config, new ToolTestingConfig(), CancellationToken.None);

        var toolResult = result.ToolResults.FirstOrDefault(t => t.ToolName == "slow_tool");
        toolResult!.Status.Should().Be(TestStatus.Inconclusive);
        toolResult.ExecutionSuccessful.Should().BeFalse();
        toolResult.DiscoveredCorrectly.Should().BeTrue();
        result.Status.Should().Be(TestStatus.Inconclusive);
        result.ToolsTestPassed.Should().Be(0);
        result.ToolsTestFailed.Should().Be(0);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task ValidateToolDiscovery_WithCancelledInvocation_ShouldRetainMetadataWithoutPassCredit(bool callerCancellation)
    {
        using var cancellation = new CancellationTokenSource();
        SetupToolsList(_httpClient, """[{"name":"cancelled_tool","inputSchema":{"type":"object","properties":{}}}]""");
        _httpClient.Setup(client => client.CallAsync(It.IsAny<string>(), "tools/call", It.IsAny<object>(), It.IsAny<CancellationToken>()))
            .Returns(() =>
            {
                if (callerCancellation) cancellation.Cancel();
                return Task.FromException<JsonRpcResponse>(new OperationCanceledException(cancellation.Token));
            });

        var result = await _validator.ValidateToolDiscoveryAsync(
            new McpServerConfig { Endpoint = "https://example.invalid/mcp", Transport = "http" },
            new ToolTestingConfig(), cancellation.Token);

        result.Status.Should().Be(callerCancellation ? TestStatus.Cancelled : TestStatus.Inconclusive);
        result.ToolsDiscovered.Should().Be(1);
        result.ToolResults.Should().ContainSingle();
        result.ToolResults[0].DiscoveredCorrectly.Should().BeTrue();
        result.ToolResults[0].ExecutionSuccessful.Should().BeFalse();
        result.ToolsTestPassed.Should().Be(0);
    }

    [Fact]
    public async Task ValidateToolDiscovery_WithRateLimitedToolCall_ShouldReturnInconclusiveToolWithoutFailingDiscovery()
    {
        var config = new McpServerConfig { Endpoint = "https://test.com/mcp", Transport = "http" };
        SetupToolsList(_httpClient, "[{\"name\":\"rate_limited_tool\",\"inputSchema\":{\"type\":\"object\",\"properties\":{}}}]");
        _httpClient.Setup(x => x.CallAsync(It.IsAny<string>(), "tools/call", It.IsAny<object>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new JsonRpcResponse { StatusCode = 429, IsSuccess = false, Error = "HTTP 429 Too Many Requests", RawJson = "too many requests" });

        var result = await _validator.ValidateToolDiscoveryAsync(config, new ToolTestingConfig(), CancellationToken.None);

        result.Status.Should().Be(TestStatus.Inconclusive);
        result.ToolsTestFailed.Should().Be(0);
        var toolResult = result.ToolResults.FirstOrDefault(t => t.ToolName == "rate_limited_tool");
        toolResult.Should().NotBeNull();
        toolResult!.Status.Should().Be(TestStatus.Inconclusive);
        toolResult.Issues.Should().Contain(i => i.Contains("transient transport pressure"));
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
        result.AiReadinessFindings.Should().Contain(f => f.RuleId == ValidationFindingRuleIds.AiReadinessMissingParameterDescriptions);
        result.AiReadinessFindings.Should().Contain(f => f.RuleId == ValidationFindingRuleIds.AiReadinessVagueStringSchema);
    }

    [Fact]
    public async Task ValidateToolDiscovery_WithRequiredArraysAndChoiceLikeStrings_ShouldEmitStructuredSchemaFindings()
    {
        var config = new McpServerConfig { Endpoint = "https://test.com/mcp", Transport = "http" };
        SetupToolsList(_httpClient, "[{\"name\":\"workflow_tool\",\"inputSchema\":{\"type\":\"object\",\"required\":[\"targets\",\"mode\",\"callbackUrl\"],\"properties\":{\"targets\":{\"type\":\"array\",\"description\":\"Targets to process\"},\"mode\":{\"type\":\"string\",\"description\":\"Execution mode\"},\"callbackUrl\":{\"type\":\"string\",\"description\":\"Webhook URL for completion notices\"}}}}]");
        SetupToolCall(_httpClient, 400, null);

        var result = await _validator.ValidateToolDiscoveryAsync(config, new ToolTestingConfig(), CancellationToken.None);

        result.AiReadinessFindings.Should().Contain(f => f.RuleId == ValidationFindingRuleIds.AiReadinessRequiredArraySchema);
        result.AiReadinessFindings.Should().Contain(f => f.RuleId == ValidationFindingRuleIds.AiReadinessEnumCoverageMissing);
        result.AiReadinessFindings.Should().Contain(f => f.RuleId == ValidationFindingRuleIds.AiReadinessFormatHintMissing);
        result.AiReadinessFindings.Should().NotContain(f => f.RuleId == ValidationFindingRuleIds.AiReadinessVagueStringSchema);
        result.AiReadinessIssues.Should().Contain(i => i.Contains("required array parameters", StringComparison.OrdinalIgnoreCase));
        result.AiReadinessIssues.Should().Contain(i => i.Contains("fixed-choice fields", StringComparison.OrdinalIgnoreCase));
        result.AiReadinessIssues.Should().Contain(i => i.Contains("structured values", StringComparison.OrdinalIgnoreCase));
        result.AiReadinessScore.Should().BeLessThan(100);
    }

    [Fact]
    public async Task ValidateToolDiscovery_WithDescribedFreeformQuery_ShouldNotEmitVagueStringFinding()
    {
        var config = new McpServerConfig { Endpoint = "https://test.com/mcp", Transport = "http" };
        SetupToolsList(_httpClient, "[{\"name\":\"search_tool\",\"inputSchema\":{\"type\":\"object\",\"properties\":{\"query\":{\"type\":\"string\",\"description\":\"Freeform search text used to look up matching issues\"}}}}]");
        SetupToolCall(_httpClient, 400, null);

        var result = await _validator.ValidateToolDiscoveryAsync(config, new ToolTestingConfig(), CancellationToken.None);

        result.AiReadinessFindings.Should().NotContain(f =>
            f.RuleId == ValidationFindingRuleIds.AiReadinessVagueStringSchema &&
            f.Component == "search_tool");
    }

    [Fact]
    public async Task ValidateToolDiscovery_WithWellDescribedParams_ShouldScoreHigh()
    {
        var config = new McpServerConfig { Endpoint = "https://test.com/mcp", Transport = "http" };
        SetupToolsList(_httpClient, "[{\"name\":\"good_tool\",\"inputSchema\":{\"type\":\"object\",\"properties\":{\"color\":{\"type\":\"string\",\"description\":\"The color\",\"enum\":[\"red\",\"blue\"]}}}}]");
        SetupToolCall(_httpClient, 200, "{\"jsonrpc\":\"2.0\",\"result\":{\"content\":[{\"type\":\"text\",\"text\":\"ok\"}]},\"id\":1}");

        var result = await _validator.ValidateToolDiscoveryAsync(config, new ToolTestingConfig(), CancellationToken.None);

        result.AiReadinessScore.Should().BeGreaterThanOrEqualTo(80);
        result.AiReadinessFindings.Should().NotContain(f => f.RuleId == ValidationFindingRuleIds.AiReadinessRequiredArraySchema);
        result.AiReadinessFindings.Should().NotContain(f => f.RuleId == ValidationFindingRuleIds.AiReadinessEnumCoverageMissing);
        result.AiReadinessFindings.Should().NotContain(f => f.RuleId == ValidationFindingRuleIds.AiReadinessFormatHintMissing);
    }

    [Fact]
    public async Task ValidateToolDiscovery_WithMissingGuidelineHints_ShouldEmitGuidelineFindings()
    {
        var config = new McpServerConfig { Endpoint = "https://test.com/mcp", Transport = "http" };
        SetupToolsList(_httpClient, "[{\"name\":\"plain_tool\",\"inputSchema\":{\"type\":\"object\",\"properties\":{}}}]");
        SetupToolCall(_httpClient, 200, "{\"jsonrpc\":\"2.0\",\"result\":{\"content\":[{\"type\":\"text\",\"text\":\"ok\"}]},\"id\":1}");

        var result = await _validator.ValidateToolDiscoveryAsync(config, new ToolTestingConfig(), CancellationToken.None);

        var toolResult = result.ToolResults.Single(t => t.ToolName == "plain_tool");
        toolResult.Findings.Should().Contain(f => f.RuleId == ValidationFindingRuleIds.ToolGuidelineDisplayTitleMissing);
        toolResult.Findings.Should().Contain(f => f.RuleId == ValidationFindingRuleIds.ToolGuidelineReadOnlyHintMissing);
        toolResult.Findings.Should().Contain(f => f.RuleId == ValidationFindingRuleIds.ToolGuidelineDestructiveHintMissing);
        toolResult.Findings.Should().Contain(f => f.RuleId == ValidationFindingRuleIds.ToolGuidelineOpenWorldHintMissing);
        toolResult.Findings.Should().Contain(f => f.RuleId == ValidationFindingRuleIds.ToolGuidelineIdempotentHintMissing);
    }

    [Fact]
    public async Task ValidateToolDiscovery_WithReadOnlyTool_ShouldNotRequireWriteOnlyHints()
    {
        var config = new McpServerConfig { Endpoint = "https://test.com/mcp", Transport = "http" };
        SetupToolsList(_httpClient, "[{\"name\":\"read_tool\",\"inputSchema\":{\"type\":\"object\",\"properties\":{}},\"annotations\":{\"readOnlyHint\":true}}]");
        SetupToolCall(_httpClient, 200, "{\"jsonrpc\":\"2.0\",\"result\":{\"content\":[{\"type\":\"text\",\"text\":\"ok\"}]},\"id\":1}");

        var result = await _validator.ValidateToolDiscoveryAsync(config, new ToolTestingConfig(), CancellationToken.None);

        var toolResult = result.ToolResults.Single(t => t.ToolName == "read_tool");
        toolResult.Findings.Should().NotContain(f => f.RuleId == ValidationFindingRuleIds.ToolGuidelineDestructiveHintMissing);
        toolResult.Findings.Should().NotContain(f => f.RuleId == ValidationFindingRuleIds.ToolGuidelineIdempotentHintMissing);
        toolResult.Findings.Should().Contain(f =>
            f.RuleId == ValidationFindingRuleIds.ToolGuidelineOpenWorldHintMissing &&
            f.Summary.Contains("MCP defaults it to true", StringComparison.Ordinal));
    }

    [Fact]
    public async Task ValidateToolDiscovery_WithReadOnlyDestructiveConflict_ShouldEmitHighSeverityFinding()
    {
        var config = new McpServerConfig { Endpoint = "https://test.com/mcp", Transport = "http" };
        SetupToolsList(_httpClient, "[{\"name\":\"conflicting_tool\",\"inputSchema\":{\"type\":\"object\",\"properties\":{}},\"annotations\":{\"readOnlyHint\":true,\"destructiveHint\":true}}]");
        SetupToolCall(_httpClient, 200, "{\"jsonrpc\":\"2.0\",\"result\":{\"content\":[{\"type\":\"text\",\"text\":\"ok\"}]},\"id\":1}");

        var result = await _validator.ValidateToolDiscoveryAsync(config, new ToolTestingConfig(), CancellationToken.None);

        result.ToolResults.Single(t => t.ToolName == "conflicting_tool").Findings.Should().ContainSingle(finding =>
            finding.RuleId == ValidationFindingRuleIds.ToolGuidelineHintConflict &&
            finding.Severity == ValidationFindingSeverity.High &&
            finding.Summary.Contains("conflicting annotations", StringComparison.Ordinal));
    }

    [Fact]
    public async Task ValidateToolDiscovery_WithMissingInputSchema_ShouldEmitSpecFindingAndFailTool()
    {
        var config = new McpServerConfig { Endpoint = "https://test.com/mcp", Transport = "http" };
        SetupToolsList(_httpClient, "[{\"name\":\"missing_schema\"}]");
        SetupToolCall(_httpClient, 200, "{\"jsonrpc\":\"2.0\",\"result\":{\"content\":[{\"type\":\"text\",\"text\":\"ok\"}]},\"id\":1}");

        var result = await _validator.ValidateToolDiscoveryAsync(config, new ToolTestingConfig(), CancellationToken.None);

        var toolResult = result.ToolResults.Single(t => t.ToolName == "missing_schema");
        toolResult.Status.Should().Be(TestStatus.Failed);
        toolResult.MetadataValid.Should().BeFalse();
        toolResult.Findings.Should().Contain(f => f.RuleId == ValidationFindingRuleIds.ToolInputSchemaMissing);
        result.Status.Should().Be(TestStatus.Failed);
    }

    [Fact]
    public async Task ValidateToolDiscovery_WithNonObjectInputSchemaRoot_ShouldEmitSpecFinding()
    {
        var config = new McpServerConfig { Endpoint = "https://test.com/mcp", Transport = "http" };
        SetupToolsList(_httpClient, "[{\"name\":\"bad_schema\",\"inputSchema\":{\"type\":\"string\"}}]");
        SetupToolCall(_httpClient, 200, "{\"jsonrpc\":\"2.0\",\"result\":{\"content\":[{\"type\":\"text\",\"text\":\"ok\"}]},\"id\":1}");

        var result = await _validator.ValidateToolDiscoveryAsync(config, new ToolTestingConfig(), CancellationToken.None);

        var toolResult = result.ToolResults.Single(t => t.ToolName == "bad_schema");
        toolResult.Findings.Should().Contain(f => f.RuleId == ValidationFindingRuleIds.ToolInputSchemaRootTypeInvalid);
        toolResult.Status.Should().Be(TestStatus.Failed);
    }

    [Fact]
    public async Task ValidateToolDiscovery_WithOutputSchemaAndMatchingStructuredContent_ShouldPassSchemaCheck()
    {
        var config = new McpServerConfig { Endpoint = "https://test.com/mcp", Transport = "http" };
        SetupToolsList(_httpClient, "[{\"name\":\"structured_tool\",\"inputSchema\":{\"type\":\"object\",\"properties\":{}},\"outputSchema\":{\"type\":\"object\",\"required\":[\"answer\"],\"properties\":{\"answer\":{\"type\":\"string\"}}}}]");
        SetupToolCall(_httpClient, 200, "{\"jsonrpc\":\"2.0\",\"result\":{\"content\":[{\"type\":\"text\",\"text\":\"ok\"}],\"structuredContent\":{\"answer\":\"42\"}},\"id\":1}");

        var result = await _validator.ValidateToolDiscoveryAsync(config, new ToolTestingConfig(), CancellationToken.None);

        var toolResult = result.ToolResults.Single(t => t.ToolName == "structured_tool");
        toolResult.Findings.Should().NotContain(f => f.RuleId == ValidationFindingRuleIds.ToolCallStructuredContentSchemaMismatch);
        toolResult.Findings.Should().NotContain(f => f.RuleId == ValidationFindingRuleIds.ToolCallStructuredContentMissing);
        toolResult.Status.Should().Be(TestStatus.Passed);
    }

    [Fact]
    public async Task ValidateToolDiscovery_WithOutputSchemaMismatch_ShouldEmitStructuredContentFinding()
    {
        var config = new McpServerConfig { Endpoint = "https://test.com/mcp", Transport = "http" };
        SetupToolsList(_httpClient, "[{\"name\":\"structured_tool\",\"inputSchema\":{\"type\":\"object\",\"properties\":{}},\"outputSchema\":{\"type\":\"object\",\"required\":[\"answer\"],\"properties\":{\"answer\":{\"type\":\"string\"}}}}]");
        SetupToolCall(_httpClient, 200, "{\"jsonrpc\":\"2.0\",\"result\":{\"content\":[{\"type\":\"text\",\"text\":\"ok\"}],\"structuredContent\":{\"answer\":7}},\"id\":1}");

        var result = await _validator.ValidateToolDiscoveryAsync(config, new ToolTestingConfig(), CancellationToken.None);

        var toolResult = result.ToolResults.Single(t => t.ToolName == "structured_tool");
        toolResult.Findings.Should().Contain(f => f.RuleId == ValidationFindingRuleIds.ToolCallStructuredContentSchemaMismatch);
        toolResult.Status.Should().Be(TestStatus.Failed);
    }

    [Fact]
    public async Task ValidateToolDiscovery_WithInvalidIconsAndTaskSupport_ShouldEmitMetadataFindings()
    {
        var config = new McpServerConfig { Endpoint = "https://test.com/mcp", Transport = "http" };
        SetupToolsList(_httpClient, "[{\"name\":\"metadata_tool\",\"inputSchema\":{\"type\":\"object\",\"properties\":{}},\"icons\":[{\"theme\":\"blue\",\"sizes\":[\"large\"]}],\"execution\":{\"taskSupport\":\"sometimes\"}}]");
        SetupToolCall(_httpClient, 200, "{\"jsonrpc\":\"2.0\",\"result\":{\"content\":[{\"type\":\"text\",\"text\":\"ok\"}]},\"id\":1}");

        var result = await _validator.ValidateToolDiscoveryAsync(config, new ToolTestingConfig(), CancellationToken.None);

        var toolResult = result.ToolResults.Single(t => t.ToolName == "metadata_tool");
        toolResult.Findings.Should().Contain(f => f.RuleId == ValidationFindingRuleIds.ToolIconInvalid);
        toolResult.Findings.Should().Contain(f => f.RuleId == ValidationFindingRuleIds.ToolExecutionTaskSupportInvalid);
        toolResult.Status.Should().Be(TestStatus.Failed);
    }

    [Fact]
    public async Task ValidateToolDiscovery_WithTaskSupportButNoToolsCallTaskCapability_ShouldEmitAiSafetyFinding()
    {
        var config = new McpServerConfig { Endpoint = "https://test.com/mcp", Transport = "http" };
        var capabilitySnapshot = new TransportResult<CapabilitySummary>
        {
            IsSuccessful = true,
            Payload = new CapabilitySummary
            {
                CapabilityDeclarationsAvailable = true,
                AdvertisedCapabilities = new[]
                {
                    McpSpecConstants.Capabilities.Tools,
                    McpSpecConstants.Capabilities.Tasks
                }
            }
        };

        SetupToolsList(_httpClient, "[{\"name\":\"long_running_tool\",\"title\":\"Long Running Tool\",\"inputSchema\":{\"type\":\"object\",\"properties\":{}},\"execution\":{\"taskSupport\":\"required\"},\"annotations\":{\"readOnlyHint\":false,\"destructiveHint\":false,\"openWorldHint\":false,\"idempotentHint\":true}}]");
        SetupToolCall(_httpClient, 200, "{\"jsonrpc\":\"2.0\",\"result\":{\"content\":[{\"type\":\"text\",\"text\":\"ok\"}]},\"id\":1}");

        var result = await _validator.ValidateToolDiscoveryAsync(
            config,
            new ToolTestingConfig { CapabilitySnapshot = capabilitySnapshot },
            CancellationToken.None);

        var finding = result.Findings.Should().ContainSingle(f => f.RuleId == ValidationFindingRuleIds.AiTaskSupportWithoutCapability).Subject;
        finding.Severity.Should().Be(ValidationFindingSeverity.High);
        finding.Metadata["capability"].Should().Be(McpSpecConstants.Capabilities.TasksRequestsToolsCall);

        var toolResult = result.ToolResults.Single(t => t.ToolName == "long_running_tool");
        toolResult.Findings.Should().Contain(f => f.RuleId == ValidationFindingRuleIds.AiTaskSupportWithoutCapability);
    }

    [Fact]
    public async Task ValidateToolDiscovery_WithAnnotationMetadata_ShouldExposeParsedHints()
    {
        var config = new McpServerConfig { Endpoint = "https://test.com/mcp", Transport = "http" };
        SetupToolsList(_httpClient, "[{\"name\":\"annotated_tool\",\"title\":\"Primary Title\",\"inputSchema\":{\"type\":\"object\",\"properties\":{}},\"annotations\":{\"title\":\"Fallback Title\",\"readOnlyHint\":true,\"destructiveHint\":false,\"openWorldHint\":true,\"idempotentHint\":true}}]");
        SetupToolCall(_httpClient, 200, "{\"jsonrpc\":\"2.0\",\"result\":{\"content\":[{\"type\":\"text\",\"text\":\"ok\"}]},\"id\":1}");

        var result = await _validator.ValidateToolDiscoveryAsync(config, new ToolTestingConfig(), CancellationToken.None);

        var toolResult = result.ToolResults.Single(t => t.ToolName == "annotated_tool");
        toolResult.DisplayTitle.Should().Be("Primary Title");
        toolResult.ReadOnlyHint.Should().BeTrue();
        toolResult.DestructiveHint.Should().BeFalse();
        toolResult.OpenWorldHint.Should().BeTrue();
        toolResult.IdempotentHint.Should().BeTrue();
        toolResult.Findings.Should().NotContain(f => f.Category == "McpGuideline");
    }

    [Fact]
    public async Task ValidateToolDiscovery_WithCapabilitySnapshot_ShouldPreserveAnthropicMaxResultSizeAnnotation()
    {
        var config = new McpServerConfig { Endpoint = "https://test.com/mcp", Transport = "http" };
        var snapshot = new TransportResult<CapabilitySummary>
        {
            IsSuccessful = true,
            Payload = new CapabilitySummary
            {
                ToolListingSucceeded = true,
                DiscoveredToolsCount = 1,
                ToolListResponse = new JsonRpcResponse
                {
                    StatusCode = 200,
                    IsSuccess = true,
                    RawJson = "{\"jsonrpc\":\"2.0\",\"result\":{\"tools\":[{\"name\":\"get_schema\",\"description\":\"Returns the full database schema\",\"inputSchema\":{\"type\":\"object\",\"properties\":{}},\"_meta\":{\"anthropic/maxResultSizeChars\":200000}}]},\"id\":1}"
                }
            }
        };
        SetupToolCall(_httpClient, 200, "{\"jsonrpc\":\"2.0\",\"result\":{\"content\":[{\"type\":\"text\",\"text\":\"ok\"}]},\"id\":1}");

        var result = await _validator.ValidateToolDiscoveryAsync(
            config,
            new ToolTestingConfig { CapabilitySnapshot = snapshot },
            CancellationToken.None);

        var toolResult = result.ToolResults.Single(t => t.ToolName == "get_schema");
        toolResult.AnthropicMaxResultSizeChars.Should().Be(200000);
        _httpClient.Verify(x => x.CallAsync(It.IsAny<string>(), "tools/list", It.IsAny<object>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task ValidateToolDiscovery_WithDestructiveHintAndNoConfirmationGuidance_ShouldEmitHeuristicFinding()
    {
        var config = new McpServerConfig { Endpoint = "https://test.com/mcp", Transport = "http" };
        SetupToolsList(_httpClient, "[{\"name\":\"delete_repository\",\"description\":\"Deletes the selected repository.\",\"inputSchema\":{\"type\":\"object\",\"properties\":{}},\"annotations\":{\"title\":\"Delete Repository\",\"readOnlyHint\":false,\"destructiveHint\":true,\"openWorldHint\":true,\"idempotentHint\":false}}]");
        SetupToolCall(_httpClient, 200, "{\"jsonrpc\":\"2.0\",\"result\":{\"content\":[{\"type\":\"text\",\"text\":\"ok\"}]},\"id\":1}");

        var result = await _validator.ValidateToolDiscoveryAsync(config, new ToolTestingConfig(), CancellationToken.None);

        var toolResult = result.ToolResults.Single(t => t.ToolName == "delete_repository");
        toolResult.Findings.Should().Contain(f => f.RuleId == ValidationFindingRuleIds.ToolDestructiveConfirmationGuidanceMissing);
    }

    [Fact]
    public async Task ValidateToolDiscovery_WithDestructiveHintAndExplicitConfirmationGuidance_ShouldNotEmitHeuristicFinding()
    {
        var config = new McpServerConfig { Endpoint = "https://test.com/mcp", Transport = "http" };
        SetupToolsList(_httpClient, "[{\"name\":\"delete_repository\",\"description\":\"Deletes the selected repository after explicit user confirmation and warning review.\",\"inputSchema\":{\"type\":\"object\",\"properties\":{}},\"annotations\":{\"title\":\"Delete Repository\",\"readOnlyHint\":false,\"destructiveHint\":true,\"openWorldHint\":true,\"idempotentHint\":false}}]");
        SetupToolCall(_httpClient, 200, "{\"jsonrpc\":\"2.0\",\"result\":{\"content\":[{\"type\":\"text\",\"text\":\"ok\"}]},\"id\":1}");

        var result = await _validator.ValidateToolDiscoveryAsync(config, new ToolTestingConfig(), CancellationToken.None);

        var toolResult = result.ToolResults.Single(t => t.ToolName == "delete_repository");
        toolResult.Findings.Should().NotContain(f => f.RuleId == ValidationFindingRuleIds.ToolDestructiveConfirmationGuidanceMissing);
    }

    [Fact]
    public async Task ValidateToolDiscovery_WithPaginatedToolsList_ShouldFetchAllPages()
    {
        var config = new McpServerConfig { Endpoint = "https://test.com/mcp", Transport = "http" };

        _httpClient.Setup(x => x.CallAsync(It.IsAny<string>(), "tools/list", It.IsAny<object>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new JsonRpcResponse
            {
                StatusCode = 200,
                IsSuccess = true,
                RawJson = "{\"jsonrpc\":\"2.0\",\"result\":{\"tools\":[{\"name\":\"page_one_tool\",\"inputSchema\":{\"type\":\"object\",\"properties\":{}}}],\"nextCursor\":\"cursor-2\"},\"id\":1}"
            });
        _httpClient.Setup(x => x.CallAsync(It.IsAny<string>(), "tools/list", It.IsAny<object>(), It.IsAny<AuthenticationConfig>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new JsonRpcResponse
            {
                StatusCode = 200,
                IsSuccess = true,
                RawJson = "{\"jsonrpc\":\"2.0\",\"result\":{\"tools\":[{\"name\":\"page_two_tool\",\"inputSchema\":{\"type\":\"object\",\"properties\":{}}}]},\"id\":2}"
            });
        SetupToolCall(_httpClient, 200, "{\"jsonrpc\":\"2.0\",\"result\":{\"content\":[{\"type\":\"text\",\"text\":\"ok\"}]},\"id\":3}");

        var result = await _validator.ValidateToolDiscoveryAsync(config, new ToolTestingConfig(), CancellationToken.None);

        result.ToolsDiscovered.Should().Be(2);
        result.DiscoveredToolNames.Should().Contain(new[] { "page_one_tool", "page_two_tool" });
        result.Issues.Should().Contain(issue => issue.Contains("Pagination", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task ValidateToolDiscovery_WithRepeatedCursor_ShouldEmitPaginationStabilityFinding()
    {
        var config = new McpServerConfig { Endpoint = "https://test.com/mcp", Transport = "http" };

        _httpClient.Setup(x => x.CallAsync(It.IsAny<string>(), "tools/list", It.IsAny<object>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new JsonRpcResponse
            {
                StatusCode = 200,
                IsSuccess = true,
                RawJson = "{\"jsonrpc\":\"2.0\",\"result\":{\"tools\":[{\"name\":\"page_one_tool\",\"inputSchema\":{\"type\":\"object\",\"properties\":{}}}],\"nextCursor\":\"repeat\"},\"id\":1}"
            });
        _httpClient.Setup(x => x.CallAsync(It.IsAny<string>(), "tools/list", It.IsAny<object>(), It.IsAny<AuthenticationConfig>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new JsonRpcResponse
            {
                StatusCode = 200,
                IsSuccess = true,
                RawJson = "{\"jsonrpc\":\"2.0\",\"result\":{\"tools\":[{\"name\":\"page_two_tool\",\"inputSchema\":{\"type\":\"object\",\"properties\":{}}}],\"nextCursor\":\"repeat\"},\"id\":2}"
            });
        SetupToolCall(_httpClient, 200, "{\"jsonrpc\":\"2.0\",\"result\":{\"content\":[{\"type\":\"text\",\"text\":\"ok\"}]},\"id\":3}");

        var result = await _validator.ValidateToolDiscoveryAsync(config, new ToolTestingConfig(), CancellationToken.None);

        result.Findings.Should().Contain(f => f.RuleId == ValidationFindingRuleIds.ToolListCursorLoopDetected);
        result.Issues.Should().Contain(issue => issue.Contains("repeated cursor", StringComparison.OrdinalIgnoreCase));
    }

    // ─── Tool Execution Standalone ───────────────────────────────

    [Theory]
    [InlineData(true, 400)]
    [InlineData(true, 404)]
    [InlineData(false, 400)]
    [InlineData(false, 404)]
    [InlineData(false, -1)]
    public async Task ToolInvocation_WithoutExpectedResponse_ShouldNotEarnExecutionCredit(bool discovery, int statusCode)
    {
        const string catalog = """{"jsonrpc":"2.0","id":1,"result":{"tools":[{"name":"fixture","inputSchema":{"type":"object"}}]}}""";
        SetupToolsList(_httpClient, """[{"name":"fixture","inputSchema":{"type":"object"}}]""");
        SetupToolCall(_httpClient, statusCode, null);
        _httpClient.Setup(client => client.CallAsync(It.IsAny<string>(), "tools/list", It.IsAny<object>(), It.IsAny<AuthenticationConfig>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new JsonRpcResponse { IsSuccess = true, StatusCode = 200, RawJson = catalog });
        _httpClient.Setup(client => client.CallAsync(It.IsAny<string>(), "tools/call", It.IsAny<object>(), It.IsAny<AuthenticationConfig>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new JsonRpcResponse { IsSuccess = false, StatusCode = statusCode });
        var server = new McpServerConfig { Endpoint = "https://example.invalid/mcp", Transport = "http" };

        var result = discovery
            ? await _validator.ValidateToolDiscoveryAsync(server, new ToolTestingConfig())
            : await _validator.ValidateToolExecutionAsync(server, new ToolTestingConfig());

        result.Status.Should().Be(TestStatus.Inconclusive);
        result.ToolsTestPassed.Should().Be(0);
        result.ToolResults.Should().OnlyContain(tool => !tool.ExecutionSuccessful);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ValidateToolExecution_Cancellation_ShouldRetainFirstToolAndNotInvokeSecond(bool schemaObserved)
    {
        using var cancellation = new CancellationTokenSource();
        _httpClient.Setup(client => client.CallAsync(It.IsAny<string>(), "tools/list", It.IsAny<object>(), It.IsAny<AuthenticationConfig>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new JsonRpcResponse
            {
                IsSuccess = true, StatusCode = 200,
                RawJson = schemaObserved
                    ? """{"jsonrpc":"2.0","id":1,"result":{"tools":[{"name":"first","inputSchema":{"type":"object"}},{"name":"second","inputSchema":{"type":"object"}}]}}"""
                    : """{"jsonrpc":"2.0","id":1,"result":{"tools":[{"name":"first"},{"name":"second"}]}}"""
            });
        var invocations = 0;
        _httpClient.Setup(client => client.CallAsync(It.IsAny<string>(), "tools/call", It.IsAny<object>(), It.IsAny<AuthenticationConfig>(), It.IsAny<CancellationToken>()))
            .Returns(() =>
            {
                invocations++;
                cancellation.Cancel();
                return Task.FromCanceled<JsonRpcResponse>(cancellation.Token);
            });

        var result = await _validator.ValidateToolExecutionAsync(
            new McpServerConfig { Endpoint = "https://example.invalid/mcp", Transport = "http" }, new ToolTestingConfig(), cancellation.Token);

        invocations.Should().Be(schemaObserved ? 1 : 0);
        result.Status.Should().Be(schemaObserved ? TestStatus.Cancelled : TestStatus.Inconclusive);
        result.ToolsDiscovered.Should().Be(2);
        if (schemaObserved)
            result.ToolResults.Should().ContainSingle().Which.ToolName.Should().Be("first");
        else
            result.ToolResults.Should().HaveCount(2).And.OnlyContain(tool => !tool.ExecutionSuccessful);
        result.ToolsTestPassed.Should().Be(0);
    }

    [Fact]
    public async Task ValidateToolExecution_WithAuth_ShouldReturnAuthRequired()
    {
        var config = new McpServerConfig { Endpoint = "https://test.com/mcp", Transport = "http" };
        _httpClient.Setup(x => x.CallAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<object>(), It.IsAny<AuthenticationConfig>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new JsonRpcResponse { StatusCode = 401, IsSuccess = false });

        var result = await _validator.ValidateToolExecutionAsync(config, new ToolTestingConfig(), CancellationToken.None);

        result.Status.Should().Be(TestStatus.AuthRequired);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ValidateToolExecution_WithTools_ShouldCallEach(bool schemaObserved)
    {
        var config = new McpServerConfig { Endpoint = "https://test.com/mcp", Transport = "http" };
        _httpClient.Setup(x => x.CallAsync(It.IsAny<string>(), "tools/list", It.IsAny<object>(), It.IsAny<AuthenticationConfig>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new JsonRpcResponse
            {
                StatusCode = 200, IsSuccess = true,
                RawJson = schemaObserved
                    ? "{\"jsonrpc\":\"2.0\",\"result\":{\"tools\":[{\"name\":\"t1\",\"inputSchema\":{\"type\":\"object\"}},{\"name\":\"t2\",\"inputSchema\":{\"type\":\"object\"}}]},\"id\":1}"
                    : "{\"jsonrpc\":\"2.0\",\"result\":{\"tools\":[{\"name\":\"t1\"},{\"name\":\"t2\"}]},\"id\":1}"
            });
        _httpClient.Setup(x => x.CallAsync(It.IsAny<string>(), "tools/call", It.IsAny<object>(), It.IsAny<AuthenticationConfig>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new JsonRpcResponse { StatusCode = 200, IsSuccess = true, RawJson = "{\"jsonrpc\":\"2.0\",\"result\":{\"content\":[{\"type\":\"text\",\"text\":\"ok\"}]},\"id\":1}" });

        var result = await _validator.ValidateToolExecutionAsync(config, new ToolTestingConfig(), CancellationToken.None);

        result.ToolsDiscovered.Should().Be(2);
        result.ToolsTestPassed.Should().Be(schemaObserved ? 2 : 0);
        result.Status.Should().Be(schemaObserved ? TestStatus.Passed : TestStatus.Inconclusive);
        _httpClient.Verify(client => client.CallAsync(It.IsAny<string>(), "tools/call", It.IsAny<object>(),
            It.IsAny<AuthenticationConfig>(), It.IsAny<CancellationToken>()), Times.Exactly(schemaObserved ? 2 : 0));
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

    private static void SetupToolsList(Mock<IMcpHttpClient> mock, string toolsJson, bool authenticated = false)
    {
        var response = new JsonRpcResponse
        {
            StatusCode = 200, IsSuccess = true,
            RawJson = $"{{\"jsonrpc\":\"2.0\",\"result\":{{\"tools\":{toolsJson}}},\"id\":1}}"
        };
        if (authenticated)
            mock.Setup(client => client.CallAsync(It.IsAny<string>(), "tools/list", It.IsAny<object>(), It.IsAny<AuthenticationConfig?>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(response);
        else
            mock.Setup(client => client.CallAsync(It.IsAny<string>(), "tools/list", It.IsAny<object>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(response);
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
