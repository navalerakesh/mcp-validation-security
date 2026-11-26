using Mcp.Benchmark.Core.Abstractions;
using Mcp.Benchmark.Core.Constants;
using Mcp.Benchmark.Core.Models;
using Mcp.Benchmark.Core.Services;

namespace Mcp.Benchmark.ClientProfiles;

public sealed class BuiltInClientProfilePack : IClientProfilePack
{
    private const string MissingCatalogRecommendation =
        "Complete authorized discovery, including resource templates where supported, then rerun client interpretation. Supply the required credentials when discovery is blocked by authentication.";
    private const string ClaudeCodeToolSearchDocsUrl = "https://code.claude.com/docs/en/mcp#scale-with-mcp-tool-search";
    private const string ClaudeCodeListChangedDocsUrl = "https://code.claude.com/docs/en/mcp#dynamic-tool-updates";
    private const string ClaudeCodeOutputBudgetDocsUrl = "https://code.claude.com/docs/en/mcp#raise-the-limit-for-a-specific-tool";
    private const string ClaudeCodeOAuthDocsUrl = "https://code.claude.com/docs/en/mcp#authenticate-with-remote-mcp-servers";
    private const string VisualStudioToolLifecycleDocsUrl = "https://learn.microsoft.com/en-us/visualstudio/ide/mcp-servers?view=vs-2022#tool-lifecycle";
    private const string VisualStudioSamplingDocsUrl = "https://learn.microsoft.com/en-us/visualstudio/ide/mcp-servers?view=vs-2022#mcp-sampling";
    private const string VisualStudioToolApprovalDocsUrl = "https://learn.microsoft.com/en-us/visualstudio/ide/mcp-servers?view=vs-2022#management-of-tool-approvals";
    private const string VsCodeToolsConceptDocsUrl = "https://code.visualstudio.com/docs/copilot/concepts/tools";
    private const string VsCodeAgentToolsDocsUrl = "https://code.visualstudio.com/docs/copilot/agents/agent-tools";
    private const string VsCodeOtherCapabilitiesDocsUrl = "https://code.visualstudio.com/docs/copilot/customization/mcp-servers#_other-mcp-capabilities";
    private const string CopilotCloudAgentIntroDocsUrl = "https://docs.github.com/en/copilot/how-tos/use-copilot-agents/cloud-agent/extend-cloud-agent-with-mcp#introduction";
    private const string CopilotCliAddDocsUrl = "https://docs.github.com/en/copilot/how-tos/copilot-cli/customize-copilot/add-mcp-servers";
    private const string McpToolNamingSpecUrl = "https://modelcontextprotocol.io/specification/2025-11-25/server/tools#tool-names";

    private static readonly string[] ToolContractRuleIds =
    {
        ValidationFindingRuleIds.ToolCallMissingResultObject,
        ValidationFindingRuleIds.ToolCallMissingContentArray,
        ValidationFindingRuleIds.ToolCallContentNotArray,
        ValidationFindingRuleIds.ToolCallContentMissingType,
        ValidationFindingRuleIds.ToolCallContentInvalidType,
        ValidationFindingRuleIds.ToolCallContentMissingText,
        ValidationFindingRuleIds.ToolCallContentMissingImageData,
        ValidationFindingRuleIds.ToolCallContentMissingAudioData,
        ValidationFindingRuleIds.ToolCallContentMissingResource,
        ValidationFindingRuleIds.ToolCallResponseValidationFailed
    };

    private static readonly string[] PromptContractRuleIds =
    {
        ValidationFindingRuleIds.PromptMissingName,
        ValidationFindingRuleIds.PromptArgumentsNotArray,
        ValidationFindingRuleIds.PromptArgumentMissingName,
        ValidationFindingRuleIds.PromptGetMissingMessagesArray,
        ValidationFindingRuleIds.PromptMessageMissingRole,
        ValidationFindingRuleIds.PromptMessageInvalidRole,
        ValidationFindingRuleIds.PromptMessageMissingContent,
        ValidationFindingRuleIds.PromptContentMissingType
    };

    private static readonly string[] PromptMetadataRuleIds =
    {
        ValidationFindingRuleIds.PromptGuidelineDescriptionMissing,
        ValidationFindingRuleIds.PromptGuidelineArgumentDescriptionMissing,
        ValidationFindingRuleIds.PromptArgumentComplexityGuidanceMissing,
        ValidationFindingRuleIds.PromptSafetyGuidanceMissing
    };

    private static readonly string[] ResourceContractRuleIds =
    {
        ValidationFindingRuleIds.ResourceMissingUri,
        ValidationFindingRuleIds.ResourceMissingName,
        ValidationFindingRuleIds.ResourceReadMissingContentArray,
        ValidationFindingRuleIds.ResourceReadMissingContentUri,
        ValidationFindingRuleIds.ResourceReadMissingTextOrBlob,
        ValidationFindingRuleIds.ResourceTemplateMissingUriTemplate,
        ValidationFindingRuleIds.ResourceTemplateListItemInvalid,
        ValidationFindingRuleIds.ResourceTemplateRequiredNameMissing
    };

    private static readonly IReadOnlyDictionary<string, ClientProfileDefinition> Definitions = BuildDefinitions();

    public ValidationPackDescriptor Descriptor => new()
    {
        Key = new ValidationDescriptorKey("client-profile-pack/built-in"),
        Kind = ValidationPackKind.ClientProfilePack,
        Revision = new ValidationRevision(ClientProfileCatalog.InterpretationRevision),
        DisplayName = "Built-in Client Profile Pack",
        Stability = ValidationStability.Stable
    };

    public ValidationApplicability Applicability => new();

    public IReadOnlyList<ClientProfileDescriptor> GetProfiles()
    {
        return ClientProfileCatalog.SupportedProfiles;
    }

    public ClientProfileAssessment Evaluate(
        ClientProfileDescriptor profile,
        ValidationResult validationResult,
        ValidationApplicabilityContext applicabilityContext)
    {
        ArgumentNullException.ThrowIfNull(profile);
        ArgumentNullException.ThrowIfNull(validationResult);
        ArgumentNullException.ThrowIfNull(applicabilityContext);

        if (!Definitions.TryGetValue(profile.Id, out var definition))
        {
            throw new ArgumentException($"Unknown client profile '{profile.Id}'.", nameof(profile));
        }

        var context = new ClientProfileContext(validationResult);
        var requirements = definition.Rules
            .Select(rule => EvaluateRequirement(rule, context))
            .ToList();

        var passed = requirements.Count(requirement => requirement.Outcome == ClientProfileRequirementOutcome.Satisfied);
        var warnings = requirements.Count(requirement => requirement.Outcome == ClientProfileRequirementOutcome.Warning);
        var failed = requirements.Count(requirement => requirement.Outcome == ClientProfileRequirementOutcome.Failed);

        var status = failed > 0
            ? ClientProfileCompatibilityStatus.Incompatible
            : warnings > 0
                ? ClientProfileCompatibilityStatus.CompatibleWithWarnings
                : ClientProfileCompatibilityStatus.Compatible;

        return new ClientProfileAssessment
        {
            ProfileId = definition.Profile.Id,
            DisplayName = definition.Profile.DisplayName,
            Revision = definition.Profile.Revision,
            DocumentationUrl = definition.Profile.DocumentationUrl,
            EvidenceBasis = definition.Profile.EvidenceBasis,
            Status = status,
            Summary = BuildProfileSummary(status, passed, warnings, failed,
                requirements.Count(requirement => requirement.Level == ClientProfileRequirementLevel.Required &&
                    requirement.Outcome == ClientProfileRequirementOutcome.Warning)),
            PassedRequirements = passed,
            WarningRequirements = warnings,
            FailedRequirements = failed,
            Requirements = requirements
        };
    }

    private static ClientProfileRequirementAssessment EvaluateRequirement(ClientProfileRuleDefinition rule, ClientProfileContext context)
    {
        var observation = rule.Evaluate(context);
        if (rule.RequiresToolMetadata && !context.ToolsKnownAbsent && !context.HasCompleteToolMetadata &&
            observation.Outcome is ClientProfileRequirementOutcome.Satisfied or ClientProfileRequirementOutcome.NotApplicable)
        {
            observation = ClientProfileRuleObservation.Warning(
                "The complete tool metadata inventory was not recorded; this requirement cannot be established from a catalog count alone.",
                recommendation: "Collect complete tool metadata before relying on this compatibility requirement.");
        }

        return new ClientProfileRequirementAssessment
        {
            RequirementId = rule.Id,
            Title = rule.Title,
            Level = rule.Level,
            EvidenceBasis = rule.EvidenceBasis,
            Outcome = observation.Outcome,
            Summary = observation.Summary,
            RuleIds = observation.RuleIds.ToList(),
            ExampleComponents = observation.ExampleComponents.ToList(),
            Recommendation = observation.Recommendation,
            DocumentationUrl = rule.DocumentationUrl
        };
    }

