namespace Mcp.Benchmark.Core.Models;

public class ValidationFinding
{
    public string RuleId { get; set; } = string.Empty;

    public string Category { get; set; } = string.Empty;

    public string Component { get; set; } = string.Empty;

    public ValidationFindingSeverity Severity { get; set; } = ValidationFindingSeverity.Medium;

    public string Summary { get; set; } = string.Empty;

    public string? Recommendation { get; set; }

    public ValidationRuleSource Source { get; set; } = ValidationRuleSource.Unspecified;

    public string? SpecReference { get; set; }

    public Dictionary<string, string> Metadata { get; set; } = new();

    public ValidationRuleSource EffectiveSource => ValidationRuleSourceClassifier.GetSource(this);

    public string EffectiveSourceLabel => ValidationRuleSourceClassifier.GetLabel(this);

    public string? EffectiveSpecReference => ValidationRuleSourceClassifier.GetSpecReference(this);
}

public enum ValidationRuleSource
{
    Unspecified = 0,
    Spec = 1,
    Guideline = 2,
    Heuristic = 3
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
    public const string PerformanceAuthRequiredAdvisory = "MCP.GUIDELINE.PERFORMANCE.AUTH_REQUIRED_ADVISORY";
    public const string PerformanceRecalibratedAfterTransientLimits = "MCP.GUIDELINE.PERFORMANCE.RECALIBRATED_AFTER_TRANSIENT_LIMITS";
    public const string PerformancePublicRemoteAdvisory = "MCP.GUIDELINE.PERFORMANCE.PUBLIC_REMOTE_ADVISORY";
    public const string PerformancePublicRemoteTimeoutAdvisory = "MCP.GUIDELINE.PERFORMANCE.PUBLIC_REMOTE_TIMEOUT_ADVISORY";
    public const string PerformancePublicRemoteRampUp = "MCP.GUIDELINE.PERFORMANCE.PUBLIC_REMOTE_RAMP_UP";
    public const string PerformancePressureSignalsObserved = "MCP.GUIDELINE.PERFORMANCE.PRESSURE_SIGNALS_OBSERVED";

