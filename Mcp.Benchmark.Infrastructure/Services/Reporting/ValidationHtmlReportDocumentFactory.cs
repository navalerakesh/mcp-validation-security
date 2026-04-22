using Mcp.Benchmark.Core.Models;
using Mcp.Benchmark.Core.Services;

namespace Mcp.Benchmark.Infrastructure.Services.Reporting;

internal sealed class ValidationHtmlReportDocumentFactory
{
    public ValidationHtmlReportDocument Create(ValidationResult result, ReportingConfig reportConfig, bool verbose)
    {
        ArgumentNullException.ThrowIfNull(result);
        ArgumentNullException.ThrowIfNull(reportConfig);

        var bootstrap = ResolveBootstrapHealth(result);
        var detailLabel = verbose ? "Full" : "Minimal";
        var actionHints = ReportActionHintBuilder.Build(result);
        var priorityFindings = CollectPriorityFindings(result);
        var recommendations = result.Recommendations
            .Where(recommendation => !string.IsNullOrWhiteSpace(recommendation))
            .Except(actionHints, StringComparer.Ordinal)
            .ToList();

        return new ValidationHtmlReportDocument
        {
            Result = result,
            ReportConfig = reportConfig,
            GeneratedAtLabel = $"Generated on {DateTime.UtcNow:yyyy-MM-dd HH:mm:ss UTC}",
            DetailLabel = detailLabel,
            Verbose = verbose,
            Hero = BuildHero(result, reportConfig),
            DecisionPanel = BuildDecisionPanel(result, priorityFindings),
            OverviewMetrics = BuildOverviewMetrics(result),
            RiskMetrics = BuildRiskMetrics(result),
            PriorityFindings = priorityFindings,
            ActionHints = actionHints,
            AdditionalRecommendations = recommendations,
            Bootstrap = BuildBootstrapSummary(result, bootstrap)
        };
    }

    private static ValidationHtmlHero BuildHero(ValidationResult result, ReportingConfig reportConfig)
    {
        var statusTone = result.OverallStatus switch
        {
            ValidationStatus.Passed => HtmlReportTone.Success,
            ValidationStatus.Failed => HtmlReportTone.Danger,
            _ => HtmlReportTone.Warning
        };

        var title = result.OverallStatus switch
        {
            ValidationStatus.Passed => "Validation Ready",
            ValidationStatus.Failed => "Validation Review Required",
            _ => "Validation Completed with Warnings"
        };

        var subtitle = BuildHeroSubtitle(result);
        var trustLabel = result.TrustAssessment?.TrustLabel ?? "Trust model unavailable";
        var trustTone = MapTrustTone(result.TrustAssessment?.TrustLevel);
        var protocolVersion = result.ProtocolVersion
            ?? result.InitializationHandshake?.Payload?.ProtocolVersion
            ?? result.ServerConfig.ProtocolVersion
            ?? "n/a";
        var duration = result.Duration?.TotalSeconds > 0
            ? $"{result.Duration.Value.TotalSeconds:F1}s"
            : "n/a";

        return new ValidationHtmlHero
        {
            Eyebrow = "MCP Validation Report",
            Title = title,
            Subtitle = subtitle,
            StatusLabel = result.OverallStatus.ToString(),
            StatusTone = statusTone,
            TrustLabel = trustLabel,
            TrustTone = trustTone,
            MetaItems = new List<ValidationHtmlMetaItem>
            {
                new() { Label = "Endpoint", Value = result.ServerConfig.Endpoint ?? "n/a" },
                new() { Label = "Validation ID", Value = result.ValidationId },
                new() { Label = "Duration", Value = duration },
                new() { Label = "Spec Profile", Value = string.IsNullOrWhiteSpace(reportConfig.SpecProfile) ? "latest" : reportConfig.SpecProfile },
                new() { Label = "Server Profile", Value = $"{result.ServerProfile} ({result.ServerProfileSource})" },
                new() { Label = "Protocol Version", Value = protocolVersion }
            }
        };
    }

    private static ValidationHtmlDecisionPanel BuildDecisionPanel(ValidationResult result, IReadOnlyList<string> priorityFindings)
    {
        if (result.PolicyOutcome is { Passed: false } policyOutcome)
        {
            return new ValidationHtmlDecisionPanel
            {
                Eyebrow = "Immediate Decision",
                Title = "Release decision requires review",
                Summary = policyOutcome.Summary,
                Tone = HtmlReportTone.Danger,
                Highlights = policyOutcome.Reasons.Take(3).ToList()
            };
        }

        if (result.OverallStatus == ValidationStatus.Failed)
        {
            return new ValidationHtmlDecisionPanel
            {
                Eyebrow = "Immediate Decision",
                Title = "Blocking signals require review",
                Summary = "Start with the highlighted protocol, security, or compatibility issues before wider rollout.",
                Tone = HtmlReportTone.Warning,
                Highlights = priorityFindings.Take(3).ToList()
            };
        }

        return new ValidationHtmlDecisionPanel
        {
            Eyebrow = "Immediate Decision",
            Title = "Proceed with focused follow-up",
            Summary = "Core requirements passed. Use the follow-up items to close remaining advisory gaps.",
            Tone = HtmlReportTone.Info,
            Highlights = priorityFindings.Take(3).ToList()
        };
    }

