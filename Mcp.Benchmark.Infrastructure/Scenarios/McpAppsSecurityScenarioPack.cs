using Mcp.Benchmark.Core.Abstractions;
using Mcp.Benchmark.Core.Constants;
using Mcp.Benchmark.Core.Models;

namespace Mcp.Benchmark.Infrastructure.Scenarios;

public sealed class McpAppsSecurityScenarioPack : IValidationScenarioPack
{
    private readonly IReadOnlyList<IValidationScenario> _scenarios;

    public McpAppsSecurityScenarioPack(IMcpHttpClient? httpClient = null)
    {
        _scenarios = [new AppsPassiveScenario(), new AppsResourceScenario(httpClient)];
    }

    public ValidationPackDescriptor Descriptor => new()
    {
        Key = new ValidationDescriptorKey("scenario-pack/apps-server-security"),
        Kind = ValidationPackKind.ScenarioPack,
        Revision = new ValidationRevision("2026-11-04.1"),
        DisplayName = "MCP Apps Server Security",
        Stability = ValidationStability.Stable,
        DocumentationUrl = McpExtensionCatalog.Apps.SpecificationUrl,
        ExtensionId = McpExtensionCatalog.Apps.Id,
        ExtensionStage = ExtensionLifecycleStage.Stable,
        SpecificationRevision = McpExtensionCatalog.Apps.SpecificationRevision,
        SpecificationSha256 = McpExtensionCatalog.Apps.SpecificationSha256,
        ExtensionPolicyBehavior = ExtensionPolicyBehavior.ExplicitExtensionGate
    };

    public ValidationApplicability Applicability => new()
    {
        RequiredExtensionIds = [McpExtensionCatalog.Apps.Id]
    };

    public IReadOnlyList<IValidationScenario> GetScenarios() => _scenarios;

    private sealed class AppsResourceScenario(IMcpHttpClient? httpClient) : IValidationScenario
    {
        private const int MaximumUiResources = 32;

        public ValidationScenarioDescriptor Descriptor => new()
        {
            Key = new ValidationDescriptorKey("apps-linked-resource-security"),
            Revision = new ValidationRevision("2026-11-04.1"),
            DisplayName = "Apps Linked Resource Security",
            LayerKey = "extension/apps",
            Stability = ValidationStability.Stable,
            MutatesState = false
        };

