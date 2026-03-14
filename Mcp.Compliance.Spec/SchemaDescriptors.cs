namespace Mcp.Compliance.Spec;

/// <summary>
/// Describes a JSON Schema associated with a particular MCP protocol version and feature area.
/// </summary>
public sealed class SchemaDescriptor
{
    public required ProtocolVersion Version { get; init; }

    /// <summary>
    /// Functional area for this schema, such as "protocol", "tools", "resources", or "prompts".
    /// </summary>
    public required string Area { get; init; }

    /// <summary>
    /// Logical name of the schema within the given area, such as "initialize-request".
    /// </summary>
    public required string Name { get; init; }

    public string? Description { get; init; }

    public Uri? SpecReference { get; init; }
}
