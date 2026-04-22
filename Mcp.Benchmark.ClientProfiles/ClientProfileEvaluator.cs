using Mcp.Benchmark.Core.Models;
using Mcp.Benchmark.Core.Services;

namespace Mcp.Benchmark.ClientProfiles;

public sealed class ClientProfileEvaluator : IClientProfileEvaluator
{
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

    private static readonly string[] ToolMetadataRuleIds =
    {
        ValidationFindingRuleIds.ToolGuidelineDisplayTitleMissing,
        ValidationFindingRuleIds.ToolGuidelineReadOnlyHintMissing,
        ValidationFindingRuleIds.ToolGuidelineDestructiveHintMissing,
        ValidationFindingRuleIds.ToolGuidelineOpenWorldHintMissing,
        ValidationFindingRuleIds.ToolGuidelineIdempotentHintMissing,
        ValidationFindingRuleIds.ToolGuidelineHintConflict,
        ValidationFindingRuleIds.ToolDestructiveConfirmationGuidanceMissing
    };

    private static readonly string[] ToolSchemaRuleIds =
    {
        ValidationFindingRuleIds.AiReadinessMissingParameterDescriptions,
        ValidationFindingRuleIds.AiReadinessVagueStringSchema,
        ValidationFindingRuleIds.AiReadinessRequiredArraySchema,
        ValidationFindingRuleIds.AiReadinessEnumCoverageMissing,
        ValidationFindingRuleIds.AiReadinessFormatHintMissing,
        ValidationFindingRuleIds.AiReadinessTokenBudgetExceeded,
        ValidationFindingRuleIds.AiReadinessTokenBudgetWarning
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
        ValidationFindingRuleIds.ResourceTemplateMissingUriTemplate
    };

    private static readonly string[] ResourceMetadataRuleIds =
    {
        ValidationFindingRuleIds.ResourceGuidelineMimeTypeMissing,
        ValidationFindingRuleIds.ResourceUriSchemeUnclear,
        ValidationFindingRuleIds.ResourceTemplateNameMissing,
        ValidationFindingRuleIds.ResourceTemplateDescriptionMissing
    };

    private static readonly IReadOnlyDictionary<string, ClientProfileDefinition> Definitions = BuildDefinitions();

    public ClientCompatibilityReport? Evaluate(ValidationResult validationResult, ClientProfileOptions? options)
    {
        ArgumentNullException.ThrowIfNull(validationResult);

        var requestedProfiles = NormalizeRequestedProfiles(options);
        if (requestedProfiles.Count == 0)
        {
            return null;
        }

        var context = new ClientProfileContext(validationResult);
        var assessments = requestedProfiles
            .Select(profileId => EvaluateProfile(Definitions[profileId], context))
            .ToList();

        return new ClientCompatibilityReport
        {
            RequestedProfiles = requestedProfiles,
            Assessments = assessments
        };
    }

    public IReadOnlyList<ClientProfileDescriptor> GetSupportedProfiles()
    {
        return ClientProfileCatalog.SupportedProfiles;
    }