    public const string ToolGuidelineDisplayTitleMissing = "MCP.GUIDELINE.TOOL.DISPLAY_TITLE_MISSING";
    public const string ToolGuidelineReadOnlyHintMissing = "MCP.GUIDELINE.TOOL.READONLY_HINT_MISSING";
    public const string ToolGuidelineDestructiveHintMissing = "MCP.GUIDELINE.TOOL.DESTRUCTIVE_HINT_MISSING";
    public const string ToolGuidelineOpenWorldHintMissing = "MCP.GUIDELINE.TOOL.OPENWORLD_HINT_MISSING";
    public const string ToolGuidelineIdempotentHintMissing = "MCP.GUIDELINE.TOOL.IDEMPOTENT_HINT_MISSING";
    public const string ToolGuidelineHintConflict = "MCP.GUIDELINE.TOOL.HINT_CONFLICT";
    public const string ToolListPaginationRecommended = "MCP.GUIDELINE.TOOL.PAGINATION_RECOMMENDED";
    public const string ToolListCursorLoopDetected = "AI.TOOL.PAGINATION.CURSOR_LOOP_DETECTED";
    public const string ToolDestructiveConfirmationGuidanceMissing = "AI.TOOL.SAFETY.CONFIRMATION_GUIDANCE_MISSING";
    public const string ToolNameInvalid = "MCP.TOOL.LIST.NAME_INVALID";
    public const string ToolInputSchemaMissing = "MCP.TOOL.LIST.INPUT_SCHEMA_MISSING";
    public const string ToolInputSchemaInvalid = "MCP.TOOL.LIST.INPUT_SCHEMA_INVALID";
    public const string ToolInputSchemaRootTypeInvalid = "MCP.TOOL.LIST.INPUT_SCHEMA_ROOT_TYPE_INVALID";
    public const string ToolOutputSchemaInvalid = "MCP.TOOL.LIST.OUTPUT_SCHEMA_INVALID";
    public const string ToolOutputSchemaRootTypeInvalid = "MCP.TOOL.LIST.OUTPUT_SCHEMA_ROOT_TYPE_INVALID";
    public const string ToolIconInvalid = "MCP.TOOL.LIST.ICON_INVALID";
    public const string ToolExecutionTaskSupportInvalid = "MCP.TOOL.LIST.EXECUTION_TASK_SUPPORT_INVALID";

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
    public const string ToolCallStructuredContentMissing = "MCP.TOOL.CALL.STRUCTURED_CONTENT_MISSING";
    public const string ToolCallStructuredContentInvalid = "MCP.TOOL.CALL.STRUCTURED_CONTENT_INVALID";
    public const string ToolCallStructuredContentSchemaMismatch = "MCP.TOOL.CALL.STRUCTURED_CONTENT_SCHEMA_MISMATCH";
    public const string ToolLlmFriendliness = "AI.TOOL.ERROR.LLM_FRIENDLINESS";
    public const string AiReadinessMissingParameterDescriptions = "AI.TOOL.SCHEMA.PARAMETER_DESCRIPTION_MISSING";
    public const string AiReadinessVagueStringSchema = "AI.TOOL.SCHEMA.STRING_CONSTRAINT_MISSING";
    public const string AiReadinessRequiredArraySchema = "AI.TOOL.SCHEMA.REQUIRED_ARRAY_SHAPE_MISSING";
    public const string AiReadinessEnumCoverageMissing = "AI.TOOL.SCHEMA.ENUM_COVERAGE_MISSING";
    public const string AiReadinessFormatHintMissing = "AI.TOOL.SCHEMA.FORMAT_HINT_MISSING";
    public const string AiReadinessTokenBudgetExceeded = "AI.TOOL.SCHEMA.TOKEN_BUDGET_EXCEEDED";
    public const string AiReadinessTokenBudgetWarning = "AI.TOOL.SCHEMA.TOKEN_BUDGET_WARNING";
    public const string ResourceMissingUri = "MCP.RESOURCE.LIST.URI_MISSING";
    public const string ResourceMissingName = "MCP.RESOURCE.LIST.NAME_MISSING";
    public const string ResourceGuidelineMimeTypeMissing = "MCP.GUIDELINE.RESOURCE.MIMETYPE_MISSING";
    public const string ResourceUriSchemeUnclear = "AI.RESOURCE.URI_SCHEME_UNCLEAR";
    public const string ResourceReadMissingContentArray = "MCP.RESOURCE.READ.CONTENT_ARRAY_MISSING";
    public const string ResourceReadMissingContentUri = "MCP.RESOURCE.READ.CONTENT_URI_MISSING";
    public const string ResourceReadMissingTextOrBlob = "MCP.RESOURCE.READ.CONTENT_PAYLOAD_MISSING";
    public const string ResourceTemplateMissingUriTemplate = "MCP.RESOURCE_TEMPLATE.URITEMPLATE_MISSING";
    public const string ResourceTemplateNameMissing = "MCP.GUIDELINE.RESOURCE_TEMPLATE.NAME_MISSING";
    public const string ResourceTemplateDescriptionMissing = "MCP.GUIDELINE.RESOURCE_TEMPLATE.DESCRIPTION_MISSING";
    public const string PromptGuidelineDescriptionMissing = "MCP.GUIDELINE.PROMPT.DESCRIPTION_MISSING";
    public const string PromptGuidelineArgumentDescriptionMissing = "MCP.GUIDELINE.PROMPT.ARGUMENT_DESCRIPTION_MISSING";
    public const string PromptArgumentComplexityGuidanceMissing = "AI.PROMPT.ARGUMENTS.COMPLEXITY_GUIDANCE_MISSING";
    public const string PromptSafetyGuidanceMissing = "AI.PROMPT.SAFETY.CONFIRMATION_GUIDANCE_MISSING";
    public const string OptionalCapabilityRootsSupported = "MCP.GUIDELINE.CAPABILITY.ROOTS_SUPPORTED";
    public const string OptionalCapabilityLoggingSupported = "MCP.GUIDELINE.CAPABILITY.LOGGING_SUPPORTED";
    public const string OptionalCapabilitySamplingSupported = "MCP.GUIDELINE.CAPABILITY.SAMPLING_SUPPORTED";
    public const string OptionalCapabilityCompletionsSupported = "MCP.GUIDELINE.CAPABILITY.COMPLETIONS_SUPPORTED";
    public const string OptionalCapabilityLoggingDeclaredButUnsupported = "MCP.GUIDELINE.CAPABILITY.LOGGING_DECLARED_BUT_UNSUPPORTED";
    public const string OptionalCapabilityLoggingSupportedButUndeclared = "MCP.GUIDELINE.CAPABILITY.LOGGING_SUPPORTED_BUT_UNDECLARED";
    public const string OptionalCapabilityCompletionsDeclaredButUnsupported = "MCP.GUIDELINE.CAPABILITY.COMPLETIONS_DECLARED_BUT_UNSUPPORTED";
    public const string OptionalCapabilityCompletionsSupportedButUndeclared = "MCP.GUIDELINE.CAPABILITY.COMPLETIONS_SUPPORTED_BUT_UNDECLARED";
    public const string PromptMissingName = "MCP.PROMPT.LIST.NAME_MISSING";
    public const string PromptArgumentsNotArray = "MCP.PROMPT.LIST.ARGUMENTS_NOT_ARRAY";
    public const string PromptArgumentMissingName = "MCP.PROMPT.LIST.ARGUMENT_NAME_MISSING";
    public const string PromptGetMissingMessagesArray = "MCP.PROMPT.GET.MESSAGES_MISSING";
    public const string PromptMessageMissingRole = "MCP.PROMPT.GET.MESSAGE_ROLE_MISSING";
    public const string PromptMessageInvalidRole = "MCP.PROMPT.GET.MESSAGE_ROLE_INVALID";
    public const string PromptMessageMissingContent = "MCP.PROMPT.GET.MESSAGE_CONTENT_MISSING";
    public const string PromptContentMissingType = "MCP.PROMPT.GET.CONTENT_TYPE_MISSING";
}

