using System.Text.Json;
using Mcp.Benchmark.Core.Models;
using Mcp.Benchmark.Core.Services;
using Mcp.Benchmark.Infrastructure.Services;
using Mcp.Benchmark.Infrastructure.Validators;
using Mcp.Compliance.Spec;

namespace Mcp.Benchmark.Tests.Unit.Services;

public sealed class SyntheticToolClientInputsTests
{
    private const string Protocol = "2026-07-28";
    private readonly EmbeddedSchemaRegistry _registry = new();
    private readonly JsonSchemaValidator _validator = new();

    [Theory]
    [InlineData("rounds", 8, true)]
    [InlineData("rounds", 9, false)]
    [InlineData("inputs", 8, true)]
    [InlineData("inputs", 9, false)]
    [InlineData("id", 128, true)]
    [InlineData("id", 129, false)]
    [InlineData("values", 1017, true)]
    [InlineData("values", 1018, false)]
    public void Capture_ShouldEnforceExactPlanBoundaries(string kind, int count, bool valid)
    {
        var requests = new Dictionary<string, object>();
        var responses = new Dictionary<string, object>();
        for (var index = 0; index < (kind == "inputs" ? count : 1); index++)
        {
            var id = kind == "id" ? new string('i', count) : $"input{index}";
            requests[id] = new { method = "roots/list" };
            responses[id] = kind == "values" ? new { values = new int[count] } : new object();
        }
        var round = Round(JsonSerializer.Serialize(requests), JsonSerializer.Serialize(responses));
        var capture = () => Capture(Enumerable.Repeat(round, kind == "rounds" ? count : 1).ToArray());
        if (valid) capture().ClientInputRounds.Should().HaveCount(kind == "rounds" ? count : 1);
        else capture.Should().Throw<ArgumentException>().Which.Message.Should().NotBeNullOrWhiteSpace();
    }

    [Theory]
    [InlineData("roots", true)]
    [InlineData("sampling", true)]
    [InlineData("sampling-tools", false)]
    [InlineData("sampling-empty-tools", false)]
    [InlineData("sampling-tool-choice", false)]
    [InlineData("form", true)]
    [InlineData("decline", true)]
    [InlineData("cancel", true)]
    [InlineData("wrong-form", false)]
    [InlineData("out-of-band", false)]
    [InlineData("decline-content", false)]
    [InlineData("real-model", false)]
    [InlineData("malformed-request", false)]
    [InlineData("malformed-response", false)]
    public void Capabilities_ShouldRequireExplicitSchemaValidSyntheticReplies(string kind, bool valid)
    {
        var request = """{"method":"roots/list"}""";
        var response = """{"roots":[]}""";
        var capability = "roots";
        if (kind is "sampling" or "real-model" or "sampling-tools" or "sampling-empty-tools" or "sampling-tool-choice")
        {
            request = """{"method":"sampling/createMessage","params":{"messages":[{"role":"user","content":{"type":"text","text":"fixture"}}],"maxTokens":1}}""";
            response = $$$"""{"role":"assistant","model":"{{{(kind == "sampling" ? SyntheticToolClientInputs.SyntheticModelName : "unrecorded-model")}}}","content":{"type":"text","text":"fixture"}}""";
            capability = "sampling";
            if (kind is "sampling-tools" or "sampling-empty-tools" or "sampling-tool-choice")
            {
                var node = System.Text.Json.Nodes.JsonNode.Parse(request)!;
                node["params"]![kind == "sampling-tool-choice" ? "toolChoice" : "tools"] = System.Text.Json.Nodes.JsonNode.Parse(
                    kind == "sampling-tools" ? """[{"name":"unsupported","inputSchema":{"type":"object"}}]""" :
                    kind == "sampling-empty-tools" ? "[]" : """{"mode":"auto"}""");
                request = node.ToJsonString();
                response = response.Replace("unrecorded-model", SyntheticToolClientInputs.SyntheticModelName, StringComparison.Ordinal);
            }
        }
        if (kind is "form" or "wrong-form" or "out-of-band" or "decline" or "cancel" or "decline-content")
        {
            request = $$$$"""{"method":"elicitation/create","params":{"mode":"{{{{(kind == "out-of-band" ? "url" : "form")}}}}","message":"fixture","requestedSchema":{"type":"object","properties":{"ok":{"type":"boolean"}},"required":["ok"]}}}""";
            response = kind switch
            {
                "wrong-form" => """{"action":"accept","content":{"ok":"invalid"}}""",
                "decline" => """{"action":"decline"}""",
                "cancel" => """{"action":"cancel"}""",
                "decline-content" => """{"action":"decline","content":{"ok":true}}""",
                _ => """{"action":"accept","content":{"ok":true}}"""
            };
            capability = "elicitation";
        }
        if (kind == "malformed-request") request = """{"method":"sampling/createMessage","params":{}}""";
        if (kind == "malformed-response") response = """{"roots":"invalid"}""";
        var scenario = Capture(Round($$"""{"input":{{request}}}""", $$"""{"input":{{response}}}"""));
        var create = () => SyntheticToolClientInputs.CreateCapabilities(scenario, Protocol, _registry, _validator);
        if (!valid) create.Should().Throw<ArgumentException>().WithMessage("*pinned request/result contracts*");
        else create().EnumerateObject().Select(property => property.Name).Should().Equal(capability);
    }