    private static ClientProfileAssessment EvaluateProfile(ClientProfileDefinition definition, ClientProfileContext context)
    {
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
            Summary = BuildProfileSummary(status, passed, warnings, failed),
            PassedRequirements = passed,
            WarningRequirements = warnings,
            FailedRequirements = failed,
            Requirements = requirements
        };
    }

    private static ClientProfileRequirementAssessment EvaluateRequirement(ClientProfileRuleDefinition rule, ClientProfileContext context)
    {
        var observation = rule.Evaluate(context);

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
            DocumentationUrl = null
        };
    }

    private static string BuildProfileSummary(ClientProfileCompatibilityStatus status, int passed, int warnings, int failed)
    {
        return status switch
        {
            ClientProfileCompatibilityStatus.Compatible => $"All applicable compatibility checks passed ({passed} satisfied).",
            ClientProfileCompatibilityStatus.CompatibleWithWarnings => warnings == 1
                ? "Required compatibility checks passed; 1 advisory requirement still needs follow-up."
                : $"Required compatibility checks passed; {warnings} advisory requirements still need follow-up.",
            ClientProfileCompatibilityStatus.Incompatible => $"{failed} required compatibility check(s) failed; review the affected surfaces before relying on this client profile.",
            _ => "Compatibility outcome unavailable."
        };
    }

    private static List<string> NormalizeRequestedProfiles(ClientProfileOptions? options)
    {
        if (options?.Profiles == null || options.Profiles.Count == 0)
        {
            return ClientProfileCatalog.SupportedProfileIds.ToList();
        }

        var requested = options.Profiles
            .SelectMany(value => value.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        if (requested.Count == 0)
        {
            return ClientProfileCatalog.SupportedProfileIds.ToList();
        }

        if (requested.Any(value => string.Equals(value, ClientProfileCatalog.AllProfilesToken, StringComparison.OrdinalIgnoreCase)))
        {
            return ClientProfileCatalog.SupportedProfileIds.ToList();
        }

        return requested
            .Select(ClientProfileCatalog.ResolveCanonicalProfileId)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private static IReadOnlyDictionary<string, ClientProfileDefinition> BuildDefinitions()
    {
        var definitions = new[]
        {
            CreateInteractiveProfile(
                "claude-code",
                RequireInteractiveSurface("surfaces"),
                RequireToolContract("tools-contract"),
                RequirePromptContract("prompts-contract"),
                RequireResourceContract("resources-contract"),
                RecommendToolMetadata("tool-metadata"),
                RecommendToolSchema("tool-schema"),
                RecommendPromptMetadata("prompt-metadata"),
                RecommendResourceMetadata("resource-metadata")),
            CreateInteractiveProfile(
                "vscode-copilot-agent",
                RequireInteractiveSurface("surfaces"),
                RequireToolContract("tools-contract"),
                RequirePromptContract("prompts-contract"),
                RequireResourceContract("resources-contract"),
                RecommendToolMetadata("tool-metadata"),
                RecommendToolSchema("tool-schema"),
                RecommendPromptMetadata("prompt-metadata"),
                RecommendResourceMetadata("resource-metadata")),
            CreateToolsOnlyProfile(
                "github-copilot-cli",
                RequireToolsAvailable("tools-present"),
                RequireToolContract("tools-contract"),
                RecommendToolMetadata("tool-metadata"),
                RecommendToolSchema("tool-schema")),
            CreateToolsOnlyProfile(
                "github-copilot-cloud-agent",
                RequireToolsAvailable("tools-present"),
                RequireToolContract("tools-contract"),
                WarnExtraPromptAndResourceSurfaces("tools-only-surface"),
                RecommendToolMetadata("tool-metadata"),
                RecommendToolSchema("tool-schema")),
            CreateInteractiveProfile(
                "visual-studio-copilot",
                RequireInteractiveSurface("surfaces"),
                RequireToolContract("tools-contract"),
                RequirePromptContract("prompts-contract"),
                RequireResourceContract("resources-contract"),
                RecommendToolMetadata("tool-metadata"),
                RecommendToolSchema("tool-schema"),
                RecommendPromptMetadata("prompt-metadata"),
                RecommendResourceMetadata("resource-metadata"))
        };

        return definitions.ToDictionary(definition => definition.Profile.Id, StringComparer.OrdinalIgnoreCase);
    }

    private static ClientProfileDefinition CreateInteractiveProfile(string profileId, params ClientProfileRuleDefinition[] rules)
    {
        return new ClientProfileDefinition(ClientProfileCatalog.GetDescriptor(profileId), rules);
    }

    private static ClientProfileDefinition CreateToolsOnlyProfile(string profileId, params ClientProfileRuleDefinition[] rules)
    {
        return new ClientProfileDefinition(ClientProfileCatalog.GetDescriptor(profileId), rules);
    }

    private static ClientProfileRuleDefinition RequireInteractiveSurface(string suffix)
    {
        return new ClientProfileRuleDefinition(
            $"interactive-{suffix}",
            "At least one interactive MCP surface is available",
            ClientProfileRequirementLevel.Required,
            ClientProfileEvidenceBasis.Documented,
            context =>
            {
                var surfaces = context.GetSurfaceLabels();
                return surfaces.Count > 0
                    ? ClientProfileRuleObservation.Satisfied($"Server exposes {string.Join(", ", surfaces)}.")
                    : ClientProfileRuleObservation.Failed("No tools, prompts, or resources were discovered for this profile.");
            });
    }

    private static ClientProfileRuleDefinition RequireToolsAvailable(string suffix)
    {
        return new ClientProfileRuleDefinition(
            $"tool-{suffix}",
            "Tool surface is available",
            ClientProfileRequirementLevel.Required,
            ClientProfileEvidenceBasis.Documented,
            context => context.ToolCount > 0
                ? ClientProfileRuleObservation.Satisfied($"Observed {context.ToolCount} tool(s) for this client profile.")
                : ClientProfileRuleObservation.Failed("No tools were discovered, but this client profile currently depends on the tool surface."));
    }

    private static ClientProfileRuleDefinition RequireToolContract(string suffix)
    {
        return new ClientProfileRuleDefinition(
            $"tool-{suffix}",
            "Tool call contract is structurally valid",
            ClientProfileRequirementLevel.Required,
            ClientProfileEvidenceBasis.Documented,
            context => EvaluateContractRule(
                context,
                context.ToolCount,
                "tool",
                ToolContractRuleIds,
                context.Result.ToolValidation?.Status,
                "No tools were discovered, so tool contract validation was not applicable.",
                "Observed tools without blocking tool-call contract gaps."));
    }

    private static ClientProfileRuleDefinition RequirePromptContract(string suffix)
    {
        return new ClientProfileRuleDefinition(
            $"prompt-{suffix}",
            "Prompt get/list contract is structurally valid",
            ClientProfileRequirementLevel.Required,
            ClientProfileEvidenceBasis.Documented,
            context => EvaluateContractRule(
                context,
                context.PromptCount,
                "prompt",
                PromptContractRuleIds,
                context.Result.PromptTesting?.Status,
                "No prompts were discovered, so prompt contract validation was not applicable.",
                "Observed prompts without blocking prompt contract gaps."));
    }

    private static ClientProfileRuleDefinition RequireResourceContract(string suffix)
    {
        return new ClientProfileRuleDefinition(
            $"resource-{suffix}",
            "Resource read/list contract is structurally valid",
            ClientProfileRequirementLevel.Required,
            ClientProfileEvidenceBasis.Documented,
            context => EvaluateContractRule(
                context,
                context.ResourceCount,
                "resource",
                ResourceContractRuleIds,
                context.Result.ResourceTesting?.Status,
                "No resources were discovered, so resource contract validation was not applicable.",
                "Observed resources without blocking resource contract gaps."));
    }

    private static ClientProfileRuleDefinition RecommendToolMetadata(string suffix)
    {
        return new ClientProfileRuleDefinition(
            $"tool-{suffix}",
            "Tool presentation and approval metadata is complete",
            ClientProfileRequirementLevel.Recommended,
            ClientProfileEvidenceBasis.Documented,
            context => EvaluateGuidanceRule(
                context,
                context.ToolCount,
                "tool",
                ToolMetadataRuleIds,
                "No tools were discovered, so tool metadata guidance was not applicable.",
                "Tool metadata is sufficiently descriptive for client-side selection and approval flows."));
    }

    private static ClientProfileRuleDefinition RecommendToolSchema(string suffix)
    {
        return new ClientProfileRuleDefinition(
            $"tool-{suffix}",
            "Tool schemas are clear enough for agent planning",
            ClientProfileRequirementLevel.Recommended,
            ClientProfileEvidenceBasis.Documented,
            context => EvaluateGuidanceRule(
                context,
                context.ToolCount,
                "tool",
                ToolSchemaRuleIds,
                "No tools were discovered, so schema-readiness guidance was not applicable.",
                "Tool schemas avoided the AI-readiness gaps tracked by the validator."));
    }

    private static ClientProfileRuleDefinition RecommendPromptMetadata(string suffix)
    {
        return new ClientProfileRuleDefinition(
            $"prompt-{suffix}",
            "Prompt metadata is descriptive enough for guided use",
            ClientProfileRequirementLevel.Recommended,
            ClientProfileEvidenceBasis.Documented,
            context => EvaluateGuidanceRule(
                context,
                context.PromptCount,
                "prompt",
                PromptMetadataRuleIds,
                "No prompts were discovered, so prompt metadata guidance was not applicable.",
                "Prompt metadata includes the descriptions and safety guidance expected by this profile."));
    }

    private static ClientProfileRuleDefinition RecommendResourceMetadata(string suffix)
    {
        return new ClientProfileRuleDefinition(
            $"resource-{suffix}",
            "Resource metadata is descriptive enough for attachment and browsing flows",
            ClientProfileRequirementLevel.Recommended,
            ClientProfileEvidenceBasis.Documented,
            context => EvaluateGuidanceRule(
                context,
                context.ResourceCount,
                "resource",
                ResourceMetadataRuleIds,
                "No resources were discovered, so resource metadata guidance was not applicable.",
                "Resource metadata includes the MIME and template cues expected by this profile."));
    }

    private static ClientProfileRuleDefinition WarnExtraPromptAndResourceSurfaces(string suffix)
    {
        return new ClientProfileRuleDefinition(
            $"surface-{suffix}",
            "Only the tool surface is currently consumed",
            ClientProfileRequirementLevel.Recommended,
            ClientProfileEvidenceBasis.Documented,
            context =>
            {
                if (context.PromptCount == 0 && context.ResourceCount == 0)
                {
                    return ClientProfileRuleObservation.Satisfied("Server exposes tools only, which aligns cleanly with this profile's current surface support.");
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

                return ClientProfileRuleObservation.Warning(
                    $"This profile currently consumes tools only; {string.Join(" and ", ignoredSurfaces)} will not contribute to compatibility.");
            });
    }

    private static ClientProfileRuleObservation EvaluateContractRule(
        ClientProfileContext context,
        int totalComponents,
        string componentLabel,
        IReadOnlyList<string> ruleIds,
        TestStatus? categoryStatus,
        string notApplicableSummary,
        string successSummary)
    {
        if (totalComponents <= 0)
        {
            return ClientProfileRuleObservation.NotApplicable(notApplicableSummary);
        }

        var findings = context.GetFindings(ruleIds);
        if (findings.Count == 0)
        {
            if (categoryStatus == TestStatus.Failed)
            {
                return ClientProfileRuleObservation.Warning(
                    $"The {componentLabel} validation category reported a non-contract failure, but no blocking structured rule matched this profile. Inspect the detailed validation output.");
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

    private static ClientProfileRuleObservation EvaluateGuidanceRule(
        ClientProfileContext context,
        int totalComponents,
        string componentLabel,
        IReadOnlyList<string> ruleIds,
        string notApplicableSummary,
        string successSummary)
    {
        if (totalComponents <= 0)
        {
            return ClientProfileRuleObservation.NotApplicable(notApplicableSummary);
        }

        var findings = context.GetFindings(ruleIds);
        if (findings.Count == 0)
        {
            return ClientProfileRuleObservation.Satisfied(successSummary);
        }

        var affectedComponents = context.GetAffectedComponents(ruleIds);
        var coverage = BuildCoverageLabel(affectedComponents.Count, totalComponents, componentLabel);
        var summarizedRuleIds = findings.Select(finding => finding.RuleId).Distinct(StringComparer.OrdinalIgnoreCase).Take(3).ToList();

        return ClientProfileRuleObservation.Warning(
            $"Advisory {componentLabel} guidance gaps affect {coverage}.",
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
        Func<ClientProfileContext, ClientProfileRuleObservation> Evaluate);

    private sealed class ClientProfileContext
    {
        private readonly IReadOnlyList<ValidationFinding> _findings;

        public ClientProfileContext(ValidationResult result)
        {
            Result = result;
            ToolCount = DetermineToolCount(result.ToolValidation);
            PromptCount = DeterminePromptCount(result.PromptTesting);
            ResourceCount = DetermineResourceCount(result.ResourceTesting);
            _findings = CollectFindings(result);
        }

        public ValidationResult Result { get; }

        public int ToolCount { get; }

        public int PromptCount { get; }

        public int ResourceCount { get; }

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
                .GroupBy(
                    finding => $"{finding.RuleId}|{finding.Component}|{finding.Summary}",
                    StringComparer.OrdinalIgnoreCase)
                .Select(group => group.First())
                .ToList();
        }

        public IReadOnlyList<string> GetAffectedComponents(IReadOnlyList<string> ruleIds)
        {
            return GetFindings(ruleIds)
                .Select(finding => string.IsNullOrWhiteSpace(finding.Component) ? "unknown" : finding.Component)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();
        }

        private static IReadOnlyList<ValidationFinding> CollectFindings(ValidationResult result)
        {
            var findings = new List<ValidationFinding>();

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

        private static int DetermineToolCount(ToolTestResult? toolValidation)
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
                return toolValidation.DiscoveredToolNames
                    .Where(IsRealToolName)
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .Count();
            }

            return toolValidation.ToolResults
                .Select(tool => tool.ToolName)
                .Where(IsRealToolName)
                .Distinct(StringComparer.OrdinalIgnoreCase)
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

        private static bool IsRealToolName(string? toolName)
        {
            return !string.IsNullOrWhiteSpace(toolName)
                && !toolName.Equals("tools/list", StringComparison.OrdinalIgnoreCase)
                && !toolName.Contains("schema compliance", StringComparison.OrdinalIgnoreCase)
                && !toolName.Contains("auth", StringComparison.OrdinalIgnoreCase);
        }
    }

    private sealed record ClientProfileRuleObservation(
        ClientProfileRequirementOutcome Outcome,
        string Summary,
        IReadOnlyList<string> RuleIds,
        IReadOnlyList<string> ExampleComponents,
        string? Recommendation)
    {
        public static ClientProfileRuleObservation Satisfied(string summary)
        {
            return new ClientProfileRuleObservation(ClientProfileRequirementOutcome.Satisfied, summary, Array.Empty<string>(), Array.Empty<string>(), null);
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