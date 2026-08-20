using System.Text.RegularExpressions;
using Mcp.Benchmark.Core.Models;
using Mcp.Benchmark.Core.Services;

namespace Mcp.Benchmark.Infrastructure.Services.Reporting;

internal sealed class ValidationHtmlReportDocumentFactory
{
    private const int MaxDecisionTraceItems = 6;
    private const int MaxHotspots = 6;
    private const int MaxCompatibilityThemes = 4;

    public ValidationHtmlReportDocument Create(ValidationResult result, ReportingConfig reportConfig, bool verbose)
    {
        ArgumentNullException.ThrowIfNull(result);
        ArgumentNullException.ThrowIfNull(reportConfig);

        var bootstrap = ResolveBootstrapHealth(result);
        var detailLabel = verbose ? "Full" : "Minimal";
        var decisionTrace = BuildDecisionTrace(result);
        var compatibilityThemes = BuildCompatibilityThemes(result);
        var priorityFindings = ExecutiveFindingSummaryBuilder.BuildPriorityFindings(result);
        var remediationOrder = RemediationOrderBuilder.Build(result);
        var actionHints = ReportActionHintBuilder.Build(result)
            .Where(hint => !MatchesPolicySummary(hint, result.PolicyOutcome?.Summary))
            .ToList();
        var recommendations = result.Recommendations
            .Where(recommendation => !string.IsNullOrWhiteSpace(recommendation))
            .Except(actionHints, StringComparer.Ordinal)
            .Except(priorityFindings, StringComparer.Ordinal)
            .ToList();

        return new ValidationHtmlReportDocument
        {
            Result = result,
            ReportConfig = reportConfig,
            GeneratedAtLabel = $"Generated on {ResolveArtifactTime(result):yyyy-MM-dd HH:mm:ss UTC}",
            DetailLabel = detailLabel,
            Verbose = verbose,
            Hero = BuildHero(result, reportConfig),
            ReleaseDecision = BuildReleaseDecision(result, priorityFindings, decisionTrace, compatibilityThemes),
            OverviewMetrics = BuildOverviewMetrics(result),
            RiskMetrics = BuildRiskMetrics(result),
            DomainSummaries = BuildDomainSummaries(result),
            DecisionTrace = decisionTrace,
            Hotspots = BuildHotspots(result),
            CompatibilityThemes = compatibilityThemes,
            PriorityFindings = priorityFindings,
            ActionHints = actionHints,
            RemediationOrder = remediationOrder,
            AdditionalRecommendations = recommendations,
            Bootstrap = BuildBootstrapSummary(result, bootstrap)
        };
    }

    private static DateTime ResolveArtifactTime(ValidationResult result) =>
        (result.EndTime ?? (result.StartTime == default ? DateTime.UnixEpoch : result.StartTime)).ToUniversalTime();

    private static ValidationHtmlHero BuildHero(ValidationResult result, ReportingConfig reportConfig)
    {
        var releaseTone = ResolveReleaseTone(result);
        var title = result.PolicyOutcome is { Passed: false }
            ? "Release Hold"
            : result.OverallStatus switch
            {
                ValidationStatus.Passed => "Validation Ready",
                ValidationStatus.Failed => "Validation Review Required",
                _ => "Validation Completed with Warnings"
            };
        var protocolVersion = result.ProtocolVersion
            ?? result.InitializationHandshake?.Payload?.ProtocolVersion
            ?? result.ServerConfig.ProtocolVersion
            ?? "Not available";
        var duration = result.Duration?.TotalSeconds > 0
            ? $"{result.Duration.Value.TotalSeconds:F1}s"
            : "Not available";

        return new ValidationHtmlHero
        {
            Eyebrow = "Model Context Protocol (MCP) Validation Report",
            Title = title,
            Subtitle = BuildHeroSubtitle(result),
            StatusLabel = ResolveReleaseStatusLabel(result),
            StatusTone = releaseTone,
            VerdictLabel = BuildHeroVerdictLabel(result),
            VerdictTone = result.VerdictAssessment != null
                ? MapVerdictTone(result.VerdictAssessment.BaselineVerdict)
                : HtmlReportTone.Neutral,
            TrustLevelLabel = BuildTrustLevelValue(result.TrustAssessment),
            TrustLevelDetail = BuildTrustLevelDetail(result.TrustAssessment),
            TrustLevelTone = ResolveTrustTone(result.TrustAssessment),
            MetaItems = new List<ValidationHtmlMetaItem>
            {
                new() { Label = "Endpoint", Value = result.ServerConfig.Endpoint ?? "Not available" },
                new() { Label = "Validation Identifier", Value = result.ValidationId },
                new() { Label = "Duration", Value = duration },
                new() { Label = "MCP Specification Profile", Value = string.IsNullOrWhiteSpace(reportConfig.SpecProfile) ? "latest" : reportConfig.SpecProfile },
                new() { Label = "Server Profile", Value = $"{result.ServerProfile} ({result.ServerProfileSource})" },
                new() { Label = "Protocol Version", Value = protocolVersion }
            }
        };
    }

    private static string BuildHeroVerdictLabel(ValidationResult result)
    {
        return result.VerdictAssessment == null
            ? "Not evaluated"
            : BuildVerdictCompositeLabel(result);
    }

    private static string BuildTrustLevelValue(McpTrustAssessment? assessment)
    {
        if (assessment == null)
        {
            return "Not available";
        }

        var separatorIndex = assessment.TrustLabel.IndexOf(':', StringComparison.Ordinal);
        var shortLabel = separatorIndex > 0
            ? assessment.TrustLabel[..separatorIndex]
            : assessment.TrustLevel.ToString().Split('_', 2)[0];
        return $"Level {(int)assessment.TrustLevel} ({shortLabel})";
    }

    private static string BuildTrustLevelDetail(McpTrustAssessment? assessment)
    {
        if (assessment == null)
        {
            return "Trust assessment unavailable";
        }

        var separatorIndex = assessment.TrustLabel.IndexOf(':', StringComparison.Ordinal);
        var detail = separatorIndex >= 0
            ? assessment.TrustLabel[(separatorIndex + 1)..].Trim()
            : assessment.TrustLabel;
        if (!assessment.LimitedByIncompleteEvidence)
        {
            return detail;
        }

        var missing = assessment.UnevaluatedDimensions.Count > 0
            ? $" Missing: {string.Join(", ", assessment.UnevaluatedDimensions)}."
            : string.Empty;
        return $"Limited by incomplete evidence.{missing} {detail}".Trim();
    }

