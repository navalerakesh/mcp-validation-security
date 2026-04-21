using System.Collections.Frozen;

namespace Mcp.Benchmark.Core.Models;

public sealed record McpGuidelineRuleDefinition
{
    public required string RuleId { get; init; }

    public required string Scope { get; init; }

    public required string Title { get; init; }

    public required string Description { get; init; }

    public string? Recommendation { get; init; }
}

public static class McpGuidelineRulePack
{
    private static readonly FrozenDictionary<string, McpGuidelineRuleDefinition> Rules =
        CreateRules().ToFrozenDictionary(rule => rule.RuleId, StringComparer.OrdinalIgnoreCase);

    public static IReadOnlyCollection<McpGuidelineRuleDefinition> GetAll() => Rules.Values;

    public static McpGuidelineRuleDefinition? Find(string? ruleId)
    {
        if (string.IsNullOrWhiteSpace(ruleId))
        {
            return null;
        }

        return Rules.TryGetValue(ruleId, out var rule) ? rule : null;
    }

    private static IEnumerable<McpGuidelineRuleDefinition> CreateRules()
    {
        yield return Rule(
            ValidationFindingRuleIds.ToolGuidelineDisplayTitleMissing,
            "tool",
            "Missing tool display title",
            "Tool metadata should expose a human-friendly title so clients can render safer, clearer tool choices.",
            "Add title or annotations.title with a concise human-readable label.");

        yield return Rule(
            ValidationFindingRuleIds.ToolGuidelineReadOnlyHintMissing,
            "tool",
            "Missing read-only hint",
            "Tools should declare whether execution is read-only so agents can distinguish safe inspection from mutation.",
            "Declare annotations.readOnlyHint based on actual tool behavior.");

        yield return Rule(
            ValidationFindingRuleIds.ToolGuidelineDestructiveHintMissing,
            "tool",
            "Missing destructive hint",
            "Tools should declare destructive behavior so clients can request confirmation before execution.",
            "Declare annotations.destructiveHint based on whether the tool can delete, overwrite, or mutate state.");

        yield return Rule(
            ValidationFindingRuleIds.ToolGuidelineOpenWorldHintMissing,
            "tool",
            "Missing open-world hint",
            "Tools should declare whether they can affect unknown external systems so agents can plan safely.",
            "Declare annotations.openWorldHint for tools that can touch systems outside the local request boundary.");

        yield return Rule(
            ValidationFindingRuleIds.ToolGuidelineIdempotentHintMissing,
            "tool",
            "Missing idempotent hint",
            "Tools should declare idempotency so clients can reason about retries and duplicate execution.",
            "Declare annotations.idempotentHint when repeated execution is or is not safe.");

        yield return Rule(
            ValidationFindingRuleIds.ToolGuidelineHintConflict,
            "tool",
            "Conflicting tool hints",
            "Tool annotations should present one internally consistent safety model.",
            "Resolve conflicting annotations so read-only and destructive signals agree with each other.");

        yield return Rule(
            ValidationFindingRuleIds.ToolListPaginationRecommended,
            "tool",
            "Pagination recommended for large tool catalogs",
            "Large tools/list payloads should be paginated so clients can discover tools predictably without oversized responses.",
            "Support cursor-based tools/list pagination for large catalogs.");

        yield return Rule(
            ValidationFindingRuleIds.ResourceGuidelineMimeTypeMissing,
            "resource",
            "Missing resource MIME type",
            "Resources should declare MIME types so clients can render and route content safely.",
            "Populate mimeType for every resource entry when the content type is known.");

        yield return Rule(
            ValidationFindingRuleIds.ResourceTemplateNameMissing,
            "resource-template",
            "Missing resource template name",
            "Resource templates should expose a stable name so clients can present reusable parameterized resources clearly.",
            "Add a concise template name that matches the template purpose.");

        yield return Rule(
            ValidationFindingRuleIds.ResourceTemplateDescriptionMissing,
            "resource-template",
            "Missing resource template description",
            "Parameterized templates should describe expected inputs and behavior so clients can safely construct URIs.",
            "Add a description that explains the template parameters and returned content.");

        yield return Rule(
            ValidationFindingRuleIds.PromptGuidelineDescriptionMissing,
            "prompt",
            "Missing prompt description",
            "Prompts should describe their intent so clients can present and select them accurately.",
            "Add a clear description describing when the prompt should be used.");

        yield return Rule(
            ValidationFindingRuleIds.PromptGuidelineArgumentDescriptionMissing,
            "prompt",
            "Missing prompt argument description",
            "Prompt arguments should explain expected values so clients can collect safe input.",
            "Add descriptions for every prompt argument.");

        yield return Rule(
            ValidationFindingRuleIds.OptionalCapabilityRootsSupported,
            "protocol",
            "Optional roots support detected",
            "The server responded to roots/list, indicating optional roots workflow support.",
            "Keep optional capability reporting stable so clients can rely on it.");

        yield return Rule(
            ValidationFindingRuleIds.OptionalCapabilityLoggingSupported,
            "protocol",
            "Optional logging support detected",
            "The server responded to logging/setLevel, indicating optional logging controls are available.",
            "Advertise logging support consistently in initialize and keep logging/setLevel behavior stable.");

        yield return Rule(
            ValidationFindingRuleIds.OptionalCapabilitySamplingSupported,
            "protocol",
            "Optional sampling support detected",
            "The server responded to sampling/createMessage, indicating optional sampling workflows are available.",
            "Keep sampling behavior explicit and document any user-approval expectations.");

        yield return Rule(
            ValidationFindingRuleIds.OptionalCapabilityCompletionsSupported,
            "protocol",
            "Optional completions support detected",
            "The server responded to completion/complete, indicating optional completion workflows are available.",
            "Advertise completions support consistently in initialize and keep completion contracts stable.");

        yield return Rule(
            ValidationFindingRuleIds.OptionalCapabilityLoggingDeclaredButUnsupported,
            "protocol",
            "Logging declared but unsupported",
            "If logging is advertised during initialize, logging/setLevel should be callable or cleanly supported.",
            "Either implement logging/setLevel or stop advertising the logging capability.");

        yield return Rule(
            ValidationFindingRuleIds.OptionalCapabilityLoggingSupportedButUndeclared,
            "protocol",
            "Logging supported but undeclared",
            "Optional capabilities should be advertised during initialize so clients can discover them without speculative probing.",
            "Declare logging in initialize when logging/setLevel is supported.");

        yield return Rule(
            ValidationFindingRuleIds.OptionalCapabilityCompletionsDeclaredButUnsupported,
            "protocol",
            "Completions declared but unsupported",
            "If completions are advertised during initialize, completion/complete should be callable or cleanly supported.",
            "Either implement completion/complete or stop advertising the completions capability.");

        yield return Rule(
            ValidationFindingRuleIds.OptionalCapabilityCompletionsSupportedButUndeclared,
            "protocol",
            "Completions supported but undeclared",
            "Optional capabilities should be advertised during initialize so clients can discover them without speculative probing.",
            "Declare completions in initialize when completion/complete is supported.");
    }

    private static McpGuidelineRuleDefinition Rule(
        string ruleId,
        string scope,
        string title,
        string description,
        string recommendation)
    {
        return new McpGuidelineRuleDefinition
        {
            RuleId = ruleId,
            Scope = scope,
            Title = title,
            Description = description,
            Recommendation = recommendation
        };
    }
}