public sealed class ValidationRuleDescriptor
{
    public required string RuleId { get; init; }

    public required ValidationRuleSource Source { get; init; }

    public string? SpecReference { get; init; }
}

public static class ValidationRuleCatalog
{
    private static readonly Dictionary<string, ValidationRuleDescriptor> Descriptors = BuildDescriptors();

    public static ValidationRuleDescriptor? Find(string? ruleId)
    {
        if (string.IsNullOrWhiteSpace(ruleId))
        {
            return null;
        }

        return Descriptors.TryGetValue(ruleId, out var descriptor) ? descriptor : null;
    }

    private static Dictionary<string, ValidationRuleDescriptor> BuildDescriptors()
    {
        var descriptors = new Dictionary<string, ValidationRuleDescriptor>(StringComparer.OrdinalIgnoreCase);

        foreach (var rule in McpGuidelineRulePack.GetAll())
        {
            descriptors[rule.RuleId] = GuidelineRule(rule.RuleId);
        }

        descriptors[ValidationFindingRuleIds.ToolListCursorLoopDetected] = HeuristicRule(ValidationFindingRuleIds.ToolListCursorLoopDetected);
        descriptors[ValidationFindingRuleIds.ToolDestructiveConfirmationGuidanceMissing] = HeuristicRule(ValidationFindingRuleIds.ToolDestructiveConfirmationGuidanceMissing);

        descriptors[ValidationFindingRuleIds.ToolNameInvalid] = SpecRule(ValidationFindingRuleIds.ToolNameInvalid);
        descriptors[ValidationFindingRuleIds.ToolInputSchemaMissing] = SpecRule(ValidationFindingRuleIds.ToolInputSchemaMissing);
        descriptors[ValidationFindingRuleIds.ToolInputSchemaInvalid] = SpecRule(ValidationFindingRuleIds.ToolInputSchemaInvalid);
        descriptors[ValidationFindingRuleIds.ToolInputSchemaRootTypeInvalid] = SpecRule(ValidationFindingRuleIds.ToolInputSchemaRootTypeInvalid);
        descriptors[ValidationFindingRuleIds.ToolOutputSchemaInvalid] = SpecRule(ValidationFindingRuleIds.ToolOutputSchemaInvalid);
        descriptors[ValidationFindingRuleIds.ToolOutputSchemaRootTypeInvalid] = SpecRule(ValidationFindingRuleIds.ToolOutputSchemaRootTypeInvalid);
        descriptors[ValidationFindingRuleIds.ToolIconInvalid] = SpecRule(ValidationFindingRuleIds.ToolIconInvalid);
        descriptors[ValidationFindingRuleIds.ToolExecutionTaskSupportInvalid] = SpecRule(ValidationFindingRuleIds.ToolExecutionTaskSupportInvalid);

        descriptors[ValidationFindingRuleIds.ToolCallMissingResultObject] = SpecRule(ValidationFindingRuleIds.ToolCallMissingResultObject);
        descriptors[ValidationFindingRuleIds.ToolCallMissingContentArray] = SpecRule(ValidationFindingRuleIds.ToolCallMissingContentArray);
        descriptors[ValidationFindingRuleIds.ToolCallContentNotArray] = SpecRule(ValidationFindingRuleIds.ToolCallContentNotArray);
        descriptors[ValidationFindingRuleIds.ToolCallContentMissingType] = SpecRule(ValidationFindingRuleIds.ToolCallContentMissingType);
        descriptors[ValidationFindingRuleIds.ToolCallContentInvalidType] = SpecRule(ValidationFindingRuleIds.ToolCallContentInvalidType);
        descriptors[ValidationFindingRuleIds.ToolCallContentMissingText] = SpecRule(ValidationFindingRuleIds.ToolCallContentMissingText);
        descriptors[ValidationFindingRuleIds.ToolCallContentMissingImageData] = SpecRule(ValidationFindingRuleIds.ToolCallContentMissingImageData);
        descriptors[ValidationFindingRuleIds.ToolCallContentMissingAudioData] = SpecRule(ValidationFindingRuleIds.ToolCallContentMissingAudioData);
        descriptors[ValidationFindingRuleIds.ToolCallContentMissingResource] = SpecRule(ValidationFindingRuleIds.ToolCallContentMissingResource);
        descriptors[ValidationFindingRuleIds.ToolCallMissingIsError] = SpecRule(ValidationFindingRuleIds.ToolCallMissingIsError);
        descriptors[ValidationFindingRuleIds.ToolCallResponseValidationFailed] = SpecRule(ValidationFindingRuleIds.ToolCallResponseValidationFailed);
        descriptors[ValidationFindingRuleIds.ToolCallStructuredContentMissing] = SpecRule(ValidationFindingRuleIds.ToolCallStructuredContentMissing);
        descriptors[ValidationFindingRuleIds.ToolCallStructuredContentInvalid] = SpecRule(ValidationFindingRuleIds.ToolCallStructuredContentInvalid);
        descriptors[ValidationFindingRuleIds.ToolCallStructuredContentSchemaMismatch] = SpecRule(ValidationFindingRuleIds.ToolCallStructuredContentSchemaMismatch);

        descriptors[ValidationFindingRuleIds.ToolLlmFriendliness] = HeuristicRule(ValidationFindingRuleIds.ToolLlmFriendliness);
        descriptors[ValidationFindingRuleIds.AiReadinessMissingParameterDescriptions] = HeuristicRule(ValidationFindingRuleIds.AiReadinessMissingParameterDescriptions);
        descriptors[ValidationFindingRuleIds.AiReadinessVagueStringSchema] = HeuristicRule(ValidationFindingRuleIds.AiReadinessVagueStringSchema);
        descriptors[ValidationFindingRuleIds.AiReadinessRequiredArraySchema] = HeuristicRule(ValidationFindingRuleIds.AiReadinessRequiredArraySchema);
        descriptors[ValidationFindingRuleIds.AiReadinessEnumCoverageMissing] = HeuristicRule(ValidationFindingRuleIds.AiReadinessEnumCoverageMissing);
        descriptors[ValidationFindingRuleIds.AiReadinessFormatHintMissing] = HeuristicRule(ValidationFindingRuleIds.AiReadinessFormatHintMissing);
        descriptors[ValidationFindingRuleIds.AiReadinessTokenBudgetExceeded] = HeuristicRule(ValidationFindingRuleIds.AiReadinessTokenBudgetExceeded);
        descriptors[ValidationFindingRuleIds.AiReadinessTokenBudgetWarning] = HeuristicRule(ValidationFindingRuleIds.AiReadinessTokenBudgetWarning);

        descriptors[ValidationFindingRuleIds.ResourceMissingUri] = SpecRule(ValidationFindingRuleIds.ResourceMissingUri);
        descriptors[ValidationFindingRuleIds.ResourceMissingName] = SpecRule(ValidationFindingRuleIds.ResourceMissingName);
        descriptors[ValidationFindingRuleIds.ResourceUriSchemeUnclear] = HeuristicRule(ValidationFindingRuleIds.ResourceUriSchemeUnclear);
        descriptors[ValidationFindingRuleIds.ResourceReadMissingContentArray] = SpecRule(ValidationFindingRuleIds.ResourceReadMissingContentArray);
        descriptors[ValidationFindingRuleIds.ResourceReadMissingContentUri] = SpecRule(ValidationFindingRuleIds.ResourceReadMissingContentUri);
        descriptors[ValidationFindingRuleIds.ResourceReadMissingTextOrBlob] = SpecRule(ValidationFindingRuleIds.ResourceReadMissingTextOrBlob);
        descriptors[ValidationFindingRuleIds.ResourceTemplateMissingUriTemplate] = SpecRule(ValidationFindingRuleIds.ResourceTemplateMissingUriTemplate);

        descriptors[ValidationFindingRuleIds.PromptArgumentComplexityGuidanceMissing] = HeuristicRule(ValidationFindingRuleIds.PromptArgumentComplexityGuidanceMissing);
        descriptors[ValidationFindingRuleIds.PromptSafetyGuidanceMissing] = HeuristicRule(ValidationFindingRuleIds.PromptSafetyGuidanceMissing);
        descriptors[ValidationFindingRuleIds.PromptMissingName] = SpecRule(ValidationFindingRuleIds.PromptMissingName);
        descriptors[ValidationFindingRuleIds.PromptArgumentsNotArray] = SpecRule(ValidationFindingRuleIds.PromptArgumentsNotArray);
        descriptors[ValidationFindingRuleIds.PromptArgumentMissingName] = SpecRule(ValidationFindingRuleIds.PromptArgumentMissingName);
        descriptors[ValidationFindingRuleIds.PromptGetMissingMessagesArray] = SpecRule(ValidationFindingRuleIds.PromptGetMissingMessagesArray);
        descriptors[ValidationFindingRuleIds.PromptMessageMissingRole] = SpecRule(ValidationFindingRuleIds.PromptMessageMissingRole);
        descriptors[ValidationFindingRuleIds.PromptMessageInvalidRole] = SpecRule(ValidationFindingRuleIds.PromptMessageInvalidRole);
        descriptors[ValidationFindingRuleIds.PromptMessageMissingContent] = SpecRule(ValidationFindingRuleIds.PromptMessageMissingContent);
        descriptors[ValidationFindingRuleIds.PromptContentMissingType] = SpecRule(ValidationFindingRuleIds.PromptContentMissingType);

        return descriptors;
    }