    private static ValidationHtmlReleaseDecision BuildReleaseDecision(
        ValidationResult result,
        IReadOnlyList<string> priorityFindings,
        IReadOnlyList<ValidationHtmlDecisionTraceItem> decisionTrace,
        IReadOnlyList<ValidationHtmlCompatibilityTheme> compatibilityThemes)
    {
        var policyOutcome = result.PolicyOutcome;
        var verdictLabel = BuildVerdictCompositeLabel(result);
        var highlights = BuildReleaseHighlights(result, priorityFindings, decisionTrace, compatibilityThemes).ToList();
        var blockingSignalCount = ResolveBlockingSignalCount(result);
        var unsuppressedSignalCount = ResolveUnsuppressedSignalCount(result);
        var totalSignalCount = ResolveTotalSignalCount(result);
        var suppressedSignalCount = policyOutcome?.SuppressedSignalCount ?? 0;
        if (result.BaselineComparison is { } baseline)
        {
            highlights.Insert(0, $"Baseline regression: {(baseline.IsRegression ? "yes" : "no")}; score delta {baseline.ScoreDelta:+0.##;-0.##;0} points.");
        }
        if (policyOutcome?.AppliedWaivers.Count > 0)
        {
            highlights.Add($"{policyOutcome.AppliedWaivers.Count} governed waiver(s) applied.");
        }

        if (policyOutcome is { Passed: false })
        {
            return new ValidationHtmlReleaseDecision
            {
                Eyebrow = "Release Decision",
                Title = "Hold release until blocking signals are resolved",
                Summary = policyOutcome.Summary,
                Tone = HtmlReportTone.Danger,
                Highlights = highlights,
                PolicyModeLabel = FormatModeLabel(policyOutcome.Mode),
                ExitCodeLabel = policyOutcome.RecommendedExitCode.ToString(),
                VerdictLabel = verdictLabel,
                TotalSignalCount = totalSignalCount,
                UnsuppressedSignalCount = unsuppressedSignalCount,
                BlockingSignalCount = blockingSignalCount,
                SuppressedSignalCount = suppressedSignalCount
            };
        }

        if (result.OverallStatus == ValidationStatus.Failed)
        {
            return new ValidationHtmlReleaseDecision
            {
                Eyebrow = "Release Decision",
                Title = "Resolve failed validation areas before rollout",
                Summary = "Deterministic validation failed even though no additional host policy block was applied. Use the decision trace to verify which domains still need remediation.",
                Tone = HtmlReportTone.Warning,
                Highlights = highlights,
                PolicyModeLabel = policyOutcome != null ? FormatModeLabel(policyOutcome.Mode) : "Not applied",
                ExitCodeLabel = policyOutcome?.RecommendedExitCode.ToString() ?? "1",
                VerdictLabel = verdictLabel,
                TotalSignalCount = totalSignalCount,
                UnsuppressedSignalCount = unsuppressedSignalCount,
                BlockingSignalCount = blockingSignalCount,
                SuppressedSignalCount = suppressedSignalCount
            };
        }

        return new ValidationHtmlReleaseDecision
        {
            Eyebrow = "Release Decision",
            Title = "Release gate satisfied with documented follow-up",
            Summary = "Core release criteria passed. Remaining notes are advisory and should be scheduled according to the domain summaries below.",
            Tone = HtmlReportTone.Info,
            Highlights = highlights,
            PolicyModeLabel = policyOutcome != null ? FormatModeLabel(policyOutcome.Mode) : "Not applied",
            ExitCodeLabel = policyOutcome?.RecommendedExitCode.ToString() ?? "0",
            VerdictLabel = verdictLabel,
            TotalSignalCount = totalSignalCount,
            UnsuppressedSignalCount = unsuppressedSignalCount,
            BlockingSignalCount = blockingSignalCount,
            SuppressedSignalCount = suppressedSignalCount
        };
    }

    private static IReadOnlyList<ValidationHtmlMetricCard> BuildOverviewMetrics(ValidationResult result)
    {
        var evidenceSummary = ResolveEvidenceSummary(result);

        if (result.VerdictAssessment != null)
        {
            var verdictCards = new List<ValidationHtmlMetricCard>
            {
                CreateVerdictCard("Baseline Verdict", result.VerdictAssessment.BaselineVerdict, "Authoritative release gate"),
                CreateVerdictCard("Protocol Verdict", result.VerdictAssessment.ProtocolVerdict, "Protocol correctness and contract integrity"),
                CreateVerdictCard("Coverage Verdict", result.VerdictAssessment.CoverageVerdict, "Evidence completeness across enabled checks"),
                new()
                {
                    Eyebrow = "Benchmark",
                    Value = $"{result.ComplianceScore:F1}%",
                    Label = "Compliance Score",
                    SupportingText = "Descriptive benchmarking signal",
                    Tone = MapScoreTone(result.ComplianceScore)
                }
            };

            if (evidenceSummary.TotalDeclarations > 0)
            {
                verdictCards.Add(new ValidationHtmlMetricCard
                {
                    Eyebrow = "Evidence",
                    Value = $"{evidenceSummary.EvidenceConfidenceRatio * 100:F1}%",
                    Label = "Evidence Confidence",
                    SupportingText = $"{evidenceSummary.ConfidenceLevel}; coverage {evidenceSummary.EvidenceCoverageRatio * 100:F1}%",
                    Tone = MapScoreTone(evidenceSummary.EvidenceConfidenceRatio * 100)
                });
            }

            if (result.TrustAssessment != null)
            {
                verdictCards.Add(new ValidationHtmlMetricCard
                {
                    Eyebrow = "Benchmark",
                    Value = result.TrustAssessment.TrustLabel.Split(':', 2)[0],
                    Label = "Trust Profile",
                    SupportingText = result.TrustAssessment.TrustLabel,
                    Tone = ResolveTrustTone(result.TrustAssessment)
                });
            }

            AppendOperationalMetricCards(verdictCards, result.Run.OperationalMetrics);

            return verdictCards;
        }

        var cards = new List<ValidationHtmlMetricCard>
        {
            new()
            {
                Eyebrow = "Outcome",
                Value = $"{result.ComplianceScore:F1}%",
                Label = "Compliance Score",
                SupportingText = GetScoreNarrative(result.ComplianceScore),
                Tone = MapScoreTone(result.ComplianceScore)
            },
            new()
            {
                Eyebrow = "Coverage",
                Value = $"{result.Summary.PassRate:F1}%",
                Label = "Pass Rate",
                SupportingText = $"{result.Summary.PassedTests}/{result.Summary.TotalTests} checks passed",
                Tone = MapScoreTone(result.Summary.PassRate)
            },
            new()
            {
                Eyebrow = "Trust",
                Value = result.TrustAssessment != null ? BuildTrustLevelValue(result.TrustAssessment) : "Not available",
                Label = "Trust Level",
                SupportingText = BuildTrustLevelDetail(result.TrustAssessment),
                Tone = ResolveTrustTone(result.TrustAssessment)
            }
        };

        if (result.Summary.CoverageRatio > 0)
        {
            cards.Add(new ValidationHtmlMetricCard
            {
                Eyebrow = "Coverage",
                Value = $"{result.Summary.CoverageRatio * 100:F1}%",
                Label = "Rule Coverage",
                SupportingText = "Collected evidence coverage across enabled checks",
                Tone = MapScoreTone(result.Summary.CoverageRatio * 100)
            });
        }

        if (evidenceSummary.TotalDeclarations > 0)
        {
            cards.Add(new ValidationHtmlMetricCard
            {
                Eyebrow = "Evidence",
                Value = $"{evidenceSummary.EvidenceConfidenceRatio * 100:F1}%",
                Label = "Evidence Confidence",
                SupportingText = $"{evidenceSummary.ConfidenceLevel}; coverage {evidenceSummary.EvidenceCoverageRatio * 100:F1}%",
                Tone = MapScoreTone(evidenceSummary.EvidenceConfidenceRatio * 100)
            });
        }

        AppendOperationalMetricCards(cards, result.Run.OperationalMetrics);

        return cards;
    }

