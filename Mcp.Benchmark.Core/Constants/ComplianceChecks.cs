using System.Collections.Frozen;

namespace Mcp.Benchmark.Core.Constants;

/// <summary>
/// Defines stable check IDs and references for MCP compliance validation.
/// </summary>
public static class ComplianceChecks
{
    private const string SpecBaseUrl = "https://spec.modelcontextprotocol.io/specification/2025-11-25";

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
        { Protocol.VersionNegotiation, $"{SpecBaseUrl}/basic/lifecycle#initialization" },
        { Protocol.Initialization, $"{SpecBaseUrl}/basic/lifecycle#initialization" },
        { Protocol.JsonRpcFormat, $"{SpecBaseUrl}/basic/json-rpc" },
        { Protocol.ErrorHandling, $"{SpecBaseUrl}/basic/json-rpc#errors" },
        { Protocol.CapabilityNegotiation, $"{SpecBaseUrl}/basic/capabilities" },
        { Protocol.Lifecycle, $"{SpecBaseUrl}/basic/lifecycle" },
        { Protocol.Notification, $"{SpecBaseUrl}/basic/json-rpc#notifications" },
        
        { Tools.ListTools, $"{SpecBaseUrl}/server/tools#listing-tools" },
        { Tools.CallTool, $"{SpecBaseUrl}/server/tools#calling-a-tool" },
        { Tools.ToolSchema, $"{SpecBaseUrl}/server/tools#tool-definition" },

        { Resources.ListResources, $"{SpecBaseUrl}/server/resources#listing-resources" },
        { Resources.ReadResource, $"{SpecBaseUrl}/server/resources#reading-resources" },
        { Resources.ResourceTemplates, $"{SpecBaseUrl}/server/resources#resource-templates" },

        { Prompts.ListPrompts, $"{SpecBaseUrl}/server/prompts#listing-prompts" },
        { Prompts.GetPrompt, $"{SpecBaseUrl}/server/prompts#getting-a-prompt" }
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
