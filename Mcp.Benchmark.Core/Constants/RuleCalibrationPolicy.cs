namespace Mcp.Benchmark.Core.Constants;

public static class RuleCalibrationPolicy
{
    public const string Revision = "2026-11-10.1";
    public const int MinimumLabeledSamples = 100;
    public const int MaximumCorpusSamples = 100_000;
    public const int MaximumIdentifierCharacters = 512;
    public const int MinimumSourceGroups = 3;
    public const int MinimumScenarioFamilies = 2;
    public const double MaximumSourceGroupFraction = 0.5;
    public const double MinimumPredictionCoverage = 0.95;
    public const double MinimumPrecision = 0.95;
    public const double MinimumRecall = 0.90;
    public const double MaximumFalsePositiveRate = 0.05;
    public const double MaximumFalseNegativeRate = 0.10;
}