    private static void AppendOperationalMetricCards(
        ICollection<ValidationHtmlMetricCard> cards,
        ValidationOperationalMetrics? metrics)
    {
        if (metrics == null)
        {
            return;
        }

        cards.Add(new ValidationHtmlMetricCard
        {
            Eyebrow = "Validator",
            Value = $"{metrics.ValidatorOverheadMs:F1} ms",
            Label = "Run Overhead",
            SupportingText = $"Queue 95th percentile {metrics.QueueTimeP95Ms:F1} ms · {metrics.RetryCount} retries · {metrics.RetryDelayMs:F1} ms backoff",
            Tone = HtmlReportTone.Info
        });
        cards.Add(new ValidationHtmlMetricCard
        {
            Eyebrow = "Target",
            Value = $"{metrics.TargetLatencyP95Ms:F1} ms",
            Label = "Target 95th-Percentile Latency",
            SupportingText = $"50th percentile {metrics.TargetLatencyP50Ms:F1} ms · 99th percentile {metrics.TargetLatencyP99Ms:F1} ms · {metrics.ThroughputRequestsPerSecond:F1} requests/second · {metrics.RequestsFailed} failed transport attempt(s) ({metrics.ErrorRate:P1})",
            Tone = HtmlReportTone.Info
        });
        cards.Add(new ValidationHtmlMetricCard
        {
            Eyebrow = "Operations",
            Value = $"{metrics.RequestsCompleted}/{metrics.RequestsStarted}",
            Label = "Requests Completed",
            SupportingText = $"Budget {metrics.RequestBudgetLimit} · {metrics.TruncatedResponseCount} rejected responses · evidence {metrics.EvidenceCoverageRatio:P1}",
            Tone = HtmlReportTone.Info
        });
    }

    private static EvidenceCoverageSummary ResolveEvidenceSummary(ValidationResult result)
    {
        return result.VerdictAssessment?.EvidenceSummary
               ?? result.ScoringDetails?.EvidenceSummary
               ?? ValidationEvidenceSummarizer.Summarize(result.Evidence.Coverage);
    }

    private static IReadOnlyList<ValidationHtmlMetricCard> BuildRiskMetrics(ValidationResult result)
    {
        if (result.TrustAssessment != null)
        {
            return new List<ValidationHtmlMetricCard>
            {
                CreateRiskCard("Protocol", result.TrustAssessment.ProtocolCompliance, "Specification adherence and response structure"),
                CreateRiskCard("Security", result.TrustAssessment.SecurityPosture, "Authentication boundaries and exploit resistance"),
                CreateRiskCard("AI Safety", result.TrustAssessment.AiSafety, "Schema quality and agent safety posture"),
                CreateRiskCard("Operations", result.TrustAssessment.OperationalReadiness, "Latency, throughput, and stability")
            };
        }

        return new List<ValidationHtmlMetricCard>
        {
            CreateRiskCard("Protocol", result.ProtocolCompliance?.ComplianceScore ?? 0, "Specification adherence and response structure"),
            CreateRiskCard("Security", result.SecurityTesting?.SecurityScore ?? 0, "Authentication boundaries and exploit resistance"),
            CreateRiskCard("Tools", result.ToolValidation?.Score ?? 0, "Contract quality and execution evidence"),
            CreateRiskCard("Performance", result.PerformanceTesting?.Score ?? 0, "Latency, throughput, and stability")
        };
    }

    private static IReadOnlyList<ValidationHtmlDomainSummary> BuildDomainSummaries(ValidationResult result)
    {
        return new List<ValidationHtmlDomainSummary>
        {
            BuildProtocolDomainSummary(result),
            BuildSecurityDomainSummary(result),
            BuildAiSafetyDomainSummary(result),
            BuildOperationsDomainSummary(result),
            BuildCompatibilityDomainSummary(result),
            BuildCoverageDomainSummary(result)
        };
    }

    private static ValidationHtmlDomainSummary BuildProtocolDomainSummary(ValidationResult result)
    {
        var allSignals = GetDomainDecisions(result, "Protocol", blockingOnly: false);
        var blockingSignals = GetDomainDecisions(result, "Protocol", blockingOnly: true);
        var violations = result.ProtocolCompliance?.Violations.Count ?? 0;
        var errorScenarioCount = result.ErrorHandling?.ErrorScenariosTestCount ?? 0;
        var handledCorrectly = result.ErrorHandling?.ErrorScenariosHandledCorrectly ?? 0;
        var verdict = result.VerdictAssessment?.ProtocolVerdict ?? ValidationVerdict.Unknown;
        var protocolStatus = result.ProtocolCompliance?.Status;
        var tone = verdict != ValidationVerdict.Unknown
            ? MapVerdictTone(verdict)
            : blockingSignals.Count > 0
                ? HtmlReportTone.Danger
                : MapScoreTone(result.ProtocolCompliance?.ComplianceScore ?? 0);
        var summary = protocolStatus == TestStatus.Inconclusive
            ? "Protocol evidence is inconclusive because active conformance probes were not run or did not produce authoritative evidence."
            : protocolStatus is TestStatus.Skipped or TestStatus.NotRun
                ? "Protocol validation was not executed for this run."
                : blockingSignals.FirstOrDefault()?.Summary
            ?? (violations > 0
                ? $"{violations} protocol violation(s) were recorded even though none escalated to the release gate."
                : "Protocol checks completed without blocking findings.");
        var action = result.ProtocolCompliance?.Violations.FirstOrDefault(violation => !string.IsNullOrWhiteSpace(violation.Recommendation))?.Recommendation
            ?? "Fix protocol negotiation, JSON-RPC error semantics, and contract shape before broader rollout.";

        return new ValidationHtmlDomainSummary
        {
            Domain = "Protocol",
            StatusLabel = verdict != ValidationVerdict.Unknown ? FormatVerdictLabel(verdict) : $"{result.ProtocolCompliance?.ComplianceScore ?? 0:F0}%",
            SignalLabel = BuildSignalLabel(blockingSignals.Count, allSignals.Count),
            EvidenceLabel = $"{violations} protocol violation(s) · {handledCorrectly}/{errorScenarioCount} error scenarios handled correctly",
            Summary = summary,
            ActionLabel = action,
            Tone = tone
        };
    }

    private static ValidationHtmlDomainSummary BuildSecurityDomainSummary(ValidationResult result)
    {
        var allSignals = GetDomainDecisions(result, "Security", blockingOnly: false);
        var blockingSignals = GetDomainDecisions(result, "Security", blockingOnly: true);
        var vulnerabilities = result.SecurityTesting?.Vulnerabilities.Count ?? 0;
        var attackSimulations = result.SecurityTesting?.AttackSimulations.Count ?? 0;
        var score = result.SecurityTesting?.SecurityScore ?? result.TrustAssessment?.SecurityPosture ?? 0;
        var securityStatus = result.SecurityTesting?.Status;
        var summary = securityStatus is null or TestStatus.Skipped or TestStatus.NotRun
            ? "Security validation was not executed; absence of findings is not evidence of a secure target."
            : securityStatus == TestStatus.Inconclusive
                ? "Security validation was inconclusive and requires additional evidence."
                : blockingSignals.FirstOrDefault()?.Summary
            ?? (vulnerabilities > 0
                ? $"{vulnerabilities} security vulnerability finding(s) require remediation."
                : "Security boundary and attack simulations completed without blocking findings.");
        var action = result.SecurityTesting?.Vulnerabilities.FirstOrDefault(vulnerability => !string.IsNullOrWhiteSpace(vulnerability.Remediation))?.Remediation
            ?? "Review authentication boundary behavior, exploit resistance, and reflected server responses.";

        return new ValidationHtmlDomainSummary
        {
            Domain = "Security",
            StatusLabel = securityStatus is null or TestStatus.Skipped or TestStatus.NotRun ? "Not evaluated" : securityStatus == TestStatus.Inconclusive ? "Inconclusive" : $"{score:F0}%",
            SignalLabel = BuildSignalLabel(blockingSignals.Count, allSignals.Count),
            EvidenceLabel = $"{vulnerabilities} vulnerability finding(s) · {attackSimulations} attack simulation(s)",
            Summary = summary,
            ActionLabel = action,
            Tone = blockingSignals.Count > 0 ? HtmlReportTone.Danger : securityStatus is null or TestStatus.Skipped or TestStatus.NotRun or TestStatus.Inconclusive ? HtmlReportTone.Warning : MapScoreTone(score)
        };
    }

