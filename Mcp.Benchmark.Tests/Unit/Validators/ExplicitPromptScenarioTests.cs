using System.Text.Json;
using Mcp.Benchmark.Core.Abstractions;
using Mcp.Benchmark.Core.Models;
using Mcp.Benchmark.Core.Services;
using Mcp.Benchmark.Infrastructure.Services;
using Mcp.Benchmark.Infrastructure.Validators;
using Mcp.Compliance.Spec;

namespace Mcp.Benchmark.Tests.Unit.Validators;

public sealed class ExplicitPromptScenarioTests
{
    public static IEnumerable<object[]> CompletedContentCases()
    {
        foreach (var version in new[] { "2025-11-25", "2026-07-28" })
        foreach (var mode in version == "2026-07-28" ? new[] { "generated", "explicit", "continuation" } : new[] { "generated", "explicit" })
        foreach (var content in new[] { "text", "missing-text", "numeric-text", "unknown-type", "empty-content-array", "content-array", "empty-messages" })
            yield return [version, mode, content];
    }

    [Theory]
    [MemberData(nameof(CompletedContentCases))]
    public async Task CompletedPrompt_ShouldValidateConcreteDatedPayload(string version, string mode, string content)
    {
        var client = new Mock<IMcpHttpClient>();
        client.Setup(call => call.CallAsync(It.IsAny<string>(), "prompts/list", It.IsAny<object?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new JsonRpcResponse { IsSuccess = true, StatusCode = 200,
                RawJson = """{"jsonrpc":"2.0","id":"list","result":{"resultType":"complete","cacheScope":"public","ttlMs":0,"prompts":[{"name":"owned","description":"An owned prompt"}]}}""" });
        var block = content switch
        {
            "missing-text" => """{"type":"text"}""",
            "numeric-text" => """{"type":"text","text":42}""",
            "unknown-type" => """{"type":"unsupported","text":"fixture"}""",
            "empty-content-array" => "[]",
            "content-array" => """[{"type":"text","text":"fixture"}]""",
            _ => """{"type":"text","text":"fixture"}"""
        };
        var messages = content == "empty-messages" ? "[]" : $$"""[{"role":"user","content":{{block}}}]""";
        var calls = 0;
        client.Setup(call => call.CallAsync(It.IsAny<string>(), "prompts/get", It.IsAny<object?>(), It.IsAny<AuthenticationConfig?>(), It.IsAny<CancellationToken>()))
            .Returns(() => Task.FromResult(new JsonRpcResponse { IsSuccess = true, StatusCode = 200,
                RawJson = mode == "continuation" && ++calls == 1
                    ? """{"jsonrpc":"2.0","id":"get","result":{"resultType":"input_required","inputRequests":{"roots":{"method":"roots/list"}}}}"""
                    : $$$"""{"jsonrpc":"2.0","id":"get","result":{"resultType":"complete","messages":{{{messages}}}}}""" }));
        using var requests = JsonDocument.Parse("""{"roots":{"method":"roots/list"}}""");
        using var replies = JsonDocument.Parse("""{"roots":{"roots":[]}}""");
        var configuration = new PromptTestingConfig { TestPromptExecution = true };
        if (mode != "generated") configuration.CustomScenarios.Add(new()
        {
            PromptName = "owned", Arguments = new(),
            ClientInputRounds = mode == "continuation" ? [new() { ExpectedRequests = requests.RootElement, Responses = replies.RootElement }] : null
        });
        var safety = new Mock<IContentSafetyAnalyzer>();
        safety.Setup(analyzer => analyzer.AnalyzePrompt(It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<int>())).Returns([]);
        var validator = new PromptValidator(Mock.Of<ILogger<PromptValidator>>(), client.Object, new JsonSchemaValidator(),
            new EmbeddedSchemaRegistry(), safety.Object);

        var result = await validator.ValidatePromptDiscoveryAsync(new() { Endpoint = "https://example.invalid/mcp", ProtocolVersion = version }, configuration);

        var valid = content is "text" or "empty-messages";
        result.Status.Should().Be(valid ? TestStatus.Passed : TestStatus.Failed);
        result.PromptResults.Should().ContainSingle().Which.ExecutionSuccessful.Should().Be(valid);
    }

    [Theory]
    [InlineData("complete", 2, TestStatus.Passed)]
    [InlineData("mismatch", 1, TestStatus.Inconclusive)]
    [InlineData("missing", 0, TestStatus.Inconclusive)]
    [InlineData("duplicate", 0, TestStatus.Inconclusive)]
    [InlineData("disabled", 0, TestStatus.Inconclusive)]
    [InlineData("required-argument", 0, TestStatus.Inconclusive)]
    [InlineData("legacy", 0, TestStatus.Inconclusive)]
    [InlineData("malformed", 2, TestStatus.Failed)]
    [InlineData("exception", 1, TestStatus.Error)]
    public async Task ExplicitPrompt_ShouldMatchFinitePrivateInputsAndRetainObligations(string variant, int expectedCalls, TestStatus expected)
    {
        const string privateValue = "private-prompt-fixture-canary";
        using var requests = JsonDocument.Parse("""{"roots":{"method":"roots/list"}}""");
        using var replies = JsonDocument.Parse("""{"roots":{"roots":[]}}""");
        var client = new Mock<IMcpHttpClient>();
        var metadata = variant == "required-argument"
            ? """{"name":"owned","arguments":[{"name":"required","required":true}]}""" : """{"name":"owned"}""";
        var items = variant == "missing" ? "" : variant == "duplicate" ? $"{metadata},{metadata}" : metadata;
        client.Setup(call => call.CallAsync(It.IsAny<string>(), "prompts/list", It.IsAny<object?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new JsonRpcResponse { IsSuccess = true, StatusCode = 200,
                RawJson = $$$"""{"jsonrpc":"2.0","id":"list","result":{"resultType":"complete","cacheScope":"public","ttlMs":0,"prompts":[{{{items}}}]}}""" });
        var calls = 0;
        client.Setup(call => call.CallAsync(It.IsAny<string>(), "prompts/get", It.IsAny<object?>(), It.IsAny<AuthenticationConfig?>(), It.IsAny<CancellationToken>()))
            .Returns((string _, string _, object? parameters, AuthenticationConfig? _, CancellationToken _) =>
            {
                calls++;
                if (variant == "exception") throw new InvalidOperationException(privateValue);
                var input = parameters.Should().BeOfType<PromptCallParameters>().Subject;
                input.Arguments.GetProperty("value").GetString().Should().Be(privateValue);
                input.ClientCapabilities!.Value.TryGetProperty("roots", out _).Should().BeTrue();
                if (calls == 1)
                    return Task.FromResult(new JsonRpcResponse { IsSuccess = true, StatusCode = 200,
                        RawJson = $$$"""{"jsonrpc":"2.0","id":"first","result":{"resultType":"input_required","inputRequests":{"{{{(variant == "mismatch" ? "unapproved" : "roots")}}}":{"method":"roots/list"}},"requestState":"private-state"}}""" });
                input.RequestState.Should().Be("private-state");
                JsonElement.DeepEquals(input.InputResponses!.Value, replies.RootElement).Should().BeTrue();
                return Task.FromResult(new JsonRpcResponse { IsSuccess = true, StatusCode = 200,
                    RawJson = variant == "malformed" ? """{"jsonrpc":"2.0","id":"last","result":{"resultType":"complete","messages":[{}]}}"""
                        : $$$"""{"jsonrpc":"2.0","id":"last","result":{"resultType":"complete","messages":[{"role":"user","content":{"type":"text","text":"{{{privateValue}}}"}}]}}""" });
            });
        var config = new PromptTestingConfig
        {
            TestPromptExecution = variant != "disabled",
            CustomScenarios = [new PromptTestScenario { PromptName = "owned", Arguments = new() { ["value"] = privateValue },
                ClientInputRounds = [new() { ExpectedRequests = requests.RootElement, Responses = replies.RootElement }] }]
        };
        var safety = new Mock<IContentSafetyAnalyzer>();
        safety.Setup(analyzer => analyzer.AnalyzePrompt(It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<int>())).Returns([]);
        var validator = new PromptValidator(Mock.Of<ILogger<PromptValidator>>(), client.Object, new JsonSchemaValidator(),
            new EmbeddedSchemaRegistry(), safety.Object);
        var result = await validator.ValidatePromptDiscoveryAsync(new McpServerConfig
        { Endpoint = "https://example.invalid/mcp", ProtocolVersion = variant == "legacy" ? "2025-11-25" : "2026-07-28" }, config);
        calls.Should().Be(expectedCalls);
        result.Status.Should().Be(expected);
        result.PromptResults.Should().NotBeEmpty().And.OnlyContain(prompt => prompt.ExecutionSuccessful == (expected == TestStatus.Passed));
        JsonSerializer.Serialize(result).Should().NotContain(privateValue).And.NotContain("private-state");
    }

    [Fact]
    public void PromptCapture_ShouldBePrivateImmutableAndRejectUnavailableArguments()
    {
        var configuration = new McpValidatorConfiguration();
        configuration.Validation.Categories.PromptTesting.CustomScenarios.Add(new()
        { PromptName = "fixture", Arguments = new() { ["key"] = "private-prompt-input" } });
        var request = ValidationRunRequest.Capture(configuration);
        configuration.Validation.Categories.PromptTesting.CustomScenarios[0].Arguments!.Clear();
        request.CreateConfiguration().Validation.Categories.PromptTesting.CustomScenarios[0].Arguments.Should().ContainKey("key");
        var clone = request.CreateConfiguration().CloneWithoutSecrets();
        JsonSerializer.Serialize(clone).Should().NotContain("private-prompt-input");
        FluentActions.Invoking(() => ToolScenarioInputs.CapturePromptScenarios(clone.Validation.Categories.PromptTesting))
            .Should().Throw<ArgumentException>();
    }

    [Theory]
    [InlineData(false, TestStatus.Error)]
    [InlineData(true, TestStatus.Inconclusive)]
    public async Task CompletedPrompt_WithoutUsableSchema_ShouldNotEarnCreditOrBlameTarget(bool processingFailure, TestStatus expected)
    {
        var client = new Mock<IMcpHttpClient>();
        client.Setup(call => call.CallAsync(It.IsAny<string>(), "prompts/list", It.IsAny<object?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new JsonRpcResponse { IsSuccess = true, StatusCode = 200,
                RawJson = """{"jsonrpc":"2.0","id":"list","result":{"resultType":"complete","cacheScope":"public","ttlMs":0,"prompts":[{"name":"owned","description":"An owned prompt"}]}}""" });
        client.Setup(call => call.CallAsync(It.IsAny<string>(), "prompts/get", It.IsAny<object?>(), It.IsAny<AuthenticationConfig?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new JsonRpcResponse { IsSuccess = true, StatusCode = 200,
                RawJson = """{"jsonrpc":"2.0","id":"get","result":{"resultType":"complete","messages":[{"role":"user","content":{"type":"text","text":"private-schema-canary"}}]}}""" });
        var missing = new Mock<ISchemaRegistry>();
        missing.Setup(registry => registry.GetSchema(It.IsAny<ProtocolVersion>(), It.IsAny<string>(), It.IsAny<string>()))
            .Throws(new FileNotFoundException("private-schema-canary"));
        var failing = new Mock<ISchemaValidator>();
        failing.Setup(validator => validator.Validate(It.IsAny<System.Text.Json.Nodes.JsonNode>(), It.IsAny<System.Text.Json.Nodes.JsonNode>()))
            .Returns((System.Text.Json.Nodes.JsonNode? instance, System.Text.Json.Nodes.JsonNode schema) =>
                schema["$ref"]?.GetValue<string>().EndsWith("/GetPromptResult", StringComparison.Ordinal) == true
                    ? new SchemaValidationResult { IsValid = false, Errors = ["Schema processing error: private-schema-canary"] }
                    : new JsonSchemaValidator().Validate(instance, schema));
        var safety = new Mock<IContentSafetyAnalyzer>();
        safety.Setup(analyzer => analyzer.AnalyzePrompt(It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<int>())).Returns([]);
        var validator = new PromptValidator(Mock.Of<ILogger<PromptValidator>>(), client.Object,
            processingFailure ? failing.Object : new JsonSchemaValidator(), processingFailure ? new EmbeddedSchemaRegistry() : missing.Object, safety.Object);
        var result = await validator.ValidatePromptDiscoveryAsync(new()
        {
            Endpoint = "https://example.invalid/mcp", ProtocolVersion = "2026-07-28"
        }, new PromptTestingConfig { CustomScenarios = [new() { PromptName = "owned", Arguments = new() }] });

        result.Status.Should().Be(expected);
        result.PromptResults.Should().ContainSingle().Which.ExecutionSuccessful.Should().BeFalse();
        result.Findings.Should().NotContain(finding => finding.RuleId == ValidationFindingRuleIds.PromptGetResultInvalid);
        JsonSerializer.Serialize(result).Should().NotContain("private-schema-canary");
    }
}