    private static ValidationRuleDescriptor SpecRule(string ruleId) => new()
    {
        RuleId = ruleId,
        Source = ValidationRuleSource.Spec
    };

    private static ValidationRuleDescriptor GuidelineRule(string ruleId) => new()
    {
        RuleId = ruleId,
        Source = ValidationRuleSource.Guideline
    };

    private static ValidationRuleDescriptor HeuristicRule(string ruleId) => new()
    {
        RuleId = ruleId,
        Source = ValidationRuleSource.Heuristic
    };
}

public static class ValidationRuleSourceClassifier
{
    public static ValidationRuleSource GetSource(ValidationFinding finding)
    {
        if (finding.Source != ValidationRuleSource.Unspecified)
        {
            return finding.Source;
        }

        var descriptor = ValidationRuleCatalog.Find(finding.RuleId);
        if (descriptor != null)
        {
            return descriptor.Source;
        }

        return InferSource(finding.RuleId, finding.Category);
    }

    public static ValidationRuleSource GetSource(ComplianceViolation _) => ValidationRuleSource.Spec;

    public static ValidationRuleSource GetSource(SecurityVulnerability _) => ValidationRuleSource.Heuristic;

    public static string GetLabel(ValidationFinding finding) => GetLabel(GetSource(finding));

    public static string GetLabel(ComplianceViolation violation) => GetLabel(GetSource(violation));

