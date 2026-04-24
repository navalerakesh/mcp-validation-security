using Mcp.Benchmark.Core.Models;

namespace Mcp.Benchmark.Core.Services;

public static class ValidationVerdictEngine
{
    public static VerdictAssessment Calculate(ValidationResult result)
    {
        ArgumentNullException.ThrowIfNull(result);

        var decisions = new List<DecisionRecord>();
        var coverageDecisions = new List<DecisionRecord>();
        var decisionIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var coverageDecisionIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        ProjectExecutionErrors(result, decisions, decisionIds);
        ProjectTierChecks(result, decisions, decisionIds);
        ProjectProtocolViolations(result, decisions, decisionIds);
        ProjectSecurityVulnerabilities(result, decisions, decisionIds);
        ProjectBoundaryFindings(result, decisions, decisionIds);
        ProjectStructuredFindings(result, decisions, decisionIds);
        ProjectContentSafetyFindings(result, decisions, decisionIds);
        ProjectCoverage(result, coverageDecisions, coverageDecisionIds);

        var orderedDecisions = Order(decisions);
        var orderedCoverageDecisions = Order(coverageDecisions);
        var blockingDecisions = Order(orderedDecisions
            .Concat(orderedCoverageDecisions)
            .Where(decision => decision.Gate >= GateOutcome.CoverageDebt)
            .ToList());

        return new VerdictAssessment
        {
            BaselineVerdict = DetermineBaselineVerdict(orderedDecisions, orderedCoverageDecisions),
            ProtocolVerdict = DetermineProtocolVerdict(result, orderedDecisions),
            CoverageVerdict = DetermineCoverageVerdict(result, orderedCoverageDecisions),
            Summary = BuildSummary(result, orderedDecisions, orderedCoverageDecisions),
            TriggeredDecisions = orderedDecisions,
            BlockingDecisions = blockingDecisions,
            CoverageDecisions = orderedCoverageDecisions
        };
    }

    public static bool IsPassing(VerdictAssessment? assessment)
    {
        if (assessment == null)
        {
            return false;
        }

        return IsPassing(assessment.BaselineVerdict)
            && IsPassing(assessment.ProtocolVerdict)
            && assessment.CoverageVerdict == ValidationVerdict.Trusted;
    }

    private static bool IsPassing(ValidationVerdict verdict)
    {
        return verdict is ValidationVerdict.ConditionallyAcceptable or ValidationVerdict.Trusted;
    }

    private static void ProjectExecutionErrors(ValidationResult result, List<DecisionRecord> decisions, HashSet<string> decisionIds)
    {
        foreach (var criticalError in result.CriticalErrors.Where(error => !string.IsNullOrWhiteSpace(error)))
        {
            AddDecision(
                decisions,
                decisionIds,
                new DecisionRecord
                {
                    DecisionId = $"baseline:execution:{NormalizeToken(criticalError)}",
                    Lane = EvaluationLane.Baseline,
                    Authority = ValidationRuleSource.Heuristic,
                    Origin = EvidenceOrigin.DeterministicObservation,
                    Gate = GateOutcome.Reject,
                    Severity = ValidationFindingSeverity.Critical,
                    Category = "Execution",
                    Component = "validation-run",
                    Summary = $"Critical execution error recorded: {criticalError}",
                    ImpactAreas = [ImpactArea.OperationalResilience]
                });
        }
    }

    private static void ProjectTierChecks(ValidationResult result, List<DecisionRecord> decisions, HashSet<string> decisionIds)
    {
        if (result.TrustAssessment?.TierChecks is not { Count: > 0 } tierChecks)
        {
            return;
        }

        foreach (var check in tierChecks.Where(check => !check.Passed))
        {
            var normalizedTier = check.Tier?.Trim();
            var gate = normalizedTier switch
            {
                var tier when string.Equals(tier, "MUST", StringComparison.OrdinalIgnoreCase) => GateOutcome.Reject,
                var tier when string.Equals(tier, "SHOULD", StringComparison.OrdinalIgnoreCase) => GateOutcome.ReviewRequired,
                _ => GateOutcome.Note
            };
            var severity = gate switch
            {
                GateOutcome.Reject => ValidationFindingSeverity.Critical,
                GateOutcome.ReviewRequired => ValidationFindingSeverity.High,
                _ => ValidationFindingSeverity.Low
            };

            AddDecision(
                decisions,
                decisionIds,
                new DecisionRecord
                {
                    DecisionId = $"baseline:tier:{NormalizeToken(normalizedTier)}:{NormalizeToken(check.Component)}:{NormalizeToken(check.Requirement)}",
                    Lane = EvaluationLane.Baseline,
                    Authority = ValidationRuleSource.Spec,
                    Origin = EvidenceOrigin.DeterministicAggregation,
                    Gate = gate,
                    Severity = severity,
                    Category = "TierCheck",
                    Component = string.IsNullOrWhiteSpace(check.Component) ? "tier-check" : check.Component,
                    Summary = $"{normalizedTier} requirement failed: {check.Requirement}{FormatDetail(check.Detail)}",
                    ImpactAreas = [ImpactArea.ProtocolInteroperability, ImpactArea.CapabilityContract]
                });
        }
    }