    private static string BuildProfileSummary(ClientProfileCompatibilityStatus status, int passed, int warnings, int failed, int unverifiedRequired)
    {
        return status switch
        {
            ClientProfileCompatibilityStatus.Compatible => $"All applicable compatibility checks passed ({passed} satisfied).",
            ClientProfileCompatibilityStatus.CompatibleWithWarnings when unverifiedRequired > 0 =>
                $"{unverifiedRequired} required compatibility check(s) remain unverified; no native-client execution is established by this interpretation.",
            ClientProfileCompatibilityStatus.CompatibleWithWarnings => warnings == 1
                ? "Required compatibility checks passed; 1 advisory requirement still needs follow-up."
                : $"Required compatibility checks passed; {warnings} advisory requirements still need follow-up.",
            ClientProfileCompatibilityStatus.Incompatible => $"{failed} required compatibility check(s) failed; review the affected surfaces before relying on this client profile.",
            _ => "Compatibility outcome unavailable."
        };
    }

    private static IReadOnlyDictionary<string, ClientProfileDefinition> BuildDefinitions()
    {
        var claudeCode = ClientProfileCatalog.GetDescriptor("claude-code");
        var vscodeCopilotAgent = ClientProfileCatalog.GetDescriptor("vscode-copilot-agent");
        var githubCopilotCli = ClientProfileCatalog.GetDescriptor("github-copilot-cli");
        var githubCopilotCloudAgent = ClientProfileCatalog.GetDescriptor("github-copilot-cloud-agent");
        var visualStudioCopilot = ClientProfileCatalog.GetDescriptor("visual-studio-copilot");

        var definitions = new[]
        {
            CreateProfile(
                claudeCode,
                RequireInteractiveSurface("surfaces", claudeCode.DocumentationUrl!),
                RequireToolContract("tools-contract", claudeCode.DocumentationUrl!),
                RequirePromptContract("prompts-contract", claudeCode.DocumentationUrl!),
                RequireResourceContract("resources-contract", claudeCode.DocumentationUrl!),
                RecommendToolDescriptions("tool-descriptions", claudeCode.DocumentationUrl!, "Claude Code shows tool descriptions in /mcp and uses them to drive tool search."),
                RecommendToolDisplayTitles("tool-titles", claudeCode.DocumentationUrl!, "Claude Code prefers `title` for human-friendly tool display."),
                RecommendToolAnnotations("tool-annotations", claudeCode.DocumentationUrl!, "Claude Code uses readOnlyHint/destructiveHint to drive consent prompts and safety messaging."),
                RecommendDestructiveHintForWriteTools("destructive-hint", claudeCode.DocumentationUrl!, "Claude Code"),
                RecommendServerIdentity("server-identity", claudeCode.DocumentationUrl!, "Claude Code"),
                RecommendClaudeServerInstructions("server-instructions", ClaudeCodeToolSearchDocsUrl),
                RecommendClaudeDescriptionSizeBudget("description-size", ClaudeCodeToolSearchDocsUrl),
                ObserveClaudeOutputBudgetAnnotation("output-budget", ClaudeCodeOutputBudgetDocsUrl),
                ObserveListChangedSupport(
                    "list-changed",
                    "Dynamic tool, prompt, and resource updates are declared for Claude Code",
                    ClaudeCodeListChangedDocsUrl,
                    new[] { "tools", "prompts", "resources" },
                    "Claude Code"),
                RecommendClaudeRemoteOAuthDiscovery("oauth-discovery", ClaudeCodeOAuthDocsUrl)),
            CreateProfile(
                vscodeCopilotAgent,
                RequireInteractiveSurface("surfaces", vscodeCopilotAgent.DocumentationUrl!),
                RequireToolContract("tools-contract", vscodeCopilotAgent.DocumentationUrl!),
                RequirePromptContract("prompts-contract", vscodeCopilotAgent.DocumentationUrl!),
                RequireResourceContract("resources-contract", vscodeCopilotAgent.DocumentationUrl!),
                RecommendToolDescriptions("tool-descriptions", VsCodeAgentToolsDocsUrl, "VS Code Copilot Agent uses tool descriptions in the tool picker and chat surface."),
                RecommendToolDisplayTitles("tool-titles", VsCodeOtherCapabilitiesDocsUrl, "VS Code Copilot Agent uses `title` (or annotations.title) for chat-mode tool labels."),
                RecommendToolAnnotations("tool-annotations", VsCodeAgentToolsDocsUrl, "VS Code Copilot Agent reads tool annotations to populate its tool-approval prompt."),
                RecommendDestructiveHintForWriteTools("destructive-hint", VsCodeAgentToolsDocsUrl, "VS Code Copilot Agent"),
                RecommendServerIdentity("server-identity", vscodeCopilotAgent.DocumentationUrl!, "VS Code Copilot Agent"),
                ObserveListChangedSupport(
                    "list-changed",
                    "Tool catalog updates are declared so VS Code can refresh without a server restart",
                    VsCodeAgentToolsDocsUrl,
                    new[] { "tools" },
                    "VS Code Copilot Agent")),
            CreateProfile(
                githubCopilotCli,
                RequireToolsAvailable("tools-present", githubCopilotCli.DocumentationUrl!),
                RequireToolContract("tools-contract", githubCopilotCli.DocumentationUrl!),
                RecommendToolDescriptions("tool-descriptions", CopilotCliAddDocsUrl, "Copilot CLI uses tool descriptions when listing tools and resolving prompt intent."),
                RecommendToolDisplayTitles("tool-titles", CopilotCliAddDocsUrl, "Copilot CLI surfaces tool titles in interactive flows."),
                RecommendToolAnnotations("tool-annotations", CopilotCliAddDocsUrl, "Copilot CLI surfaces destructiveHint when displaying tools.")),
            CreateProfile(
                githubCopilotCloudAgent,
                RequireToolsAvailable("tools-present", githubCopilotCloudAgent.DocumentationUrl!),
                RequireToolContract("tools-contract", githubCopilotCloudAgent.DocumentationUrl!),
                WarnExtraPromptAndResourceSurfaces("tools-only-surface", githubCopilotCloudAgent.DocumentationUrl!),
                RequireRemoteOAuthCompatibility("remote-auth", githubCopilotCloudAgent.DocumentationUrl!),
                RecommendDestructiveHintForWriteToolsAutonomous("destructive-hint", CopilotCloudAgentIntroDocsUrl),
                RecommendToolDescriptions("tool-descriptions", CopilotCloudAgentIntroDocsUrl, "Copilot Cloud Agent invokes tools autonomously; clear descriptions reduce wrong-tool risk."),
                RecommendToolDisplayTitles("tool-titles", CopilotCloudAgentIntroDocsUrl, "Cloud agent run logs use tool titles in human-readable summaries."),
                RecommendServerIdentity("server-identity", githubCopilotCloudAgent.DocumentationUrl!, "GitHub Copilot Cloud Agent")),
            CreateProfile(
                visualStudioCopilot,
                RequireInteractiveSurface("surfaces", visualStudioCopilot.DocumentationUrl!),
                RequireToolContract("tools-contract", visualStudioCopilot.DocumentationUrl!),
                RequirePromptContract("prompts-contract", visualStudioCopilot.DocumentationUrl!),
                RequireResourceContract("resources-contract", visualStudioCopilot.DocumentationUrl!),
                RecommendToolDescriptions("tool-descriptions", VisualStudioToolLifecycleDocsUrl, "Visual Studio displays tool descriptions in the agent tool picker."),
                RecommendToolDisplayTitles("tool-titles", VisualStudioToolLifecycleDocsUrl, "Visual Studio uses `title` for tool labels in the chat UI."),
                RecommendToolAnnotations("tool-annotations", VisualStudioToolApprovalDocsUrl, "Visual Studio's tool approval flow honours destructiveHint and readOnlyHint."),
                RecommendDestructiveHintForWriteTools("destructive-hint", VisualStudioToolApprovalDocsUrl, "Visual Studio Copilot"),
                RecommendServerIdentity("server-identity", visualStudioCopilot.DocumentationUrl!, "Visual Studio Copilot"),
                ObserveListChangedSupport(
                    "list-changed",
                    "Tool updates are declared through notifications/tools/list_changed",
                    VisualStudioToolLifecycleDocsUrl,
                    new[] { "tools" },
                    "Visual Studio"),
                ObserveOptionalCapability(
                    "sampling-supported",
                    "Sampling workflows remain available when the server advertises them",
                    VisualStudioSamplingDocsUrl,
                    ValidationFindingRuleIds.OptionalCapabilitySamplingSupported,
                    "Observed protocol evidence that sampling support is available for this profile.",
                    "Sampling support was not advertised or detected, so this optional Visual Studio capability was not applicable."))
        };

        return definitions.ToDictionary(definition => definition.Profile.Id, StringComparer.OrdinalIgnoreCase);
    }

