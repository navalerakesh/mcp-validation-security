using Mcp.Benchmark.Core.Models;

namespace Mcp.Benchmark.Infrastructure.Registries;

public static class BuiltInProtocolRuleMatrix
{
    private const string LifecycleSpec = "https://modelcontextprotocol.io/specification/2025-11-25/basic/lifecycle";
    private const string TransportSpec = "https://modelcontextprotocol.io/specification/2025-11-25/basic/transports";
    private static readonly string[] SupportedProtocolVersions = ["2024-11-05", "2025-03-26", "2025-06-18", "2025-11-25"];
    private static readonly string[] HttpTransports = ["http", "https"];
    private static readonly string[] StdioTransport = ["stdio"];

    public static class RuleIds
    {
        public const string InitializeFirst = "MCP.LIFECYCLE.INITIALIZE_FIRST";
        public const string VersionNegotiation = "MCP.LIFECYCLE.VERSION_NEGOTIATION";
        public const string CapabilityNegotiation = "MCP.LIFECYCLE.CAPABILITY_NEGOTIATION";
        public const string InitializedNotification = "MCP.LIFECYCLE.INITIALIZED_NOTIFICATION";
        public const string OperationRespectsNegotiation = "MCP.LIFECYCLE.OPERATION_NEGOTIATED_CAPABILITIES";
        public const string StdioShutdown = "MCP.LIFECYCLE.STDIO_SHUTDOWN";
        public const string HttpShutdown = "MCP.LIFECYCLE.HTTP_SHUTDOWN";
        public const string JsonRpcUtf8 = "MCP.PROTOCOL.JSONRPC_UTF8";
        public const string JsonRpcCaseSensitive = "MCP.PROTOCOL.JSONRPC_CASE_SENSITIVE";
        public const string StdioNewlineFraming = "MCP.TRANSPORT.STDIO.NEWLINE_FRAMING";
        public const string StdioStdoutMcpOnly = "MCP.TRANSPORT.STDIO.STDOUT_MCP_ONLY";
        public const string StdioStderrLogging = "MCP.TRANSPORT.STDIO.STDERR_LOGGING";
        public const string HttpOriginValidation = "MCP.TRANSPORT.HTTP.ORIGIN_VALIDATION";
        public const string HttpPostJsonRpc = "MCP.TRANSPORT.HTTP.POST_JSON_RPC";
        public const string HttpAcceptHeader = "MCP.TRANSPORT.HTTP.ACCEPT_HEADER";
        public const string HttpRequestContentType = "MCP.TRANSPORT.HTTP.REQUEST_CONTENT_TYPE";
        public const string HttpRequestResponseContentType = "MCP.TRANSPORT.HTTP.REQUEST_RESPONSE_CONTENT_TYPE";
        public const string HttpNotificationAccepted = "MCP.TRANSPORT.HTTP.NOTIFICATION_ACCEPTED";
        public const string HttpGetSseOrMethodNotAllowed = "MCP.TRANSPORT.HTTP.GET_SSE_OR_405";
        public const string HttpSessionIdPropagation = "MCP.TRANSPORT.HTTP.SESSION_ID_PROPAGATION";
        public const string HttpProtocolVersionHeader = "MCP.TRANSPORT.HTTP.PROTOCOL_VERSION_HEADER";
        public const string HttpInvalidProtocolVersionRejected = "MCP.TRANSPORT.HTTP.INVALID_PROTOCOL_VERSION_REJECTED";
    }