    private static void ProjectProtocolViolations(ValidationResult result, List<DecisionRecord> decisions, HashSet<string> decisionIds)
    {
        if (result.ProtocolCompliance?.Violations is not { Count: > 0 } violations)
        {
            return;
        }

        foreach (var violation in violations)
        {
            var severity = MapSeverity(violation.Severity);
            AddDecision(
                decisions,
                decisionIds,
                new DecisionRecord
                {
                    DecisionId = $"baseline:protocol:{NormalizeToken(violation.CheckId)}:{NormalizeToken(violation.Category)}:{NormalizeToken(violation.Description)}",
                    RuleId = string.IsNullOrWhiteSpace(violation.CheckId) ? null : violation.CheckId,
                    Lane = EvaluationLane.Baseline,
                    Authority = ValidationRuleSourceClassifier.GetSource(violation),
                    Origin = EvidenceOrigin.DeterministicObservation,
                    Gate = GateForProtocolSeverity(violation.Severity),
                    Severity = severity,
                    Category = string.IsNullOrWhiteSpace(violation.Category) ? "ProtocolCompliance" : violation.Category,
                    Component = string.IsNullOrWhiteSpace(violation.Category) ? "protocol" : violation.Category,
                    Summary = violation.Description,
                    ImpactAreas = [ImpactArea.ProtocolInteroperability, ImpactArea.CapabilityContract]
                });
        }
    }

    private static void ProjectSecurityVulnerabilities(ValidationResult result, List<DecisionRecord> decisions, HashSet<string> decisionIds)
    {
        if (result.SecurityTesting?.Vulnerabilities is not { Count: > 0 } vulnerabilities)
        {
            return;
        }

        foreach (var vulnerability in vulnerabilities)
        {
            var severity = MapSeverity(vulnerability.Severity);
            AddDecision(
                decisions,
                decisionIds,
                new DecisionRecord
                {
                    DecisionId = $"baseline:security:{NormalizeToken(vulnerability.Id)}:{NormalizeToken(vulnerability.AffectedComponent)}",
                    RuleId = string.IsNullOrWhiteSpace(vulnerability.Id) ? null : vulnerability.Id,
                    Lane = EvaluationLane.Baseline,
                    Authority = ValidationRuleSourceClassifier.GetSource(vulnerability),
                    Origin = EvidenceOrigin.DeterministicObservation,
                    Gate = GateForVulnerability(vulnerability),
                    Severity = severity,
                    Category = string.IsNullOrWhiteSpace(vulnerability.Category) ? "SecurityTesting" : vulnerability.Category,
                    Component = string.IsNullOrWhiteSpace(vulnerability.AffectedComponent) ? "security" : vulnerability.AffectedComponent,
                    Summary = vulnerability.Description,
                    ImpactAreas = GetImpactAreas(vulnerability.Category, vulnerability.AffectedComponent, vulnerability.Name, vulnerability.Description)
                });
        }

        foreach (var attack in result.SecurityTesting.AttackSimulations.Where(attack => attack.AttackSuccessful))
        {
            AddDecision(
                decisions,
                decisionIds,
                new DecisionRecord
                {
                    DecisionId = $"baseline:attack:{NormalizeToken(attack.AttackVector)}:{NormalizeToken(attack.Description)}",
                    Lane = EvaluationLane.Baseline,
                    Authority = ValidationRuleSource.Heuristic,
                    Origin = EvidenceOrigin.DeterministicObservation,
                    Gate = GateForAttackSimulation(attack),
                    Severity = SeverityForAttackSimulation(attack),
                    Category = "SecuritySimulation",
                    Component = string.IsNullOrWhiteSpace(attack.AttackVector) ? "security" : attack.AttackVector,
                    Summary = string.IsNullOrWhiteSpace(attack.Description) ? "Attack simulation succeeded." : attack.Description,
                    ImpactAreas = GetImpactAreas(attack.AttackVector, attack.AttackVector, attack.Description, attack.ServerResponse)
                });
        }
    }

