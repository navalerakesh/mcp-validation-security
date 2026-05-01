namespace Mcp.Benchmark.Core.Constants;

/// <summary>
/// Centralized constants for validation logic, error messages, and configuration defaults.
/// </summary>
public static class ValidationConstants
{
    public static class Transports
    {
        public const string Stdio = "stdio";
        public const string Http = "http";
        public const string Sse = "sse";
    }

    public static class CheckIds
    {
        public const string TransportStdioNotImplemented = "MCP-TRANS-001";
        public const string ConfigNoEndpoint = "MCP-CONF-001";
        public const string HttpContentType = "MCP-HTTP-001";
        public const string HttpNotificationStatus = "MCP-HTTP-002";
        public const string HttpGetSseOrMethodNotAllowed = "MCP-HTTP-003";
        public const string HttpInvalidProtocolVersion = "MCP-HTTP-004";
        public const string HttpOriginValidation = "MCP-HTTP-005";
        public const string HttpSessionId = "MCP-HTTP-006";
        public const string HttpSseEventStream = "MCP-HTTP-007";
        public const string HttpSessionPropagation = "MCP-HTTP-008";
        public const string StdioNewlineFraming = "MCP-STDIO-001";
        public const string StdioStdoutJsonRpcOnly = "MCP-STDIO-002";
        public const string StdioStderrLogging = "MCP-STDIO-003";
        public const string StdioLifecycleShutdown = "MCP-STDIO-004";
        public const string StdioParserBoundary = "MCP-STDIO-005";
        public const string ProtocolErrorHandling = "MCP-PROTO-ERR";
        public const string ProtocolJsonRpcFormat = "MCP-PROTO-JSONRPC";
        public const string ProtocolLifecycle = "MCP-PROTO-LIFE";
        public const string ProtocolInitializeResponse = "MCP-PROTO-INIT-RESP";
        public const string ProtocolInitializeMissingProtocolVersion = "MCP-PROTO-INIT-PROTOCOL-VERSION";
        public const string ProtocolInitializeUnsupportedProtocolVersion = "MCP-PROTO-INIT-UNSUPPORTED-PROTOCOL-VERSION";
        public const string ProtocolInitializeMissingCapabilities = "MCP-PROTO-INIT-CAPABILITIES";
        public const string ProtocolInitializeMissingServerInfo = "MCP-PROTO-INIT-SERVERINFO";
        public const string ProtocolInitializeMissingServerInfoName = "MCP-PROTO-INIT-SERVERINFO-NAME";
        public const string ProtocolInitializeMissingServerInfoVersion = "MCP-PROTO-INIT-SERVERINFO-VERSION";
        public const string ProtocolNotification = "MCP-PROTO-NOTIF";
    }

    public static class Categories
    {
        public const string Transport = "Transport";
        public const string Configuration = "Configuration";
        public const string JsonRpcCompliance = "JSON-RPC Compliance";
        public const string ProtocolLifecycle = "Protocol Lifecycle";
        public const string AuthenticationSecurity = "Authentication Security";
    }

    public static class Messages
    {
        public const string StdioNotImplemented = "STDIO transport requires process-based tool discovery - not implemented in HTTP validator";
        public const string NoHttpEndpoint = "No HTTP endpoint provided for validation";
        public const string ProcessSpawningNotImplemented = "Process spawning and stdin/stdout communication not implemented";
        public const string OperationTimedOutOrWasCancelled = "Operation timed out or was cancelled";
    }

    public static class Methods
    {
        public const string Initialize = "initialize";
        public const string Initialized = "initialized";
        public const string ToolsList = "tools/list";
        public const string ToolsCall = "tools/call";
        public const string ResourcesList = "resources/list";
        public const string ResourcesRead = "resources/read";
        public const string ResourcesTemplatesList = "resources/templates/list";
        public const string ResourcesSubscribe = "resources/subscribe";
        public const string ResourcesUnsubscribe = "resources/unsubscribe";
        public const string PromptsList = "prompts/list";
        public const string PromptsGet = "prompts/get";
        public const string CompletionComplete = "completion/complete";
        public const string RootsList = "roots/list";
        public const string LoggingSetLevel = "logging/setLevel";
        public const string SamplingCreateMessage = "sampling/createMessage";
        public const string ElicitationCreate = "elicitation/create";
        public const string TasksList = "tasks/list";
        public const string TasksGet = "tasks/get";
        public const string TasksResult = "tasks/result";
        public const string TasksCancel = "tasks/cancel";
        public const string Ping = "ping";
    }

    /// <summary>
    /// Input validation attack vector IDs.
    /// Centralized so tests and validators share the same identifiers.
    /// These test whether MCP servers validate input — they do NOT simulate
    /// actual SQL/XSS/command attacks (MCP servers aren't databases or browsers).
    /// </summary>
    public static class AttackVectors
    {
        public const string InputValidation1 = "INJ-001 (Input Validation)";
        public const string InputValidation2 = "INJ-002 (Input Validation)";
        public const string InputValidation3 = "INJ-003 (Input Validation)";
    }
}