    private static ValidationHtmlDomainSummary BuildAiSafetyDomainSummary(ValidationResult result)
    {
        var allSignals = GetDomainDecisions(result, "AI Safety", blockingOnly: false);
        var blockingSignals = GetDomainDecisions(result, "AI Safety", blockingOnly: true);
        var aiReadinessFindings = result.ToolValidation?.AiReadinessFindings.Count ?? 0;
        var boundaryFindings = result.TrustAssessment?.BoundaryFindings.Count ?? 0;
        var contentSafetyFindings = (result.ToolValidation?.ContentSafetyFindings.Count ?? 0)
            + (result.ResourceTesting?.ContentSafetyFindings.Count ?? 0)
            + (result.PromptTesting?.ContentSafetyFindings.Count ?? 0);
        var score = result.TrustAssessment?.AiSafety ?? result.ToolValidation?.AiReadinessScore ?? 0;
        var summary = blockingSignals.FirstOrDefault()?.Summary
            ?? ((aiReadinessFindings + boundaryFindings + contentSafetyFindings) > 0
                ? $"AI-facing contract and content guidance issues were recorded across the advertised surfaces."
                : "Schema clarity, content handling, and autonomy boundaries did not surface blocking issues.");
        var action = result.TrustAssessment?.BoundaryFindings.FirstOrDefault(finding => !string.IsNullOrWhiteSpace(finding.Mitigation))?.Mitigation
            ?? "Tighten tool schemas, approval metadata, and agent-facing error quality for safer autonomous use.";

        return new ValidationHtmlDomainSummary
        {
            Domain = "AI Safety",
            StatusLabel = score >= 0 ? $"{score:F0}%" : "Not available",
            SignalLabel = BuildSignalLabel(blockingSignals.Count, allSignals.Count),
            EvidenceLabel = $"{aiReadinessFindings} schema finding(s) · {boundaryFindings} boundary finding(s) · {contentSafetyFindings} content-safety finding(s)",
            Summary = summary,
            ActionLabel = action,
            Tone = blockingSignals.Count > 0 ? HtmlReportTone.Danger : MapScoreTone(score)
        };
    }

    private static ValidationHtmlDomainSummary BuildOperationsDomainSummary(ValidationResult result)
    {
        var allSignals = GetDomainDecisions(result, "Operations", blockingOnly: false);
        var blockingSignals = GetDomainDecisions(result, "Operations", blockingOnly: true);
        var performance = result.PerformanceTesting;
        var measurementsCaptured = performance != null && PerformanceMeasurementEvaluator.HasObservedMetrics(performance);
        var score = result.TrustAssessment?.OperationalReadiness ?? (performance?.Score ?? 0);
        var evidenceLabel = performance == null
            ? "No performance probe recorded"
            : measurementsCaptured
                ? $"Average {performance.LoadTesting.AverageResponseTimeMs:F1} ms · 95th percentile {performance.LoadTesting.P95ResponseTimeMs:F1} ms · {performance.LoadTesting.ErrorRate:F2}% errors"
                : $"Measurements unavailable · {PerformanceMeasurementEvaluator.GetUnavailableReason(performance, "Performance measurements were not captured before the run ended.")}";
        var summary = performance?.Status is null or TestStatus.Skipped or TestStatus.NotRun
            ? "Performance validation was not executed; no runtime readiness claim can be made."
            : blockingSignals.FirstOrDefault()?.Summary
            ?? (measurementsCaptured
                ? "Runtime probe completed with measured latency, throughput, and failure-rate data."
                : "Operational posture is constrained by incomplete runtime measurements or failed recovery probes.");
        var action = performance?.PerformanceBottlenecks.FirstOrDefault()
            ?? result.ErrorHandling?.CriticalErrors.FirstOrDefault()
            ?? "Stabilize timeout recovery, connection interruption handling, and repeat the runtime probe with captured measurements.";

        return new ValidationHtmlDomainSummary
        {
            Domain = "Operations",
            StatusLabel = measurementsCaptured ? $"{score:F0}%" : "Measured with gaps",
            SignalLabel = BuildSignalLabel(blockingSignals.Count, allSignals.Count),
            EvidenceLabel = evidenceLabel,
            Summary = summary,
            ActionLabel = action,
            Tone = blockingSignals.Count > 0 ? HtmlReportTone.Danger : measurementsCaptured ? MapScoreTone(score) : HtmlReportTone.Warning
        };
    }

    private static ValidationHtmlDomainSummary BuildCompatibilityDomainSummary(ValidationResult result)
    {
        var compatibility = result.ClientCompatibility;
        if (compatibility == null || compatibility.Assessments.Count == 0)
        {
            return new ValidationHtmlDomainSummary
            {
                Domain = "Compatibility",
                StatusLabel = "Not evaluated",
                SignalLabel = "No client-profile interpretation",
                EvidenceLabel = "No profile pack evaluation requested or recorded",
                Summary = "Client compatibility was not part of this run.",
                ActionLabel = "Run with documented client profiles when deployment depends on a specific host.",
                Tone = HtmlReportTone.Neutral
            };
        }

        var tone = compatibility.IncompatibleCount > 0
            ? HtmlReportTone.Danger
            : compatibility.WarningCount > 0
                ? HtmlReportTone.Warning
                : HtmlReportTone.Success;
        var statusLabel = compatibility.IncompatibleCount > 0
            ? "Incompatible profiles present"
            : compatibility.WarningCount > 0
                ? "Compatible with warnings"
                : "Compatible";
        var leadingAssessment = compatibility.Assessments
            .OrderByDescending(assessment => assessment.Status)
            .ThenByDescending(assessment => assessment.WarningRequirements)
            .First();

        return new ValidationHtmlDomainSummary
        {
            Domain = "Compatibility",
            StatusLabel = statusLabel,
            SignalLabel = $"{compatibility.IncompatibleCount} incompatible · {compatibility.WarningCount} warning profile(s)",
            EvidenceLabel = $"{compatibility.Assessments.Count} profile(s) evaluated",
            Summary = leadingAssessment.Summary,
            ActionLabel = "Fix shared requirement themes first, then re-check the profile cards for host-specific exceptions.",
            Tone = tone
        };
    }

    private static ValidationHtmlDomainSummary BuildCoverageDomainSummary(ValidationResult result)
    {
        var coverageDeclarations = result.Evidence.Coverage;
        var blockedCount = coverageDeclarations.Count(item => item.Status == ValidationCoverageStatus.Blocked);
        var skippedCount = coverageDeclarations.Count(item => item.Status == ValidationCoverageStatus.Skipped);
        var coveredCount = coverageDeclarations.Count(item => item.Status == ValidationCoverageStatus.Covered);
        var coverageVerdict = result.VerdictAssessment?.CoverageVerdict ?? ValidationVerdict.Unknown;
        var coverageDecision = result.VerdictAssessment?.CoverageDecisions.FirstOrDefault();

        return new ValidationHtmlDomainSummary
        {
            Domain = "Coverage",
            StatusLabel = coverageVerdict != ValidationVerdict.Unknown ? FormatVerdictLabel(coverageVerdict) : "Unknown",
            SignalLabel = blockedCount > 0 ? $"{blockedCount} blocked scope(s)" : $"{coveredCount} covered scope(s)",
            EvidenceLabel = $"{coveredCount} covered · {skippedCount} skipped · {blockedCount} blocked declaration(s)",
            Summary = coverageDecision?.Summary ?? "Coverage declarations explain what ran, what was skipped, and where the result may be incomplete.",
            ActionLabel = blockedCount > 0
                ? "Resolve blocked scopes or document why the affected evidence cannot be collected safely."
                : "Keep skipped and not-applicable reasons explicit so downstream readers can trust the scope of the run.",
            Tone = coverageVerdict != ValidationVerdict.Unknown ? MapVerdictTone(coverageVerdict) : HtmlReportTone.Info
        };
    }