    private static void ProjectBoundaryFindings(ValidationResult result, List<DecisionRecord> decisions, HashSet<string> decisionIds)
    {
        if (result.TrustAssessment?.BoundaryFindings is not { Count: > 0 } findings)
        {
            return;
        }

        foreach (var finding in findings)
        {
            var severity = MapSeverity(finding.Severity);
            AddDecision(
                decisions,
                decisionIds,
                new DecisionRecord
                {
                    DecisionId = $"baseline:boundary:{NormalizeToken(finding.Category)}:{NormalizeToken(finding.Component)}:{NormalizeToken(finding.Description)}",
                    Lane = EvaluationLane.Baseline,
                    Authority = ValidationRuleSource.Heuristic,
                    Origin = EvidenceOrigin.HeuristicInference,
                    Gate = GateForBoundaryFinding(finding),
                    Severity = severity,
                    Category = string.IsNullOrWhiteSpace(finding.Category) ? "Boundary" : finding.Category,
                    Component = string.IsNullOrWhiteSpace(finding.Component) ? "boundary" : finding.Component,
                    Summary = finding.Description,
                    ImpactAreas = GetImpactAreas(finding.Category, finding.Component, finding.Description, finding.Mitigation)
                });
        }
    }

    private static void ProjectStructuredFindings(ValidationResult result, List<DecisionRecord> decisions, HashSet<string> decisionIds)
    {
        foreach (var finding in CollectDistinctFindings(
                     result.ProtocolCompliance?.Findings,
                     result.ToolValidation?.AuthenticationSecurity?.StructuredFindings,
                     result.ToolValidation?.Findings,
                     result.ToolValidation?.AiReadinessFindings,
                     result.ToolValidation?.ToolResults.SelectMany(tool => tool.Findings),
                     result.ResourceTesting?.Findings,
                     result.ResourceTesting?.ResourceResults.SelectMany(resource => resource.Findings),
                     result.PromptTesting?.Findings,
                     result.PromptTesting?.PromptResults.SelectMany(prompt => prompt.Findings),
                     result.SecurityTesting?.Findings,
                     result.PerformanceTesting?.Findings,
                     result.ErrorHandling?.Findings))
        {
            AddDecision(
                decisions,
                decisionIds,
                new DecisionRecord
                {
                    DecisionId = $"baseline:finding:{NormalizeToken(finding.RuleId)}:{NormalizeToken(finding.Component)}:{NormalizeToken(finding.Summary)}",
                    RuleId = string.IsNullOrWhiteSpace(finding.RuleId) ? null : finding.RuleId,
                    Lane = EvaluationLane.Baseline,
                    Authority = finding.EffectiveSource,
                    Origin = OriginForFinding(finding),
                    Gate = GateForFinding(finding),
                    Severity = finding.Severity,
                    Category = string.IsNullOrWhiteSpace(finding.Category) ? "Validation" : finding.Category,
                    Component = string.IsNullOrWhiteSpace(finding.Component) ? "validation" : finding.Component,
                    Summary = finding.Summary,
                    SpecReference = finding.EffectiveSpecReference,
                    ImpactAreas = GetImpactAreas(finding.Category, finding.Component, finding.RuleId, finding.Summary),
                    Metadata = new Dictionary<string, string>(finding.Metadata, StringComparer.OrdinalIgnoreCase)
                });
        }
    }

