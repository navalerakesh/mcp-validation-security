using System.Collections.Generic;

namespace Mcp.Compliance.Spec;

/// <summary>
/// Abstraction for accessing MCP JSON Schemas without exposing file-system or resource details.
/// </summary>
public interface ISchemaRegistry
{
    /// <summary>
    /// Returns a readable stream for the requested schema.
    /// Implementations should throw a descriptive exception if the schema is not found.
    /// </summary>
    Stream GetSchema(ProtocolVersion version, string area, string name);

    /// <summary>
    /// Lists all known schemas in this registry.
    /// </summary>
    IReadOnlyCollection<SchemaDescriptor> ListSchemas();

    /// <summary>
    /// Lists known schemas filtered by MCP protocol version and optional area.
    /// </summary>
    IReadOnlyCollection<SchemaDescriptor> ListSchemas(ProtocolVersion version, string? area = null);
}