    private static IReadOnlyList<ValidationHtmlDecisionTraceItem> BuildDecisionTrace(ValidationResult result)
    {
        var sourceDecisions = result.VerdictAssessment?.BlockingDecisions is { Count: > 0 }
            ? result.VerdictAssessment.BlockingDecisions
            : result.VerdictAssessment?.TriggeredDecisions ?? new List<DecisionRecord>();

        return sourceDecisions
            .Where(decision => !string.IsNullOrWhiteSpace(decision.Summary))
            .GroupBy(BuildDecisionThemeKey, StringComparer.OrdinalIgnoreCase)
            .OrderBy(group => group.Min(item => ValidationAuthorityHierarchy.GetSortOrder(item.Authority)))
            .ThenByDescending(group => group.Max(item => item.Gate))
            .ThenByDescending(group => group.Count())
            .ThenByDescending(group => group.Max(item => item.Severity))
            .Take(MaxDecisionTraceItems)
            .Select(BuildDecisionTraceItem)
            .ToList();
    }

    private static ValidationHtmlDecisionTraceItem BuildDecisionTraceItem(IGrouping<string, DecisionRecord> group)
    {
        var representative = group.First();
        var affectedComponents = group
            .Select(item => item.Component)
            .Where(component => !string.IsNullOrWhiteSpace(component))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
        var impactAreas = group
            .SelectMany(item => item.ImpactAreas)
            .Distinct()
            .Select(FormatImpactArea)
            .Take(3)
            .ToList();
        var facts = new List<ValidationHtmlMetaItem>
        {
            new() { Label = "Affected Components", Value = FormatComponentSet(affectedComponents) },
            new() { Label = "Decision Count", Value = group.Count().ToString() }
        };

        var evidenceIds = group
            .SelectMany(item => item.RelatedEvidenceIds)
            .Where(id => !string.IsNullOrWhiteSpace(id))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Take(6)
            .ToList();
        if (evidenceIds.Count > 0)
        {
            facts.Add(new ValidationHtmlMetaItem { Label = "Evidence IDs", Value = string.Join(", ", evidenceIds) });
        }

        var payloadPreview = group
            .SelectMany(item => item.EvidenceReferences)
            .Select(reference => reference.RedactedPayloadPreview)
            .FirstOrDefault(preview => !string.IsNullOrWhiteSpace(preview));
        if (!string.IsNullOrWhiteSpace(payloadPreview))
        {
            facts.Add(new ValidationHtmlMetaItem { Label = "Payload Preview", Value = payloadPreview });
        }

        var remediation = group
            .SelectMany(item => item.EvidenceReferences)
            .Select(reference => reference.Remediation)
            .FirstOrDefault(value => !string.IsNullOrWhiteSpace(value));
        if (!string.IsNullOrWhiteSpace(remediation))
        {
            facts.Add(new ValidationHtmlMetaItem { Label = "Remediation", Value = remediation });
        }

        foreach (var pair in representative.Metadata.Take(3))
        {
            facts.Add(new ValidationHtmlMetaItem { Label = pair.Key, Value = pair.Value });
        }

        return new ValidationHtmlDecisionTraceItem
        {
            Title = !string.IsNullOrWhiteSpace(representative.RuleId) ? representative.RuleId! : representative.Category,
            Summary = BuildDecisionTraceSummary(group, representative, affectedComponents),
            Category = representative.Category,
            ComponentLabel = FormatComponentSet(affectedComponents),
            GateLabel = FormatGateLabel(representative.Gate),
            AuthorityLabel = ValidationRuleSourceClassifier.GetLabel(representative.Authority),
            EvidenceLabel = BuildDecisionEvidenceLabel(representative),
            RuleId = representative.RuleId,
            SpecReference = representative.SpecReference,
            Tone = MapDecisionTone(representative.Gate, representative.Severity),
            ImpactAreas = impactAreas,
            Facts = facts
        };
    }

    private static IReadOnlyList<ValidationHtmlHotspot> BuildHotspots(ValidationResult result)
    {
        var sourceDecisions = result.VerdictAssessment?.BlockingDecisions is { Count: > 0 }
            ? result.VerdictAssessment.BlockingDecisions
            : result.VerdictAssessment?.TriggeredDecisions ?? new List<DecisionRecord>();

        return sourceDecisions
            .Where(decision => !string.IsNullOrWhiteSpace(decision.Component))
            .GroupBy(decision => decision.Component, StringComparer.OrdinalIgnoreCase)
            .OrderByDescending(group => group.Count())
            .ThenByDescending(group => group.Max(item => item.Gate))
            .Take(MaxHotspots)
            .Select(group =>
            {
                var representative = group.First();
                return new ValidationHtmlHotspot
                {
                    Component = group.Key,
                    Domain = MapDecisionToDomain(representative),
                    SignalCount = group.Count(),
                    Summary = group.Count() == 1
                        ? representative.Summary
                        : $"{group.Count()} escalated decisions. Representative issue: {representative.Summary}",
                    Tone = MapDecisionTone(representative.Gate, representative.Severity)
                };
            })
            .ToList();
    }

    private static IReadOnlyList<ValidationHtmlCompatibilityTheme> BuildCompatibilityThemes(ValidationResult result)
    {
        var compatibility = result.ClientCompatibility;
        if (compatibility == null || compatibility.Assessments.Count == 0)
        {
            return Array.Empty<ValidationHtmlCompatibilityTheme>();
        }

        return compatibility.Assessments
            .SelectMany(assessment => assessment.Requirements
                .Where(requirement => requirement.Outcome is ClientProfileRequirementOutcome.Warning or ClientProfileRequirementOutcome.Failed)
                .Select(requirement => (Assessment: assessment, Requirement: requirement)))
            .GroupBy(
                item => string.IsNullOrWhiteSpace(item.Requirement.RequirementId)
                    ? item.Requirement.Title
                    : item.Requirement.RequirementId,
                StringComparer.OrdinalIgnoreCase)
            .OrderByDescending(group => group.Count())
            .ThenByDescending(group => group.Any(item => item.Requirement.Outcome == ClientProfileRequirementOutcome.Failed))
            .Take(MaxCompatibilityThemes)
            .Select(group =>
            {
                var representative = group.First();
                var profiles = group.Select(item => item.Assessment.DisplayName).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
                return new ValidationHtmlCompatibilityTheme
                {
                    Title = representative.Requirement.Title,
                    Summary = $"Affects {profiles.Count}/{compatibility.Assessments.Count} profile(s). {representative.Requirement.Summary}",
                    ProfileCount = profiles.Count,
                    Profiles = profiles,
                    Tone = group.Any(item => item.Requirement.Outcome == ClientProfileRequirementOutcome.Failed)
                        ? HtmlReportTone.Danger
                        : HtmlReportTone.Warning
                };
            })
            .ToList();
    }

    private static ValidationHtmlMetricCard CreateRiskCard(string label, double score, string supportingText)
    {
        return new ValidationHtmlMetricCard
        {
            Eyebrow = "Risk Snapshot",
            Value = $"{score:F0}%",
            Label = label,
            SupportingText = supportingText,
            Tone = MapScoreTone(score)
        };
    }

