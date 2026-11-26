using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Mcp.Benchmark.Core.Abstractions;
using Mcp.Benchmark.Core.Constants;
using Mcp.Benchmark.Core.Models;
using Mcp.Benchmark.Core.Services;
using Mcp.Benchmark.Infrastructure.Utilities;
using Mcp.Benchmark.Infrastructure.Validators;
using Mcp.Compliance.Spec;
using Microsoft.Extensions.Logging.Abstractions;

namespace Mcp.Benchmark.Infrastructure.Services;

internal static class SyntheticToolClientInputs
{
    internal const int MaximumInvocationSeconds = 30;
    internal const string SyntheticModelName = "mcpval-synthetic-fixture";

    public static JsonElement CreateCapabilities(CapturedToolScenario scenario, string protocolVersion,
        ISchemaRegistry registry, ISchemaValidator validator)
    {
        if (!ProtocolEraVersions.IsModern(protocolVersion)) throw InvalidPlan();
        var capabilities = new JsonObject();
        foreach (var round in scenario.ClientInputRounds)
        {
            foreach (var request in round.ExpectedRequests.EnumerateObject())
            {
                var method = request.Value.GetProperty("method").GetString();
                var (requestDefinition, responseDefinition, capability) = method switch
                {
                    ValidationConstants.Methods.RootsList => ("ListRootsRequest", "ListRootsResult", McpSpecConstants.Capabilities.Roots),
                    ValidationConstants.Methods.SamplingCreateMessage => ("CreateMessageRequest", "CreateMessageResult", McpSpecConstants.Capabilities.Sampling),
                    ValidationConstants.Methods.ElicitationCreate => ("ElicitRequest", "ElicitResult", McpSpecConstants.Capabilities.Elicitation),
                    _ => throw InvalidPlan()
                };
                var response = round.Responses.GetProperty(request.Name);
                RequireDefinition(requestDefinition, request.Value);
                RequireDefinition(responseDefinition, response);
                if (method == ValidationConstants.Methods.ElicitationCreate)
                {
                    var parameters = request.Value.GetProperty("params");
                    if (parameters.TryGetProperty("mode", out var mode) && !mode.ValueEquals("form")) throw InvalidPlan();
                    var schema = parameters.GetProperty("requestedSchema");
                    if (!new SchemaInspectionContext(schema).CanEvaluateSuppliedArguments()) throw InvalidPlan();
                    if (response.GetProperty("action").ValueEquals("accept") &&
                        (!response.TryGetProperty("content", out var content) ||
                        !validator.Validate(JsonNode.Parse(content.GetRawText()), JsonNode.Parse(schema.GetRawText())!).IsValid)) throw InvalidPlan();
                    if (!response.GetProperty("action").ValueEquals("accept") && response.TryGetProperty("content", out _)) throw InvalidPlan();
                    capabilities[capability] = new JsonObject { ["form"] = new JsonObject() };
                }
                else
                {
                    if (method == ValidationConstants.Methods.SamplingCreateMessage)
                    {
                        var parameters = request.Value.GetProperty("params");
                        if (parameters.TryGetProperty("tools", out _) || parameters.TryGetProperty("toolChoice", out _) ||
                            !response.TryGetProperty("model", out var model) || !model.ValueEquals(SyntheticModelName))
                            throw InvalidPlan();
                    }
                    capabilities[capability] = new JsonObject();
                }
            }
        }
        return JsonSerializer.SerializeToElement(capabilities);

        void RequireDefinition(string definition, JsonElement value)
        {
            if (!SchemaValidationHelpers.ValidateDefinition(registry, validator, SchemaValidationHelpers.ResolveProtocolVersion(registry, protocolVersion), definition,
                    JsonNode.Parse(value.GetRawText())!).IsValid) throw InvalidPlan();
        }
    }

    public static Task<JsonRpcResponse> ExecuteAsync(ToolCallParameters initial, CapturedToolScenario scenario,
        string protocolVersion, ISchemaRegistry registry, ISchemaValidator validator,
        Func<ToolCallParameters, CancellationToken, Task<JsonRpcResponse>> send, CancellationToken cancellationToken)
        => ExecuteRoundsAsync(initial, scenario, protocolVersion, registry, validator, "CallToolResultResponse",
            (responses, state) => new ToolCallParameters(initial.Name, initial.Arguments, initial.InputSchema)
            { ClientCapabilities = initial.ClientCapabilities, InputResponses = responses, RequestState = state }, send, cancellationToken);