    private static ClientProfileDefinition CreateProfile(ClientProfileDescriptor profile, params ClientProfileRuleDefinition[] rules)
    {
        return new ClientProfileDefinition(profile, rules);
    }

    private static ClientProfileRuleDefinition RequireInteractiveSurface(string suffix, string documentationUrl)
    {
        return new ClientProfileRuleDefinition(
            $"interactive-{suffix}",
            "At least one interactive MCP surface is available",
            ClientProfileRequirementLevel.Required,
            ClientProfileEvidenceBasis.Documented,
            documentationUrl,
            context =>
            {
                var surfaces = context.GetSurfaceLabels();
                if (surfaces.Count > 0)
                    return ClientProfileRuleObservation.Satisfied($"Server exposes {string.Join(", ", surfaces)}.");
                return context.ToolsKnownAbsent && context.PromptsKnownAbsent && context.ResourcesKnownAbsent
                    ? ClientProfileRuleObservation.Failed("No tools, prompts, or resources were discovered for this profile.")
                    : ClientProfileRuleObservation.Warning(
                        "Discovery did not establish which interactive surfaces are available; compatibility remains unverified.",
                        recommendation: MissingCatalogRecommendation);
            });
    }

    private static ClientProfileRuleDefinition RequireToolsAvailable(string suffix, string documentationUrl)
    {
        return new ClientProfileRuleDefinition(
            $"tool-{suffix}",
            "Tool surface is available",
            ClientProfileRequirementLevel.Required,
            ClientProfileEvidenceBasis.Documented,
            documentationUrl,
            context => context.ToolCount > 0
                ? ClientProfileRuleObservation.Satisfied($"Observed {context.ToolCount} tool(s) for this client profile.")
                : context.ToolsKnownAbsent
                    ? ClientProfileRuleObservation.Failed("No tools were discovered, but this client profile currently depends on the tool surface.")
                    : ClientProfileRuleObservation.Warning(
                        "Tool discovery was not established; this client's required tool surface remains unverified.",
                        recommendation: MissingCatalogRecommendation));
    }

    private static ClientProfileRuleDefinition RequireToolContract(string suffix, string documentationUrl)
    {
        return new ClientProfileRuleDefinition(
            $"tool-{suffix}",
            "Tool call contract is structurally valid",
            ClientProfileRequirementLevel.Required,
            ClientProfileEvidenceBasis.Documented,
            documentationUrl,
            context => EvaluateContractRule(
                context,
                context.ToolCount,
                context.ToolsKnownAbsent,
                "tool",
                ToolContractRuleIds,
                context.Result.ToolValidation?.Status,
                context.HasCompletedToolCalls,
                "No tools were discovered, so tool contract validation was not applicable.",
                "Observed tools without blocking tool-call contract gaps."),
            RequiresToolMetadata: true);
    }

    private static ClientProfileRuleDefinition RequirePromptContract(string suffix, string documentationUrl)
    {
        return new ClientProfileRuleDefinition(
            $"prompt-{suffix}",
            "Prompt get/list contract is structurally valid",
            ClientProfileRequirementLevel.Required,
            ClientProfileEvidenceBasis.Documented,
            documentationUrl,
            context => EvaluateContractRule(
                context,
                context.PromptCount,
                context.PromptsKnownAbsent,
                "prompt",
                PromptContractRuleIds,
                context.Result.PromptTesting?.Status,
                context.HasCompletedPromptGets,
                "No prompts were discovered, so prompt contract validation was not applicable.",
                "Observed prompts without blocking prompt contract gaps."));
    }

    private static ClientProfileRuleDefinition RequireResourceContract(string suffix, string documentationUrl)
    {
        return new ClientProfileRuleDefinition(
            $"resource-{suffix}",
            "Resource read/list contract is structurally valid",
            ClientProfileRequirementLevel.Required,
            ClientProfileEvidenceBasis.Documented,
            documentationUrl,
            context => EvaluateContractRule(
                context,
                context.ResourceCount,
                context.ResourcesKnownAbsent,
                "resource",
                ResourceContractRuleIds,
                context.Result.ResourceTesting?.Status,
                context.HasCompletedResourceReads,
                "No resources were discovered, so resource contract validation was not applicable.",
                "Observed resources without blocking resource contract gaps."));
    }

    private static ClientProfileRuleDefinition RecommendClaudeServerInstructions(string suffix, string documentationUrl)
    {
        return new ClientProfileRuleDefinition(
            $"initialize-{suffix}",
            "Initialize instructions help Claude Code find and use the server correctly",
            ClientProfileRequirementLevel.Recommended,
            ClientProfileEvidenceBasis.Documented,
            documentationUrl,
            context =>
            {
                if (context.ToolCount <= 0)
                {
                    return ClientProfileRuleObservation.NotApplicable(
                        "No tools were discovered, so Claude Code instruction guidance was not applicable.");
                }

                if (!context.HasInitializationPayload)
                {
                    return ClientProfileRuleObservation.NotApplicable(
                        "Initialize handshake details were not captured, so Claude Code instruction guidance could not be assessed.");
                }

                return string.IsNullOrWhiteSpace(context.InitializeInstructions)
                    ? ClientProfileRuleObservation.Warning(
                        "Claude Code documentation recommends clear initialize instructions for tool search and server guidance, but initialize.instructions was missing.",
                        recommendation: "Populate initialize.instructions with concise guidance about when Claude should search and use this server.")
                    : ClientProfileRuleObservation.Satisfied(
                        "Initialize instructions were present, which aligns with Claude Code's documented server-guidance flow.");
            });
    }

    /// <summary>
    /// Claude Code documentation: "Claude Code truncates tool descriptions and server
    /// instructions at 2KB each. Keep them concise to avoid truncation, and put critical
    /// details near the start." Anything past 2,048 bytes is silently dropped, so very
    /// long descriptions stop helping the model.
    /// </summary>
    private const int ClaudeCodeDescriptionByteCeiling = 2048;

    private static ClientProfileRuleDefinition RecommendClaudeDescriptionSizeBudget(string suffix, string documentationUrl)
    {
        return new ClientProfileRuleDefinition(
            $"tool-{suffix}",
            "Tool descriptions and server instructions stay within Claude Code's 2KB truncation cap",
            ClientProfileRequirementLevel.Recommended,
            ClientProfileEvidenceBasis.Documented,
            documentationUrl,
            context =>
            {
                if (context.ToolCount <= 0 && !context.HasInitializationPayload)
                {
                    return ClientProfileRuleObservation.NotApplicable(
                        "No tools or initialize payload were captured, so Claude Code's 2KB truncation cap was not applicable.");
                }

                var oversizedTools = context.GetToolsExceedingDescriptionBytes(ClaudeCodeDescriptionByteCeiling);
                var instructionsOversize = !string.IsNullOrEmpty(context.InitializeInstructions) &&
                    System.Text.Encoding.UTF8.GetByteCount(context.InitializeInstructions) > ClaudeCodeDescriptionByteCeiling;

                if (oversizedTools.Count == 0 && !instructionsOversize)
                {
                    return ClientProfileRuleObservation.Satisfied(
                        $"All discovered tool descriptions and initialize.instructions stayed within Claude Code's documented 2KB truncation cap.");
                }

                var notes = new List<string>();
                if (oversizedTools.Count > 0)
                {
                    notes.Add($"{oversizedTools.Count}/{context.ToolCount} tool description(s) exceed 2KB");
                }
                if (instructionsOversize)
                {
                    notes.Add("initialize.instructions exceeds 2KB");
                }

                return ClientProfileRuleObservation.Warning(
                    $"Claude Code truncates tool descriptions and server instructions at 2KB; {string.Join(", ", notes)}.",
                    exampleComponents: oversizedTools.Take(3).ToList(),
                    recommendation: "Trim tool descriptions and initialize.instructions below 2,048 UTF-8 bytes; place critical disambiguation in the first sentence so it survives truncation.");
            }, RequiresToolMetadata: true);
    }

