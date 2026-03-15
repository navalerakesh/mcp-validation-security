namespace Mcp.Benchmark.Core.Models;

/// <summary>
/// Represents the declared or inferred operating profile for an MCP server.
/// Profiles describe the server's intent so validation rules can interpret findings contextually.
/// </summary>
public enum McpServerProfile
{
    /// <summary>
    /// No profile has been declared or inferred.
    /// </summary>
    Unspecified = 0,

    /// <summary>
    /// Public servers that are intentionally unauthenticated and optimized for broad access.
    /// </summary>
    Public,

    /// <summary>
    /// Servers that expect authenticated access but do not enforce enterprise controls.
    /// </summary>
    Authenticated,

    /// <summary>
    /// Enterprise servers that enforce strict authentication and policy compliance.
    /// </summary>
    Enterprise
}

/// <summary>
/// Indicates how a server profile was determined so reports can describe provenance.
/// </summary>
public enum ServerProfileSource
{
    /// <summary>
    /// No profile source was provided.
    /// </summary>
    Unknown = 0,

    /// <summary>
    /// Profile was explicitly supplied by the user via configuration or CLI.
    /// </summary>
    UserDeclared,

    /// <summary>
    /// Profile was emitted by the server during negotiation.
    /// </summary>
    ServerDeclared,

    /// <summary>
    /// Profile was inferred heuristically based on observed behavior.
    /// </summary>
    Inferred
}
