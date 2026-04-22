using Mcp.Benchmark.Core.Models;

namespace Mcp.Benchmark.Infrastructure.Services.Reporting;

internal sealed class ValidationHtmlReportDocument
{
    public required ValidationResult Result { get; init; }

    public required ReportingConfig ReportConfig { get; init; }

    public required string GeneratedAtLabel { get; init; }

    public required string DetailLabel { get; init; }

    public required bool Verbose { get; init; }

    public required ValidationHtmlHero Hero { get; init; }

    public required ValidationHtmlDecisionPanel DecisionPanel { get; init; }

    public required IReadOnlyList<ValidationHtmlMetricCard> OverviewMetrics { get; init; }

    public required IReadOnlyList<ValidationHtmlMetricCard> RiskMetrics { get; init; }

    public required IReadOnlyList<string> PriorityFindings { get; init; }

    public required IReadOnlyList<string> ActionHints { get; init; }

    public required IReadOnlyList<string> AdditionalRecommendations { get; init; }

    public ValidationHtmlBootstrapSummary? Bootstrap { get; init; }
}

internal sealed class ValidationHtmlHero
{
    public required string Eyebrow { get; init; }

    public required string Title { get; init; }

    public required string Subtitle { get; init; }

    public required string StatusLabel { get; init; }

    public required HtmlReportTone StatusTone { get; init; }

    public required string TrustLabel { get; init; }

    public required HtmlReportTone TrustTone { get; init; }

    public required IReadOnlyList<ValidationHtmlMetaItem> MetaItems { get; init; }
}

internal sealed class ValidationHtmlDecisionPanel
{
    public required string Eyebrow { get; init; }

    public required string Title { get; init; }

    public required string Summary { get; init; }

    public required HtmlReportTone Tone { get; init; }

    public required IReadOnlyList<string> Highlights { get; init; }
}

internal sealed class ValidationHtmlMetricCard
{
    public required string Eyebrow { get; init; }

    public required string Value { get; init; }

    public required string Label { get; init; }

    public string? SupportingText { get; init; }

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