    // ─── Cross-cutting recommended checks (each profile registers individually
    //     so the documentation URL points at the client's own page) ─────────

    private static ClientProfileRuleDefinition RecommendToolDescriptions(string suffix, string documentationUrl, string clientReason)
    {
        return new ClientProfileRuleDefinition(
            $"tool-{suffix}",
            "Every tool declares a human-readable description",
            ClientProfileRequirementLevel.Recommended,
            ClientProfileEvidenceBasis.Documented,
            documentationUrl,
            context =>
            {
                if (context.ToolCount <= 0)
                {
                    return ClientProfileRuleObservation.NotApplicable("No tools were discovered, so description coverage was not applicable.");
                }
                var missing = context.GetToolsMissingDescription();
                if (missing.Count == 0)
                {
                    return ClientProfileRuleObservation.Satisfied($"All {context.ToolCount} discovered tool(s) declare a description.");
                }
                return ClientProfileRuleObservation.Warning(
                    $"{missing.Count}/{context.ToolCount} tool(s) are missing a `description`. {clientReason}",
                    exampleComponents: missing.Take(3).ToList(),
                    recommendation: "Add a one-line `description` to every tool in tools/list. Per MCP §5.1 this is SHOULD; downstream clients use it to drive tool selection.");
            }, RequiresToolMetadata: true);
    }

    private static ClientProfileRuleDefinition RecommendToolDisplayTitles(string suffix, string documentationUrl, string clientReason)
    {
        return new ClientProfileRuleDefinition(
            $"tool-{suffix}",
            "Tools declare a human-friendly `title` for display",
            ClientProfileRequirementLevel.Recommended,
            ClientProfileEvidenceBasis.Documented,
            documentationUrl,
            context =>
            {
                if (context.ToolCount <= 0)
                {
                    return ClientProfileRuleObservation.NotApplicable("No tools were discovered, so title coverage was not applicable.");
                }
                var missing = context.GetToolsMissingDisplayTitle();
                if (missing.Count == 0)
                {
                    return ClientProfileRuleObservation.Satisfied($"All {context.ToolCount} discovered tool(s) declare a display title.");
                }
                return ClientProfileRuleObservation.Warning(
                    $"{missing.Count}/{context.ToolCount} tool(s) are missing `title` (or annotations.title). {clientReason}",
                    exampleComponents: missing.Take(3).ToList(),
                    recommendation: "Add a UI-friendly `title` to each tool. Per MCP §5.1 display precedence is title → annotations.title → name; without title, clients fall back to the programmatic name.");
            }, RequiresToolMetadata: true);
    }

    private static ClientProfileRuleDefinition RecommendToolAnnotations(string suffix, string documentationUrl, string clientReason)
    {
        return new ClientProfileRuleDefinition(
            $"tool-{suffix}",
            "Tools declare behavioural annotations (readOnlyHint / destructiveHint / idempotentHint / openWorldHint)",
            ClientProfileRequirementLevel.Recommended,
            ClientProfileEvidenceBasis.Documented,
            documentationUrl,
            context =>
            {
                if (context.ToolCount <= 0)
                {
                    return ClientProfileRuleObservation.NotApplicable("No tools were discovered, so annotation coverage was not applicable.");
                }
                var missing = context.GetToolsMissingAnyAnnotation();
                if (missing.Count == 0)
                {
                    return ClientProfileRuleObservation.Satisfied($"All {context.ToolCount} discovered tool(s) declare at least one behavioural annotation.");
                }
                return ClientProfileRuleObservation.Warning(
                    $"{missing.Count}/{context.ToolCount} tool(s) declare no behavioural annotations. {clientReason}",
                    exampleComponents: missing.Take(3).ToList(),
                    recommendation: "Add `annotations` to every tool: at minimum `readOnlyHint` for read-only tools, `destructiveHint` for write-capable tools, plus `idempotentHint` and `openWorldHint` where applicable. These are hints (untrusted by spec), but every documented client uses them for safety UX.");
            }, RequiresToolMetadata: true);
    }

    private static ClientProfileRuleDefinition RecommendDestructiveHintForWriteTools(string suffix, string documentationUrl, string clientName)
    {
        return new ClientProfileRuleDefinition(
            $"tool-{suffix}",
            "Write-capable tools explicitly declare destructiveHint",
            ClientProfileRequirementLevel.Recommended,
            ClientProfileEvidenceBasis.Documented,
            documentationUrl,
            context =>
            {
                if (context.ToolCount <= 0)
                {
                    return ClientProfileRuleObservation.NotApplicable("No tools were discovered, so destructiveHint coverage was not applicable.");
                }
                var missing = context.GetWriteCapableToolsWithoutDestructiveHint();
                if (missing.Count == 0)
                {
                    return ClientProfileRuleObservation.Satisfied("Every non-read-only tool declares an explicit destructiveHint.");
                }
                return ClientProfileRuleObservation.Warning(
                    $"{missing.Count}/{context.ToolCount} write-capable tool(s) do not declare an explicit destructiveHint. {clientName} surfaces this hint in its consent flow.",
                    exampleComponents: missing.Take(3).ToList(),
                    recommendation: "For each tool that mutates external state, set `annotations.destructiveHint: true` (or `destructiveHint: false` if the operation is reversible/additive only). The MCP spec defaults destructiveHint to true when readOnlyHint is false, but explicit declaration produces accurate consent UI in every documented client.");
            }, RequiresToolMetadata: true);
    }

    /// <summary>
    /// GitHub Copilot Cloud Agent calls tools autonomously with no per-call human approval
    /// ("Once you've configured an MCP server, Copilot will be able to use the tools provided
    /// by the server autonomously, and will not ask for your approval before using them.").
    /// Missing destructiveHint on a write tool is therefore a stronger warning here than for
    /// IDE clients with interactive approval, but GitHub does not document it as an admission requirement.
    /// </summary>
    private static ClientProfileRuleDefinition RecommendDestructiveHintForWriteToolsAutonomous(string suffix, string documentationUrl)
    {
        return new ClientProfileRuleDefinition(
            $"tool-{suffix}",
            "Write-capable tools explicitly declare destructiveHint for autonomous-execution review",
            ClientProfileRequirementLevel.Recommended,
            ClientProfileEvidenceBasis.Documented,
            documentationUrl,
            context =>
            {
                if (context.ToolCount <= 0)
                {
                    return ClientProfileRuleObservation.NotApplicable("No tools were discovered, so destructiveHint coverage was not applicable.");
                }
                var missing = context.GetWriteCapableToolsWithoutDestructiveHint();
                if (missing.Count == 0)
                {
                    return ClientProfileRuleObservation.Satisfied("Every non-read-only tool declares an explicit destructiveHint for autonomous-execution review.");
                }
                return ClientProfileRuleObservation.Warning(
                    $"{missing.Count}/{context.ToolCount} write-capable tool(s) rely on the MCP destructiveHint default instead of declaring it explicitly. GitHub Copilot Cloud Agent invokes enabled tools autonomously without per-call approval, so explicit metadata improves configuration review.",
                    exampleComponents: missing.Take(5).ToList(),
                    recommendation: "Set `annotations.destructiveHint` explicitly for write-capable tools, or mark read-only tools with `readOnlyHint: true`. GitHub recommends allowlisting specific read-only tools for autonomous use.");
            }, RequiresToolMetadata: true);
    }

