using System.Text.Json;
using System.Text.Json.Nodes;
using Mcp.Compliance.Spec;
using Mcp.Benchmark.Core.Abstractions;
using Microsoft.Extensions.Logging;
using System.Globalization;

namespace Mcp.Benchmark.Infrastructure.Validators;

internal static class SchemaValidationHelpers
{
    public const string ListToolsResultDefinition = "ListToolsResult";
    public const string ListResourcesResultDefinition = "ListResourcesResult";
    public const string ListPromptsResultDefinition = "ListPromptsResult";
    public const string DiscoverResultResponseDefinition = "DiscoverResultResponse";

    /// <summary>
    /// Resolves a protocol version string (as negotiated during initialization
    /// or configured) into an embedded <see cref="ProtocolVersion"/>. If the
    /// value is null, empty, or unknown, this method falls back to the
    /// backwards-compatible embedded schema profile.
    /// </summary>
    public static ProtocolVersion ResolveProtocolVersion(ISchemaRegistry schemaRegistry, string? negotiatedVersion)
    {
        if (schemaRegistry == null)
        {
            throw new ArgumentNullException(nameof(schemaRegistry));
        }

        return SchemaRegistryProtocolVersions.ResolveSchemaVersion(negotiatedVersion, schemaRegistry);
    }

    /// <summary>
    /// Attempts to validate the <c>result</c> property of a JSON-RPC response
    /// against a specific List*Result definition in the official MCP schema bundle
    /// for the given protocol version.
    ///
    /// If the schema is not available or cannot be loaded, this method returns
    /// false without throwing.
    /// </summary>
    public static bool TryValidateListResult(
        ISchemaRegistry schemaRegistry,
        ISchemaValidator schemaValidator,
        ProtocolVersion protocolVersion,
        string listResultDefinitionName,
        string? rawJson,
        ILogger logger,
        out SchemaValidationResult? validationResult)
    {
        validationResult = null;

        if (string.IsNullOrWhiteSpace(rawJson))
        {
            return false;
        }

        try
        {
            var rootNode = JsonNode.Parse(rawJson);
            var resultNode = rootNode?["result"];
            if (resultNode is null)
            {
                return false;
            }

            using var schemaStream = schemaRegistry.GetSchema(protocolVersion, area: "protocol", name: "schema");
            if (schemaStream == null)
            {
                return false;
            }

            using var schemaDoc = JsonDocument.Parse(schemaStream);
            var schemaRoot = JsonNode.Parse(schemaDoc.RootElement.GetRawText());
            if (schemaRoot is null)
            {
                return false;
            }

            var defsNode = schemaRoot["$defs"] ?? schemaRoot["definitions"];
            var listResultSchema = defsNode?[listResultDefinitionName];
            if (listResultSchema is null)
            {
                return false;
            }

            validationResult = schemaValidator.Validate(resultNode, CreateDefinitionWrapper(schemaRoot, listResultDefinitionName));
            return true;
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex,
                "Unable to validate list result using MCP bundle for protocol version {ProtocolVersion} and definition {Definition}",
                protocolVersion.Value,
                listResultDefinitionName);
            return false;
        }
    }

    public static bool TryValidateResponseDefinition(
        ISchemaRegistry schemaRegistry,
        ISchemaValidator schemaValidator,
        ProtocolVersion protocolVersion,
        string responseDefinitionName,
        string? rawJson,
        ILogger logger,
        out SchemaValidationResult? validationResult)
    {
        validationResult = null;
        if (string.IsNullOrWhiteSpace(rawJson))
        {
            return false;
        }

        try
        {
            var instance = JsonNode.Parse(rawJson);
            if (instance is null)
            {
                return false;
            }

            using var schemaStream = schemaRegistry.GetSchema(protocolVersion, area: "protocol", name: "schema");
            using var schemaDocument = JsonDocument.Parse(schemaStream);
            var schemaRoot = JsonNode.Parse(schemaDocument.RootElement.GetRawText());
            var definition = (schemaRoot?["$defs"] ?? schemaRoot?["definitions"])?[responseDefinitionName];
            if (definition is null)
            {
                return false;
            }

            validationResult = schemaValidator.Validate(instance, CreateDefinitionWrapper(schemaRoot!, responseDefinitionName));
            return true;
        }
        catch (Exception ex)
        {
            logger.LogWarning(
                ex,
                "Unable to validate MCP response using protocol version {ProtocolVersion} and definition {Definition}",
                protocolVersion.Value,
                responseDefinitionName);
            return false;
        }
    }

    public static SchemaValidationResult ValidateDefinition(
        ISchemaRegistry schemaRegistry,
        ISchemaValidator schemaValidator,
        ProtocolVersion protocolVersion,
        string definitionName,
        JsonNode instance)
    {
        ArgumentNullException.ThrowIfNull(schemaRegistry);
        ArgumentNullException.ThrowIfNull(schemaValidator);
        ArgumentException.ThrowIfNullOrWhiteSpace(definitionName);
        ArgumentNullException.ThrowIfNull(instance);

        using var schemaStream = schemaRegistry.GetSchema(protocolVersion, area: "protocol", name: "schema");
        using var schemaDocument = JsonDocument.Parse(schemaStream);
        var schemaRoot = JsonNode.Parse(schemaDocument.RootElement.GetRawText())
            ?? throw new InvalidOperationException("Embedded protocol schema could not be parsed.");
        if ((schemaRoot["$defs"] ?? schemaRoot["definitions"])?[definitionName] is null)
        {
            throw new InvalidOperationException($"Embedded protocol schema definition '{definitionName}' was not found.");
        }

        return schemaValidator.Validate(instance, CreateDefinitionWrapper(schemaRoot, definitionName));
    }

    private static JsonObject CreateDefinitionWrapper(JsonNode schemaRoot, string definitionName)
    {
        var wrapper = new JsonObject
        {
            ["$schema"] = schemaRoot["$schema"]?.DeepClone(),
            ["$defs"] = (schemaRoot["$defs"] ?? schemaRoot["definitions"])?.DeepClone(),
            ["$ref"] = $"#/$defs/{definitionName}"
        };
        return wrapper;
    }

    public static bool HasSchemaProcessingError(SchemaValidationResult? validationResult)
    {
        return validationResult?.Errors?.Any(error =>
            error.Contains("Schema processing error", StringComparison.OrdinalIgnoreCase)) == true;
    }

    public static string FormatListSchemaIssueHeader(string methodName, bool hasProcessingError)
    {
        if (string.IsNullOrWhiteSpace(methodName))
        {
            methodName = "list result";
        }

        return hasProcessingError
            ? $"⚠️ Schema validation warning: {methodName} schema could not be fully processed"
            : string.Format(CultureInfo.InvariantCulture, "❌ NON-COMPLIANT: {0} response does not conform to MCP JSON Schema", methodName);
    }
}
