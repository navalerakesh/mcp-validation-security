using System.Collections.Frozen;

namespace Mcp.Benchmark.Core.Constants;

/// <summary>
/// Model Context Protocol (MCP) specification constants and references.
/// Official MCP Specification: https://modelcontextprotocol.io/introduction
/// </summary>
public static class McpSpecConstants
{
    /// <summary>
    /// Current MCP protocol version as defined by the official specification.
    /// Reference: https://modelcontextprotocol.io/specification/basic/protocol
    /// </summary>
    public const string CurrentProtocolVersion = "2024-11-05";

    /// <summary>
    /// Supported MCP protocol versions for backward compatibility.
    /// Reference: https://modelcontextprotocol.io/specification/basic/protocol#versioning
    /// </summary>
    public static readonly FrozenSet<string> SupportedProtocolVersions = new[]
    {
        "2024-11-05",
        "2024-10-07"
    }.ToFrozenSet();

    /// <summary>
    /// Required MCP initialization method name.
    /// Reference: https://modelcontextprotocol.io/specification/basic/lifecycle#initialization
    /// </summary>
    public const string InitializeMethod = "initialize";

    /// <summary>
    /// MCP notification method for initialized state.
    /// Reference: https://modelcontextprotocol.io/specification/basic/lifecycle#initialization
    /// </summary>
    public const string InitializedNotification = "notifications/initialized";

    /// <summary>
    /// Standard MCP capability names as defined in the specification.
    /// Reference: https://modelcontextprotocol.io/specification/basic/capabilities
    /// </summary>
    public static class Capabilities
    {
        public const string Tools = "tools";
        public const string Resources = "resources";
        public const string Prompts = "prompts";
        public const string Logging = "logging";
        public const string Roots = "roots";
        public const string Sampling = "sampling";
    }

    /// <summary>
    /// Standard MCP method names for tool operations.
    /// Reference: https://modelcontextprotocol.io/specification/server/tools
    /// </summary>
    public static class ToolMethods
    {
        public const string List = "tools/list";
        public const string Call = "tools/call";
    }

    /// <summary>
    /// Standard MCP method names for resource operations.
    /// Reference: https://modelcontextprotocol.io/specification/server/resources
    /// </summary>
    public static class ResourceMethods
    {
        public const string List = "resources/list";
        public const string Read = "resources/read";
        public const string Templates = "resources/templates";
        public const string Subscribe = "resources/subscribe";
        public const string Unsubscribe = "resources/unsubscribe";
    }

    /// <summary>
    /// Standard MCP method names for prompt operations.
    /// Reference: https://modelcontextprotocol.io/specification/server/prompts
    /// </summary>
    public static class PromptMethods
    {
        public const string List = "prompts/list";
        public const string Get = "prompts/get";
    }

    /// <summary>
    /// Standard MCP method names for logging operations.
    /// Reference: https://modelcontextprotocol.io/specification/server/logging
    /// </summary>
    public static class LoggingMethods
    {
        public const string SetLevel = "logging/setLevel";
    }

    /// <summary>
    /// Standard MCP content types for tool responses.
    /// Reference: https://modelcontextprotocol.io/specification/basic/types#content
    /// </summary>
    public static class ContentTypes
    {
        public const string Text = "text";
        public const string Image = "image";
        public const string Resource = "resource";
    }

    /// <summary>
    /// Standard MCP transport types.
    /// Reference: https://modelcontextprotocol.io/specification/basic/transports
    /// </summary>
    public static class TransportTypes
    {
        public const string Stdio = "stdio";
        public const string Http = "http";
        public const string Websocket = "websocket";
        public const string Sse = "sse";
    }

    /// <summary>
    /// Required HTTP headers for MCP HTTP transport.
    /// Reference: https://modelcontextprotocol.io/specification/basic/transports#http
    /// </summary>
    public static class HttpHeaders
    {
        public const string ContentType = "Content-Type";
        public const string Authorization = "Authorization";
        public const string UserAgent = "User-Agent";
        public const string McpVersion = "X-MCP-Version";
    }

    /// <summary>
    /// Standard HTTP content type for MCP JSON-RPC communication.
    /// Reference: https://modelcontextprotocol.io/specification/basic/transports#http
    /// </summary>
    public const string JsonRpcContentType = "application/json";

    /// <summary>
    /// Default timeout values for MCP operations (in milliseconds).
    /// Reference: https://modelcontextprotocol.io/specification/basic/lifecycle#timeouts
    /// </summary>
    public static class DefaultTimeouts
    {
        public const int InitializationMs = 30_000;
        public const int MethodCallMs = 10_000;
        public const int HealthCheckMs = 5_000;
        public const int ConnectionMs = 15_000;
    }
}