    private static void ProjectContentSafetyFindings(ValidationResult result, List<DecisionRecord> decisions, HashSet<string> decisionIds)
    {
        foreach (var finding in CollectDistinctContentSafetyFindings(
                     result.ToolValidation?.ContentSafetyFindings,
                     result.ResourceTesting?.ContentSafetyFindings,
                     result.PromptTesting?.ContentSafetyFindings))
        {
            var category = $"ContentSafety.{finding.Axis}";
            AddDecision(
                decisions,
                decisionIds,
                new DecisionRecord
                {
                    DecisionId = $"baseline:content:{NormalizeToken(category)}:{NormalizeToken(finding.ItemName)}:{NormalizeToken(finding.Reason)}",
                    Lane = EvaluationLane.Baseline,
                    Authority = ValidationRuleSource.Heuristic,
                    Origin = EvidenceOrigin.HeuristicInference,
                    Gate = GateForContentSafety(finding),
                    Severity = MapSeverity(finding.RiskLevel),
                    Category = category,
                    Component = string.IsNullOrWhiteSpace(finding.ItemName) ? finding.ItemKind.ToString() : finding.ItemName,
                    Summary = finding.Reason,
                    ImpactAreas = GetImpactAreas(category, finding.ItemName, finding.Axis.ToString(), finding.Reason)
                });
        }
    }

    private static void ProjectCoverage(ValidationResult result, List<DecisionRecord> decisions, HashSet<string> decisionIds)
    {
        foreach (var coverage in result.Evidence.Coverage)
        {
            if (coverage.Status is ValidationCoverageStatus.Covered or ValidationCoverageStatus.NotApplicable)
            {
                continue;
            }

            AddDecision(
                decisions,
                decisionIds,
                new DecisionRecord
                {
                    DecisionId = $"coverage:{NormalizeToken(coverage.LayerId)}:{NormalizeToken(coverage.Scope)}:{coverage.Status}",
                    Lane = EvaluationLane.Baseline,
                    Authority = ValidationRuleSource.Heuristic,
                    Origin = EvidenceOrigin.DeterministicAggregation,
                    Gate = GateOutcome.CoverageDebt,
                    Severity = coverage.Status is ValidationCoverageStatus.Blocked or ValidationCoverageStatus.Unavailable
                        ? ValidationFindingSeverity.High
                        : ValidationFindingSeverity.Medium,
                    Category = "Coverage",
                    Component = string.IsNullOrWhiteSpace(coverage.LayerId) ? "coverage" : coverage.LayerId,
                    Summary = BuildCoverageSummary(coverage),
                    ImpactAreas = [ImpactArea.CoverageIntegrity]
                });
        }
    }

