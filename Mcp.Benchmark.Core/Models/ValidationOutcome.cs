namespace Mcp.Benchmark.Core.Models;

public enum ValidationOutcome
{
    NotEvaluated = 0,
    Running = 1,
    Succeeded = 2,
    Failed = 3,
    Skipped = 4,
    AuthRequired = 5,
    Inconclusive = 6,
    NotApplicable = 7,
    Unavailable = 8,
    Blocked = 9,
    Cancelled = 10,
    Error = 11
}

public static class ValidationOutcomeTaxonomy
{
    public const string Version = "1.0.0";

    public static ValidationOutcome From(TestStatus status) => status switch
    {
        TestStatus.NotRun => ValidationOutcome.NotEvaluated,
        TestStatus.InProgress => ValidationOutcome.Running,
        TestStatus.Passed => ValidationOutcome.Succeeded,
        TestStatus.Failed => ValidationOutcome.Failed,
        TestStatus.Skipped => ValidationOutcome.Skipped,
        TestStatus.AuthRequired => ValidationOutcome.AuthRequired,
        TestStatus.Inconclusive => ValidationOutcome.Inconclusive,
        TestStatus.Error => ValidationOutcome.Error,
        TestStatus.Cancelled => ValidationOutcome.Cancelled,
        _ => throw new ArgumentOutOfRangeException(nameof(status), status, "Unknown test status.")
    };

    public static ValidationOutcome From(ValidationStatus status) => status switch
    {
        ValidationStatus.InProgress => ValidationOutcome.Running,
        ValidationStatus.Passed => ValidationOutcome.Succeeded,
        ValidationStatus.Failed => ValidationOutcome.Failed,
        ValidationStatus.PartiallyCompleted => ValidationOutcome.Inconclusive,
        ValidationStatus.Cancelled => ValidationOutcome.Cancelled,
        ValidationStatus.Error => ValidationOutcome.Error,
        _ => throw new ArgumentOutOfRangeException(nameof(status), status, "Unknown validation status.")
    };

    public static ValidationOutcome From(ValidationCoverageStatus status) => status switch
    {
        ValidationCoverageStatus.Covered => ValidationOutcome.Succeeded,
        ValidationCoverageStatus.Skipped => ValidationOutcome.Skipped,
        ValidationCoverageStatus.AuthRequired => ValidationOutcome.AuthRequired,
        ValidationCoverageStatus.Inconclusive => ValidationOutcome.Inconclusive,
        ValidationCoverageStatus.NotApplicable => ValidationOutcome.NotApplicable,
        ValidationCoverageStatus.Unavailable => ValidationOutcome.Unavailable,
        ValidationCoverageStatus.Blocked => ValidationOutcome.Blocked,
        _ => throw new ArgumentOutOfRangeException(nameof(status), status, "Unknown coverage status.")
    };

    public static ValidationOutcome From(ProbeResponseClassification classification) => classification switch
    {
        ProbeResponseClassification.Unknown => ValidationOutcome.NotEvaluated,
        ProbeResponseClassification.Success => ValidationOutcome.Succeeded,
        ProbeResponseClassification.ProtocolError => ValidationOutcome.Failed,
        ProbeResponseClassification.AuthenticationChallenge => ValidationOutcome.AuthRequired,
        ProbeResponseClassification.AuthorizationFailure => ValidationOutcome.Failed,
        ProbeResponseClassification.TransientFailure => ValidationOutcome.Inconclusive,
        ProbeResponseClassification.TransportFailure => ValidationOutcome.Unavailable,
        ProbeResponseClassification.ParserBoundary => ValidationOutcome.Inconclusive,
        ProbeResponseClassification.Timeout => ValidationOutcome.Unavailable,
        ProbeResponseClassification.NoResponse => ValidationOutcome.Unavailable,
        _ => throw new ArgumentOutOfRangeException(nameof(classification), classification, "Unknown probe classification.")
    };

    public static bool IsTerminal(ValidationOutcome outcome) => outcome is not (ValidationOutcome.NotEvaluated or ValidationOutcome.Running);

    public static bool IsDeterministicFailure(ValidationOutcome outcome) => outcome is ValidationOutcome.Failed or ValidationOutcome.Error;
}