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
    /// or configured) into a well-known <see cref="ProtocolVersion"/>. If the
    /// value is null, empty, or unknown, this method falls back to the
    /// backwards-compatible default defined by the MCP spec (2025-03-26).
    /// </summary>
    public static ProtocolVersion ResolveProtocolVersion(string? negotiatedVersion)
    {
        if (string.IsNullOrWhiteSpace(negotiatedVersion))
        {
            // Per MCP spec, when the protocol version is not otherwise known,
            // servers SHOULD assume 2025-03-26 for backwards compatibility.
            return ProtocolVersions.V2025_03_26;
        }

        if (string.Equals(negotiatedVersion, ProtocolVersions.V2024_11_05.Value, StringComparison.Ordinal))
        {
            return ProtocolVersions.V2024_11_05;
        }

        if (string.Equals(negotiatedVersion, ProtocolVersions.V2025_03_26.Value, StringComparison.Ordinal))
        {
            return ProtocolVersions.V2025_03_26;
        }

        if (string.Equals(negotiatedVersion, ProtocolVersions.V2025_06_18.Value, StringComparison.Ordinal))
        {
            return ProtocolVersions.V2025_06_18;
        }

        if (string.Equals(negotiatedVersion, ProtocolVersions.V2025_11_25.Value, StringComparison.Ordinal))
        {
            return ProtocolVersions.V2025_11_25;
        }

        // Unknown version: use the MCP spec's backwards-compatible default
        // so we still have a concrete schema bundle to validate against.
        return ProtocolVersions.V2025_03_26;
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
