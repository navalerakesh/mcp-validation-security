using Mcp.Benchmark.Core.Models;

namespace Mcp.Benchmark.Infrastructure.Services.Reporting;

internal sealed class ValidationHtmlReportDocument
{
    public required ValidationResult Result { get; init; }

    public required ReportingConfig ReportConfig { get; init; }

    public required string GeneratedAtLabel { get; init; }

    public required string DetailLabel { get; init; }

    public required bool Verbose { get; init; }

    public required string DecisionTitle { get; init; }

    public required string DecisionReason { get; init; }

    public required string ScopeLimit { get; init; }

    public required IReadOnlyList<ValidationHtmlMetaItem> DecisionFacts { get; init; }

    public required string RunReference { get; init; }

    public required IReadOnlyList<ValidationHtmlClaimEntry> ClaimEntries { get; init; }

    public required string TargetName { get; init; }

    public required IReadOnlyList<ValidationHtmlMetaItem> AgentFacts { get; init; }

    public required string CatalogSummary { get; init; }

    public required IReadOnlyList<ValidationHtmlPriorityAction> PriorityActions { get; init; }

    public required IReadOnlyList<ValidationHtmlMetaItem> ProvenanceFacts { get; init; }

    public required IReadOnlyList<string> GovernanceNotes { get; init; }

    public required IReadOnlyList<ValidationHtmlMetricCard> OverviewMetrics { get; init; }


    public required IReadOnlyList<ValidationHtmlDomainSummary> DomainSummaries { get; init; }

    public required IReadOnlyList<ValidationHtmlDecisionTraceItem> DecisionTrace { get; init; }

    public required IReadOnlyList<ValidationHtmlHotspot> Hotspots { get; init; }

    public required IReadOnlyList<ValidationHtmlCompatibilityTheme> CompatibilityThemes { get; init; }

    public required IReadOnlyList<string> ActionHints { get; init; }

    public required IReadOnlyList<RemediationOrderGroup> RemediationOrder { get; init; }

    public required IReadOnlyList<string> AdditionalRecommendations { get; init; }

    public ValidationHtmlBootstrapSummary? Bootstrap { get; init; }
}

internal sealed record ValidationHtmlClaimEntry(AssuranceClaim Claim, string Anchor);

internal sealed record ValidationHtmlPriorityAction(
    int Priority, string Category, string Impact, RemediationOrderItem Item,
    string Anchor, string Kind, IReadOnlyList<ValidationHtmlClaimEntry> Claims);

internal sealed class ValidationHtmlMetricCard
{
    public required string Eyebrow { get; init; }

    public required string Value { get; init; }

    public required string Label { get; init; }

    public string? SupportingText { get; init; }

    public required HtmlReportTone Tone { get; init; }
}

internal sealed class ValidationHtmlDomainSummary
{
    public string AnchorId { get; init; } = "technical-evidence";

    public required string Domain { get; init; }

    public required string StatusLabel { get; init; }

    public required string SignalLabel { get; init; }

    public required string EvidenceLabel { get; init; }

    public required string Summary { get; init; }

    public required string ActionLabel { get; init; }

    public required HtmlReportTone Tone { get; init; }
}

internal sealed class ValidationHtmlDecisionTraceItem
{
    public required string Title { get; init; }

    public required string Summary { get; init; }

    public required string Category { get; init; }

    public required string ComponentLabel { get; init; }

    public required string GateLabel { get; init; }

    public required string AuthorityLabel { get; init; }

    public required string EvidenceLabel { get; init; }

    public string? RuleId { get; init; }

    public string? SpecReference { get; init; }

    public required HtmlReportTone Tone { get; init; }

    public IReadOnlyList<string> ImpactAreas { get; init; } = Array.Empty<string>();

    public IReadOnlyList<ValidationHtmlMetaItem> Facts { get; init; } = Array.Empty<ValidationHtmlMetaItem>();
}

internal sealed class ValidationHtmlHotspot
{
    public required string Component { get; init; }

    public required string Domain { get; init; }

    public required int SignalCount { get; init; }

    public required string Summary { get; init; }

    public required HtmlReportTone Tone { get; init; }
}

internal sealed class ValidationHtmlCompatibilityTheme
{
    public required string Title { get; init; }

    public required string Summary { get; init; }

    public required int ProfileCount { get; init; }

    public IReadOnlyList<string> Profiles { get; init; } = Array.Empty<string>();

    public required HtmlReportTone Tone { get; init; }
}

internal sealed class ValidationHtmlBootstrapSummary
{
    public required string Eyebrow { get; init; }

    public required string Title { get; init; }

    public required string Summary { get; init; }

    public required string BadgeLabel { get; init; }

    public required HtmlReportTone Tone { get; init; }

    public required IReadOnlyList<ValidationHtmlMetaItem> MetaItems { get; init; }

    public string? Note { get; init; }
}

internal sealed class ValidationHtmlMetaItem
{
    public required string Label { get; init; }

    public required string Value { get; init; }
}

internal enum HtmlReportTone
{
    Neutral,
    Info,
    Success,
    Warning,
    Danger
}