    private static ClientProfileRuleDefinition RecommendServerIdentity(string suffix, string documentationUrl, string clientName)
    {
        return new ClientProfileRuleDefinition(
            $"server-{suffix}",
            "Initialize response declares server name and version",
            ClientProfileRequirementLevel.Recommended,
            ClientProfileEvidenceBasis.Documented,
            documentationUrl,
            context =>
            {
                if (!context.HasInitializationPayload)
                {
                    return ClientProfileRuleObservation.NotApplicable("Initialize handshake details were not captured, so server identity could not be assessed.");
                }
                if (context.HasServerIdentity())
                {
                    return ClientProfileRuleObservation.Satisfied("Initialize.serverInfo carries both name and version.");
                }
                return ClientProfileRuleObservation.Warning(
                    $"Initialize.serverInfo is missing name or version. {clientName} displays these to the user during install/trust prompts.",
                    recommendation: "Populate `serverInfo.name` and `serverInfo.version` in the initialize result. Per MCP spec these are MUST-required fields; some clients (e.g. trust-prompt UIs) silently degrade when they are missing.");
            });
    }

    private static ClientProfileRuleDefinition RecommendClaudeRemoteOAuthDiscovery(string suffix, string documentationUrl)
    {
        return new ClientProfileRuleDefinition(
            $"transport-{suffix}",
            "Remote MCP servers expose RFC 9728 protected resource metadata for Claude Code's OAuth discovery chain",
            ClientProfileRequirementLevel.Recommended,
            ClientProfileEvidenceBasis.Documented,
            documentationUrl,
            context =>
            {
                if (!context.IsRemoteTransport)
                {
                    return ClientProfileRuleObservation.NotApplicable("Server is not on a remote HTTP/SSE transport, so OAuth discovery does not apply.");
                }

                var auth = context.Result.ToolValidation?.AuthenticationSecurity;
                if (auth?.AuthenticationRequired != true)
                {
                    return ClientProfileRuleObservation.NotApplicable("Server did not require authentication during validation, so OAuth discovery was not exercised.");
                }
                if (auth.AuthMetadata != null)
                {
                    return ClientProfileRuleObservation.Satisfied(
                        "Server advertised RFC 9728 protected resource metadata; Claude Code's default discovery chain (`/.well-known/oauth-protected-resource` → `/.well-known/oauth-authorization-server`) will resolve cleanly.");
                }
                return ClientProfileRuleObservation.Warning(
                    "Authentication is required but the server did not advertise RFC 9728 protected resource metadata. Claude Code falls back to RFC 8414 authorization-server metadata, but discovery may break if neither well-known endpoint is reachable.",
                    recommendation: "Expose `/.well-known/oauth-protected-resource` and reference it via the `resource_metadata=...` parameter on 401 WWW-Authenticate challenges so Claude Code (and other RFC 9728-aware clients) can complete the authorization flow without manual configuration.");
            });
    }

    private static ClientProfileRuleDefinition ObserveClaudeOutputBudgetAnnotation(string suffix, string documentationUrl)
    {
        return new ClientProfileRuleDefinition(
            $"tool-{suffix}",
            "Claude Code large-output overrides are declared when needed",
            ClientProfileRequirementLevel.Recommended,
            ClientProfileEvidenceBasis.Documented,
            documentationUrl,
            context =>
            {
                if (context.ToolCount <= 0)
                {
                    return ClientProfileRuleObservation.NotApplicable(
                        "No tools were discovered, so Claude Code output-budget guidance was not applicable.");
                }

                var annotatedTools = context.GetAnthropicMaxResultSizeAnnotatedTools();
                if (annotatedTools.Count > 0)
                {
                    return ClientProfileRuleObservation.Satisfied(
                        $"Observed {annotatedTools.Count}/{context.ToolCount} tool(s) declaring _meta[\"anthropic/maxResultSizeChars\"].",
                        exampleComponents: annotatedTools.Take(3).ToList());
                }

                return ClientProfileRuleObservation.NotApplicable(
                    "No discovered tools declared _meta[\"anthropic/maxResultSizeChars\"]. Claude Code uses this override only for tools that need larger-than-default text outputs.");
            }, RequiresToolMetadata: true);
    }

    private static ClientProfileRuleDefinition RequireRemoteOAuthCompatibility(string suffix, string documentationUrl)
    {
        return new ClientProfileRuleDefinition(
            $"transport-{suffix}",
            "Remote servers do not rely on OAuth flows the cloud agent does not support",
            ClientProfileRequirementLevel.Required,
            ClientProfileEvidenceBasis.Documented,
            documentationUrl,
            context =>
            {
                if (!context.IsRemoteTransport)
                {
                    return ClientProfileRuleObservation.NotApplicable(
                        "This server is not using a remote HTTP/SSE transport, so the cloud agent's remote OAuth limitation does not apply.");
                }

                return context.UsesObservedRemoteOAuth
                    ? ClientProfileRuleObservation.Failed(
                        "GitHub Copilot Cloud Agent documents that remote MCP servers leveraging OAuth are not currently supported, and this validation observed OAuth challenge or metadata evidence.",
                        recommendation: "Use a local stdio deployment or a remote deployment path that does not depend on unsupported OAuth flows for cloud-agent access.")
                    : ClientProfileRuleObservation.Satisfied(
                        "No remote OAuth challenge or metadata evidence was observed for this server.");
            });
    }

    private static ClientProfileRuleDefinition WarnExtraPromptAndResourceSurfaces(string suffix, string documentationUrl)
    {
        return new ClientProfileRuleDefinition(
            $"surface-{suffix}",
            "Only the tool surface is currently consumed",
            ClientProfileRequirementLevel.Recommended,
            ClientProfileEvidenceBasis.Documented,
            documentationUrl,
            context =>
            {
                if (context.PromptCount == 0 && context.ResourceCount == 0 && context.ResourceTemplateCount == 0)
                {
                    return context.PromptsKnownAbsent && context.ResourcesKnownAbsent
                        ? ClientProfileRuleObservation.Satisfied("Server exposes tools only, which aligns cleanly with this profile's current surface support.")
                        : ClientProfileRuleObservation.Warning(
                            "Prompt and resource discovery was not established; a tools-only inventory cannot be confirmed.",
                            recommendation: MissingCatalogRecommendation);
                }

                var ignoredSurfaces = new List<string>();
                if (context.PromptCount > 0)
                {
                    ignoredSurfaces.Add($"{context.PromptCount} prompt(s)");
                }

                if (context.ResourceCount > 0)
                {
                    ignoredSurfaces.Add($"{context.ResourceCount} resource(s)");
                }
                if (context.ResourceTemplateCount > 0)
                {
                    ignoredSurfaces.Add($"{context.ResourceTemplateCount} resource template(s)");
                }

                return ClientProfileRuleObservation.Warning(
                    $"This profile currently consumes tools only; {string.Join(" and ", ignoredSurfaces)} will not contribute to compatibility.");
            });
    }

    private static ClientProfileRuleDefinition ObserveListChangedSupport(
        string requirementId,
        string title,
        string documentationUrl,
        IReadOnlyList<string> surfaces,
        string clientDisplayName)
    {
        return new ClientProfileRuleDefinition(
            requirementId,
            title,
            ClientProfileRequirementLevel.Recommended,
            ClientProfileEvidenceBasis.Documented,
            documentationUrl,
            context =>
            {
                var relevantSurfaces = surfaces
                    .Where(context.HasObservedSurface)
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .ToList();

                if (relevantSurfaces.Count == 0)
                {
                    return ClientProfileRuleObservation.NotApplicable(
                        "No relevant interactive surfaces were discovered, so list_changed support was not applicable.");
                }

                if (!context.HasInitializationPayload)
                {
                    return ClientProfileRuleObservation.NotApplicable(
                        "Initialize handshake details were not captured, so list_changed support could not be assessed.");
                }

                var declaredSurfaces = relevantSurfaces
                    .Where(context.HasListChangedSupport)
                    .ToList();

                if (declaredSurfaces.Count == relevantSurfaces.Count)
                {
                    return ClientProfileRuleObservation.Satisfied(
                        $"Initialize capabilities declare listChanged for {FormatSurfaceList(declaredSurfaces)}.",
                        exampleComponents: declaredSurfaces);
                }

                if (declaredSurfaces.Count == 0)
                {
                    return ClientProfileRuleObservation.Warning(
                        $"{clientDisplayName} documents dynamic list_changed updates, but this server did not advertise listChanged for the discovered {FormatSurfaceList(relevantSurfaces)} surfaces.",
                        exampleComponents: relevantSurfaces,
                        recommendation: $"Declare listChanged for {FormatSurfaceList(relevantSurfaces)} when the server can notify clients about catalog changes without reconnecting.");
                }

                var missingSurfaces = relevantSurfaces
                    .Except(declaredSurfaces, StringComparer.OrdinalIgnoreCase)
                    .ToList();

                return ClientProfileRuleObservation.Warning(
                    $"{clientDisplayName} documents dynamic list_changed updates, and this server advertised them for {FormatSurfaceList(declaredSurfaces)} but not {FormatSurfaceList(missingSurfaces)}.",
                    exampleComponents: missingSurfaces,
                    recommendation: $"Declare listChanged for {FormatSurfaceList(missingSurfaces)} when the server can notify clients about catalog changes without reconnecting.");
            });
    }

