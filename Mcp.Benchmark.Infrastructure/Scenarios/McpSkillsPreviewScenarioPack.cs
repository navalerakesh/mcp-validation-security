using Mcp.Benchmark.Core.Abstractions;
using Mcp.Benchmark.Core.Constants;
using Mcp.Benchmark.Core.Models;

namespace Mcp.Benchmark.Infrastructure.Scenarios;

public sealed class McpSkillsPreviewScenarioPack(IMcpHttpClient httpClient) : IValidationScenarioPack
{
    private readonly IReadOnlyList<IValidationScenario> _scenarios =
    [
        new SkillsCatalogScenario(httpClient),
        new SkillsContentScenario(httpClient)
    ];

    public ValidationPackDescriptor Descriptor => new()
    {
        Key = new ValidationDescriptorKey("scenario-pack/skills-preview"),
        Kind = ValidationPackKind.ScenarioPack,
        Revision = new ValidationRevision("2026-11-04.1"),
        DisplayName = "MCP Skills Security Preview",
        Stability = ValidationStability.Preview,
        DocumentationUrl = McpExtensionCatalog.Skills.SpecificationUrl,
        ExtensionId = McpExtensionCatalog.Skills.Id,
        ExtensionStage = ExtensionLifecycleStage.Beta,
        SpecificationRevision = McpExtensionCatalog.Skills.SpecificationRevision,
        SpecificationSha256 = McpExtensionCatalog.Skills.SpecificationSha256,
        ExtensionPolicyBehavior = ExtensionPolicyBehavior.Preview
    };

    public ValidationApplicability Applicability => new()
    {
        ProtocolVersions = ["2026-07-28"],
        RequiredCapabilities = [McpSpecConstants.Capabilities.Resources],
        RequiredExtensionIds = [McpExtensionCatalog.Skills.Id]
    };

    public IReadOnlyList<IValidationScenario> GetScenarios() => _scenarios;

    private sealed class SkillsCatalogScenario(IMcpHttpClient httpClient) : IValidationScenario
    {
        public ValidationScenarioDescriptor Descriptor => new()
        {
            Key = new ValidationDescriptorKey("skills-passive-catalog"),
            Revision = new ValidationRevision("2026-11-04.1"),
            DisplayName = "Skills Passive Catalog Security",
            LayerKey = "extension/skills",
            Stability = ValidationStability.Preview,
            MutatesState = false
        };