    private static ValidationHtmlMetricCard CreateVerdictCard(string label, ValidationVerdict verdict, string supportingText)
    {
        return new ValidationHtmlMetricCard
        {
            Eyebrow = "Verdict",
            Value = FormatVerdictLabel(verdict),
            Label = label,
            SupportingText = supportingText,
            Tone = MapVerdictTone(verdict)
        };
    }

    private static ValidationHtmlBootstrapSummary? BuildBootstrapSummary(ValidationResult result, HealthCheckResult? bootstrapHealth)
    {
        if (bootstrapHealth == null)
        {
            return null;
        }

        var handshakeStatus = bootstrapHealth.InitializationDetails?.Transport.StatusCode is int statusCode
            ? $"HTTP {statusCode}"
            : "Not available";
        var handshakeLatency = bootstrapHealth.ResponseTimeMs > 0
            ? $"{bootstrapHealth.ResponseTimeMs:F1} ms"
            : "Not available";
        var protocolVersion = result.ProtocolVersion
            ?? bootstrapHealth.ProtocolVersion
            ?? result.ServerConfig.ProtocolVersion
            ?? "Not available";
        var serverVersion = !string.IsNullOrWhiteSpace(bootstrapHealth.ServerVersion) &&
                            !string.Equals(bootstrapHealth.ServerVersion, "Unknown", StringComparison.OrdinalIgnoreCase)
            ? bootstrapHealth.ServerVersion
            : "Not available";
        var deferredLabel = bootstrapHealth.AllowsDeferredValidation && !bootstrapHealth.IsHealthy
            ? "Yes"
            : "No";

        return new ValidationHtmlBootstrapSummary
        {
            Eyebrow = "Preflight Connectivity",
            Title = GetBootstrapHeadline(bootstrapHealth),
            Summary = GetBootstrapNarrative(bootstrapHealth),
            BadgeLabel = GetBootstrapBadgeLabel(bootstrapHealth),
            Tone = MapBootstrapTone(bootstrapHealth.Disposition),
            MetaItems = new List<ValidationHtmlMetaItem>
            {
                new() { Label = "Bootstrap State", Value = GetBootstrapStateLabel(bootstrapHealth) },
                new() { Label = "Deferred Validation", Value = deferredLabel },
                new() { Label = "Handshake HTTP", Value = handshakeStatus },
                new() { Label = "Handshake Latency", Value = handshakeLatency },
                new() { Label = "Negotiated Protocol", Value = protocolVersion },
                new() { Label = "Observed Server Version", Value = serverVersion }
            },
            Note = bootstrapHealth.ErrorMessage
        };
    }

    private static string BuildHeroSubtitle(ValidationResult result)
    {
        if (result.PolicyOutcome is { Passed: false } policyOutcome)
        {
            var modeLabel = FormatModeLabel(policyOutcome.Mode);
            var blockingSignalCount = ResolveBlockingSignalCount(result);
            var unsuppressedSignalCount = ResolveUnsuppressedSignalCount(result);
            var totalSignalCount = ResolveTotalSignalCount(result);

            return policyOutcome.SuppressedSignalCount > 0
                ? $"{modeLabel} policy blocked release readiness with {blockingSignalCount} blocking signal(s); {policyOutcome.SuppressedSignalCount} signal(s) were suppressed from {totalSignalCount} evaluated."
                : $"{modeLabel} policy blocked release readiness with {blockingSignalCount} blocking signal(s) across {unsuppressedSignalCount} unsuppressed signal(s).";
        }

        if (result.CriticalErrors.Count > 0)
        {
            return $"Review {result.CriticalErrors.Count} critical signal(s) before adoption.";
        }

        if (result.VerdictAssessment != null)
        {
            return result.VerdictAssessment.Summary;
        }

        if (result.TrustAssessment != null)
        {
            return "Protocol, security, AI safety, and operational evidence are summarized below.";
        }

        return "Review the summarized validation evidence before adoption.";
    }

    private static HealthCheckResult? ResolveBootstrapHealth(ValidationResult validationResult)
    {
        if (validationResult.BootstrapHealth != null)
        {
            return validationResult.BootstrapHealth;
        }

        if (validationResult.InitializationHandshake == null)
        {
            return null;
        }

        return new HealthCheckResult
        {
            IsHealthy = validationResult.InitializationHandshake.IsSuccessful,
            Disposition = ValidationReliability.ClassifyHealthCheck(validationResult.InitializationHandshake),
            ResponseTimeMs = validationResult.InitializationHandshake.Transport.Duration.TotalMilliseconds,
            ServerVersion = validationResult.InitializationHandshake.Payload?.ServerInfo?.Version,
            ProtocolVersion = validationResult.InitializationHandshake.Payload?.ProtocolVersion,
            ErrorMessage = validationResult.InitializationHandshake.IsSuccessful ? null : validationResult.InitializationHandshake.Error,
            InitializationDetails = validationResult.InitializationHandshake
        };
    }

    private static List<DecisionRecord> GetDomainDecisions(ValidationResult result, string domain, bool blockingOnly)
    {
        var source = blockingOnly
            ? result.VerdictAssessment?.BlockingDecisions
            : result.VerdictAssessment?.TriggeredDecisions;

        return source?
            .Where(decision => string.Equals(MapDecisionToDomain(decision), domain, StringComparison.OrdinalIgnoreCase))
            .ToList()
            ?? new List<DecisionRecord>();
    }

    private static string MapDecisionToDomain(DecisionRecord decision)
    {
        var category = decision.Category ?? string.Empty;
        var ruleId = decision.RuleId ?? string.Empty;

        if (ruleId.Contains("AUTH", StringComparison.OrdinalIgnoreCase)
            || category.Contains("auth", StringComparison.OrdinalIgnoreCase)
            || category.Contains("exfil", StringComparison.OrdinalIgnoreCase)
            || decision.ImpactAreas.Any(area => area is ImpactArea.AuthenticationBoundary or ImpactArea.DataExposure))
        {
            return "Security";
        }

        if (ruleId.StartsWith("AI.", StringComparison.OrdinalIgnoreCase)
            || category.Contains("aireadiness", StringComparison.OrdinalIgnoreCase)
            || category.Contains("contentsafety", StringComparison.OrdinalIgnoreCase)
            || category.Contains("promptinjection", StringComparison.OrdinalIgnoreCase)
            || category.Contains("destructive", StringComparison.OrdinalIgnoreCase)
            || category.Contains("llm", StringComparison.OrdinalIgnoreCase)
            || decision.ImpactAreas.Any(area => area is ImpactArea.UnsafeAutonomy or ImpactArea.OutputIntegrity))
        {
            return "AI Safety";
        }

        if (category.Contains("error", StringComparison.OrdinalIgnoreCase)
            || category.Contains("performance", StringComparison.OrdinalIgnoreCase)
            || category.Contains("execution", StringComparison.OrdinalIgnoreCase)
            || category.Contains("recovery", StringComparison.OrdinalIgnoreCase)
            || decision.ImpactAreas.Any(area => area is ImpactArea.RecoveryIntegrity or ImpactArea.OperationalResilience))
        {
            return "Operations";
        }

        if (category.Contains("coverage", StringComparison.OrdinalIgnoreCase)
            || decision.ImpactAreas.Any(area => area == ImpactArea.CoverageIntegrity))
        {
            return "Coverage";
        }

        if (category.Contains("compat", StringComparison.OrdinalIgnoreCase))
        {
            return "Compatibility";
        }

        if (decision.ImpactAreas.Any(area => area is ImpactArea.ProtocolInteroperability or ImpactArea.CapabilityContract))
        {
            return "Protocol";
        }

        return "Protocol";
    }