    private static IReadOnlyList<ValidationHtmlMetricCard> BuildOverviewMetrics(ValidationResult result)
    {
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
                Value = result.TrustAssessment?.TrustLabel.Split(':', 2)[0] ?? "n/a",
                Label = "Trust Level",
                SupportingText = result.TrustAssessment?.TrustLabel ?? "Trust assessment unavailable",
                Tone = MapTrustTone(result.TrustAssessment?.TrustLevel)
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

        return cards;
    }

    private static IReadOnlyList<ValidationHtmlMetricCard> BuildRiskMetrics(ValidationResult result)
    {
        if (result.TrustAssessment != null)
        {
            return new List<ValidationHtmlMetricCard>
            {
                CreateRiskCard("Protocol", result.TrustAssessment.ProtocolCompliance, "Spec adherence and response structure"),
                CreateRiskCard("Security", result.TrustAssessment.SecurityPosture, "Auth boundaries and exploit resistance"),
                CreateRiskCard("AI Safety", result.TrustAssessment.AiSafety, "Schema quality and agent safety posture"),
                CreateRiskCard("Operations", result.TrustAssessment.OperationalReadiness, "Latency, throughput, and stability")
            };
        }

        return new List<ValidationHtmlMetricCard>
        {
            CreateRiskCard("Protocol", result.ProtocolCompliance?.ComplianceScore ?? 0, "Spec adherence and response structure"),
            CreateRiskCard("Security", result.SecurityTesting?.SecurityScore ?? 0, "Auth boundaries and exploit resistance"),
            CreateRiskCard("Tools", result.ToolValidation?.Score ?? 0, "Contract quality and execution evidence"),
            CreateRiskCard("Performance", result.PerformanceTesting?.Score ?? 0, "Latency, throughput, and stability")
        };
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

    private static ValidationHtmlBootstrapSummary? BuildBootstrapSummary(ValidationResult result, HealthCheckResult? bootstrapHealth)
    {
        if (bootstrapHealth == null)
        {
            return null;
        }

        var handshakeStatus = bootstrapHealth.InitializationDetails?.Transport.StatusCode is int statusCode
            ? $"HTTP {statusCode}"
            : "n/a";
        var handshakeLatency = bootstrapHealth.ResponseTimeMs > 0
            ? $"{bootstrapHealth.ResponseTimeMs:F1} ms"
            : "n/a";
        var protocolVersion = result.ProtocolVersion
            ?? bootstrapHealth.ProtocolVersion
            ?? result.ServerConfig.ProtocolVersion
            ?? "n/a";
        var serverVersion = !string.IsNullOrWhiteSpace(bootstrapHealth.ServerVersion) &&
                            !string.Equals(bootstrapHealth.ServerVersion, "Unknown", StringComparison.OrdinalIgnoreCase)
            ? bootstrapHealth.ServerVersion
            : "n/a";
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
            var modeLabel = string.IsNullOrWhiteSpace(policyOutcome.Mode)
                ? "Validation"
                : $"{char.ToUpperInvariant(policyOutcome.Mode[0])}{policyOutcome.Mode[1..]}";
            return $"{modeLabel} policy blocked release readiness on {policyOutcome.Reasons.Count} unsuppressed signal(s).";
        }

        if (result.CriticalErrors.Count > 0)
        {
            return $"Review {result.CriticalErrors.Count} critical signal(s) before adoption.";
        }

        if (result.TrustAssessment != null)
        {
            return "Protocol, security, AI safety, and operational evidence are summarized below.";
        }

        return "Review the summarized validation evidence before adoption.";
    }

    private static IReadOnlyList<string> CollectPriorityFindings(ValidationResult result)
    {
        var findings = new List<string>();

        if (result.PolicyOutcome is { Passed: false } policyOutcome)
        {
            findings.Add($"Policy {policyOutcome.Mode} blocked the run: {policyOutcome.Summary}");
            findings.AddRange(policyOutcome.Reasons.Take(2));
        }

        if (result.ClientCompatibility?.Assessments.Count > 0)
        {
            findings.AddRange(result.ClientCompatibility.Assessments
                .Where(assessment => assessment.Status != ClientProfileCompatibilityStatus.Compatible)
                .Take(2)
                .Select(assessment => $"Client profile {assessment.DisplayName}: {assessment.StatusLabel}. {assessment.Summary}"));
        }

        if (result.CriticalErrors.Count > 0)
        {
            findings.AddRange(result.CriticalErrors.Take(2));
        }

        if (result.SecurityTesting?.Vulnerabilities.Count > 0)
        {
            findings.AddRange(result.SecurityTesting.Vulnerabilities
                .OrderByDescending(vulnerability => vulnerability.Severity)
                .Take(2)
                .Select(vulnerability => $"{vulnerability.Id}: {vulnerability.Description}"));
        }

        if (result.ProtocolCompliance?.Violations.Count > 0)
        {
            findings.AddRange(result.ProtocolCompliance.Violations
                .OrderByDescending(violation => violation.Severity)
                .Take(2)
                .Select(violation => $"{violation.CheckId}: {violation.Description}"));
        }

        return findings
            .Where(finding => !string.IsNullOrWhiteSpace(finding))
            .Distinct(StringComparer.Ordinal)
            .Take(5)
            .ToList();
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