        public async Task<ValidationScenarioExecutionResult> ExecuteAsync(
            ValidationScenarioContext context,
            CancellationToken cancellationToken)
        {
            if ((context.ValidationConfiguration.Execution?.Mode ?? ExecutionMode.Safe) == ExecutionMode.Safe)
            {
                return NoEvidence(
                    TestStatus.Skipped,
                    ValidationCoverageStatus.Skipped,
                    ValidationOutcome.Skipped,
                    "Linked UI resources are read only in standard or elevated mode.");
            }
            if (httpClient == null)
            {
                return NoEvidence(
                    TestStatus.NotRun,
                    ValidationCoverageStatus.Unavailable,
                    ValidationOutcome.Unavailable,
                    "No governed MCP transport is available for linked resource reads.");
            }

            var resourceUris = context.ValidationResult.ToolValidation?.ToolResults
                .Select(tool => tool.AppMetadata?.ResourceUri)
                .Where(uri => Uri.TryCreate(uri, UriKind.Absolute, out var parsed) && parsed.Scheme == "ui")
                .Cast<string>()
                .Distinct(StringComparer.Ordinal)
                .OrderBy(uri => uri, StringComparer.Ordinal)
                .ToArray()
                ?? Array.Empty<string>();
            if (resourceUris.Length == 0)
            {
                return NoEvidence(
                    TestStatus.Inconclusive,
                    ValidationCoverageStatus.Inconclusive,
                    ValidationOutcome.Inconclusive,
                    "No valid linked ui:// resource URI was available to read.");
            }
            if (resourceUris.Length > MaximumUiResources)
            {
                return NoEvidence(
                    TestStatus.Inconclusive,
                    ValidationCoverageStatus.Inconclusive,
                    ValidationOutcome.Inconclusive,
                    $"Linked UI resource count exceeds the bounded {MaximumUiResources}-resource analysis limit.");
            }

            var analyses = new List<AppsResourceAnalysis>();
            foreach (var resourceUri in resourceUris)
            {
                try
                {
                    var response = await httpClient.CallAsync(
                        context.ServerConfig.Endpoint!,
                        McpSpecConstants.ResourceMethods.Read,
                        new { uri = resourceUri },
                        context.ServerConfig.Authentication,
                        cancellationToken);
                    if (!response.IsSuccess || string.IsNullOrWhiteSpace(response.RawJson))
                    {
                        analyses.Add(FailedRead(resourceUri));
                        continue;
                    }
                    analyses.Add(McpAppResourceAnalyzer.Analyze(resourceUri, response.RawJson));
                }
                catch (Exception exception) when (exception is not OperationCanceledException)
                {
                    analyses.Add(FailedRead(resourceUri));
                }
            }

            var findings = analyses.SelectMany(analysis => analysis.Findings).ToList();
            var failed = findings.Any(finding =>
                finding.Severity >= ValidationFindingSeverity.High &&
                finding.Source is ValidationRuleSource.Spec or ValidationRuleSource.Heuristic);
            return new ValidationScenarioExecutionResult
            {
                Scenario = new ValidationScenarioResult
                {
                    ScenarioId = Descriptor.Key.Value,
                    DisplayName = Descriptor.DisplayName,
                    Status = failed ? TestStatus.Failed : TestStatus.Passed,
                    Summary = $"Read and statically analyzed {analyses.Count} linked MCP Apps UI resource(s).",
                    Findings = findings
                },
                Coverage =
                [
                    new ValidationCoverageDeclaration
                    {
                        LayerId = Descriptor.LayerKey,
                        Scope = Descriptor.Key.Value,
                        Status = ValidationCoverageStatus.Covered,
                        ObservedOutcome = failed ? ValidationOutcome.Failed : ValidationOutcome.Succeeded,
                        Lane = EvaluationLane.ExtensionReadiness,
                        Origin = EvidenceOrigin.DeterministicObservation,
                        Confidence = EvidenceConfidenceLevel.High
                    }
                ],
                Observations = analyses.Select(analysis => new ValidationObservation
                {
                    Id = $"apps-resource/{analysis.ResourceUri}",
                    LayerId = Descriptor.LayerKey,
                    Component = analysis.ResourceUri,
                    ObservationKind = "apps-ui-resource-analysis",
                    ScenarioId = Descriptor.Key.Value,
                    Lane = EvaluationLane.ExtensionReadiness,
                    Origin = EvidenceOrigin.DeterministicObservation,
                    Metadata = new Dictionary<string, string>(StringComparer.Ordinal)
                    {
                        ["contentSha256"] = analysis.ContentSha256 ?? string.Empty,
                        ["permissions"] = string.Join(",", analysis.Permissions),
                        ["cspDirectiveCount"] = analysis.Csp.Count.ToString(System.Globalization.CultureInfo.InvariantCulture)
                    }
                }).ToList()
            };
        }

        private static AppsResourceAnalysis FailedRead(string resourceUri) => new(
            resourceUri,
            null,
            new Dictionary<string, IReadOnlyList<string>>(StringComparer.Ordinal),
            [],
            [new ValidationFinding
            {
                RuleId = ValidationFindingRuleIds.AppsResourceReadInvalid,
                Category = "MCPApps",
                Component = resourceUri,
                Severity = ValidationFindingSeverity.High,
                Source = ValidationRuleSource.Spec,
                Summary = "Linked MCP Apps UI resource could not be read.",
                Recommendation = "Serve every _meta.ui.resourceUri through resources/read under the active access policy.",
                SpecReference = McpExtensionCatalog.Apps.SpecificationUrl,
                ExcludedFromAggregate = true
            }]);

        private ValidationScenarioExecutionResult NoEvidence(
            TestStatus status,
            ValidationCoverageStatus coverageStatus,
            ValidationOutcome outcome,
            string reason) => new()
            {
                Scenario = new ValidationScenarioResult
                {
                    ScenarioId = Descriptor.Key.Value,
                    DisplayName = Descriptor.DisplayName,
                    Status = status,
                    Summary = reason
                },
                Coverage =
                [
                    new ValidationCoverageDeclaration
                    {
                        LayerId = Descriptor.LayerKey,
                        Scope = Descriptor.Key.Value,
                        Status = coverageStatus,
                        ObservedOutcome = outcome,
                        Lane = EvaluationLane.ExtensionReadiness,
                        Origin = EvidenceOrigin.DeterministicObservation,
                        Confidence = EvidenceConfidenceLevel.None,
                        Reason = reason
                    }
                ]
            };
    }

    private sealed class AppsPassiveScenario : IValidationScenario
    {
        public ValidationScenarioDescriptor Descriptor => new()
        {
            Key = new ValidationDescriptorKey("apps-passive-tool-linkage"),
            Revision = new ValidationRevision("2026-11-04.1"),
            DisplayName = "Apps Passive Tool And Resource Linkage",
            LayerKey = "extension/apps",
            Stability = ValidationStability.Stable,
            MutatesState = false
        };