    private static ClientProfileRuleDefinition ObserveOptionalCapability(
        string requirementId,
        string title,
        string documentationUrl,
        string findingRuleId,
        string successSummary,
        string notApplicableSummary)
    {
        return new ClientProfileRuleDefinition(
            requirementId,
            title,
            ClientProfileRequirementLevel.Recommended,
            ClientProfileEvidenceBasis.Documented,
            documentationUrl,
            context => context.HasFinding(findingRuleId)
                ? ClientProfileRuleObservation.Satisfied(successSummary, new[] { findingRuleId })
                : ClientProfileRuleObservation.NotApplicable(notApplicableSummary));
    }

    private static ClientProfileRuleObservation EvaluateContractRule(
        ClientProfileContext context,
        int totalComponents,
        bool knownAbsent,
        string componentLabel,
        IReadOnlyList<string> ruleIds,
        TestStatus? categoryStatus,
        bool operationsCompleted,
        string notApplicableSummary,
        string successSummary)
    {
        var findings = context.GetFindings(ruleIds);
        if (findings.Count == 0)
        {
            if (totalComponents <= 0)
            {
                return knownAbsent
                    ? ClientProfileRuleObservation.NotApplicable(notApplicableSummary)
                    : ClientProfileRuleObservation.Warning(
                        $"The {componentLabel} inventory was not established; an empty recovered count does not prove this contract is not applicable.",
                        recommendation: MissingCatalogRecommendation);
            }

            if (categoryStatus == TestStatus.Failed)
            {
                return ClientProfileRuleObservation.Warning(
                    $"The {componentLabel} validation category reported a non-contract failure, but no blocking structured rule matched this profile. Inspect the detailed validation output.");
            }

            if (categoryStatus != TestStatus.Passed)
            {
                return ClientProfileRuleObservation.Warning(
                    $"The {componentLabel} contract evidence is {categoryStatus?.ToString() ?? "not recorded"}; absence of a blocking finding does not establish a completed check.");
            }

            if (!operationsCompleted)
            {
                return ClientProfileRuleObservation.Warning(
                    $"Completed {componentLabel} operation evidence was not recorded for every observed item; metadata or listing success does not establish a call/read contract.",
                    recommendation: "Collect authorized per-item execution evidence in standard mode on an owned or explicitly approved target before relying on this contract.");
            }

            return ClientProfileRuleObservation.Satisfied(successSummary);
        }

        var affectedComponents = context.GetAffectedComponents(ruleIds);
        var coverage = BuildCoverageLabel(affectedComponents.Count, totalComponents, componentLabel);
        var summarizedRuleIds = findings.Select(finding => finding.RuleId).Distinct(StringComparer.OrdinalIgnoreCase).Take(3).ToList();

        return ClientProfileRuleObservation.Failed(
            $"Blocking {componentLabel} contract gaps affect {coverage}.",
            summarizedRuleIds,
            affectedComponents.Take(3).ToList(),
            findings.Select(finding => finding.Recommendation).FirstOrDefault(recommendation => !string.IsNullOrWhiteSpace(recommendation)));
    }

    private static string BuildCoverageLabel(int affectedComponents, int totalComponents, string componentLabel)
    {
        if (totalComponents <= 0)
        {
            return $"{affectedComponents} {componentLabel}(s)";
        }

        return $"{affectedComponents}/{totalComponents} {componentLabel}(s)";
    }

    private static string FormatSurfaceList(IReadOnlyList<string> surfaces)
    {
        return string.Join(", ", surfaces);
    }

    private sealed record ClientProfileDefinition(ClientProfileDescriptor Profile, IReadOnlyList<ClientProfileRuleDefinition> Rules)
    {
        public ClientProfileDefinition(ClientProfileDescriptor profile, params ClientProfileRuleDefinition[] rules)
            : this(profile, (IReadOnlyList<ClientProfileRuleDefinition>)rules)
        {
        }
    }

    private sealed record ClientProfileRuleDefinition(
        string Id,
        string Title,
        ClientProfileRequirementLevel Level,
        ClientProfileEvidenceBasis EvidenceBasis,
        string DocumentationUrl,
        Func<ClientProfileContext, ClientProfileRuleObservation> Evaluate,
        bool RequiresToolMetadata = false);

    private sealed class ClientProfileContext
    {
        private readonly IReadOnlyList<ValidationFinding> _findings;
        private readonly HashSet<string> _catalogToolNames;
        private readonly HashSet<string> _metadataToolNames;

        public ClientProfileContext(ValidationResult result)
        {
            Result = result;
            _catalogToolNames = (result.ToolValidation?.DiscoveredToolNames ?? [])
                .Where(name => !string.IsNullOrWhiteSpace(name)).ToHashSet(StringComparer.Ordinal);
            var snapshot = result.CapabilitySnapshot?.Payload;
            ToolCount = Math.Max(DetermineToolCount(result.ToolValidation),
                snapshot?.ToolListingSucceeded == true ? snapshot.DiscoveredToolsCount : 0);
            _metadataToolNames = (result.ToolValidation?.ToolResults ?? [])
                .Select(tool => tool.ToolName).Where(IsRealToolName).ToHashSet(StringComparer.Ordinal);
            PromptCount = Math.Max(DeterminePromptCount(result.PromptTesting),
                snapshot?.PromptListingSucceeded == true ? snapshot.DiscoveredPromptsCount : 0);
            ResourceCount = Math.Max(DetermineResourceCount(result.ResourceTesting),
                snapshot?.ResourceListingSucceeded == true ? snapshot.DiscoveredResourcesCount : 0);
            _findings = CollectFindings(result);
        }

        public ValidationResult Result { get; }

        public int ToolCount { get; }

        public bool HasCompleteToolMetadata => ToolCount > 0 && _metadataToolNames.Count == ToolCount &&
            (_catalogToolNames.Count == 0 || _catalogToolNames.SetEquals(_metadataToolNames)) &&
            (Result.ToolValidation?.CatalogEnumeration is not { } catalog ||
                (IsCompleteCatalog(catalog) && catalog.RecoveredItems == ToolCount));

        public int PromptCount { get; }

        public int ResourceCount { get; }

        public int ResourceTemplateCount => Result.ResourceTesting?.TemplateCatalogEnumeration is { } catalog
            ? (int)Math.Clamp(catalog.RecoveredItems, 0, int.MaxValue)
            : 0;

        public bool HasCompletedToolCalls => HasCompletedOperations(
            (Result.ToolValidation?.ToolResults ?? []).Where(tool => IsRealToolName(tool.ToolName))
                .Select(tool => (tool.ToolName, tool.Status == TestStatus.Passed && tool.ExecutionSuccessful)),
            ToolCount, Result.ToolValidation?.CatalogEnumeration, _catalogToolNames.Count > 0 ? _catalogToolNames : null);

        public bool HasCompletedPromptGets => HasCompletedOperations(
            (Result.PromptTesting?.PromptResults ?? [])
                .Select(prompt => (prompt.PromptName, prompt.Status == TestStatus.Passed && prompt.ExecutionSuccessful)),
            PromptCount, Result.PromptTesting?.CatalogEnumeration, Result.PromptTesting?.DiscoveredPromptNames);

        public bool HasCompletedResourceReads => ResourceTemplatesKnownAbsent &&
            HasCompletedOperations(
                (Result.ResourceTesting?.ResourceResults ?? [])
                    .Select(resource => (resource.ResourceUri, resource.Status == TestStatus.Passed && resource.AccessSuccessful)),
                ResourceCount, Result.ResourceTesting?.CatalogEnumeration, Result.ResourceTesting?.DiscoveredResourceUris);