    private static readonly ProtocolRuleMatrixEntry[] Entries =
    [
        Entry(
            RuleIds.InitializeFirst,
            "Initialization must be the first client/server interaction",
            ProtocolRuleRequirement.Must,
            $"{LifecycleSpec}#initialization",
            allTransports: true,
            validatorAreas: ["lifecycle", "initialize"]),
        Entry(
            RuleIds.VersionNegotiation,
            "Server must negotiate a protocol version in initialize result",
            ProtocolRuleRequirement.Must,
            $"{LifecycleSpec}#version-negotiation",
            allTransports: true,
            validatorAreas: ["lifecycle", "initialize", "version-negotiation"]),
        Entry(
            RuleIds.CapabilityNegotiation,
            "Initialization must exchange client and server capabilities",
            ProtocolRuleRequirement.Must,
            $"{LifecycleSpec}#capability-negotiation",
            allTransports: true,
            validatorAreas: ["lifecycle", "capabilities"]),
        Entry(
            RuleIds.InitializedNotification,
            "Client must send notifications/initialized after successful initialization",
            ProtocolRuleRequirement.Must,
            $"{LifecycleSpec}#initialization",
            allTransports: true,
            validatorAreas: ["lifecycle", "initialized-notification"]),
        Entry(
            RuleIds.OperationRespectsNegotiation,
            "Operation phase must respect negotiated protocol version and capabilities",
            ProtocolRuleRequirement.Must,
            $"{LifecycleSpec}#operation",
            allTransports: true,
            validatorAreas: ["operation", "capabilities"]),
        Entry(
            RuleIds.StdioShutdown,
            "STDIO shutdown is signaled through stdin closure and process termination",
            ProtocolRuleRequirement.Should,
            $"{LifecycleSpec}#stdio",
            transports: StdioTransport,
            validatorAreas: ["transport", "shutdown"]),
        Entry(
            RuleIds.HttpShutdown,
            "HTTP shutdown is indicated by closing associated HTTP connections",
            ProtocolRuleRequirement.Should,
            $"{LifecycleSpec}#http",
            transports: HttpTransports,
            validatorAreas: ["transport", "shutdown"]),
        Entry(
            RuleIds.JsonRpcUtf8,
            "MCP JSON-RPC messages must be UTF-8 encoded",
            ProtocolRuleRequirement.Must,
            TransportSpec,
            allTransports: true,
            validatorAreas: ["json-rpc", "encoding"]),
        Entry(
            RuleIds.JsonRpcCaseSensitive,
            "JSON-RPC member names are case-sensitive",
            ProtocolRuleRequirement.Must,
            "https://www.jsonrpc.org/specification",
            allTransports: true,
            validatorAreas: ["json-rpc", "framing"]),
        Entry(
            RuleIds.StdioNewlineFraming,
            "STDIO messages are newline-delimited and must not contain embedded newlines",
            ProtocolRuleRequirement.Must,
            $"{TransportSpec}#stdio",
            transports: StdioTransport,
            validatorAreas: ["transport", "stdio", "framing"]),
        Entry(
            RuleIds.StdioStdoutMcpOnly,
            "STDIO stdout must contain only valid MCP messages",
            ProtocolRuleRequirement.Must,
            $"{TransportSpec}#stdio",
            transports: StdioTransport,
            validatorAreas: ["transport", "stdio", "stdout"]),
        Entry(
            RuleIds.StdioStderrLogging,
            "STDIO stderr may carry UTF-8 logging without implying protocol failure",
            ProtocolRuleRequirement.May,
            $"{TransportSpec}#stdio",
            transports: StdioTransport,
            validatorAreas: ["transport", "stdio", "stderr"]),
        Entry(
            RuleIds.HttpOriginValidation,
            "Streamable HTTP servers must validate Origin when present",
            ProtocolRuleRequirement.Must,
            $"{TransportSpec}#security-warning",
            transports: HttpTransports,
            validatorAreas: ["transport", "http", "security"]),
        Entry(
            RuleIds.HttpPostJsonRpc,
            "Every client-to-server JSON-RPC message must use a new HTTP POST to the MCP endpoint",
            ProtocolRuleRequirement.Must,
            $"{TransportSpec}#sending-messages-to-the-server",
            transports: HttpTransports,
            validatorAreas: ["transport", "http", "post"]),
        Entry(
            RuleIds.HttpAcceptHeader,
            "HTTP POST requests must accept both application/json and text/event-stream responses",
            ProtocolRuleRequirement.Must,
            $"{TransportSpec}#sending-messages-to-the-server",
            transports: HttpTransports,
            validatorAreas: ["transport", "http", "headers"]),
        Entry(
            RuleIds.HttpRequestContentType,
            "HTTP JSON-RPC requests must reject non-JSON request content types",
            ProtocolRuleRequirement.Must,
            $"{TransportSpec}#sending-messages-to-the-server",
            transports: HttpTransports,
            validatorAreas: ["transport", "http", "headers"]),
        Entry(
            RuleIds.HttpRequestResponseContentType,
            "HTTP requests must return either application/json or text/event-stream for JSON-RPC requests",
            ProtocolRuleRequirement.Must,
            $"{TransportSpec}#sending-messages-to-the-server",
            transports: HttpTransports,
            validatorAreas: ["transport", "http", "response"]),
        Entry(
            RuleIds.HttpNotificationAccepted,
            "HTTP JSON-RPC notifications or responses accepted by the server must return 202 with no body",
            ProtocolRuleRequirement.Must,
            $"{TransportSpec}#sending-messages-to-the-server",
            transports: HttpTransports,
            validatorAreas: ["transport", "http", "notification"]),
        Entry(
            RuleIds.HttpGetSseOrMethodNotAllowed,
            "HTTP GET must return an SSE stream or 405 Method Not Allowed",
            ProtocolRuleRequirement.Must,
            $"{TransportSpec}#listening-for-messages-from-the-server",
            transports: HttpTransports,
            validatorAreas: ["transport", "http", "sse"]),
        Entry(
            RuleIds.HttpSessionIdPropagation,
            "Clients must include returned MCP-Session-Id headers on subsequent HTTP requests",
            ProtocolRuleRequirement.Must,
            $"{TransportSpec}#session-management",
            transports: HttpTransports,
            validatorAreas: ["transport", "http", "session"]),
        Entry(
            RuleIds.HttpProtocolVersionHeader,
            "HTTP clients must send MCP-Protocol-Version on subsequent requests",
            ProtocolRuleRequirement.Must,
            $"{TransportSpec}#protocol-version-header",
            transports: HttpTransports,
            validatorAreas: ["transport", "http", "version"]),
        Entry(
            RuleIds.HttpInvalidProtocolVersionRejected,
            "HTTP servers must reject invalid or unsupported MCP-Protocol-Version values with 400",
            ProtocolRuleRequirement.Must,
            $"{TransportSpec}#protocol-version-header",
            transports: HttpTransports,
            validatorAreas: ["transport", "http", "version"])
    ];

