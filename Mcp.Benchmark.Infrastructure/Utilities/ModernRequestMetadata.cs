using System.Reflection;
using System.Text.Json;
using System.Text.Json.Nodes;
using Mcp.Benchmark.Core.Constants;
using Mcp.Compliance.Spec;

namespace Mcp.Benchmark.Infrastructure.Utilities;

internal static class ModernRequestMetadata
{
    private const string ProtocolVersionKey = "io.modelcontextprotocol/protocolVersion";
    private const string ClientCapabilitiesKey = "io.modelcontextprotocol/clientCapabilities";
    private const string ClientInfoKey = "io.modelcontextprotocol/clientInfo";
    private static readonly string ClientVersion =
        typeof(ModernRequestMetadata).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion
        ?? typeof(ModernRequestMetadata).Assembly.GetName().Version?.ToString()
        ?? "0.0.0";

    public static object? Enrich(object? parameters, string? protocolVersion, JsonSerializerOptions serializerOptions)
    {
        ArgumentNullException.ThrowIfNull(serializerOptions);
        if (!ProtocolEraVersions.IsModern(protocolVersion))
        {
            return parameters;
        }

        var root = parameters == null
            ? new JsonObject()
            : JsonSerializer.SerializeToNode(parameters, serializerOptions) as JsonObject
              ?? throw new InvalidOperationException("Modern MCP request parameters must serialize as a JSON object.");
        var metadata = root["_meta"] as JsonObject ?? new JsonObject();
        metadata[ProtocolVersionKey] = protocolVersion;
        metadata[ClientCapabilitiesKey] = new JsonObject();
        metadata[ClientInfoKey] = new JsonObject
        {
            ["name"] = ValidationConstants.Product.ClientName,
            ["version"] = ClientVersion
        };
        root["_meta"] = metadata;
        return root;
    }
}