    private static List<DecisionRecord> Order(List<DecisionRecord> decisions)
    {
        return decisions
            .OrderByDescending(decision => decision.Gate)
            .ThenByDescending(decision => decision.Severity)
            .ThenBy(decision => decision.Category, StringComparer.OrdinalIgnoreCase)
            .ThenBy(decision => decision.Component, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private static void AddDecision(List<DecisionRecord> decisions, HashSet<string> decisionIds, DecisionRecord decision)
    {
        if (decision.Gate == GateOutcome.Note && decision.Severity == ValidationFindingSeverity.Info)
        {
            return;
        }

        if (decisionIds.Add(decision.DecisionId))
        {
            decisions.Add(decision);
        }
    }

    private static ValidationVerdict DetermineBaselineVerdict(IEnumerable<DecisionRecord> decisions, IEnumerable<DecisionRecord> coverageDecisions)
    {
        var combined = decisions.Concat(coverageDecisions).ToList();
        if (combined.Any(decision => decision.Gate == GateOutcome.Reject))
        {
            return ValidationVerdict.Reject;
        }

        if (combined.Any(decision => decision.Gate is GateOutcome.ReviewRequired or GateOutcome.CoverageDebt))
        {
            return ValidationVerdict.ReviewRequired;
        }

        if (combined.Count == 0)
        {
            return ValidationVerdict.Trusted;
        }

        return combined.Any(decision => decision.Gate == GateOutcome.Note)
            ? ValidationVerdict.ConditionallyAcceptable
            : ValidationVerdict.Trusted;
    }

    private static ValidationVerdict DetermineProtocolVerdict(ValidationResult result, IEnumerable<DecisionRecord> decisions)
    {
        var protocolDecisions = decisions
            .Where(decision => decision.Authority == ValidationRuleSource.Spec || decision.ImpactAreas.Contains(ImpactArea.ProtocolInteroperability))
            .ToList();

        if (protocolDecisions.Any(decision => decision.Gate == GateOutcome.Reject))
        {
            return ValidationVerdict.Reject;
        }

        if (protocolDecisions.Any(decision => decision.Gate == GateOutcome.ReviewRequired))
        {
            return ValidationVerdict.ReviewRequired;
        }

        if (protocolDecisions.Count == 0)
        {
            return result.ProtocolCompliance == null ? ValidationVerdict.Unknown : ValidationVerdict.Trusted;
        }

        return protocolDecisions.Any(decision => decision.Gate == GateOutcome.Note)
            ? ValidationVerdict.ConditionallyAcceptable
            : ValidationVerdict.Trusted;
    }

    private static ValidationVerdict DetermineCoverageVerdict(ValidationResult result, IEnumerable<DecisionRecord> coverageDecisions)
    {
        var coverageList = coverageDecisions.ToList();
        if (coverageList.Any())
        {
            return ValidationVerdict.ReviewRequired;
        }

        return result.Evidence.Coverage.Count == 0
            ? ValidationVerdict.Unknown
            : ValidationVerdict.Trusted;
    }

    private static string BuildSummary(ValidationResult result, IReadOnlyCollection<DecisionRecord> decisions, IReadOnlyCollection<DecisionRecord> coverageDecisions)
    {
        var protocolVerdict = DetermineProtocolVerdict(result, decisions);
        var coverageVerdict = DetermineCoverageVerdict(result, coverageDecisions);
        var baselineVerdict = DetermineBaselineVerdict(decisions, coverageDecisions);
        var blockingCount = decisions.Count(decision => decision.Gate >= GateOutcome.ReviewRequired)
            + coverageDecisions.Count(decision => decision.Gate >= GateOutcome.CoverageDebt);

        return $"Baseline={baselineVerdict}; Protocol={protocolVerdict}; Coverage={coverageVerdict}; BlockingDecisions={blockingCount}.";
    }

    private static IEnumerable<ValidationFinding> CollectDistinctFindings(params IEnumerable<ValidationFinding>?[] sources)
    {
        return sources
            .Where(source => source != null)
            .SelectMany(source => source!)
            .Where(finding => !string.IsNullOrWhiteSpace(finding.Summary))
            .GroupBy(
                finding => $"{finding.RuleId}|{finding.Category}|{finding.Component}|{finding.Summary}",
                StringComparer.OrdinalIgnoreCase)
            .Select(group => group.First());
    }

    private static IEnumerable<ContentSafetyFinding> CollectDistinctContentSafetyFindings(params IEnumerable<ContentSafetyFinding>?[] sources)
    {
        return sources
            .Where(source => source != null)
            .SelectMany(source => source!)
            .Where(finding => finding.RiskLevel != ContentRiskLevel.None && !string.IsNullOrWhiteSpace(finding.Reason))
            .GroupBy(
                finding => $"{finding.ItemKind}|{finding.ItemName}|{finding.Axis}|{finding.Reason}",
                StringComparer.OrdinalIgnoreCase)
            .Select(group => group.First());
    }

    private static GateOutcome GateForProtocolSeverity(ViolationSeverity severity)
    {
        return severity switch
        {
            ViolationSeverity.Critical or ViolationSeverity.High => GateOutcome.Reject,
            _ => GateOutcome.ReviewRequired
        };
    }

    private static GateOutcome GateForVulnerability(SecurityVulnerability vulnerability)
    {
        if (vulnerability.IsExploitable || vulnerability.Severity is VulnerabilitySeverity.Critical or VulnerabilitySeverity.High)
        {
            return GateOutcome.Reject;
        }

        return vulnerability.Severity switch
        {
            VulnerabilitySeverity.Medium => GateOutcome.ReviewRequired,
            VulnerabilitySeverity.Low => GateOutcome.Note,
            _ => GateOutcome.Note
        };
    }

    private static GateOutcome GateForBoundaryFinding(AiBoundaryFinding finding)
    {
        var category = finding.Category ?? string.Empty;
        var severity = MapSeverity(finding.Severity);

        if (category.Contains("exfiltration", StringComparison.OrdinalIgnoreCase)
            || category.Contains("humaninloop", StringComparison.OrdinalIgnoreCase)
            || category.Contains("promptinjection", StringComparison.OrdinalIgnoreCase))
        {
            return severity >= ValidationFindingSeverity.High ? GateOutcome.Reject : GateOutcome.ReviewRequired;
        }

        return severity >= ValidationFindingSeverity.High
            ? GateOutcome.ReviewRequired
            : GateOutcome.Note;
    }

    private static GateOutcome GateForFinding(ValidationFinding finding)
    {
        var authority = finding.EffectiveSource;
        var ruleId = finding.RuleId ?? string.Empty;

        if (authority == ValidationRuleSource.Spec)
        {
            return finding.Severity >= ValidationFindingSeverity.High ? GateOutcome.Reject : GateOutcome.ReviewRequired;
        }

        if (ruleId.StartsWith("AI.", StringComparison.OrdinalIgnoreCase))
        {
            return finding.Severity >= ValidationFindingSeverity.High ? GateOutcome.Reject : GateOutcome.ReviewRequired;
        }

        return finding.Severity switch
        {
            ValidationFindingSeverity.Critical => GateOutcome.Reject,
            ValidationFindingSeverity.High or ValidationFindingSeverity.Medium => GateOutcome.ReviewRequired,
            _ => GateOutcome.Note
        };
    }

    private static GateOutcome GateForContentSafety(ContentSafetyFinding finding)
    {
        return finding.RiskLevel switch
        {
            ContentRiskLevel.High => GateOutcome.Reject,
            ContentRiskLevel.Medium => GateOutcome.ReviewRequired,
            _ => GateOutcome.Note
        };
    }

    private static GateOutcome GateForAttackSimulation(AttackSimulationResult attack)
    {
        var attackText = string.Join(' ', new[] { attack.AttackVector, attack.Description, attack.ServerResponse });
        if (attackText.Contains("schema", StringComparison.OrdinalIgnoreCase)
            || attackText.Contains("prompt", StringComparison.OrdinalIgnoreCase)
            || attackText.Contains("exfil", StringComparison.OrdinalIgnoreCase)
            || attackText.Contains("reflection", StringComparison.OrdinalIgnoreCase)
            || attackText.Contains("echo", StringComparison.OrdinalIgnoreCase))
        {
            return GateOutcome.Reject;
        }

        return GateOutcome.ReviewRequired;
    }

    private static ValidationFindingSeverity SeverityForAttackSimulation(AttackSimulationResult attack)
    {
        return GateForAttackSimulation(attack) == GateOutcome.Reject
            ? ValidationFindingSeverity.Critical
            : ValidationFindingSeverity.High;
    }

    private static EvidenceOrigin OriginForFinding(ValidationFinding finding)
    {
        return finding.EffectiveSource == ValidationRuleSource.Spec
            ? EvidenceOrigin.DeterministicObservation
            : EvidenceOrigin.HeuristicInference;
    }

    private static List<ImpactArea> GetImpactAreas(params string?[] values)
    {
        var text = string.Join(' ', values.Where(value => !string.IsNullOrWhiteSpace(value)));
        var impacts = new List<ImpactArea>();

        void Add(ImpactArea area)
        {
            if (!impacts.Contains(area))
            {
                impacts.Add(area);
            }
        }

        if (text.Contains("auth", StringComparison.OrdinalIgnoreCase)
            || text.Contains("oauth", StringComparison.OrdinalIgnoreCase)
            || text.Contains("token", StringComparison.OrdinalIgnoreCase))
        {
            Add(ImpactArea.AuthenticationBoundary);
        }

        if (text.Contains("protocol", StringComparison.OrdinalIgnoreCase)
            || text.Contains("json-rpc", StringComparison.OrdinalIgnoreCase)
            || text.Contains("content", StringComparison.OrdinalIgnoreCase)
            || text.Contains("schema", StringComparison.OrdinalIgnoreCase)
            || text.Contains("message", StringComparison.OrdinalIgnoreCase))
        {
            Add(ImpactArea.ProtocolInteroperability);
            Add(ImpactArea.CapabilityContract);
        }

        if (text.Contains("llm", StringComparison.OrdinalIgnoreCase)
            || text.Contains("prompt", StringComparison.OrdinalIgnoreCase)
            || text.Contains("destructive", StringComparison.OrdinalIgnoreCase)
            || text.Contains("readOnlyHint", StringComparison.OrdinalIgnoreCase)
            || text.Contains("openWorldHint", StringComparison.OrdinalIgnoreCase)
            || text.Contains("idempotent", StringComparison.OrdinalIgnoreCase)
            || text.Contains("confirm", StringComparison.OrdinalIgnoreCase))
        {
            Add(ImpactArea.UnsafeAutonomy);
        }

        if (text.Contains("reflect", StringComparison.OrdinalIgnoreCase)
            || text.Contains("echo", StringComparison.OrdinalIgnoreCase)
            || text.Contains("output", StringComparison.OrdinalIgnoreCase))
        {
            Add(ImpactArea.OutputIntegrity);
        }

        if (text.Contains("resource", StringComparison.OrdinalIgnoreCase)
            || text.Contains("uri", StringComparison.OrdinalIgnoreCase)
            || text.Contains("path", StringComparison.OrdinalIgnoreCase)
            || text.Contains("exfil", StringComparison.OrdinalIgnoreCase)
            || text.Contains("sensitive", StringComparison.OrdinalIgnoreCase)
            || text.Contains("data", StringComparison.OrdinalIgnoreCase))
        {
            Add(ImpactArea.DataExposure);
        }

        if (text.Contains("timeout", StringComparison.OrdinalIgnoreCase)
            || text.Contains("disconnect", StringComparison.OrdinalIgnoreCase)
            || text.Contains("error", StringComparison.OrdinalIgnoreCase)
            || text.Contains("recover", StringComparison.OrdinalIgnoreCase))
        {
            Add(ImpactArea.RecoveryIntegrity);
        }

        if (text.Contains("performance", StringComparison.OrdinalIgnoreCase)
            || text.Contains("load", StringComparison.OrdinalIgnoreCase)
            || text.Contains("latency", StringComparison.OrdinalIgnoreCase)
            || text.Contains("concurrency", StringComparison.OrdinalIgnoreCase)
            || text.Contains("execution", StringComparison.OrdinalIgnoreCase))
        {
            Add(ImpactArea.OperationalResilience);
        }

        if (impacts.Count == 0)
        {
            Add(ImpactArea.OperationalResilience);
        }

        return impacts;
    }

    private static ValidationFindingSeverity MapSeverity(ViolationSeverity severity)
    {
        return severity switch
        {
            ViolationSeverity.Critical => ValidationFindingSeverity.Critical,
            ViolationSeverity.High => ValidationFindingSeverity.High,
            ViolationSeverity.Medium => ValidationFindingSeverity.Medium,
            ViolationSeverity.Low => ValidationFindingSeverity.Low,
            _ => ValidationFindingSeverity.Info
        };
    }

    private static ValidationFindingSeverity MapSeverity(VulnerabilitySeverity severity)
    {
        return severity switch
        {
            VulnerabilitySeverity.Critical => ValidationFindingSeverity.Critical,
            VulnerabilitySeverity.High => ValidationFindingSeverity.High,
            VulnerabilitySeverity.Medium => ValidationFindingSeverity.Medium,
            VulnerabilitySeverity.Low => ValidationFindingSeverity.Low,
            VulnerabilitySeverity.Informational => ValidationFindingSeverity.Info,
            _ => ValidationFindingSeverity.Info
        };
    }

    private static ValidationFindingSeverity MapSeverity(ContentRiskLevel severity)
    {
        return severity switch
        {
            ContentRiskLevel.High => ValidationFindingSeverity.High,
            ContentRiskLevel.Medium => ValidationFindingSeverity.Medium,
            ContentRiskLevel.Low => ValidationFindingSeverity.Low,
            _ => ValidationFindingSeverity.Info
        };
    }

    private static ValidationFindingSeverity MapSeverity(string? severity)
    {
        return severity?.Trim().ToLowerInvariant() switch
        {
            "critical" => ValidationFindingSeverity.Critical,
            "high" => ValidationFindingSeverity.High,
            "medium" => ValidationFindingSeverity.Medium,
            "low" => ValidationFindingSeverity.Low,
            _ => ValidationFindingSeverity.Info
        };
    }

    private static string BuildCoverageSummary(ValidationCoverageDeclaration coverage)
    {
        var reason = string.IsNullOrWhiteSpace(coverage.Reason)
            ? string.Empty
            : $" {coverage.Reason.Trim()}";
        return $"Coverage is {coverage.Status} for {coverage.LayerId}/{coverage.Scope}.{reason}".Trim();
    }

    private static string NormalizeToken(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return "unknown";
        }

        return new string(value
            .Trim()
            .ToLowerInvariant()
            .Select(ch => char.IsLetterOrDigit(ch) ? ch : '-')
            .ToArray())
            .Trim('-');
    }

    private static string FormatDetail(string? detail)
    {
        return string.IsNullOrWhiteSpace(detail) ? string.Empty : $" — {detail.Trim()}";
    }
}