    private static string BuildDecisionThemeKey(DecisionRecord decision)
    {
        if (!string.IsNullOrWhiteSpace(decision.RuleId))
        {
            return $"rule:{decision.RuleId}";
        }

        return $"category:{decision.Category}:gate:{decision.Gate}";
    }

    private static string BuildDecisionTraceSummary(
        IGrouping<string, DecisionRecord> group,
        DecisionRecord representative,
        IReadOnlyCollection<string> affectedComponents)
    {
        if (group.Count() == 1)
        {
            return representative.Summary;
        }

        return $"{group.Count()} related decision(s) across {affectedComponents.Count} component(s). Representative issue: {representative.Summary}";
    }

    private static IReadOnlyList<string> BuildReleaseHighlights(
        ValidationResult result,
        IReadOnlyList<string> priorityFindings,
        IReadOnlyList<ValidationHtmlDecisionTraceItem> decisionTrace,
        IReadOnlyList<ValidationHtmlCompatibilityTheme> compatibilityThemes)
    {
        var highlights = new List<string>();

        if (result.PolicyOutcome is { Passed: false } policyOutcome)
        {
            highlights.Add($"{ResolveBlockingSignalCount(result)} blocking signal(s) remain under {FormatModeLabel(policyOutcome.Mode)} policy.");
        }

        highlights.AddRange(decisionTrace.Take(2).Select(item => item.Title));

        if (result.ClientCompatibility?.IncompatibleCount > 0 && compatibilityThemes.Any(theme => theme.Tone == HtmlReportTone.Danger))
        {
            var blockingTheme = compatibilityThemes.First(theme => theme.Tone == HtmlReportTone.Danger);
            highlights.Add($"Compatibility theme: {blockingTheme.Title} affects {blockingTheme.ProfileCount} profile(s).");
        }

        highlights.AddRange(priorityFindings.Take(2));

        return highlights
            .Where(item => !string.IsNullOrWhiteSpace(item))
            .Distinct(StringComparer.Ordinal)
            .Take(4)
            .ToList();
    }

    private static bool MatchesPolicySummary(string hint, string? policySummary)
    {
        if (string.IsNullOrWhiteSpace(policySummary))
        {
            return false;
        }

        return string.Equals(hint.TrimEnd('.'), policySummary.TrimEnd('.'), StringComparison.OrdinalIgnoreCase);
    }

    private static string ResolveReleaseStatusLabel(ValidationResult result)
    {
        return result.PolicyOutcome is { Passed: false }
            ? "Hold Release"
            : result.OverallStatus == ValidationStatus.Passed
                ? "Proceed"
                : "Review Before Release";
    }

    private static HtmlReportTone ResolveReleaseTone(ValidationResult result)
    {
        if (result.PolicyOutcome is { Passed: false })
        {
            return HtmlReportTone.Danger;
        }

        return result.OverallStatus switch
        {
            ValidationStatus.Passed => HtmlReportTone.Success,
            ValidationStatus.Failed => HtmlReportTone.Warning,
            _ => HtmlReportTone.Warning
        };
    }

    private static string BuildEvidencePostureLabel(ValidationResult result)
    {
        if (result.VerdictAssessment != null)
        {
            return $"Baseline {FormatVerdictLabel(result.VerdictAssessment.BaselineVerdict)} · Coverage {FormatVerdictLabel(result.VerdictAssessment.CoverageVerdict)}";
        }

        return result.TrustAssessment?.TrustLabel ?? "Trust model unavailable";
    }

    private static string BuildVerdictCompositeLabel(ValidationResult result)
    {
        if (result.VerdictAssessment == null)
        {
            return result.TrustAssessment?.TrustLabel ?? "No deterministic verdict";
        }

        return $"Baseline {FormatVerdictLabel(result.VerdictAssessment.BaselineVerdict)} · Protocol {FormatVerdictLabel(result.VerdictAssessment.ProtocolVerdict)} · Coverage {FormatVerdictLabel(result.VerdictAssessment.CoverageVerdict)}";
    }

    private static string BuildSignalLabel(int blockingCount, int totalCount)
    {
        if (totalCount == 0)
        {
            return "No escalated signals";
        }

        return $"{blockingCount} blocking / {totalCount} total";
    }

    private static string BuildDecisionEvidenceLabel(DecisionRecord representative)
    {
        var evidenceOrigin = representative.Origin switch
        {
            EvidenceOrigin.DeterministicObservation => "deterministic observation",
            EvidenceOrigin.DeterministicAggregation => "deterministic aggregation",
            EvidenceOrigin.HeuristicInference => "heuristic inference",
            EvidenceOrigin.ModelAssistance => "model assistance",
            _ => "recorded evidence"
        };

        return !string.IsNullOrWhiteSpace(representative.SpecReference)
            ? AppendEvidenceCount($"{ValidationRuleSourceClassifier.GetLabel(representative.Authority)} · {evidenceOrigin} · spec-linked", representative)
            : AppendEvidenceCount($"{ValidationRuleSourceClassifier.GetLabel(representative.Authority)} · {evidenceOrigin}", representative);
    }

    private static string AppendEvidenceCount(string label, DecisionRecord representative)
    {
        return representative.RelatedEvidenceIds.Count == 0
            ? label
            : $"{label} · {representative.RelatedEvidenceIds.Count} evidence link(s)";
    }

    private static string FormatComponentSet(IReadOnlyList<string> components)
    {
        if (components.Count == 0)
        {
            return "No component recorded";
        }

        if (components.Count <= 3)
        {
            return string.Join(", ", components);
        }

        return $"{string.Join(", ", components.Take(3))} +{components.Count - 3} more";
    }

    private static int ResolveBlockingSignalCount(ValidationResult result)
    {
        var policyOutcome = result.PolicyOutcome;
        if (policyOutcome == null)
        {
            return result.VerdictAssessment?.BlockingDecisions.Count ?? 0;
        }

        if (policyOutcome.BlockingSignalCount > 0)
        {
            return policyOutcome.BlockingSignalCount;
        }

        var parsedFromSummary = TryParseSummarySignalCount(policyOutcome.Summary);
        if (parsedFromSummary.HasValue && !policyOutcome.Passed)
        {
            return parsedFromSummary.Value;
        }

        return policyOutcome.Reasons.Count;
    }

    private static int ResolveUnsuppressedSignalCount(ValidationResult result)
    {
        var policyOutcome = result.PolicyOutcome;
        if (policyOutcome == null)
        {
            return result.VerdictAssessment?.TriggeredDecisions.Count(decision => decision.Gate != GateOutcome.Note) ?? 0;
        }

        if (policyOutcome.UnsuppressedSignalCount > 0)
        {
            return policyOutcome.UnsuppressedSignalCount;
        }

        var parsedFromSummary = TryParseSummarySignalCount(policyOutcome.Summary);
        if (parsedFromSummary.HasValue)
        {
            return parsedFromSummary.Value;
        }

        return policyOutcome.Reasons.Count;
    }

    private static int ResolveTotalSignalCount(ValidationResult result)
    {
        var policyOutcome = result.PolicyOutcome;
        if (policyOutcome == null)
        {
            return result.VerdictAssessment?.TriggeredDecisions.Count(decision => decision.Gate != GateOutcome.Note) ?? 0;
        }

        if (policyOutcome.TotalSignalCount > 0)
        {
            return policyOutcome.TotalSignalCount;
        }

        return ResolveUnsuppressedSignalCount(result) + Math.Max(policyOutcome.SuppressedSignalCount, 0);
    }

