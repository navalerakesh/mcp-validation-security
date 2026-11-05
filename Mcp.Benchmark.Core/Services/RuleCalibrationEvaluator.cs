using Mcp.Benchmark.Core.Constants;
using Mcp.Benchmark.Core.Models;

namespace Mcp.Benchmark.Core.Services;

public static class RuleCalibrationEvaluator
{
    public static RuleCalibrationDecision Evaluate(string ruleId, IEnumerable<RuleCalibrationSample> samples)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(ruleId);
        ArgumentNullException.ThrowIfNull(samples);
        var labeled = samples
            .Where(sample => string.Equals(sample.RuleId, ruleId, StringComparison.Ordinal))
            .ToArray();
        var truePositive = labeled.Count(sample => sample.ExpectedPositive && sample.ObservedPositive);
        var falsePositive = labeled.Count(sample => !sample.ExpectedPositive && sample.ObservedPositive);
        var trueNegative = labeled.Count(sample => !sample.ExpectedPositive && !sample.ObservedPositive);
        var falseNegative = labeled.Count(sample => sample.ExpectedPositive && !sample.ObservedPositive);
        var precision = Ratio(truePositive, truePositive + falsePositive);
        var recall = Ratio(truePositive, truePositive + falseNegative);
        var falsePositiveRate = Ratio(falsePositive, falsePositive + trueNegative);
        var falseNegativeRate = Ratio(falseNegative, truePositive + falseNegative);
        var metrics = new RuleCalibrationMetrics
        {
            RuleId = ruleId,
            LabeledSampleCount = labeled.Length,
            TruePositive = truePositive,
            FalsePositive = falsePositive,
            TrueNegative = trueNegative,
            FalseNegative = falseNegative,
            Precision = precision,
            Recall = recall,
            FalsePositiveRate = falsePositiveRate,
            FalseNegativeRate = falseNegativeRate,
            ProtocolRevisions = labeled.Select(sample => sample.ProtocolRevision).Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToArray(),
            Transports = labeled.Select(sample => sample.Transport).Distinct(StringComparer.OrdinalIgnoreCase).Order(StringComparer.OrdinalIgnoreCase).ToArray()
        };

        var reasons = new List<string>();
        if (metrics.LabeledSampleCount < RuleCalibrationPolicy.MinimumLabeledSamples)
            reasons.Add($"Labeled sample count {metrics.LabeledSampleCount} is below {RuleCalibrationPolicy.MinimumLabeledSamples}.");
        if (metrics.Precision < RuleCalibrationPolicy.MinimumPrecision)
            reasons.Add($"Precision {metrics.Precision:P1} is below {RuleCalibrationPolicy.MinimumPrecision:P1}.");
        if (metrics.Recall < RuleCalibrationPolicy.MinimumRecall)
            reasons.Add($"Recall {metrics.Recall:P1} is below {RuleCalibrationPolicy.MinimumRecall:P1}.");
        if (metrics.FalsePositiveRate > RuleCalibrationPolicy.MaximumFalsePositiveRate)
            reasons.Add($"False-positive rate {metrics.FalsePositiveRate:P1} exceeds {RuleCalibrationPolicy.MaximumFalsePositiveRate:P1}.");
        if (metrics.FalseNegativeRate > RuleCalibrationPolicy.MaximumFalseNegativeRate)
            reasons.Add($"False-negative rate {metrics.FalseNegativeRate:P1} exceeds {RuleCalibrationPolicy.MaximumFalseNegativeRate:P1}.");

        return new RuleCalibrationDecision
        {
            Metrics = metrics,
            DefaultBlockingEligible = reasons.Count == 0,
            Reasons = reasons
        };
    }

    private static double Ratio(int numerator, int denominator) =>
        denominator == 0 ? 0 : Math.Round(numerator / (double)denominator, 4);
}