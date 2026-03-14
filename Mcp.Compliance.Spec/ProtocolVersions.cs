namespace Mcp.Compliance.Spec;

/// <summary>
/// Represents a specific MCP protocol version.
/// </summary>
public readonly record struct ProtocolVersion(string Value)
{
    public override string ToString() => Value;
}

/// <summary>
/// Well-known MCP protocol versions supported by this library.
/// </summary>
public static class ProtocolVersions
{
    public static readonly ProtocolVersion V2024_11_05 = new("2024-11-05");
    public static readonly ProtocolVersion V2025_03_26 = new("2025-03-26");
    public static readonly ProtocolVersion V2025_06_18 = new("2025-06-18");
    public static readonly ProtocolVersion V2025_11_25 = new("2025-11-25");
}