        private static bool HasCompletedOperations(
            IEnumerable<(string Identity, bool Completed)> operations, int count,
            CatalogEnumerationEvidence? catalog, IEnumerable<string>? expectedNames)
        {
            if (count <= 0 || catalog is not null &&
                (!IsCompleteCatalog(catalog) || catalog.RecoveredItems != count || catalog.RawItemsObserved != count))
                return false;
            var observed = operations.ToArray();
            if (observed.Length != count || observed.Any(item => !item.Completed || string.IsNullOrWhiteSpace(item.Identity)))
                return false;
            var identities = observed.Select(item => item.Identity).ToHashSet(StringComparer.Ordinal);
            return identities.Count == count && (expectedNames is null || identities.SetEquals(expectedNames));
        }

        public bool ToolsKnownAbsent => ToolCount == 0 && IsKnownAbsent(
            Result.ToolValidation?.CatalogEnumeration, Result.CapabilitySnapshot?.Payload?.ToolListingSucceeded == true,
            McpSpecConstants.Capabilities.Tools);

        public bool PromptsKnownAbsent => PromptCount == 0 && IsKnownAbsent(
            Result.PromptTesting?.CatalogEnumeration, Result.CapabilitySnapshot?.Payload?.PromptListingSucceeded == true,
            McpSpecConstants.Capabilities.Prompts);

        private bool ResourceTemplatesKnownAbsent =>
            (Result.ResourceTesting?.ResourceTemplatesDiscovered ?? 0) == 0 && ResourceTemplateCount == 0 && IsKnownAbsent(
                Result.ResourceTesting?.TemplateCatalogEnumeration, false, McpSpecConstants.Capabilities.Resources);

        public bool ResourcesKnownAbsent => ResourceCount == 0 && ResourceTemplatesKnownAbsent && IsKnownAbsent(
            Result.ResourceTesting?.CatalogEnumeration, Result.CapabilitySnapshot?.Payload?.ResourceListingSucceeded == true,
            McpSpecConstants.Capabilities.Resources);

        private bool IsKnownAbsent(CatalogEnumerationEvidence? catalog, bool listingSucceeded, string capability)
        {
            if (catalog is not null)
                return IsCompleteCatalog(catalog) && catalog.RawItemsObserved == 0 && catalog.RecoveredItems == 0;
            if (listingSucceeded) return false;
            return Result.CapabilitySnapshot?.Payload is { CapabilityDeclarationsAvailable: true } snapshot &&
                !snapshot.AdvertisedCapabilities.Contains(capability, StringComparer.Ordinal);
        }

        private static bool IsCompleteCatalog(CatalogEnumerationEvidence catalog) => catalog is
        {
            IsComplete: true, Termination: CatalogEnumerationTermination.Complete,
            PageAttempts: > 0, ParsedPages: > 0, RejectedItems: 0, RejectedPages: 0
        };

        public bool HasInitializationPayload => Result.InitializationHandshake?.Payload != null;

        public string? InitializeInstructions => Result.InitializationHandshake?.Payload?.Instructions;

        public bool IsRemoteTransport => IsRemoteTransportValue(Result.ServerConfig.Transport);

        public bool UsesObservedRemoteOAuth => HasObservedRemoteOAuth(Result);

        public IReadOnlyList<string> GetAnthropicMaxResultSizeAnnotatedTools()
        {
            return Result.ToolValidation?.ToolResults
                .Where(tool => IsRealToolName(tool.ToolName) && tool.AnthropicMaxResultSizeChars is > 0)
                .Select(tool => tool.ToolName)
                .Distinct(StringComparer.Ordinal)
                .ToList()
                ?? new List<string>();
        }

        public IReadOnlyList<string> GetToolsExceedingDescriptionBytes(int byteCeiling)
        {
            return Result.ToolValidation?.ToolResults
                .Where(tool => IsRealToolName(tool.ToolName)
                    && !string.IsNullOrEmpty(tool.Description)
                    && System.Text.Encoding.UTF8.GetByteCount(tool.Description) > byteCeiling)
                .Select(tool => tool.ToolName)
                .Distinct(StringComparer.Ordinal)
                .ToList()
                ?? new List<string>();
        }

        public IReadOnlyList<string> GetToolsMissingDescription()
        {
            return Result.ToolValidation?.ToolResults
                .Where(tool => IsRealToolName(tool.ToolName) && string.IsNullOrWhiteSpace(tool.Description))
                .Select(tool => tool.ToolName)
                .Distinct(StringComparer.Ordinal)
                .ToList()
                ?? new List<string>();
        }

        public IReadOnlyList<string> GetToolsMissingDisplayTitle()
        {
            return Result.ToolValidation?.ToolResults
                .Where(tool => IsRealToolName(tool.ToolName) && string.IsNullOrWhiteSpace(tool.DisplayTitle))
                .Select(tool => tool.ToolName)
                .Distinct(StringComparer.Ordinal)
                .ToList()
                ?? new List<string>();
        }

        public IReadOnlyList<string> GetToolsMissingAnyAnnotation()
        {
            return Result.ToolValidation?.ToolResults
                .Where(tool => IsRealToolName(tool.ToolName)
                    && tool.ReadOnlyHint is null
                    && tool.DestructiveHint is null
                    && tool.IdempotentHint is null
                    && tool.OpenWorldHint is null)
                .Select(tool => tool.ToolName)
                .Distinct(StringComparer.Ordinal)
                .ToList()
                ?? new List<string>();
        }

        /// <summary>
        /// Tools that look write-capable (not explicitly read-only) but do not declare
        /// `destructiveHint`. The MCP spec defaults destructiveHint to true when readOnlyHint
        /// is false, but clients that auto-execute (e.g. Cloud Agent) and clients that drive
        /// approval UX (Claude Code, VS Code, Visual Studio) all benefit from explicit hints.
        /// </summary>
        public IReadOnlyList<string> GetWriteCapableToolsWithoutDestructiveHint()
        {
            return Result.ToolValidation?.ToolResults
                .Where(tool => IsRealToolName(tool.ToolName)
                    && tool.ReadOnlyHint != true
                    && tool.DestructiveHint is null)
                .Select(tool => tool.ToolName)
                .Distinct(StringComparer.Ordinal)
                .ToList()
                ?? new List<string>();
        }

        public bool HasServerIdentity()
        {
            var info = Result.InitializationHandshake?.Payload?.ServerInfo;
            return info != null
                && !string.IsNullOrWhiteSpace(info.Name)
                && !string.IsNullOrWhiteSpace(info.Version);
        }

        public bool HasObservedSurface(string surface)
        {
            return surface switch
            {
                "tools" => ToolCount > 0,
                "prompts" => PromptCount > 0,
                "resources" => ResourceCount > 0 || ResourceTemplateCount > 0,
                _ => false
            };
        }

        public bool HasListChangedSupport(string surface)
        {
            var capabilities = Result.InitializationHandshake?.Payload?.Capabilities;
            if (capabilities == null)
            {
                return false;
            }

            return surface switch
            {
                "tools" => capabilities.Tools?.ListChanged == true,
                "prompts" => capabilities.Prompts?.ListChanged == true,
                "resources" => capabilities.Resources?.ListChanged == true,
                _ => false
            };
        }

        public IReadOnlyList<string> GetSurfaceLabels()
        {
            var labels = new List<string>();
            if (ToolCount > 0)
            {
                labels.Add($"{ToolCount} tool(s)");
            }

            if (PromptCount > 0)
            {
                labels.Add($"{PromptCount} prompt(s)");
            }

            if (ResourceCount > 0)
            {
                labels.Add($"{ResourceCount} resource(s)");
            }
            if (ResourceTemplateCount > 0)
            {
                labels.Add($"{ResourceTemplateCount} resource template(s)");
            }

            return labels;
        }

        public IReadOnlyList<ValidationFinding> GetFindings(IReadOnlyList<string> ruleIds)
        {
            if (ruleIds.Count == 0)
            {
                return Array.Empty<ValidationFinding>();
            }

            var ruleSet = new HashSet<string>(ruleIds, StringComparer.OrdinalIgnoreCase);
            return _findings
                .Where(finding => ruleSet.Contains(finding.RuleId))
                .GroupBy(finding => (finding.RuleId, finding.Component, finding.Summary))
                .Select(group => group.First())
                .ToList();
        }

        public IReadOnlyList<string> GetAffectedComponents(IReadOnlyList<string> ruleIds)
        {
            return GetFindings(ruleIds)
                .Select(finding => string.IsNullOrWhiteSpace(finding.Component) ? "unknown" : finding.Component)
                .Distinct(StringComparer.Ordinal)
                .ToList();
        }

