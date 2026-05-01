using Mcp.Benchmark.Core.Models;

namespace Mcp.Benchmark.Core.Services;

public static class ReportSeverityNormalizer
{
    public static ReportSeverity From(ValidationFindingSeverity severity)
    {
        return severity switch
        {
            ValidationFindingSeverity.Critical => ReportSeverity.Critical,
            ValidationFindingSeverity.High => ReportSeverity.High,
            ValidationFindingSeverity.Medium => ReportSeverity.Medium,
            ValidationFindingSeverity.Low => ReportSeverity.Low,
            _ => ReportSeverity.Info
        };
    }

    public static ReportSeverity From(ViolationSeverity severity)
    {
        return severity switch
        {
            ViolationSeverity.Critical => ReportSeverity.Critical,
            ViolationSeverity.High => ReportSeverity.High,
            ViolationSeverity.Medium => ReportSeverity.Medium,
            _ => ReportSeverity.Low
        };
    }

    public static ReportSeverity From(VulnerabilitySeverity severity)
    {
        return severity switch
        {
            VulnerabilitySeverity.Critical => ReportSeverity.Critical,
            VulnerabilitySeverity.High => ReportSeverity.High,
            VulnerabilitySeverity.Medium => ReportSeverity.Medium,
            VulnerabilitySeverity.Low => ReportSeverity.Low,
            _ => ReportSeverity.Info
        };
    }

    public static ReportSeverity From(ContentRiskLevel riskLevel)
    {
        return riskLevel switch
        {
            ContentRiskLevel.High => ReportSeverity.High,
            ContentRiskLevel.Medium => ReportSeverity.Medium,
            ContentRiskLevel.Low => ReportSeverity.Low,
            _ => ReportSeverity.Info
        };
    }

    public static ReportSeverity From(DecisionRecord decision)
    {
        return From(decision.Severity);
    }

    public static ReportPriority PriorityFrom(DecisionRecord decision)
    {
        var severity = From(decision.Severity);
        return decision.Gate switch
        {
            GateOutcome.Reject => severity >= ReportSeverity.High ? ReportPriority.Critical : ReportPriority.High,
            GateOutcome.ReviewRequired => severity >= ReportSeverity.High ? ReportPriority.High : ReportPriority.Medium,
            GateOutcome.CoverageDebt => ReportPriority.Medium,
            GateOutcome.Note => severity switch
            {
                ReportSeverity.Critical or ReportSeverity.High => ReportPriority.Medium,
                ReportSeverity.Medium or ReportSeverity.Low => ReportPriority.Low,
                _ => ReportPriority.Info
            },
            _ => ReportPriority.Info
        };
    }

    public static string ToDisplayLabel(ReportSeverity severity)
    {
        return severity.ToString();
    }

    public static string ToDisplayLabel(ReportPriority priority)
    {
        return priority.ToString();
    }

    public static string ToMachineLabel(ReportSeverity severity)
    {
        return severity.ToString().ToLowerInvariant();
    }

    public static string ToMachineLabel(ReportPriority priority)
    {
        return priority.ToString().ToLowerInvariant();
    }

    public static string ToSarifLevel(ReportSeverity severity)
    {
        return severity switch
        {
            ReportSeverity.Critical or ReportSeverity.High => "error",
            ReportSeverity.Medium => "warning",
            _ => "note"
        };
    }
}
