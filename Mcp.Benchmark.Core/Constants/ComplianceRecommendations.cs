using System.Collections.Frozen;

namespace Mcp.Benchmark.Core.Constants;

/// <summary>
/// Actionable remediation text for protocol compliance violations.
/// Keyed by <see cref="ValidationConstants.CheckIds"/> and optionally refined by
/// description substring for violations that share the same CheckId but cover
/// distinct failure modes.
/// </summary>
public static class ComplianceRecommendations
{
    /// <summary>
    /// Returns remediation text for a compliance violation.
    /// Matches first on (CheckId, Description) for specificity, then falls back
    /// to a generic per-CheckId recommendation.
    /// </summary>
    public static string GetRecommendation(string checkId, string description)
    {
        // Try specific match first (CheckId + description keyword).
        foreach (var entry in SpecificRecommendations)
        {
            if (string.Equals(entry.CheckId, checkId, StringComparison.Ordinal) &&
                description.Contains(entry.Keyword, StringComparison.OrdinalIgnoreCase))
            {
                return entry.Recommendation;
            }
        }

        // Fall back to generic per-CheckId recommendation.
        return GenericRecommendations.GetValueOrDefault(checkId)
            ?? "Review the MCP specification for the relevant compliance requirement and update the server implementation accordingly.";
    }

    private static readonly FrozenDictionary<string, string> GenericRecommendations =
        new Dictionary<string, string>
        {
            {
                ValidationConstants.CheckIds.ProtocolErrorHandling,
                "Ensure error responses use standard JSON-RPC 2.0 error codes (-32700, -32600, -32601, -32602, -32603) and include a structured error object with code, message, and optional data fields. See https://spec.modelcontextprotocol.io/specification/2025-11-25/basic/json-rpc#errors"
            },
            {
                ValidationConstants.CheckIds.ProtocolJsonRpcFormat,
                "Verify all requests and responses conform to the JSON-RPC 2.0 wire format: include \"jsonrpc\": \"2.0\", use integer IDs for requests, and never include an ID in notifications. See https://spec.modelcontextprotocol.io/specification/2025-11-25/basic/json-rpc"
            },
            {
                ValidationConstants.CheckIds.ProtocolLifecycle,
                "The initialize response MUST include protocolVersion, serverInfo (with name and version), and a capabilities object. Review the lifecycle handshake requirements at https://spec.modelcontextprotocol.io/specification/2025-11-25/basic/lifecycle"
            },
            {
                ValidationConstants.CheckIds.ProtocolInitializeResponse,
                "The server must respond to the \"initialize\" request with a valid JSON-RPC result. Verify that the initialize handler is registered and the endpoint remains available during the MCP handshake. See https://spec.modelcontextprotocol.io/specification/2025-11-25/basic/lifecycle#initialization"
            },
            {
                ValidationConstants.CheckIds.ProtocolInitializeMissingProtocolVersion,
                "Include a \"protocolVersion\" string in the initialize response indicating the protocol version the server supports. This is required for version negotiation. See https://spec.modelcontextprotocol.io/specification/2025-11-25/basic/lifecycle#initialization"
            },
            {
                ValidationConstants.CheckIds.ProtocolInitializeMissingCapabilities,
                "The initialize response MUST include a \"capabilities\" object declaring which MCP features the server supports (tools, resources, prompts, etc.). See https://spec.modelcontextprotocol.io/specification/2025-11-25/basic/lifecycle#initialization"
            },
            {
                ValidationConstants.CheckIds.ProtocolInitializeMissingServerInfo,
                "Include a \"serverInfo\" object in the initialize response. This field is required by the current MCP initialize schema and must describe the server implementation. See https://spec.modelcontextprotocol.io/specification/2025-11-25/basic/lifecycle#initialization"
            },
            {
                ValidationConstants.CheckIds.ProtocolInitializeMissingServerInfoName,
                "The serverInfo object in the initialize response MUST include a \"name\" string identifying the server. See https://spec.modelcontextprotocol.io/specification/2025-11-25/basic/lifecycle#initialization"
            },
            {
                ValidationConstants.CheckIds.ProtocolInitializeMissingServerInfoVersion,
                "The serverInfo object in the initialize response MUST include a \"version\" string. See https://spec.modelcontextprotocol.io/specification/2025-11-25/basic/lifecycle#initialization"
            },
            {
                ValidationConstants.CheckIds.ProtocolNotification,
                "Notifications are one-way messages and MUST NOT produce a response. If the server sends a response to a notification, it violates JSON-RPC 2.0 §4.1. Remove response logic for notification handlers. See https://spec.modelcontextprotocol.io/specification/2025-11-25/basic/json-rpc#notifications"
            },
            {
                ValidationConstants.CheckIds.HttpContentType,
                "Set the Content-Type header to 'application/json' for all JSON-RPC messages and reject requests with incorrect content types. See https://spec.modelcontextprotocol.io/specification/2025-11-25/basic/transports#http"
            }
        }.ToFrozenDictionary();

