using System.Text.Json;
using System.Text.Json.Nodes;
using Mcp.Compliance.Spec;
using Mcp.Benchmark.Core.Abstractions;
using Microsoft.Extensions.Logging;

namespace Mcp.Benchmark.Infrastructure.Validators;

internal static class SchemaValidationHelpers
{
    public const string ListToolsResultDefinition = "ListToolsResult";
    public const string ListResourcesResultDefinition = "ListResourcesResult";
    public const string ListPromptsResultDefinition = "ListPromptsResult";

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

            validationResult = schemaValidator.Validate(resultNode, listResultSchema);
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
}
