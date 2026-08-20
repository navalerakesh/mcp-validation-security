using System.Text.Json;
using System.Text.Json.Nodes;
using Mcp.Benchmark.Core.Abstractions;
using Mcp.Benchmark.Core.Constants;
using Mcp.Benchmark.Core.Models;
using Mcp.Compliance.Spec;
using Mcp.Benchmark.Infrastructure.Validators;

namespace Mcp.Benchmark.Infrastructure.Utilities;

internal readonly record struct ModernResultAssessment(
    McpResultType ResultType,
    bool IsValid,
    string? Error,
    string? RequestState,
    int InputRequestCount);

internal static class ModernResultSemantics
{
    private static readonly ISchemaRegistry SchemaRegistry = new EmbeddedSchemaRegistry();
    private static readonly ISchemaValidator SchemaValidator = new JsonSchemaValidator();

    public static ModernResultAssessment Assess(string? rawJson, string? protocolVersion)
    {
        if (!ProtocolEraVersions.IsModern(protocolVersion))
        {
            return new ModernResultAssessment(McpResultType.Unknown, true, null, null, 0);
        }

        if (string.IsNullOrWhiteSpace(rawJson))
        {
            return Invalid("Modern JSON-RPC response body is empty.");
        }

        try
        {
            using var document = JsonDocument.Parse(rawJson);
            if (document.RootElement.ValueKind != JsonValueKind.Object)
            {
                return Invalid("Modern JSON-RPC response root must be an object.");
            }
            if (!document.RootElement.TryGetProperty("result", out var result))
            {
                return document.RootElement.TryGetProperty("error", out _)
                    ? new ModernResultAssessment(McpResultType.Unknown, true, null, null, 0)
                    : Invalid("Modern JSON-RPC response contains neither result nor error.");
            }

            if (result.ValueKind != JsonValueKind.Object ||
                !result.TryGetProperty("resultType", out var resultTypeElement) ||
                resultTypeElement.ValueKind != JsonValueKind.String)
            {
                return Invalid("Modern JSON-RPC result requires a string resultType.");
            }

            return resultTypeElement.GetString() switch
            {
                "complete" => new ModernResultAssessment(McpResultType.Complete, true, null, null, 0),
                "input_required" => AssessInputRequired(result),
                var value => Invalid($"Modern JSON-RPC resultType '{value}' is unsupported.")
            };
        }
        catch (JsonException ex)
        {
            return Invalid($"Modern JSON-RPC result could not be parsed: {ex.Message}");
        }
    }

    public static void Apply(JsonRpcResponse response, string? protocolVersion, string? method = null)
    {
        ArgumentNullException.ThrowIfNull(response);
        if (string.Equals(method, McpSpecConstants.InitializeMethod, StringComparison.Ordinal))
        {
            response.ResultType = McpResultType.Unknown;
            response.ProtocolSemanticsValid = null;
            response.ProtocolSemanticError = null;
            response.RequestState = null;
            response.InputRequestCount = 0;
            return;
        }

        var assessment = Assess(response.RawJson, protocolVersion);
        response.ResultType = assessment.ResultType;
        response.ProtocolSemanticsValid = ProtocolEraVersions.IsModern(protocolVersion) ? assessment.IsValid : null;
        response.ProtocolSemanticError = assessment.Error;
        response.RequestState = assessment.RequestState;
        response.InputRequestCount = assessment.InputRequestCount;
        if (response.IsSuccess && ProtocolEraVersions.IsModern(protocolVersion) && !assessment.IsValid)
        {
            response.IsSuccess = false;
            response.Error = assessment.Error;
        }
    }

    private static ModernResultAssessment AssessInputRequired(JsonElement result)
    {
        var requestState = result.TryGetProperty("requestState", out var state) && state.ValueKind == JsonValueKind.String
            ? state.GetString()
            : null;
        var inputRequestCount = result.TryGetProperty("inputRequests", out var requests) && requests.ValueKind == JsonValueKind.Object
            ? requests.EnumerateObject().Count()
            : 0;
        if (string.IsNullOrWhiteSpace(requestState) && inputRequestCount == 0)
        {
            return new ModernResultAssessment(
                McpResultType.InputRequired,
                false,
                "Modern input_required result must include requestState or at least one inputRequests entry.",
                null,
                0);
        }

        var resultNode = JsonNode.Parse(result.GetRawText())
            ?? throw new InvalidOperationException("Modern input_required result could not be parsed.");
        var schemaResult = SchemaValidationHelpers.ValidateDefinition(
            SchemaRegistry,
            SchemaValidator,
            ProtocolVersions.V2026_07_28,
            "InputRequiredResult",
            resultNode);
        return schemaResult.IsValid
            ? new ModernResultAssessment(McpResultType.InputRequired, true, null, requestState, inputRequestCount)
            : new ModernResultAssessment(
                McpResultType.InputRequired,
                false,
                string.Join(Environment.NewLine, schemaResult.Errors),
                requestState,
                inputRequestCount);
    }

    private static ModernResultAssessment Invalid(string error) =>
        new(McpResultType.Invalid, false, error, null, 0);
}