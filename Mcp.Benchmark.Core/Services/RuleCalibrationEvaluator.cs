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
            Precision = Math.Round(precision, 4),
            Recall = Math.Round(recall, 4),
            FalsePositiveRate = Math.Round(falsePositiveRate, 4),
            FalseNegativeRate = Math.Round(falseNegativeRate, 4),
            ProtocolRevisions = labeled.Select(sample => sample.ProtocolRevision).Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToArray(),
            Transports = labeled.Select(sample => sample.Transport).Order(StringComparer.Ordinal).Distinct(StringComparer.OrdinalIgnoreCase).Order(StringComparer.OrdinalIgnoreCase).ToArray()
        };

        var reasons = new List<string>();
        if (truePositive + falseNegative == 0)
            reasons.Add("Calibration requires expected-positive samples.");
        if (trueNegative + falsePositive == 0)
            reasons.Add("Calibration requires expected-negative samples.");
        if (labeled.Any(sample => string.IsNullOrWhiteSpace(sample.SampleId)))
            reasons.Add("Calibration requires non-empty sample identities.");
        if (labeled.Select(sample => sample.SampleId).Distinct(StringComparer.Ordinal).Count() != labeled.Length)
            reasons.Add("Calibration requires unique sample identities within each rule.");
        if (metrics.LabeledSampleCount < RuleCalibrationPolicy.MinimumLabeledSamples)
            reasons.Add(FormattableString.Invariant($"Labeled sample count {metrics.LabeledSampleCount} is below {RuleCalibrationPolicy.MinimumLabeledSamples}."));
        if (truePositive + falsePositive > 0 && precision < RuleCalibrationPolicy.MinimumPrecision)
            reasons.Add(FormattableString.Invariant($"Precision {truePositive}/{truePositive + falsePositive} is below {RuleCalibrationPolicy.MinimumPrecision:P1}."));
        if (truePositive + falseNegative > 0 && recall < RuleCalibrationPolicy.MinimumRecall)
            reasons.Add(FormattableString.Invariant($"Recall {truePositive}/{truePositive + falseNegative} is below {RuleCalibrationPolicy.MinimumRecall:P1}."));
        if (falsePositiveRate > RuleCalibrationPolicy.MaximumFalsePositiveRate)
            reasons.Add(FormattableString.Invariant($"False-positive rate {falsePositive}/{falsePositive + trueNegative} exceeds {RuleCalibrationPolicy.MaximumFalsePositiveRate:P1}."));
        if (falseNegativeRate > RuleCalibrationPolicy.MaximumFalseNegativeRate)
            reasons.Add(FormattableString.Invariant($"False-negative rate {falseNegative}/{truePositive + falseNegative} exceeds {RuleCalibrationPolicy.MaximumFalseNegativeRate:P1}."));

        return new RuleCalibrationDecision
        {
            Metrics = metrics,
            DefaultBlockingEligible = reasons.Count == 0,
            Reasons = reasons
        };
    }

    private static double Ratio(int numerator, int denominator) =>
        denominator == 0 ? 0 : numerator / (double)denominator;
}