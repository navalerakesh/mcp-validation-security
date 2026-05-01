namespace Mcp.Benchmark.Core.Models;

public enum AiSafetyControlKind
{
    UserConfirmation = 0,
    AuditTrail = 1,
    DataSharingDisclosure = 2,
    DestructiveActionConfirmation = 3,
    HostServerResponsibilitySplit = 4
}

public enum AiSafetyControlStatus
{
    NotApplicable = 0,
    Declared = 1,
    Missing = 2,
    NotObservable = 3
}

public sealed class AiSafetyControlTarget
{
    public string Name { get; init; } = string.Empty;

    public string? Description { get; init; }

    public bool? ReadOnlyHint { get; init; }

    public bool? DestructiveHint { get; init; }

    public bool? OpenWorldHint { get; init; }

    public IReadOnlyList<string> ParameterNames { get; init; } = Array.Empty<string>();
}

public sealed class AiSafetyControlEvidence
{
    public string SubjectKind { get; init; } = "tool";

    public string SubjectName { get; init; } = string.Empty;

    public AiSafetyControlKind ControlKind { get; init; }

    public AiSafetyControlStatus Status { get; init; }

    public ValidationRuleSource Authority { get; init; } = ValidationRuleSource.Heuristic;

    public string Summary { get; init; } = string.Empty;

    public string? Recommendation { get; init; }

    public Dictionary<string, string> Metadata { get; init; } = new(StringComparer.OrdinalIgnoreCase);
}

public sealed class AiSafetyControlAnalysis
{
    public IReadOnlyList<AiSafetyControlEvidence> Evidence { get; init; } = Array.Empty<AiSafetyControlEvidence>();
}