    private static int? TryParseSummarySignalCount(string? summary)
    {
        if (string.IsNullOrWhiteSpace(summary))
        {
            return null;
        }

        var match = Regex.Match(summary, @"with\s+(\d+)\s+unsuppressed\s+signal", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
        if (!match.Success)
        {
            return null;
        }

        return int.TryParse(match.Groups[1].Value, out var parsedCount) ? parsedCount : null;
    }

    private static string FormatGateLabel(GateOutcome gate)
    {
        return gate switch
        {
            GateOutcome.Reject => "Reject",
            GateOutcome.ReviewRequired => "Review Required",
            GateOutcome.CoverageDebt => "Coverage Debt",
            _ => "Note"
        };
    }

    private static HtmlReportTone MapDecisionTone(GateOutcome gate, ValidationFindingSeverity severity)
    {
        return gate switch
        {
            GateOutcome.Reject => HtmlReportTone.Danger,
            GateOutcome.ReviewRequired => severity >= ValidationFindingSeverity.High ? HtmlReportTone.Danger : HtmlReportTone.Warning,
            GateOutcome.CoverageDebt => HtmlReportTone.Info,
            _ => HtmlReportTone.Neutral
        };
    }

    private static string FormatImpactArea(ImpactArea impactArea)
    {
        return impactArea switch
        {
            ImpactArea.ProtocolInteroperability => "Protocol interoperability",
            ImpactArea.CapabilityContract => "Capability contract",
            ImpactArea.AuthenticationBoundary => "Authentication boundary",
            ImpactArea.UnsafeAutonomy => "Unsafe autonomy",
            ImpactArea.OutputIntegrity => "Output integrity",
            ImpactArea.DataExposure => "Data exposure",
            ImpactArea.RecoveryIntegrity => "Recovery integrity",
            ImpactArea.OperationalResilience => "Operational resilience",
            ImpactArea.CoverageIntegrity => "Coverage integrity",
            _ => impactArea.ToString()
        };
    }

    private static string FormatModeLabel(string? mode)
    {
        if (string.IsNullOrWhiteSpace(mode))
        {
            return "Validation";
        }

        return $"{char.ToUpperInvariant(mode[0])}{mode[1..]}";
    }

    private static HtmlReportTone MapScoreTone(double score)
    {
        if (score >= 88)
        {
            return HtmlReportTone.Success;
        }

        if (score >= 70)
        {
            return HtmlReportTone.Info;
        }

        if (score >= 50)
        {
            return HtmlReportTone.Warning;
        }

        return HtmlReportTone.Danger;
    }

    private static string GetScoreNarrative(double score)
    {
        if (score >= 88)
        {
            return "Healthy evidence profile";
        }

        if (score >= 70)
        {
            return "Strong with targeted follow-up";
        }

        if (score >= 50)
        {
            return "Usable, but review blockers remain";
        }

        return "High review pressure";
    }

    private static HtmlReportTone MapTrustTone(McpTrustLevel? trustLevel)
    {
        return trustLevel switch
        {
            McpTrustLevel.L5_CertifiedSecure => HtmlReportTone.Success,
            McpTrustLevel.L4_Trusted => HtmlReportTone.Success,
            McpTrustLevel.L3_Acceptable => HtmlReportTone.Info,
            McpTrustLevel.L2_Caution => HtmlReportTone.Warning,
            McpTrustLevel.L1_Untrusted => HtmlReportTone.Danger,
            _ => HtmlReportTone.Neutral
        };
    }

    private static HtmlReportTone ResolveTrustTone(McpTrustAssessment? assessment) =>
        assessment?.LimitedByIncompleteEvidence == true
            ? HtmlReportTone.Warning
            : MapTrustTone(assessment?.TrustLevel);

    private static HtmlReportTone MapVerdictTone(ValidationVerdict verdict)
    {
        return verdict switch
        {
            ValidationVerdict.Trusted => HtmlReportTone.Success,
            ValidationVerdict.ConditionallyAcceptable => HtmlReportTone.Info,
            ValidationVerdict.ReviewRequired => HtmlReportTone.Warning,
            ValidationVerdict.Reject => HtmlReportTone.Danger,
            _ => HtmlReportTone.Neutral
        };
    }

    private static string FormatVerdictLabel(ValidationVerdict verdict)
    {
        return verdict switch
        {
            ValidationVerdict.Trusted => "Trusted",
            ValidationVerdict.ConditionallyAcceptable => "Conditional",
            ValidationVerdict.ReviewRequired => "Review Required",
            ValidationVerdict.Reject => "Reject",
            _ => "Unknown"
        };
    }

    private static HtmlReportTone MapBootstrapTone(HealthCheckDisposition disposition)
    {
        return disposition switch
        {
            HealthCheckDisposition.Healthy => HtmlReportTone.Success,
            HealthCheckDisposition.Protected => HtmlReportTone.Info,
            HealthCheckDisposition.TransientFailure => HtmlReportTone.Warning,
            HealthCheckDisposition.Inconclusive => HtmlReportTone.Info,
            HealthCheckDisposition.Unhealthy => HtmlReportTone.Danger,
            _ => HtmlReportTone.Neutral
        };
    }

    private static string GetBootstrapHeadline(HealthCheckResult bootstrapHealth) => bootstrapHealth.Disposition switch
    {
        HealthCheckDisposition.Healthy => "Validation started from a clean initialize handshake.",
        HealthCheckDisposition.Protected => "Validation crossed an expected authentication boundary during bootstrap.",
        HealthCheckDisposition.TransientFailure => "Validation continued after a retry-worthy bootstrap constraint.",
        HealthCheckDisposition.Inconclusive => "Validation continued despite an inconclusive bootstrap handshake.",
        HealthCheckDisposition.Unhealthy => "Validation observed a definitive bootstrap failure.",
        _ => "Validation bootstrap state is unknown."
    };

    private static string GetBootstrapNarrative(HealthCheckResult bootstrapHealth) => bootstrapHealth.Disposition switch
    {
        HealthCheckDisposition.Healthy => "Connectivity, initialize negotiation, and readiness checks completed cleanly before category execution began.",
        HealthCheckDisposition.Protected => "The endpoint was reachable but protected, so validation continued because the outcome indicated security boundaries rather than an unreachable server.",
        HealthCheckDisposition.TransientFailure => "The preflight handshake matched a transient capacity or transport signal, so validation continued to gather evidence without treating the endpoint as hard down.",
        HealthCheckDisposition.Inconclusive => "The server responded, but the initialize handshake did not fully establish readiness. Later categories provide the authoritative evidence.",
        HealthCheckDisposition.Unhealthy => "Bootstrap failed definitively and subsequent evidence, if any, should be interpreted as partial.",
        _ => "Bootstrap classification was not available."
    };

    private static string GetBootstrapStateLabel(HealthCheckResult bootstrapHealth) => bootstrapHealth.Disposition switch
    {
        HealthCheckDisposition.Healthy => "Healthy",
        HealthCheckDisposition.Protected => "Reachable (Protected)",
        HealthCheckDisposition.TransientFailure => "Transient Failure",
        HealthCheckDisposition.Inconclusive => "Inconclusive",
        HealthCheckDisposition.Unhealthy => "Unhealthy",
        _ => "Unknown"
    };

    private static string GetBootstrapBadgeLabel(HealthCheckResult bootstrapHealth) => bootstrapHealth.Disposition switch
    {
        HealthCheckDisposition.Healthy => "Healthy bootstrap",
        HealthCheckDisposition.Protected => "Protected endpoint",
        HealthCheckDisposition.TransientFailure => "Transient constraint",
        HealthCheckDisposition.Inconclusive => "Inconclusive bootstrap",
        HealthCheckDisposition.Unhealthy => "Unhealthy bootstrap",
        _ => "Bootstrap state unknown"
    };
}