    public static string GetLabel(SecurityVulnerability vulnerability) => GetLabel(GetSource(vulnerability));

    public static string? GetSpecReference(ValidationFinding finding)
    {
        if (!string.IsNullOrWhiteSpace(finding.SpecReference))
        {
            return finding.SpecReference;
        }

        if (finding.Metadata.TryGetValue("specReference", out var metadataSpecReference) &&
            !string.IsNullOrWhiteSpace(metadataSpecReference))
        {
            return metadataSpecReference;
        }

        return ValidationRuleCatalog.Find(finding.RuleId)?.SpecReference;
    }

    public static string GetLabel(ValidationRuleSource source)
    {
        return Mcp.Benchmark.Core.Services.ValidationAuthorityHierarchy.GetMachineLabel(source);
    }

    private static ValidationRuleSource InferSource(string? ruleId, string? category)
    {
        if (!string.IsNullOrWhiteSpace(ruleId))
        {
            if (ruleId.StartsWith("MCP.GUIDELINE.", StringComparison.OrdinalIgnoreCase))
            {
                return ValidationRuleSource.Guideline;
            }

            if (ruleId.StartsWith("AI.", StringComparison.OrdinalIgnoreCase))
            {
                return ValidationRuleSource.Heuristic;
            }

            if (ruleId.StartsWith("MCP.", StringComparison.OrdinalIgnoreCase))
            {
                return ValidationRuleSource.Spec;
            }
        }

        if (!string.IsNullOrWhiteSpace(category) &&
            category.Contains("guideline", StringComparison.OrdinalIgnoreCase))
        {
            return ValidationRuleSource.Guideline;
        }

        return ValidationRuleSource.Heuristic;
    }
}