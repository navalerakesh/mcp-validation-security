using System.Text.RegularExpressions;
using Mcp.Benchmark.Core.Constants;
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
            ReleaseDecision = BuildReleaseDecision(result),
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
        var inventoryProfile = string.Equals(result.Assurance.Profile.ProfileId, AssuranceProfileCatalog.Inventory.ProfileId, StringComparison.OrdinalIgnoreCase);
        var title = result.Assurance.CertificateEligibility.Status switch
        {
            CertificateEligibilityStatus.Eligible => "Provider Certificate Eligible",
            CertificateEligibilityStatus.Ineligible when inventoryProfile => "Inventory Assessment Complete",
            CertificateEligibilityStatus.Ineligible => "Assurance Requirements Not Met",
            _ => "Assurance Review Required"
        };
        var protocolVersion = result.ProtocolVersion
            ?? result.InitializationHandshake?.Payload?.ProtocolVersion
            ?? result.ServerConfig.ProtocolVersion
            ?? "Not available";
        var duration = result.Duration?.TotalSeconds > 0
            ? $"{result.Duration.Value.TotalSeconds:F1}s"
            : "Not available";
        // Persisted run evidence is authoritative. The report option is only a fallback for legacy artifacts.
        var schemaVersion = result.Run.SchemaVersion
            ?? result.Run.ApplicabilityContext?.SchemaVersion
            ?? (string.IsNullOrWhiteSpace(reportConfig.SpecProfile) ? "latest" : reportConfig.SpecProfile);

        return new ValidationHtmlHero
        {
            Eyebrow = "Model Context Protocol (MCP) Assurance Report",
            Title = title,
            Subtitle = BuildHeroSubtitle(result),
            StatusLabel = ResolveReleaseStatusLabel(result),
            StatusTone = releaseTone,
            VerdictLabel = result.Assurance.Assessments.CoreConformance.Status.ToString(),
            VerdictTone = MapCoreTone(result.Assurance.Assessments.CoreConformance.Status),
            SecurityStatusLabel = result.Assurance.Assessments.SecurityAssurance.Status.ToString(),
            SecurityStatusDetail = $"Operational readiness: {result.Assurance.Assessments.OperationalReadiness.Status}",
            SecurityStatusTone = MapSecurityTone(result.Assurance.Assessments.SecurityAssurance.Status),
            MetaItems = new List<ValidationHtmlMetaItem>
            {
                new() { Label = "Endpoint", Value = result.ServerConfig.Endpoint ?? "Not available" },
                new() { Label = "Validation Identifier", Value = result.ValidationId },
                new() { Label = "Duration", Value = duration },
                new() { Label = "MCP Specification Profile", Value = schemaVersion },
                new() { Label = "Server Profile", Value = $"{result.ServerProfile} ({result.ServerProfileSource})" },
                new() { Label = "Protocol Version", Value = protocolVersion }
            }
        };
    }

    private static ValidationHtmlReleaseDecision BuildReleaseDecision(ValidationResult result)
    {
        var assurance = result.Assurance;
        var exitCode = AssuranceExitCodeMapper.Map(assurance, result.PolicyOutcome);
        var eligible = assurance.CertificateEligibility.Status == CertificateEligibilityStatus.Eligible;
        var inventoryProfile = string.Equals(assurance.Profile.ProfileId, AssuranceProfileCatalog.Inventory.ProfileId, StringComparison.OrdinalIgnoreCase);
        var assuranceHighlights = assurance.CertificateEligibility.Reasons.Take(4).ToList();
        if (result.PolicyOutcome?.AppliedWaivers.Count > 0)
        {
            assuranceHighlights.Add($"{result.PolicyOutcome.AppliedWaivers.Count} governed waiver(s) applied.");
        }
        return new ValidationHtmlReleaseDecision
        {
            Eyebrow = "Assurance Decision",
            Title = eligible
                ? "Selected assurance profile satisfied"
                : inventoryProfile
                    ? "Inventory completed with assurance evidence gaps"
                    : "Selected assurance profile is not satisfied",
            Summary = assurance.CertificateEligibility.Reasons.Count == 0
                ? "All requirements for the selected assurance profile are satisfied."
                : string.Join(" ", assurance.CertificateEligibility.Reasons.Take(3)),
            Tone = ResolveReleaseTone(result),
            Highlights = assuranceHighlights,
            PolicyModeLabel = assurance.Profile.ProfileId,
            ExitCodeLabel = exitCode.ToString(),
            VerdictLabel = inventoryProfile && !eligible ? "Not issued" : assurance.CertificateEligibility.Status.ToString(),
            TotalSignalCount = assurance.Claims.Count,
            UnsuppressedSignalCount = assurance.Claims.Count(claim => claim.Outcome is not (AssuranceClaimOutcome.Satisfied or AssuranceClaimOutcome.NotApplicable)),
            BlockingSignalCount = assurance.Claims.Count(claim => claim.Outcome is AssuranceClaimOutcome.Violated or AssuranceClaimOutcome.Error),
            SuppressedSignalCount = result.PolicyOutcome?.SuppressedSignalCount ?? 0
        };
    }

    private static IReadOnlyList<ValidationHtmlMetricCard> BuildOverviewMetrics(ValidationResult result)
    {
        var assurance = result.Assurance;
        var inventoryProfile = string.Equals(assurance.Profile.ProfileId, AssuranceProfileCatalog.Inventory.ProfileId, StringComparison.OrdinalIgnoreCase);
        var cards = new List<ValidationHtmlMetricCard>
        {
            new()
            {
                Eyebrow = "Certificate",
                Value = inventoryProfile && assurance.CertificateEligibility.Status != CertificateEligibilityStatus.Eligible
                    ? "Not issued"
                    : assurance.CertificateEligibility.Status.ToString(),
                Label = "Certificate Outcome",
                SupportingText = $"Profile: {assurance.Profile.ProfileId}",
                Tone = ResolveReleaseTone(result)
            },
            new()
            {
                Eyebrow = "Core",
                Value = assurance.Assessments.CoreConformance.Status.ToString(),
                Label = "Conformance",
                SupportingText = $"{assurance.Assessments.CoreConformance.SatisfiedRequiredClaims}/{assurance.Assessments.CoreConformance.RequiredClaims} required claims satisfied",
                Tone = MapCoreTone(assurance.Assessments.CoreConformance.Status)
            },
            new()
            {
                Eyebrow = "Security",
                Value = assurance.Assessments.SecurityAssurance.Status.ToString(),
                Label = "Assurance",
                SupportingText = $"{assurance.Assessments.SecurityAssurance.DeterministicControlClaims} deterministic control claim(s)",
                Tone = MapSecurityTone(assurance.Assessments.SecurityAssurance.Status)
            },
            new()
            {
                Eyebrow = "Operations",
                Value = assurance.Assessments.OperationalReadiness.Status.ToString(),
                Label = "Readiness",
                SupportingText = $"{assurance.Assessments.OperationalReadiness.ObjectiveClaims} objective claim(s)",
                Tone = MapOperationsTone(assurance.Assessments.OperationalReadiness.Status)
            },
            new()
            {
                Eyebrow = "Evidence",
                Value = $"{assurance.EvidenceSummary.EvidenceCoverageRatio:P1}",
                Label = "Coverage",
                SupportingText = $"{assurance.EvidenceSummary.EvidenceConfidenceRatio:P1} confidence",
                Tone = MapScoreTone(assurance.EvidenceSummary.EvidenceCoverageRatio * 100)
            }
        };

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
        return result.Assurance.EvidenceSummary;
    }

    private static IReadOnlyList<ValidationHtmlMetricCard> BuildRiskMetrics(ValidationResult result)
    {
        var claims = result.Assurance.Claims;
        return new List<ValidationHtmlMetricCard>
        {
            new()
            {
                Eyebrow = "Claims",
                Value = claims.Count(claim => claim.Outcome == AssuranceClaimOutcome.Violated).ToString(),
                Label = "Violated",
                SupportingText = "Deterministic claim failures",
                Tone = HtmlReportTone.Danger
            },
            new()
            {
                Eyebrow = "Claims",
                Value = claims.Count(claim => claim.Outcome == AssuranceClaimOutcome.ReviewRequired).ToString(),
                Label = "Review Required",
                SupportingText = "Claims requiring human review",
                Tone = HtmlReportTone.Warning
            },
            new()
            {
                Eyebrow = "Claims",
                Value = claims.Count(claim => claim.Outcome == AssuranceClaimOutcome.Incomplete).ToString(),
                Label = "Incomplete",
                SupportingText = "Applicable claims lacking evidence",
                Tone = HtmlReportTone.Warning
            },
            new()
            {
                Eyebrow = "Claims",
                Value = claims.Count.ToString(),
                Label = "Total",
                SupportingText = "All projected assurance claims",
                Tone = HtmlReportTone.Info
            }
        };
    }

    private static IReadOnlyList<ValidationHtmlDomainSummary> BuildDomainSummaries(ValidationResult result)
    {
        return new List<ValidationHtmlDomainSummary>
        {
            BuildAssuranceDomainSummary(result, AssuranceDimension.CoreProtocol, "Core Conformance", result.Assurance.Assessments.CoreConformance.Status.ToString(), MapCoreTone(result.Assurance.Assessments.CoreConformance.Status)),
            BuildAssuranceDomainSummary(result, AssuranceDimension.Security, "Security Assurance", result.Assurance.Assessments.SecurityAssurance.Status.ToString(), MapSecurityTone(result.Assurance.Assessments.SecurityAssurance.Status)),
            BuildAssuranceDomainSummary(result, AssuranceDimension.Operations, "Operational Readiness", result.Assurance.Assessments.OperationalReadiness.Status.ToString(), MapOperationsTone(result.Assurance.Assessments.OperationalReadiness.Status)),
            BuildCompatibilityDomainSummary(result),
            BuildCoverageDomainSummary(result)
        };
    }

    private static ValidationHtmlDomainSummary BuildAssuranceDomainSummary(
        ValidationResult result,
        AssuranceDimension dimension,
        string domain,
        string status,
        HtmlReportTone tone)
    {
        var claims = result.Assurance.Claims.Where(claim => claim.Dimension == dimension).ToList();
        var nonSuccess = claims.Where(claim => claim.Outcome is not (AssuranceClaimOutcome.Satisfied or AssuranceClaimOutcome.NotApplicable)).ToList();
        return new ValidationHtmlDomainSummary
        {
            Domain = domain,
            StatusLabel = status,
            SignalLabel = $"{nonSuccess.Count} non-success claim(s)",
            EvidenceLabel = $"{claims.Count} applicable/projected claim(s)",
            Summary = nonSuccess.FirstOrDefault()?.Summary ?? "No non-success claims were projected for this dimension.",
            ActionLabel = nonSuccess.FirstOrDefault()?.Remediation ?? "Preserve evidence and monitor for drift.",
            Tone = tone
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
        var incompleteCount = coverageDeclarations.Count(ValidationEvidenceSummarizer.IsEvidenceDebt);

        return new ValidationHtmlDomainSummary
        {
            Domain = "Coverage",
            StatusLabel = incompleteCount == 0 ? "Complete" : "Incomplete",
            SignalLabel = blockedCount > 0 ? $"{blockedCount} blocked scope(s)" : $"{coveredCount} covered scope(s)",
            EvidenceLabel = $"{coveredCount} covered · {skippedCount} skipped · {blockedCount} blocked declaration(s)",
            Summary = incompleteCount == 0
                ? "All applicable baseline coverage declarations produced evidence."
                : $"{incompleteCount} applicable coverage declaration(s) remain incomplete.",
            ActionLabel = blockedCount > 0
                ? "Resolve blocked scopes or document why the affected evidence cannot be collected safely."
                : "Keep skipped and not-applicable reasons explicit so downstream readers can trust the scope of the run.",
            Tone = incompleteCount == 0 ? HtmlReportTone.Success : HtmlReportTone.Warning
        };
    }

    private static IReadOnlyList<ValidationHtmlDecisionTraceItem> BuildDecisionTrace(ValidationResult result)
    {
        return result.Assurance.Claims
            .Where(claim => claim.Outcome is not (AssuranceClaimOutcome.Satisfied or AssuranceClaimOutcome.NotApplicable) &&
                !string.IsNullOrWhiteSpace(claim.Summary))
            .GroupBy(claim => $"{claim.Authority}|{claim.Metadata.GetValueOrDefault("ruleId") ?? claim.ClaimId}", StringComparer.OrdinalIgnoreCase)
            .OrderBy(group => group.Min(item => item.Authority))
            .ThenByDescending(group => group.Max(item => item.Outcome))
            .ThenByDescending(group => group.Count())
            .ThenByDescending(group => group.Max(item => item.Confidence))
            .Take(MaxDecisionTraceItems)
            .Select(group =>
            {
                var representative = group.First();
                var affectedComponents = group.Select(item => item.Subject)
                    .Where(component => !string.IsNullOrWhiteSpace(component))
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .ToList();
                var facts = new List<ValidationHtmlMetaItem>
                {
                    new() { Label = "Affected Components", Value = FormatComponentSet(affectedComponents) },
                    new() { Label = "Claim Count", Value = group.Count().ToString() },
                    new() { Label = "Confidence", Value = representative.Confidence.ToString() },
                    new() { Label = "Requirement", Value = representative.RequirementLevel.ToString() }
                };
                var evidenceIds = group.SelectMany(item => item.EvidenceIds).Distinct(StringComparer.Ordinal).Take(6).ToArray();
                if (evidenceIds.Length > 0)
                    facts.Add(new ValidationHtmlMetaItem { Label = "Evidence IDs", Value = string.Join(", ", evidenceIds) });
                if (!string.IsNullOrWhiteSpace(representative.Remediation))
                    facts.Add(new ValidationHtmlMetaItem { Label = "Remediation", Value = representative.Remediation! });
                foreach (var pair in representative.Metadata.Take(3))
                    facts.Add(new ValidationHtmlMetaItem { Label = pair.Key, Value = pair.Value });
                var ruleId = representative.Metadata.GetValueOrDefault("ruleId") ?? representative.ClaimId;
                return new ValidationHtmlDecisionTraceItem
                {
                    Title = ruleId,
                    Summary = group.Count() == 1
                        ? representative.Summary
                        : $"{group.Count()} related claims. Representative issue: {representative.Summary}",
                    Category = representative.Dimension.ToString(),
                    ComponentLabel = FormatComponentSet(affectedComponents),
                    GateLabel = representative.Outcome.ToString(),
                    AuthorityLabel = representative.Authority.ToString(),
                    EvidenceLabel = evidenceIds.Length == 0 ? "No evidence reference recorded" : $"{evidenceIds.Length} evidence reference(s)",
                    RuleId = ruleId,
                    SpecReference = representative.Source.Uri,
                    Tone = MapClaimTone(representative.Outcome),
                    ImpactAreas = [representative.Dimension.ToString()],
                    Facts = facts
                };
            })
            .ToList();
    }

    private static IReadOnlyList<ValidationHtmlHotspot> BuildHotspots(ValidationResult result)
    {
        return result.Assurance.Claims
            .Where(claim => claim.Outcome is not (AssuranceClaimOutcome.Satisfied or AssuranceClaimOutcome.NotApplicable) &&
                !string.IsNullOrWhiteSpace(claim.Subject))
            .GroupBy(claim => claim.Subject, StringComparer.OrdinalIgnoreCase)
            .OrderByDescending(group => group.Count())
            .ThenByDescending(group => group.Max(item => item.Outcome))
            .Take(MaxHotspots)
            .Select(group =>
            {
                var representative = group.First();
                return new ValidationHtmlHotspot
                {
                    Component = group.Key,
                    Domain = representative.Dimension.ToString(),
                    SignalCount = group.Count(),
                    Summary = group.Count() == 1
                        ? representative.Summary
                        : $"{group.Count()} non-success claims. Representative issue: {representative.Summary}",
                    Tone = MapClaimTone(representative.Outcome)
                };
            })
            .ToList();
    }

    private static HtmlReportTone MapClaimTone(AssuranceClaimOutcome outcome) => outcome switch
    {
        AssuranceClaimOutcome.Violated or AssuranceClaimOutcome.Error => HtmlReportTone.Danger,
        AssuranceClaimOutcome.Incomplete or AssuranceClaimOutcome.ReviewRequired => HtmlReportTone.Warning,
        _ => HtmlReportTone.Info
    };

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
        if (result.Assurance.CertificateEligibility.Reasons.Count > 0)
        {
            return string.Join(" ", result.Assurance.CertificateEligibility.Reasons.Take(2));
        }

        return "All requirements for the selected assurance profile are satisfied.";
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
        if (result.Assurance.CertificateEligibility.Status == CertificateEligibilityStatus.Ineligible &&
            string.Equals(result.Assurance.Profile.ProfileId, AssuranceProfileCatalog.Inventory.ProfileId, StringComparison.OrdinalIgnoreCase))
        {
            return "Not issued by inventory profile";
        }

        return result.Assurance.CertificateEligibility.Status.ToString();
    }

    private static HtmlReportTone ResolveReleaseTone(ValidationResult result)
    {
        if (result.Assurance.CertificateEligibility.Status == CertificateEligibilityStatus.Ineligible &&
            string.Equals(result.Assurance.Profile.ProfileId, AssuranceProfileCatalog.Inventory.ProfileId, StringComparison.OrdinalIgnoreCase))
        {
            return HtmlReportTone.Warning;
        }

        return result.Assurance.CertificateEligibility.Status switch
        {
            CertificateEligibilityStatus.Eligible => HtmlReportTone.Success,
            CertificateEligibilityStatus.Ineligible => HtmlReportTone.Danger,
            _ => HtmlReportTone.Warning
        };
    }

    private static HtmlReportTone MapCoreTone(CoreConformanceStatus status) => status switch
    {
        CoreConformanceStatus.Conformant => HtmlReportTone.Success,
        CoreConformanceStatus.Nonconformant => HtmlReportTone.Danger,
        _ => HtmlReportTone.Warning
    };

    private static HtmlReportTone MapSecurityTone(SecurityAssuranceStatus status) => status switch
    {
        SecurityAssuranceStatus.Verified => HtmlReportTone.Success,
        SecurityAssuranceStatus.IssuesFound => HtmlReportTone.Danger,
        _ => HtmlReportTone.Warning
    };

    private static HtmlReportTone MapOperationsTone(OperationalReadinessStatus status) => status switch
    {
        OperationalReadinessStatus.Ready => HtmlReportTone.Success,
        OperationalReadinessStatus.NotReady => HtmlReportTone.Danger,
        _ => HtmlReportTone.Warning
    };

    private static string BuildSignalLabel(int blockingCount, int totalCount)
    {
        if (totalCount == 0)
        {
            return "No escalated signals";
        }

        return $"{blockingCount} blocking / {totalCount} total";
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
            return 0;
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
            return 0;
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
            return 0;
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