    public static IReadOnlyList<ProtocolRuleMatrixEntry> GetAll() => Entries;

    public static IReadOnlyList<ProtocolRuleMatrixEntry> Resolve(ValidationApplicabilityContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        return Entries
            .Where(entry => ValidationPackApplicabilityMatcher.Matches(entry.Applicability, context))
            .ToArray();
    }

    public static ProtocolRuleMatrixEntry GetRequired(string ruleId)
    {
        return Entries.Single(entry => string.Equals(entry.RuleId, ruleId, StringComparison.OrdinalIgnoreCase));
    }

    private static ProtocolRuleMatrixEntry Entry(
        string ruleId,
        string title,
        ProtocolRuleRequirement requirement,
        string specReference,
        bool allTransports = false,
        IReadOnlyList<string>? transports = null,
        IReadOnlyList<string>? validatorAreas = null,
        string? notes = null)
    {
        return new ProtocolRuleMatrixEntry
        {
            RuleId = ruleId,
            Title = title,
            Source = ValidationRuleSource.Spec,
            Requirement = requirement,
            SpecReference = specReference,
            Applicability = new ValidationApplicability
            {
                ProtocolVersions = SupportedProtocolVersions,
                Transports = allTransports ? Array.Empty<string>() : transports ?? Array.Empty<string>()
            },
            ValidatorAreas = validatorAreas ?? Array.Empty<string>(),
            Notes = notes
        };
    }
}