        public Task<ValidationScenarioExecutionResult> ExecuteAsync(
            ValidationScenarioContext context,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var tools = context.ValidationResult.ToolValidation?.ToolResults;
            if (tools == null)
            {
                return Task.FromResult(CreateUnavailable("Tool catalog evidence is unavailable."));
            }

            var appTools = tools.Where(tool => tool.AppMetadata?.IsPresent == true).ToArray();
            if (appTools.Length == 0)
            {
                return Task.FromResult(CreateInconclusive("The server advertised MCP Apps but no UI-enabled tool metadata was observed."));
            }

            var findings = appTools.SelectMany(tool => AnalyzeTool(tool, context.ValidationResult.ResourceTesting)).ToList();
            if (context.ApplicabilityContext.AdvertisedExtensions.TryGetValue(McpExtensionCatalog.Apps.Id, out var advertisement) &&
                !advertisement.IsValid)
            {
                findings.Add(Finding(
                    ValidationFindingRuleIds.AppsCapabilitySettingsInvalid,
                    McpExtensionCatalog.Apps.Id,
                    ValidationFindingSeverity.High,
                    ValidationRuleSource.Spec,
                    "MCP Apps extension settings are malformed or omit the required HTML MIME type.",
                    $"Advertise a mimeTypes array containing {McpExtensionCatalog.Apps.HtmlMimeType}."));
            }
            var failed = findings.Any(finding =>
                finding.Source == ValidationRuleSource.Spec &&
                finding.Severity >= ValidationFindingSeverity.High);
            var status = failed ? TestStatus.Failed : TestStatus.Passed;
            return Task.FromResult(new ValidationScenarioExecutionResult
            {
                Scenario = new ValidationScenarioResult
                {
                    ScenarioId = Descriptor.Key.Value,
                    DisplayName = Descriptor.DisplayName,
                    Status = status,
                    Summary = failed
                        ? "MCP Apps tool or resource metadata violates stable server requirements."
                        : $"Validated {appTools.Length} MCP Apps tool linkage declaration(s).",
                    Findings = findings
                },
                Coverage =
                [
                    new ValidationCoverageDeclaration
                    {
                        LayerId = Descriptor.LayerKey,
                        Scope = Descriptor.Key.Value,
                        Status = ValidationCoverageStatus.Covered,
                        ObservedOutcome = failed ? ValidationOutcome.Failed : ValidationOutcome.Succeeded,
                        Lane = EvaluationLane.ExtensionReadiness,
                        Origin = EvidenceOrigin.DeterministicObservation,
                        Confidence = EvidenceConfidenceLevel.High
                    }
                ],
                Observations = appTools.Select(tool => new ValidationObservation
                {
                    Id = $"apps-tool/{tool.ToolName}",
                    LayerId = Descriptor.LayerKey,
                    Component = tool.ToolName,
                    ObservationKind = "apps-tool-linkage",
                    ScenarioId = Descriptor.Key.Value,
                    Lane = EvaluationLane.ExtensionReadiness,
                    Origin = EvidenceOrigin.DeterministicObservation,
                    Metadata = new Dictionary<string, string>(StringComparer.Ordinal)
                    {
                        ["resourceUri"] = tool.AppMetadata?.ResourceUri ?? string.Empty,
                        ["visibility"] = string.Join(",", ResolveVisibility(tool.AppMetadata!)),
                        ["deprecatedResourceUri"] = tool.AppMetadata!.UsesDeprecatedResourceUri.ToString()
                    }
                }).ToList()
            });
        }