        public async Task<ValidationScenarioExecutionResult> ExecuteAsync(
            ValidationScenarioContext context,
            CancellationToken cancellationToken)
        {
            var collection = await SkillsCatalogCollector.CollectAsync(httpClient, context.ServerConfig, cancellationToken);
            if (!collection.IsComplete)
            {
                return collection.IsUnavailable
                    ? Unavailable(collection.Reason!)
                    : Inconclusive(collection.Reason!);
            }

            var analysis = SkillsCatalogAnalyzer.Analyze(collection.Pages);
            var findings = analysis.Findings.ToList();
            if (context.ApplicabilityContext.AdvertisedExtensions.TryGetValue(McpExtensionCatalog.Skills.Id, out var advertisement) &&
                !advertisement.IsValid)
            {
                findings.Add(new ValidationFinding
                {
                    RuleId = ValidationFindingRuleIds.SkillsCapabilitySettingsInvalid,
                    Category = "MCPSkills",
                    Component = McpExtensionCatalog.Skills.Id,
                    Severity = ValidationFindingSeverity.High,
                    Source = ValidationRuleSource.Spec,
                    Summary = "MCP Skills extension settings are malformed.",
                    Recommendation = "Advertise an object and use a boolean directoryRead setting when present.",
                    SpecReference = McpExtensionCatalog.Skills.SpecificationUrl,
                    ExcludedFromAggregate = true
                });
            }
            var failed = analysis.Status == TestStatus.Failed || findings.Any(finding =>
                finding.Source == ValidationRuleSource.Spec && finding.Severity >= ValidationFindingSeverity.High);
            return new ValidationScenarioExecutionResult
            {
                Scenario = new ValidationScenarioResult
                {
                    ScenarioId = Descriptor.Key.Value,
                    DisplayName = Descriptor.DisplayName,
                    Status = failed ? TestStatus.Failed : analysis.Status,
                    Summary = $"Analyzed {analysis.SkillCount} Skills catalog entr{(analysis.SkillCount == 1 ? "y" : "ies")} without reading skill content.",
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
                Observations = analysis.Observations.ToList()
            };
        }

        private ValidationScenarioExecutionResult Unavailable(string reason) => NoEvidence(
            TestStatus.NotRun,
            ValidationCoverageStatus.Unavailable,
            ValidationOutcome.Unavailable,
            reason);

        private ValidationScenarioExecutionResult Inconclusive(string reason) => NoEvidence(
            TestStatus.Inconclusive,
            ValidationCoverageStatus.Inconclusive,
            ValidationOutcome.Inconclusive,
            reason);

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

    private sealed class SkillsContentScenario(IMcpHttpClient httpClient) : IValidationScenario
    {
        private const int MaximumSkills = 16;

        public ValidationScenarioDescriptor Descriptor => new()
        {
            Key = new ValidationDescriptorKey("skills-authorized-content"),
            Revision = new ValidationRevision("2026-11-04.1"),
            DisplayName = "Skills Authorized Content Security",
            LayerKey = "extension/skills",
            Stability = ValidationStability.Preview,
            MutatesState = false
        };

        public async Task<ValidationScenarioExecutionResult> ExecuteAsync(
            ValidationScenarioContext context,
            CancellationToken cancellationToken)
        {
            var mode = context.ValidationConfiguration.Execution?.Mode ?? ExecutionMode.Safe;
            if (mode == ExecutionMode.Safe ||
                !context.ApplicabilityContext.SelectedExtensionIds.Contains(McpExtensionCatalog.Skills.Id, StringComparer.Ordinal))
            {
                return NoEvidence(
                    TestStatus.Skipped,
                    ValidationCoverageStatus.Skipped,
                    ValidationOutcome.Skipped,
                    "Skill content reads require standard or elevated mode and explicit Skills extension selection.");
            }

            var collection = await SkillsCatalogCollector.CollectAsync(httpClient, context.ServerConfig, cancellationToken);
            if (!collection.IsComplete)
            {
                return NoEvidence(
                    collection.IsUnavailable ? TestStatus.NotRun : TestStatus.Inconclusive,
                    collection.IsUnavailable ? ValidationCoverageStatus.Unavailable : ValidationCoverageStatus.Inconclusive,
                    collection.IsUnavailable ? ValidationOutcome.Unavailable : ValidationOutcome.Inconclusive,
                    collection.Reason!);
            }

            var catalog = SkillsCatalogAnalyzer.Analyze(collection.Pages);
            var entries = catalog.Entries
                .Where(entry => !entry.IsDynamic && entry.Resources.ContainsKey(entry.Uri))
                .OrderBy(entry => entry.Uri, StringComparer.Ordinal)
                .ToArray();
            if (entries.Length == 0)
            {
                return NoEvidence(
                    TestStatus.Inconclusive,
                    ValidationCoverageStatus.Inconclusive,
                    ValidationOutcome.Inconclusive,
                    "No content-bound SKILL.md manifest entry was available to verify.");
            }
            if (entries.Length > MaximumSkills)
            {
                return NoEvidence(
                    TestStatus.Inconclusive,
                    ValidationCoverageStatus.Inconclusive,
                    ValidationOutcome.Inconclusive,
                    $"Skills catalog exceeds the bounded {MaximumSkills}-document active analysis limit.");
            }

            var analyses = new List<SkillContentAnalysis>();
            foreach (var entry in entries)
            {
                try
                {
                    var response = await httpClient.CallAsync(
                        context.ServerConfig.Endpoint!,
                        McpSpecConstants.ResourceMethods.Read,
                        new { uri = entry.Uri },
                        context.ServerConfig.Authentication,
                        cancellationToken);
                    if (!response.IsSuccess || string.IsNullOrWhiteSpace(response.RawJson))
                    {
                        analyses.Add(FailedRead(entry.Uri));
                        continue;
                    }
                    analyses.Add(SkillContentAnalyzer.Analyze(entry, entry.Resources[entry.Uri], response.RawJson));
                }
                catch (Exception exception) when (exception is not OperationCanceledException)
                {
                    analyses.Add(FailedRead(entry.Uri));
                }
            }

            var findings = analyses.SelectMany(analysis => analysis.Findings).ToList();
            var failed = findings.Any(finding => finding.Severity >= ValidationFindingSeverity.High);
            return new ValidationScenarioExecutionResult
            {
                Scenario = new ValidationScenarioResult
                {
                    ScenarioId = Descriptor.Key.Value,
                    DisplayName = Descriptor.DisplayName,
                    Status = failed ? TestStatus.Failed : TestStatus.Passed,
                    Summary = $"Verified and analyzed {analyses.Count} SKILL.md document(s) without executing instructions or scripts.",
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
                    Id = $"skills-content/{analysis.SkillUri}",
                    LayerId = Descriptor.LayerKey,
                    Component = analysis.SkillUri,
                    ObservationKind = "skills-content-verification",
                    ScenarioId = Descriptor.Key.Value,
                    Lane = EvaluationLane.ExtensionReadiness,
                    Origin = EvidenceOrigin.DeterministicObservation,
                    Metadata = new Dictionary<string, string>(StringComparer.Ordinal)
                    {
                        ["contentSha256"] = analysis.ContentSha256 ?? string.Empty,
                        ["verifiedAgainstManifest"] = (!analysis.Findings.Any(finding =>
                            finding.RuleId is ValidationFindingRuleIds.SkillsContentIntegrityMismatch or ValidationFindingRuleIds.SkillsFrontmatterMismatch)).ToString()
                    }
                }).ToList()
            };
        }

        private static SkillContentAnalysis FailedRead(string uri) => new(
            uri,
            null,
            [new ValidationFinding
            {
                RuleId = ValidationFindingRuleIds.SkillsContentIntegrityMismatch,
                Category = "MCPSkills",
                Component = uri,
                Severity = ValidationFindingSeverity.High,
                Source = ValidationRuleSource.Spec,
                Summary = "Manifest-listed SKILL.md content could not be read.",
                Recommendation = "Serve every listed skill resource through resources/read and refresh stale manifests.",
                SpecReference = McpExtensionCatalog.Skills.SpecificationUrl,
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
}