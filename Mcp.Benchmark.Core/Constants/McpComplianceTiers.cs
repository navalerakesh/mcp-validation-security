namespace Mcp.Benchmark.Core.Constants;

/// <summary>
/// MCP Compliance Tiers based on RFC 2119 language used in the MCP specification.
/// Every validation check is classified into one of these tiers.
/// 
/// MUST — Hard compliance gate. Failure = non-compliant. Score capped.
/// SHOULD — Expected behavior. Absence is a weighted penalty.
/// MAY — Optional feature. Absence is informational only (no penalty).
/// </summary>
public static class McpComplianceTiers
{
    // ─── MUST: Hard Compliance Gates ─────────────────────────────────
    // Failure in ANY of these = server is NON-COMPLIANT per MCP spec.
    // Trust level capped at L2 (Caution) or lower.

    public static class Must
    {
        // Protocol
        public const string InitializeResponse = "MUST: Server responds to 'initialize' with valid JSON-RPC";
        public const string ProtocolVersionInResponse = "MUST: Initialize response includes 'protocolVersion'";
        public const string CapabilitiesInResponse = "MUST: Initialize response includes 'capabilities' object";
        public const string ServerInfoPresent = "MUST: Initialize response includes 'serverInfo'";
        public const string ServerInfoHasName = "MUST: serverInfo includes 'name'";
        public const string ServerInfoHasVersion = "MUST: serverInfo includes 'version'";
        public const string JsonRpc20Format = "MUST: All messages follow JSON-RPC 2.0 format";
        public const string NotificationNoResponse = "MUST: Server does NOT respond to notifications";

        // Tools
        public const string ToolsListReturnsArray = "MUST: tools/list result has 'tools' array";
        public const string ToolHasName = "MUST: Each tool has 'name' field";
        public const string ToolCallReturnsContent = "MUST: tools/call result has 'content' array";
        public const string ContentHasType = "MUST: Each content item has 'type' field";

        // Resources
        public const string ResourcesListReturnsArray = "MUST: resources/list result has 'resources' array";
        public const string ResourceHasUri = "MUST: Each resource has 'uri' field";
        public const string ResourceHasName = "MUST: Each resource has 'name' field";
        public const string ResourceReadReturnsContents = "MUST: resources/read result has 'contents' array";
        public const string ResourceContentHasUri = "MUST: Each content in read result has 'uri'";
        public const string ResourceContentHasTextOrBlob = "MUST: Each content has 'text' or 'blob'";

        // Prompts
        public const string PromptsListReturnsArray = "MUST: prompts/list result has 'prompts' array";
        public const string PromptHasName = "MUST: Each prompt has 'name' field";
        public const string PromptsGetReturnsMessages = "MUST: prompts/get result has 'messages' array";
        public const string MessageHasRole = "MUST: Each message has 'role' (user|assistant)";
        public const string MessageHasContent = "MUST: Each message has 'content'";

        // Security
        public const string ValidateAllInputs = "MUST: Server validates all tool inputs";
        public const string ValidateResourceUris = "MUST: Server validates all resource URIs";
        public const string ProperAccessControl = "MUST: Access controls implemented for sensitive resources";
        public const string BinaryDataEncoded = "MUST: Binary data properly encoded (base64)";
        public const string ValidatePromptInputs = "MUST: Server validates all prompt inputs/outputs";

        // Error Handling
        public const string StandardErrorCodes = "MUST: Use standard JSON-RPC error codes";
    }

    // ─── SHOULD: Expected Behavior (Weighted Penalty) ────────────────
    // Absence reduces score but doesn't fail compliance.

    public static class Should
    {
        // Protocol
        public const string WwwAuthenticateHeader = "SHOULD: Auth rejection includes WWW-Authenticate header (RFC 9110)";
        public const string ValidateArgumentsBeforeProcessing = "SHOULD: Servers validate prompt arguments before processing";

        // Tools
        public const string ToolHasDescription = "SHOULD: Each tool has 'description' for LLM consumption";
        public const string ToolHasInputSchema = "SHOULD: Each tool has 'inputSchema' (JSON Schema)";
        public const string IsErrorFieldPresent = "SHOULD: tools/call result includes 'isError' field";
        public const string HumanInLoopForTools = "SHOULD: Human confirmation for sensitive tool operations";

        // Resources
        public const string ResourceHasDescription = "SHOULD: Each resource has 'description'";
        public const string ResourceHasMimeType = "SHOULD: Each resource has 'mimeType'";

        // Prompts
        public const string PromptHasDescription = "SHOULD: Each prompt has 'description'";
        public const string HandlePaginationForLargeLists = "SHOULD: Clients handle pagination for large prompt lists";

        // Performance
        public const string RateLimitToolInvocations = "SHOULD: Server rate-limits tool invocations";
        public const string SanitizeToolOutputs = "SHOULD: Server sanitizes tool outputs";

        // AI Safety (SHOULD — impacts LLM reasoning quality)
        public const string DescriptiveParameterTypes = "SHOULD: String parameters have enum/pattern/format constraints";
        public const string TokenEfficiency = "SHOULD: tools/list payload within context window limits";
    }

    // ─── MAY: Optional Features (Informational Only) ─────────────────
    // Absence is noted but has ZERO scoring impact.

    public static class May
    {
        // Protocol (informational — doesn't affect AI agent behavior)
        public const string InstructionsField = "MAY: Initialize response includes 'instructions'";
        public const string BatchProcessing = "MAY: Server supports batch JSON-RPC processing (optimization, not correctness)";
        public const string ContentTypeEnforcement = "MAY: Server enforces Content-Type header (JSON parses regardless)";
        public const string CaseSensitivityEnforcement = "MAY: Server enforces case sensitivity (JSON parsers handle this)";
        public const string ToolAnnotations = "MAY: Tools include 'annotations' (readOnlyHint, destructiveHint)";

        // Resources
        public const string ResourceSubscriptions = "MAY: Server supports resources/subscribe";
        public const string ResourceTemplates = "MAY: Server supports resources/templates/list";
        public const string ResourceListChanged = "MAY: Server emits notifications/resources/list_changed";

        // Prompts
        public const string PromptListChanged = "MAY: Server emits notifications/prompts/list_changed";

        // Tools
        public const string ToolListChanged = "MAY: Server emits notifications/tools/list_changed";

        // Capabilities
        public const string Logging = "MAY: Server supports logging/setLevel";
        public const string Sampling = "MAY: Server supports sampling/createMessage";
        public const string Completion = "MAY: Server supports completion/complete";
        public const string Roots = "MAY: Server supports roots/list";
    }
}