    [Theory]
    [InlineData("complete", true, 2)]
    [InlineData("early-complete", false, 1)]
    [InlineData("extra-round", false, 2)]
    [InlineData("mismatch", false, 1)]
    [InlineData("duplicate", false, 1)]
    [InlineData("missing-prerequisite", false, 1)]
    [InlineData("bad-state", false, 1)]
    [InlineData("oversized-state", false, 1)]
    [InlineData("oversized-response", false, 1)]
    [InlineData("deep-response", false, 1)]
    [InlineData("unavailable", false, 1)]
    public async Task Execution_ShouldStopWithoutSendingUnmatchedOrUnboundedReplies(string variation, bool complete, int expectedCalls)
    {
        var scenario = Capture(Round("""{"input":{"method":"roots/list"}}""", """{"input":{"roots":[]}}"""));
        var calls = 0;
        var response = await Execute(scenario, (parameters, _) =>
        {
            calls++;
            if (calls == 2)
            {
                parameters.InputResponses.Should().NotBeNull();
                parameters.RequestState.Should().Be("private-state");
                return Task.FromResult(variation == "extra-round" ? Input("""{"input":{"method":"roots/list"}}""") : Complete());
            }
            parameters.InputResponses.Should().BeNull();
            return Task.FromResult(variation switch
            {
                "early-complete" => Complete(),
                "mismatch" => Input("""{"unapproved":{"method":"roots/list"}}"""),
                "duplicate" => Input("""{"input":{"method":"roots/list"},"input":{"method":"roots/list"}}"""),
                "missing-prerequisite" => Wire("""{"resultType":"input_required"}"""),
                "bad-state" => Wire("""{"resultType":"input_required","inputRequests":{"input":{"method":"roots/list"}},"requestState":42}"""),
                "oversized-state" => Input("""{"input":{"method":"roots/list"}}""", new string('s', ToolScenarioInputs.MaximumArgumentBytes + 1)),
                "oversized-response" => Wire($$"""{"resultType":"input_required","padding":"{{new string('x', 1024 * 1024)}}"}"""),
                "deep-response" => Wire("""{"resultType":"input_required","padding":""" + new string('[', 30) + "0" + new string(']', 30) + "}"),
                "unavailable" => new JsonRpcResponse { IsSuccess = false, StatusCode = 503 },
                _ => Input("""{"input":{"method":"roots/list"}}""")
            });
        });
        calls.Should().Be(expectedCalls);
        response.IsSuccess.Should().Be(complete);
        if (!complete && variation != "unavailable")
        {
            response.ResultType.Should().Be(McpResultType.InputRequired);
            response.Error.Should().NotBeNullOrWhiteSpace();
            JsonSerializer.Serialize(response).Should().NotContain("private-state");
        }
    }