        private static IEnumerable<ValidationFinding> AnalyzeTool(
            IndividualToolResult tool,
            ResourceTestResult? resources)
        {
            var metadata = tool.AppMetadata!;
            if (metadata.IsMalformed)
            {
                yield return Finding(
                    ValidationFindingRuleIds.AppsToolMetadataMalformed,
                    tool.ToolName,
                    ValidationFindingSeverity.High,
                    ValidationRuleSource.Spec,
                    "MCP Apps tool metadata is malformed.",
                    "Use the nested _meta.ui object with typed resourceUri and visibility fields.");
            }

            if (!TryValidateUiResourceUri(metadata.ResourceUri))
            {
                yield return Finding(
                    ValidationFindingRuleIds.AppsResourceUriInvalid,
                    tool.ToolName,
                    ValidationFindingSeverity.High,
                    ValidationRuleSource.Spec,
                    "MCP Apps resourceUri must be an absolute ui:// URI.",
                    "Declare a stable absolute ui:// resource URI in _meta.ui.resourceUri.");
            }

            var visibility = ResolveVisibility(metadata);
            if (visibility.Count == 0 ||
                visibility.Distinct(StringComparer.Ordinal).Count() != visibility.Count ||
                visibility.Any(value => value is not ("model" or "app")))
            {
                yield return Finding(
                    ValidationFindingRuleIds.AppsVisibilityInvalid,
                    tool.ToolName,
                    ValidationFindingSeverity.High,
                    ValidationRuleSource.Spec,
                    "MCP Apps visibility must contain unique 'model' and/or 'app' values.",
                    "Use _meta.ui.visibility with unique model/app entries, or omit it for the default.");
            }

            if (metadata.UsesDeprecatedResourceUri)
            {
                yield return Finding(
                    ValidationFindingRuleIds.AppsDeprecatedResourceUri,
                    tool.ToolName,
                    ValidationFindingSeverity.Low,
                    ValidationRuleSource.Guideline,
                    "Tool uses the deprecated flat _meta['ui/resourceUri'] field.",
                    "Move the URI to _meta.ui.resourceUri.");
            }

            if (visibility.Contains("model", StringComparer.Ordinal) && string.IsNullOrWhiteSpace(tool.Description))
            {
                yield return Finding(
                    ValidationFindingRuleIds.AppsFallbackDescriptionMissing,
                    tool.ToolName,
                    ValidationFindingSeverity.Medium,
                    ValidationRuleSource.Guideline,
                    "UI-enabled model-visible tool lacks a meaningful non-UI description.",
                    "Describe the tool's non-UI purpose for hosts that do not support MCP Apps.");
            }

            var linkedResource = resources?.ResourceResults.FirstOrDefault(resource =>
                string.Equals(resource.ResourceUri, metadata.ResourceUri, StringComparison.Ordinal));
            if (linkedResource?.MimeType != null &&
                !string.Equals(linkedResource.MimeType, McpExtensionCatalog.Apps.HtmlMimeType, StringComparison.OrdinalIgnoreCase))
            {
                yield return Finding(
                    ValidationFindingRuleIds.AppsResourceMimeTypeInvalid,
                    metadata.ResourceUri ?? tool.ToolName,
                    ValidationFindingSeverity.High,
                    ValidationRuleSource.Spec,
                    $"MCP Apps UI resource uses unsupported MIME type '{linkedResource.MimeType}'.",
                    $"Return {McpExtensionCatalog.Apps.HtmlMimeType} for UI resources.");
            }
        }

        private static IReadOnlyList<string> ResolveVisibility(McpAppToolMetadata metadata) =>
            metadata.Visibility.Count == 0 && !metadata.IsMalformed
                ? ["model", "app"]
                : metadata.Visibility;

        private static bool TryValidateUiResourceUri(string? value) =>
            Uri.TryCreate(value, UriKind.Absolute, out var uri) &&
            string.Equals(uri.Scheme, "ui", StringComparison.Ordinal) &&
            !string.IsNullOrWhiteSpace(uri.Host);

        private static ValidationFinding Finding(
            string ruleId,
            string component,
            ValidationFindingSeverity severity,
            ValidationRuleSource source,
            string summary,
            string recommendation) => new()
            {
                RuleId = ruleId,
                Category = "MCPApps",
                Component = component,
                Severity = severity,
                Source = source,
                Summary = summary,
                Recommendation = recommendation,
                SpecReference = McpExtensionCatalog.Apps.SpecificationUrl,
                ExcludedFromAggregate = true
            };

        private static ValidationScenarioExecutionResult CreateUnavailable(string reason) => CreateNoEvidence(
            TestStatus.NotRun,
            ValidationCoverageStatus.Unavailable,
            ValidationOutcome.Unavailable,
            reason);

        private static ValidationScenarioExecutionResult CreateInconclusive(string reason) => CreateNoEvidence(
            TestStatus.Inconclusive,
            ValidationCoverageStatus.Inconclusive,
            ValidationOutcome.Inconclusive,
            reason);

        private static ValidationScenarioExecutionResult CreateNoEvidence(
            TestStatus status,
            ValidationCoverageStatus coverageStatus,
            ValidationOutcome outcome,
            string reason) => new()
            {
                Scenario = new ValidationScenarioResult
                {
                    ScenarioId = "apps-passive-tool-linkage",
                    DisplayName = "Apps Passive Tool And Resource Linkage",
                    Status = status,
                    Summary = reason
                },
                Coverage =
                [
                    new ValidationCoverageDeclaration
                    {
                        LayerId = "extension/apps",
                        Scope = "apps-passive-tool-linkage",
                        Status = coverageStatus,
                        ObservedOutcome = outcome,
                        Lane = EvaluationLane.ExtensionReadiness,
                        Origin = EvidenceOrigin.DeterministicObservation,
                        Confidence = EvidenceConfidenceLevel.None,
                        Reason = reason
                    }
                ]
            };
    }
}