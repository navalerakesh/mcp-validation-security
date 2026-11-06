namespace Mcp.Benchmark.Core.Constants;

public static class RuleCalibrationPolicy
{
    public const string Revision = "2026-11-05.1";
    public const int MinimumLabeledSamples = 100;
    public const double MinimumPrecision = 0.95;
    public const double MinimumRecall = 0.90;
    public const double MaximumFalsePositiveRate = 0.05;
    public const double MaximumFalseNegativeRate = 0.10;
}