    [Theory]
    [InlineData("")]
    [InlineData(" ")]
    [InlineData("opaque-not-json")]
    public async Task Execution_ShouldEchoOpaqueStateAndRemoveItWhenAbsentInTheNextRound(string state)
    {
        var assessed = Mcp.Benchmark.Infrastructure.Utilities.ModernResultSemantics.Assess(
            Wire($$"""{"resultType":"input_required","requestState":{{JsonSerializer.Serialize(state)}}}""").RawJson, Protocol);
        assessed.IsValid.Should().BeTrue();
        assessed.RequestState.Should().Be(state);
        var roots = Round("""{"input":{"method":"roots/list"}}""", """{"input":{"roots":[]}}""");
        var scenario = Capture(Round("{}", "{}"), roots);
        var calls = 0;
        var result = await Execute(scenario, (parameters, _) =>
        {
            calls++;
            if (calls == 1) return Task.FromResult(Wire($$"""{"resultType":"input_required","requestState":{{JsonSerializer.Serialize(state)}}}"""));
            if (calls == 2)
            {
                parameters.RequestState.Should().Be(state);
                parameters.InputResponses.Should().BeNull();
                return Task.FromResult(Wire("""{"resultType":"input_required","inputRequests":{"input":{"method":"roots/list"}}}"""));
            }
            parameters.RequestState.Should().BeNull();
            parameters.InputResponses.Should().NotBeNull();
            return Task.FromResult(Complete());
        });
        result.IsSuccess.Should().BeTrue();
        calls.Should().Be(3);
    }

    [Fact]
    public async Task Execution_ShouldHonorCancellationAndRejectLegacyBeforeDispatch()
    {
        var scenario = Capture(Round("{}", "{}"));
        using var cancelled = new CancellationTokenSource();
        cancelled.Cancel();
        var calls = 0;
        Task<JsonRpcResponse> Send(ToolCallParameters _, CancellationToken token)
        {
            calls++;
            return Task.FromResult(Complete());
        }
        var execute = () => Execute(scenario, Send, cancelled.Token);
        await execute.Should().ThrowAsync<OperationCanceledException>();
        var legacy = () => SyntheticToolClientInputs.ExecuteAsync(Initial(scenario), scenario, "2025-11-25",
            _registry, _validator, Send, CancellationToken.None);
        await legacy.Should().ThrowAsync<ArgumentException>();
        calls.Should().Be(0);
    }

    [Fact]
    public async Task Execution_ShouldBoundAggregateResponseBytesAcrossAllRounds()
    {
        var round = Round("""{"input":{"method":"roots/list"}}""", """{"input":{"roots":[]}}""");
        var scenario = Capture(Enumerable.Repeat(round, ToolScenarioInputs.MaximumClientInputRounds).ToArray());
        var calls = 0;
        var result = await Execute(scenario, (_, _) =>
        {
            calls++;
            return Task.FromResult(Wire($$$"""{"resultType":"input_required","inputRequests":{"input":{"method":"roots/list"}},"padding":"{{{new string('x', 200_000)}}}"}"""));
        });
        calls.Should().Be(6);
        result.IsSuccess.Should().BeFalse();
        result.ResultType.Should().Be(McpResultType.InputRequired);
    }

    private Task<JsonRpcResponse> Execute(CapturedToolScenario scenario,
        Func<ToolCallParameters, CancellationToken, Task<JsonRpcResponse>> send, CancellationToken token = default) =>
        SyntheticToolClientInputs.ExecuteAsync(Initial(scenario), scenario, Protocol, _registry, _validator, send, token);

    private ToolCallParameters Initial(CapturedToolScenario scenario) => new("owned", scenario.Arguments, Json("""{"type":"object"}"""))
    {
        ClientCapabilities = SyntheticToolClientInputs.CreateCapabilities(scenario, Protocol, _registry, _validator)
    };

    private static CapturedToolScenario Capture(params ToolClientInputRound[] rounds) => ToolScenarioInputs.CaptureScenarios(new ToolTestingConfig
    {
        CustomScenarios = [new ToolTestScenario { ToolName = "owned", ClientInputRounds = rounds.ToList() }]
    })["owned"];

    private static ToolClientInputRound Round(string requests, string responses) => new() { ExpectedRequests = Json(requests), Responses = Json(responses) };
    private static JsonElement Json(string value) { using var document = JsonDocument.Parse(value); return document.RootElement.Clone(); }
    private static JsonRpcResponse Complete() => Wire("""{"resultType":"complete","content":[]}""");
    private static JsonRpcResponse Input(string requests, string state = "private-state") =>
        Wire($$"""{"resultType":"input_required","inputRequests":{{requests}},"requestState":{{JsonSerializer.Serialize(state)}}}""");
    private static JsonRpcResponse Wire(string result) => new() { IsSuccess = true, StatusCode = 200, RawJson = $$"""{"jsonrpc":"2.0","id":"fixture","result":{{result}}}""" };
}
