namespace Mcp.Benchmark.Core.Models;

public class ValidationFinding
{
    public string RuleId { get; set; } = string.Empty;

    public string Category { get; set; } = string.Empty;

    public string Component { get; set; } = string.Empty;

    public ValidationFindingSeverity Severity { get; set; } = ValidationFindingSeverity.Medium;

    public string Summary { get; set; } = string.Empty;

    public string? Recommendation { get; set; }

    public Dictionary<string, string> Metadata { get; set; } = new();
}

public enum ValidationFindingSeverity
{
    Info = 0,
    Low = 1,
    Medium = 2,
    High = 3,
    Critical = 4
}

public static class ValidationFindingRuleIds
{
    public const string ToolGuidelineDisplayTitleMissing = "MCP.GUIDELINE.TOOL.DISPLAY_TITLE_MISSING";
    public const string ToolGuidelineReadOnlyHintMissing = "MCP.GUIDELINE.TOOL.READONLY_HINT_MISSING";
    public const string ToolGuidelineDestructiveHintMissing = "MCP.GUIDELINE.TOOL.DESTRUCTIVE_HINT_MISSING";
    public const string ToolGuidelineOpenWorldHintMissing = "MCP.GUIDELINE.TOOL.OPENWORLD_HINT_MISSING";
    public const string ToolGuidelineIdempotentHintMissing = "MCP.GUIDELINE.TOOL.IDEMPOTENT_HINT_MISSING";
    public const string ToolGuidelineHintConflict = "MCP.GUIDELINE.TOOL.HINT_CONFLICT";

    public const string ToolCallMissingResultObject = "MCP.TOOL.CALL.RESULT_OBJECT_MISSING";
    public const string ToolCallMissingContentArray = "MCP.TOOL.CALL.CONTENT_ARRAY_MISSING";
    public const string ToolCallContentNotArray = "MCP.TOOL.CALL.CONTENT_NOT_ARRAY";
    public const string ToolCallContentMissingType = "MCP.TOOL.CALL.CONTENT_TYPE_MISSING";
    public const string ToolCallContentInvalidType = "MCP.TOOL.CALL.CONTENT_TYPE_INVALID";
    public const string ToolCallContentMissingText = "MCP.TOOL.CALL.TEXT_CONTENT_TEXT_MISSING";
    public const string ToolCallContentMissingImageData = "MCP.TOOL.CALL.IMAGE_CONTENT_FIELDS_MISSING";
    public const string ToolCallContentMissingAudioData = "MCP.TOOL.CALL.AUDIO_CONTENT_FIELDS_MISSING";
    public const string ToolCallContentMissingResource = "MCP.TOOL.CALL.RESOURCE_CONTENT_MISSING";
    public const string ToolCallMissingIsError = "MCP.TOOL.CALL.ISERROR_MISSING";
    public const string ToolCallResponseValidationFailed = "MCP.TOOL.CALL.RESPONSE_VALIDATION_FAILED";
    public const string ToolLlmFriendliness = "AI.TOOL.ERROR.LLM_FRIENDLINESS";
    public const string AiReadinessMissingParameterDescriptions = "AI.TOOL.SCHEMA.PARAMETER_DESCRIPTION_MISSING";
    public const string AiReadinessVagueStringSchema = "AI.TOOL.SCHEMA.STRING_CONSTRAINT_MISSING";
    public const string AiReadinessTokenBudgetExceeded = "AI.TOOL.SCHEMA.TOKEN_BUDGET_EXCEEDED";
    public const string AiReadinessTokenBudgetWarning = "AI.TOOL.SCHEMA.TOKEN_BUDGET_WARNING";
    public const string ResourceMissingUri = "MCP.RESOURCE.LIST.URI_MISSING";
    public const string ResourceMissingName = "MCP.RESOURCE.LIST.NAME_MISSING";
    public const string ResourceReadMissingContentArray = "MCP.RESOURCE.READ.CONTENT_ARRAY_MISSING";
    public const string ResourceReadMissingContentUri = "MCP.RESOURCE.READ.CONTENT_URI_MISSING";
    public const string ResourceReadMissingTextOrBlob = "MCP.RESOURCE.READ.CONTENT_PAYLOAD_MISSING";
    public const string PromptMissingName = "MCP.PROMPT.LIST.NAME_MISSING";
    public const string PromptArgumentsNotArray = "MCP.PROMPT.LIST.ARGUMENTS_NOT_ARRAY";
    public const string PromptArgumentMissingName = "MCP.PROMPT.LIST.ARGUMENT_NAME_MISSING";
    public const string PromptGetMissingMessagesArray = "MCP.PROMPT.GET.MESSAGES_MISSING";
    public const string PromptMessageMissingRole = "MCP.PROMPT.GET.MESSAGE_ROLE_MISSING";
    public const string PromptMessageInvalidRole = "MCP.PROMPT.GET.MESSAGE_ROLE_INVALID";
    public const string PromptMessageMissingContent = "MCP.PROMPT.GET.MESSAGE_CONTENT_MISSING";
    public const string PromptContentMissingType = "MCP.PROMPT.GET.CONTENT_TYPE_MISSING";
}