    public static Task<JsonRpcResponse> ExecuteAsync(PromptCallParameters initial, CapturedToolScenario scenario,
        string protocolVersion, ISchemaRegistry registry, ISchemaValidator validator,
        Func<PromptCallParameters, CancellationToken, Task<JsonRpcResponse>> send, CancellationToken cancellationToken)
        => ExecuteRoundsAsync(initial, scenario, protocolVersion, registry, validator, "GetPromptResultResponse",
            (responses, state) => new PromptCallParameters(initial.Name, initial.Arguments)
            { ClientCapabilities = initial.ClientCapabilities, InputResponses = responses, RequestState = state }, send, cancellationToken);

    private static async Task<JsonRpcResponse> ExecuteRoundsAsync<T>(T initial, CapturedToolScenario scenario,
        string protocolVersion, ISchemaRegistry registry, ISchemaValidator validator, string responseDefinition,
        Func<JsonElement?, string?, T> continueRequest, Func<T, CancellationToken, Task<JsonRpcResponse>> send, CancellationToken cancellationToken)
    {
        if (!ProtocolEraVersions.IsModern(protocolVersion)) throw InvalidPlan();
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        deadline.CancelAfter(TimeSpan.FromSeconds(MaximumInvocationSeconds));
        var parameters = initial;
        var observedBytes = 0;
        for (var index = 0; index <= scenario.ClientInputRounds.Count; index++)
        {
            deadline.Token.ThrowIfCancellationRequested();
            var response = await send(parameters, deadline.Token).ConfigureAwait(false);
            if (!response.IsSuccess || response.StatusCode is < 200 or >= 300 || string.IsNullOrWhiteSpace(response.RawJson)) return response;
            if (response.RawJson.Length > ExecutionPolicyDefaults.DefaultMaxResponseBytes) return Incomplete();
            observedBytes += Encoding.UTF8.GetByteCount(response.RawJson);
            if (observedBytes > ExecutionPolicyDefaults.DefaultMaxResponseBytes) return Incomplete();
            try
            {
                using var document = JsonDocument.Parse(response.RawJson, new JsonDocumentOptions
                { AllowDuplicateProperties = false, MaxDepth = ToolScenarioInputs.MaximumArgumentDepth + 4 });
                if (document.RootElement.ValueKind != JsonValueKind.Object || document.RootElement.TryGetProperty("error", out _))
                    return Incomplete();
                if (!SchemaValidationHelpers.TryValidateResponseDefinition(registry, validator, SchemaValidationHelpers.ResolveProtocolVersion(registry, protocolVersion),
                        responseDefinition, response.RawJson, NullLogger.Instance, out var validation) || validation?.IsValid != true)
                    return Incomplete();
                var result = document.RootElement.GetProperty("result");
                var kind = result.GetProperty("resultType");
                if (kind.ValueEquals("complete")) return index == scenario.ClientInputRounds.Count ? response : Incomplete();
                if (!kind.ValueEquals("input_required") || index >= scenario.ClientInputRounds.Count) return Incomplete();
                var round = scenario.ClientInputRounds[index];
                var hasRequests = result.TryGetProperty("inputRequests", out var requests);
                if (hasRequests ? !JsonElement.DeepEquals(requests, round.ExpectedRequests) : round.ExpectedRequests.EnumerateObject().Any())
                    return Incomplete();
                string? state = null;
                if (result.TryGetProperty("requestState", out var stateElement))
                {
                    if (stateElement.ValueKind != JsonValueKind.String || stateElement.GetString()!.Length > ToolScenarioInputs.MaximumArgumentBytes ||
                        Encoding.UTF8.GetByteCount(stateElement.GetString()!) > ToolScenarioInputs.MaximumArgumentBytes)
                        return Incomplete();
                    state = stateElement.GetString();
                }
                if (!hasRequests && state is null) return Incomplete();
                parameters = continueRequest(hasRequests ? round.Responses : null, state);
            }
            catch (Exception exception) when (exception is JsonException or InvalidOperationException or KeyNotFoundException)
            { return Incomplete(); }
        }
        return Incomplete();
    }

    private static ArgumentException InvalidPlan() => new("Synthetic client inputs do not satisfy the supported pinned request/result contracts.");

    private static JsonRpcResponse Incomplete() => new()
    {
        IsSuccess = false,
        ResultType = McpResultType.InputRequired,
        Error = "Synthetic client input plan did not match a bounded completed interaction; private values were not retained."
    };
}