    /// <summary>
    /// Targeted recommendations for violation descriptions that share a CheckId
    /// but require different remediation guidance.
    /// Entries are evaluated in order; first keyword match wins.
    /// </summary>
    private static readonly IReadOnlyList<SpecificEntry> SpecificRecommendations = new[]
    {
        // ProtocolJsonRpcFormat variants
        new SpecificEntry(
            ValidationConstants.CheckIds.ProtocolJsonRpcFormat,
            "Request format",
            "Ensure every JSON-RPC request includes \"jsonrpc\": \"2.0\", a string \"method\" field, and an integer \"id\". The \"params\" field, if present, must be an object or array. See https://spec.modelcontextprotocol.io/specification/2025-11-25/basic/json-rpc"),
        new SpecificEntry(
            ValidationConstants.CheckIds.ProtocolJsonRpcFormat,
            "Response format",
            "Every JSON-RPC response must include \"jsonrpc\": \"2.0\" and the same \"id\" as the request. Successful responses use a \"result\" field; failures use an \"error\" object with code and message. See https://spec.modelcontextprotocol.io/specification/2025-11-25/basic/json-rpc"),
        new SpecificEntry(
            ValidationConstants.CheckIds.ProtocolJsonRpcFormat,
            "Batch processing",
            "If the server accepts batch requests, it must process each request independently and return an array of responses in any order. Ensure batch handling does not drop or duplicate responses. See https://www.jsonrpc.org/specification#batch"),
        new SpecificEntry(
            ValidationConstants.CheckIds.ProtocolJsonRpcFormat,
            "Notification",
            "The server MUST NOT send a response to a notification (a request without an \"id\" field). Remove any response logic that triggers for notifications. See https://spec.modelcontextprotocol.io/specification/2025-11-25/basic/json-rpc#notifications"),
        new SpecificEntry(
            ValidationConstants.CheckIds.ProtocolJsonRpcFormat,
            "Case sensitivity",
            "JSON-RPC member names (\"jsonrpc\", \"method\", \"params\", \"id\", \"result\", \"error\") are case-sensitive. The server must reject or ignore requests with incorrect casing. See https://www.jsonrpc.org/specification"),
        // ProtocolErrorHandling variants
        new SpecificEntry(
            ValidationConstants.CheckIds.ProtocolErrorHandling,
            "Error Code Violation",
            "This specific error code test failed. Ensure the server returns standard JSON-RPC error codes for known error conditions: -32700 (parse error), -32600 (invalid request), -32601 (method not found), -32602 (invalid params), -32603 (internal error). See https://spec.modelcontextprotocol.io/specification/2025-11-25/basic/json-rpc#errors"),
        new SpecificEntry(
            ValidationConstants.CheckIds.ProtocolErrorHandling,
            "Error codes do not comply",
            "Review all error responses to ensure they use standard JSON-RPC 2.0 error codes. Custom error codes must be outside the reserved range (-32000 to -32099 for server errors). See https://spec.modelcontextprotocol.io/specification/2025-11-25/basic/json-rpc#errors"),
    };

    private sealed record SpecificEntry(string CheckId, string Keyword, string Recommendation);
}
