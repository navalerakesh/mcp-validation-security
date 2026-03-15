using System.Collections.Frozen;

namespace Mcp.Benchmark.Core.Constants;

/// <summary>
/// Defines stable check IDs and references for MCP compliance validation.
/// </summary>
public static class ComplianceChecks
{
    public static class Protocol
    {
        public const string VersionNegotiation = "MCP-P-001";
        public const string Initialization = "MCP-P-002";
        public const string JsonRpcFormat = "MCP-P-003";
        public const string ErrorHandling = "MCP-P-004";
        public const string CapabilityNegotiation = "MCP-P-005";
        public const string Lifecycle = "MCP-P-006";
        public const string Notification = "MCP-P-007";
    }

    public static class Tools
    {
        public const string ListTools = "MCP-T-001";
        public const string CallTool = "MCP-T-002";
        public const string ToolSchema = "MCP-T-003";
    }

    public static class Resources
    {
        public const string ListResources = "MCP-R-001";
        public const string ReadResource = "MCP-R-002";
        public const string ResourceTemplates = "MCP-R-003";
    }

    public static class Prompts
    {
        public const string ListPrompts = "MCP-PR-001";
        public const string GetPrompt = "MCP-PR-002";
    }

    /// <summary>
    /// Maps check IDs to their official specification references.
    /// </summary>
    public static readonly FrozenDictionary<string, string> SpecReferences = new Dictionary<string, string>
    {
        { Protocol.VersionNegotiation, "https://spec.modelcontextprotocol.io/specification/2024-11-05/basic/lifecycle#initialization" },
        { Protocol.Initialization, "https://spec.modelcontextprotocol.io/specification/2024-11-05/basic/lifecycle#initialization" },
        { Protocol.JsonRpcFormat, "https://spec.modelcontextprotocol.io/specification/2024-11-05/basic/json-rpc" },
        { Protocol.ErrorHandling, "https://spec.modelcontextprotocol.io/specification/2024-11-05/basic/json-rpc#errors" },
        { Protocol.CapabilityNegotiation, "https://spec.modelcontextprotocol.io/specification/2024-11-05/basic/capabilities" },
        { Protocol.Lifecycle, "https://spec.modelcontextprotocol.io/specification/2024-11-05/basic/lifecycle" },
        { Protocol.Notification, "https://spec.modelcontextprotocol.io/specification/2024-11-05/basic/json-rpc#notifications" },
        
        { Tools.ListTools, "https://spec.modelcontextprotocol.io/specification/2024-11-05/server/tools#listing-tools" },
        { Tools.CallTool, "https://spec.modelcontextprotocol.io/specification/2024-11-05/server/tools#calling-a-tool" },
        { Tools.ToolSchema, "https://spec.modelcontextprotocol.io/specification/2024-11-05/server/tools#tool-definition" },

        { Resources.ListResources, "https://spec.modelcontextprotocol.io/specification/2024-11-05/server/resources#listing-resources" },
        { Resources.ReadResource, "https://spec.modelcontextprotocol.io/specification/2024-11-05/server/resources#reading-resources" },
        { Resources.ResourceTemplates, "https://spec.modelcontextprotocol.io/specification/2024-11-05/server/resources#resource-templates" },

        { Prompts.ListPrompts, "https://spec.modelcontextprotocol.io/specification/2024-11-05/server/prompts#listing-prompts" },
        { Prompts.GetPrompt, "https://spec.modelcontextprotocol.io/specification/2024-11-05/server/prompts#getting-a-prompt" }
    }.ToFrozenDictionary();

    /// <summary>
    /// Maps check IDs to human-readable descriptions.
    /// </summary>
    public static readonly FrozenDictionary<string, string> Descriptions = new Dictionary<string, string>
    {
        { Protocol.VersionNegotiation, "Server must negotiate protocol version correctly during initialization." },
        { Protocol.Initialization, "Server must respond to 'initialize' request with valid capabilities." },
        { Protocol.JsonRpcFormat, "Messages must conform to JSON-RPC 2.0 specification." },
        { Protocol.ErrorHandling, "Errors must be returned as valid JSON-RPC error objects." },
        { Protocol.CapabilityNegotiation, "Server must declare supported capabilities in initialize response." },

        { Tools.ListTools, "Server must support 'tools/list' if tools capability is present." },
        { Tools.CallTool, "Server must support 'tools/call' for listed tools." },
        { Tools.ToolSchema, "Tool definitions must include valid JSON Schema for arguments." },

        { Resources.ListResources, "Server must support 'resources/list' if resources capability is present." },
        { Resources.ReadResource, "Server must support 'resources/read' for listed resources." },
        { Resources.ResourceTemplates, "Resource templates must follow URI template specification." },

        { Prompts.ListPrompts, "Server must support 'prompts/list' if prompts capability is present." },
        { Prompts.GetPrompt, "Server must support 'prompts/get' for listed prompts." }
    }.ToFrozenDictionary();
}
