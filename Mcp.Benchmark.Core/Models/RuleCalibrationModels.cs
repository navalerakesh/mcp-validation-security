namespace Mcp.Benchmark.Core.Models;

public sealed class RuleCalibrationSample
{
    public required string SampleId { get; init; }

    public required string RuleId { get; init; }

    public required bool ExpectedPositive { get; init; }

    public required bool ObservedPositive { get; init; }

    public required string ProtocolRevision { get; init; }

    public required string Transport { get; init; }

    public string? Notes { get; init; }
}

public sealed class RuleCalibrationMetrics
{
    public required string RuleId { get; init; }

    public int LabeledSampleCount { get; init; }

    public int TruePositive { get; init; }

    public int FalsePositive { get; init; }

    public int TrueNegative { get; init; }

    public int FalseNegative { get; init; }

    public double Precision { get; init; }

    public double Recall { get; init; }

    public double FalsePositiveRate { get; init; }

    public double FalseNegativeRate { get; init; }

    public IReadOnlyList<string> ProtocolRevisions { get; init; } = Array.Empty<string>();

    public IReadOnlyList<string> Transports { get; init; } = Array.Empty<string>();
}

public sealed class RuleCalibrationDecision
{
    public required RuleCalibrationMetrics Metrics { get; init; }

    public bool DefaultBlockingEligible { get; init; }

    public IReadOnlyList<string> Reasons { get; init; } = Array.Empty<string>();
}