        public bool HasFinding(string ruleId)
        {
            return _findings.Any(finding => string.Equals(finding.RuleId, ruleId, StringComparison.OrdinalIgnoreCase));
        }

        private static IReadOnlyList<ValidationFinding> CollectFindings(ValidationResult result)
        {
            var findings = new List<ValidationFinding>();

            if (result.ProtocolCompliance?.Findings is { Count: > 0 } protocolFindings)
            {
                findings.AddRange(protocolFindings);
            }

            if (result.ToolValidation?.Findings is { Count: > 0 } toolFindings)
            {
                findings.AddRange(toolFindings);
            }

            if (result.ToolValidation?.AiReadinessFindings is { Count: > 0 } aiReadinessFindings)
            {
                findings.AddRange(aiReadinessFindings);
            }

            if (result.ToolValidation?.ToolResults is { Count: > 0 } toolResults)
            {
                foreach (var tool in toolResults)
                {
                    findings.AddRange(tool.Findings);
                }
            }

            if (result.ResourceTesting?.Findings is { Count: > 0 } resourceFindings)
            {
                findings.AddRange(resourceFindings);
            }

            if (result.ResourceTesting?.ResourceResults is { Count: > 0 } resourceResults)
            {
                foreach (var resource in resourceResults)
                {
                    findings.AddRange(resource.Findings);
                }
            }

            if (result.PromptTesting?.Findings is { Count: > 0 } promptFindings)
            {
                findings.AddRange(promptFindings);
            }

            if (result.PromptTesting?.PromptResults is { Count: > 0 } promptResults)
            {
                foreach (var prompt in promptResults)
                {
                    findings.AddRange(prompt.Findings);
                }
            }

            return findings;
        }

        private int DetermineToolCount(ToolTestResult? toolValidation)
        {
            if (toolValidation == null)
            {
                return 0;
            }

            if (toolValidation.ToolsDiscovered > 0)
            {
                return toolValidation.ToolsDiscovered;
            }

            if (toolValidation.DiscoveredToolNames.Count > 0)
            {
                return _catalogToolNames.Count;
            }

            return toolValidation.ToolResults
                .Select(tool => tool.ToolName)
                .Where(IsRealToolName)
                .Distinct(StringComparer.Ordinal)
                .Count();
        }

        private static int DeterminePromptCount(PromptTestResult? promptTesting)
        {
            if (promptTesting == null)
            {
                return 0;
            }

            if (promptTesting.PromptsDiscovered > 0)
            {
                return promptTesting.PromptsDiscovered;
            }

            return promptTesting.PromptResults
                .Select(prompt => prompt.PromptName)
                .Where(name => !string.IsNullOrWhiteSpace(name))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .Count();
        }

        private static int DetermineResourceCount(ResourceTestResult? resourceTesting)
        {
            if (resourceTesting == null)
            {
                return 0;
            }

            if (resourceTesting.ResourcesDiscovered > 0)
            {
                return resourceTesting.ResourcesDiscovered;
            }

            return resourceTesting.ResourceResults
                .Select(resource => string.IsNullOrWhiteSpace(resource.ResourceUri) ? resource.ResourceName : resource.ResourceUri)
                .Where(name => !string.IsNullOrWhiteSpace(name))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .Count();
        }

        private bool IsRealToolName(string? toolName)
        {
            if (string.IsNullOrWhiteSpace(toolName)) return false;
            if (_catalogToolNames.Count > 0) return _catalogToolNames.Contains(toolName);
            return !toolName.Equals("tools/list", StringComparison.OrdinalIgnoreCase)
                && !toolName.Equals("Authentication Discovery", StringComparison.OrdinalIgnoreCase)
                && !toolName.Equals("tools/list (Auth Check)", StringComparison.OrdinalIgnoreCase);
        }

        private static bool IsRemoteTransportValue(string? transport)
        {
            return !string.IsNullOrWhiteSpace(transport)
                && (transport.Contains("http", StringComparison.OrdinalIgnoreCase)
                    || transport.Equals("sse", StringComparison.OrdinalIgnoreCase));
        }

        private static bool HasObservedRemoteOAuth(ValidationResult result)
        {
            // GitHub Copilot Cloud Agent docs say: "does not currently support remote MCP
            // servers that leverage OAuth for authentication and authorization." This is a
            // narrow constraint about *OAuth flows specifically* — Bearer tokens supplied as
            // static headers (PAT-style, like the built-in GitHub MCP that Cloud Agent
            // explicitly supports) are NOT OAuth and must not trip this check.
            //
            // Strong OAuth signals:
            //   1. RFC 9728 Protected Resource Metadata document was successfully fetched.
            //   2. Validator attempted/completed interactive OAuth login.
            //   3. WWW-Authenticate challenge advertised OAuth-specific parameters
            //      (resource_metadata or authorization_uri per RFC 9728 §5.1).
            //
            // We deliberately do NOT treat a bare `Bearer realm="..."` challenge as OAuth.
            var auth = result.ToolValidation?.AuthenticationSecurity;
            if (auth?.AuthMetadata != null)
            {
                return true;
            }

            if (auth?.InteractiveLoginAttempted == true || auth?.InteractiveLoginSucceeded == true)
            {
                return true;
            }

            if (HasOAuthChallengeParameters(auth?.WwwAuthenticateHeader))
            {
                return true;
            }

            return result.ToolValidation?.ToolResults?.Any(tool =>
                tool.AuthMetadata != null ||
                HasOAuthChallengeParameters(tool.WwwAuthenticateHeader)) == true;
        }

        private static bool HasOAuthChallengeParameters(string? wwwAuthenticateHeader)
        {
            if (string.IsNullOrWhiteSpace(wwwAuthenticateHeader))
            {
                return false;
            }

            // Per RFC 9728 §5.1, an OAuth-aware resource server adds resource_metadata=...
            // to its WWW-Authenticate challenge. RFC 6750 / OAuth bearer challenges may also
            // include error="invalid_token" with authorization_uri, scope, etc. A bare
            // Bearer challenge with only realm= is NOT an OAuth flow signal.
            return wwwAuthenticateHeader.IndexOf("resource_metadata", StringComparison.OrdinalIgnoreCase) >= 0
                || wwwAuthenticateHeader.IndexOf("authorization_uri", StringComparison.OrdinalIgnoreCase) >= 0
                || wwwAuthenticateHeader.IndexOf("error=\"invalid_token\"", StringComparison.OrdinalIgnoreCase) >= 0
                || wwwAuthenticateHeader.IndexOf("error=\"insufficient_scope\"", StringComparison.OrdinalIgnoreCase) >= 0;
        }
    }

    private sealed record ClientProfileRuleObservation(
        ClientProfileRequirementOutcome Outcome,
        string Summary,
        IReadOnlyList<string> RuleIds,
        IReadOnlyList<string> ExampleComponents,
        string? Recommendation)
    {
        public static ClientProfileRuleObservation Satisfied(string summary, IReadOnlyList<string>? ruleIds = null, IReadOnlyList<string>? exampleComponents = null)
        {
            return new ClientProfileRuleObservation(
                ClientProfileRequirementOutcome.Satisfied,
                summary,
                ruleIds ?? Array.Empty<string>(),
                exampleComponents ?? Array.Empty<string>(),
                null);
        }

        public static ClientProfileRuleObservation Warning(string summary, IReadOnlyList<string>? ruleIds = null, IReadOnlyList<string>? exampleComponents = null, string? recommendation = null)
        {
            return new ClientProfileRuleObservation(
                ClientProfileRequirementOutcome.Warning,
                summary,
                ruleIds ?? Array.Empty<string>(),
                exampleComponents ?? Array.Empty<string>(),
                recommendation);
        }

        public static ClientProfileRuleObservation Failed(string summary, IReadOnlyList<string>? ruleIds = null, IReadOnlyList<string>? exampleComponents = null, string? recommendation = null)
        {
            return new ClientProfileRuleObservation(
                ClientProfileRequirementOutcome.Failed,
                summary,
                ruleIds ?? Array.Empty<string>(),
                exampleComponents ?? Array.Empty<string>(),
                recommendation);
        }

        public static ClientProfileRuleObservation NotApplicable(string summary)
        {
            return new ClientProfileRuleObservation(ClientProfileRequirementOutcome.NotApplicable, summary, Array.Empty<string>(), Array.Empty<string>(), null);
        }
    }
}