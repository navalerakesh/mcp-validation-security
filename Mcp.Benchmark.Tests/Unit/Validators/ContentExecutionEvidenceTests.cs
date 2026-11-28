using Mcp.Benchmark.Core.Abstractions;
using Mcp.Benchmark.Core.Models;
using Mcp.Benchmark.Infrastructure.Validators;
using Mcp.Compliance.Spec;

namespace Mcp.Benchmark.Tests.Unit.Validators;

public sealed class ContentExecutionEvidenceTests
{
    public static IEnumerable<object[]> Cases() =>
        from resource in new[] { false, true }
        from variant in new[] { "complete", "input-required", "unavailable", "auth", "rejected-arguments",
            "malformed", "missing-result", "missing-content", "later-invalid", "exception", "cancelled", "disabled", "empty",
            "bad-content-type", "null-item", "timeout", "duplicate-property" }
        select new object[] { resource, variant };

    [Theory]
    [MemberData(nameof(Cases))]
    public async Task ContentExecution_ShouldRequireCompletedEvidenceAndRetainFailureOwnership(bool resource, string variant)
    {
        const string privateValue = "private-response-exception-canary";
        using var cancellation = new CancellationTokenSource();
        var client = new Mock<IMcpHttpClient>();
        var listMethod = resource ? "resources/list" : "prompts/list";
        var getMethod = resource ? "resources/read" : "prompts/get";
        client.Setup(service => service.CallAsync(It.IsAny<string>(), listMethod, It.IsAny<object?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new JsonRpcResponse { IsSuccess = true, StatusCode = 200, RawJson = resource
                ? """{"jsonrpc":"2.0","id":"list","result":{"resultType":"complete","cacheScope":"public","ttlMs":0,"resources":[{"uri":"https://example.invalid/item","name":"item","description":"An owned fixture resource"}]}}"""
                : """{"jsonrpc":"2.0","id":"list","result":{"resultType":"complete","cacheScope":"public","ttlMs":0,"prompts":[{"name":"item","description":"An owned fixture prompt"}]}}""" });
        client.Setup(service => service.CallAsync(It.IsAny<string>(), "resources/templates/list", It.IsAny<object?>(),
            It.IsAny<AuthenticationConfig?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new JsonRpcResponse { IsSuccess = true, StatusCode = 200,
                RawJson = """{"jsonrpc":"2.0","id":"templates","result":{"resultType":"complete","cacheScope":"public","ttlMs":0,"resourceTemplates":[]}}""" });
        var complete = resource
            ? """{"jsonrpc":"2.0","id":"fixture","result":{"resultType":"complete","contents":[{"uri":"https://example.invalid/item","text":"fixture"}]}}"""
            : """{"jsonrpc":"2.0","id":"fixture","result":{"resultType":"complete","messages":[{"role":"user","content":{"type":"text","text":"fixture"}}]}}""";
        var raw = variant switch
        {
            "input-required" => """{"jsonrpc":"2.0","id":"fixture","result":{"resultType":"input_required","requestState":"private-state"}}""",
            "malformed" => "{",
            "missing-result" => """{"jsonrpc":"2.0","id":"fixture"}""",
            "missing-content" => """{"jsonrpc":"2.0","id":"fixture","result":{"resultType":"complete"}}""",
            "later-invalid" => complete.Replace("]}}", ",{}]}}", StringComparison.Ordinal),
            "empty" => resource
                ? """{"jsonrpc":"2.0","id":"fixture","result":{"resultType":"complete","contents":[]}}"""
                : """{"jsonrpc":"2.0","id":"fixture","result":{"resultType":"complete","messages":[]}}""",
            "bad-content-type" => resource ? complete.Replace("\"text\":\"fixture\"", "\"text\":42", StringComparison.Ordinal)
                : complete.Replace("\"role\":\"user\"", "\"role\":42", StringComparison.Ordinal),
            "null-item" => complete.Replace("]}}", ",null]}}", StringComparison.Ordinal),
            "duplicate-property" => complete.Replace("\"resultType\":\"complete\"", "\"resultType\":\"input_required\",\"resultType\":\"complete\"", StringComparison.Ordinal),
            _ => complete
        };
        var status = variant switch { "unavailable" => 503, "auth" => 401, "rejected-arguments" => 400, _ => 200 };
        client.Setup(service => service.CallAsync(It.IsAny<string>(), getMethod, It.IsAny<object?>(),
            It.IsAny<AuthenticationConfig?>(), It.IsAny<CancellationToken>()))
            .Returns(() =>
            {
                if (variant == "exception") throw new InvalidOperationException(privateValue);
                if (variant == "timeout") throw new OperationCanceledException(privateValue);
                if (variant == "cancelled")
                {
                    cancellation.Cancel();
                    return Task.FromCanceled<JsonRpcResponse>(cancellation.Token);
                }
                return Task.FromResult(new JsonRpcResponse { StatusCode = status, IsSuccess = status == 200, RawJson = raw });
            });
        var safety = new Mock<IContentSafetyAnalyzer>();
        safety.Setup(service => service.AnalyzeResource(It.IsAny<string>(), It.IsAny<string>())).Returns([]);
        safety.Setup(service => service.AnalyzePrompt(It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<int>())).Returns([]);
        var server = new McpServerConfig { Endpoint = "https://example.invalid/mcp", ProtocolVersion = "2026-07-28", Transport = "http" };
        TestResultBase result;
        TestStatus leafStatus;
        bool executed;
        if (resource)
        {
            var validator = new ResourceValidator(Mock.Of<ILogger<ResourceValidator>>(), client.Object,
                new JsonSchemaValidator(), new EmbeddedSchemaRegistry(), safety.Object);
            var resources = await validator.ValidateResourceDiscoveryAsync(server,
                new ResourceTestingConfig { TestResourceReading = variant != "disabled", TestSubscriptions = false }, cancellation.Token);
            result = resources;
            var leaf = resources.ResourceResults.Single();
            leafStatus = leaf.Status;
            executed = leaf.AccessSuccessful;
            resources.ResourcesAccessible.Should().Be(variant is "complete" or "empty" ? 1 : 0);
            resources.DiscoveredResourceUris.Should().Equal("https://example.invalid/item");
        }
        else
        {
            var validator = new PromptValidator(Mock.Of<ILogger<PromptValidator>>(), client.Object,
                new JsonSchemaValidator(), new EmbeddedSchemaRegistry(), safety.Object);
            var prompts = await validator.ValidatePromptDiscoveryAsync(server,
                new PromptTestingConfig { TestPromptExecution = variant != "disabled", TestArgumentValidation = false }, cancellation.Token);
            result = prompts;
            var leaf = prompts.PromptResults.Single();
            leafStatus = leaf.Status;
            executed = leaf.ExecutionSuccessful;
            prompts.DiscoveredPromptNames.Should().Equal("item");
        }
        var expected = variant switch
        {
            "complete" or "disabled" or "empty" => TestStatus.Passed,
            "auth" => TestStatus.AuthRequired,
            "exception" => TestStatus.Error,
            "cancelled" => TestStatus.Cancelled,
            "malformed" or "missing-result" or "missing-content" or "later-invalid" or "bad-content-type" or "null-item" or "duplicate-property" => TestStatus.Failed,
            _ => TestStatus.Inconclusive
        };
        leafStatus.Should().Be(expected);
        result.Status.Should().Be(expected);
        executed.Should().Be(variant is "complete" or "empty");
        System.Text.Json.JsonSerializer.Serialize(result).Should().NotContain(privateValue).And.NotContain("private-state");
        if (variant == "disabled")
            client.Verify(service => service.CallAsync(It.IsAny<string>(), getMethod, It.IsAny<object?>(),
                It.IsAny<AuthenticationConfig?>(), It.IsAny<CancellationToken>()), Times.Never);
    }
}
