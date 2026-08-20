using Mcp.Benchmark.Core.Models;

namespace Mcp.Benchmark.Core.Services;

public static class ValidationCoverageFactory
{
    public static ValidationCoverageDeclaration FromTestStatus(
        string layerId,
        string scope,
        TestStatus status,
        string? reason = null)
    {
        return FromOutcome(layerId, scope, ValidationOutcomeTaxonomy.From(status), reason);
    }

    public static ValidationCoverageDeclaration FromOutcome(
        string layerId,
        string scope,
        ValidationOutcome outcome,
        string? reason = null)
    {
        var status = outcome switch
        {
            ValidationOutcome.Succeeded or ValidationOutcome.Failed or ValidationOutcome.Error => ValidationCoverageStatus.Covered,
            ValidationOutcome.Skipped => ValidationCoverageStatus.Skipped,
            ValidationOutcome.AuthRequired => ValidationCoverageStatus.AuthRequired,
            ValidationOutcome.Inconclusive => ValidationCoverageStatus.Inconclusive,
            ValidationOutcome.NotApplicable => ValidationCoverageStatus.NotApplicable,
            ValidationOutcome.Unavailable => ValidationCoverageStatus.Unavailable,
            ValidationOutcome.Blocked or ValidationOutcome.Cancelled => ValidationCoverageStatus.Blocked,
            ValidationOutcome.NotEvaluated or ValidationOutcome.Running => ValidationCoverageStatus.Unavailable,
            _ => throw new ArgumentOutOfRangeException(nameof(outcome), outcome, "Unknown canonical validation outcome.")
        };

        return new ValidationCoverageDeclaration
        {
            LayerId = layerId,
            Scope = scope,
            Status = status,
            ObservedOutcome = outcome is ValidationOutcome.NotEvaluated or ValidationOutcome.Running
                ? ValidationOutcome.Unavailable
                : outcome,
            Blocker = status switch
            {
                ValidationCoverageStatus.AuthRequired => ValidationEvidenceBlocker.AuthRequired,
                ValidationCoverageStatus.Inconclusive => ValidationEvidenceBlocker.None,
                ValidationCoverageStatus.Skipped => ValidationEvidenceBlocker.ConfigDisabled,
                ValidationCoverageStatus.Unavailable => ValidationEvidenceBlocker.Unimplemented,
                ValidationCoverageStatus.Blocked => ValidationEvidenceBlocker.TransportError,
                _ => ValidationEvidenceBlocker.None
            },
            Confidence = status switch
            {
                ValidationCoverageStatus.Covered => EvidenceConfidenceLevel.High,
                ValidationCoverageStatus.AuthRequired or ValidationCoverageStatus.Inconclusive or ValidationCoverageStatus.Skipped => EvidenceConfidenceLevel.Low,
                _ => EvidenceConfidenceLevel.None
            },
            Reason = status == ValidationCoverageStatus.Covered ? null : reason
        };
    }
}
