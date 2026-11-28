using Mcp.Benchmark.Core.Constants;
using Mcp.Benchmark.Core.Models;

namespace Mcp.Benchmark.Core.Services;

public static class RuleCalibrationEvaluator
{
    public static RuleCalibrationDecision Evaluate(string ruleId, IEnumerable<RuleCalibrationSample> samples)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(ruleId);
        if (ruleId.Length > RuleCalibrationPolicy.MaximumIdentifierCharacters)
            throw new ArgumentException("Calibration rule identity exceeds the admission limit.", nameof(ruleId));
        ArgumentNullException.ThrowIfNull(samples);
        var corpus = samples.Take(RuleCalibrationPolicy.MaximumCorpusSamples + 1).ToArray();
        if (corpus.Length > RuleCalibrationPolicy.MaximumCorpusSamples)
            throw new ArgumentException("Calibration corpus exceeds the sample admission limit.", nameof(samples));
        if (corpus.Any(sample => sample == null))
            throw new ArgumentException("Calibration corpus contains a null sample.", nameof(samples));
        if (corpus.Any(sample => new[] { sample.SampleId, sample.RuleId, sample.SourceGroupId, sample.ScenarioFamily, sample.ProtocolRevision, sample.Transport }
            .Any(value => value?.Length > RuleCalibrationPolicy.MaximumIdentifierCharacters)))
            throw new ArgumentException("Calibration provenance exceeds the identifier admission limit.", nameof(samples));
        var matching = corpus
            .Where(sample => string.Equals(sample.RuleId, ruleId, StringComparison.Ordinal))
            .ToArray();
        var labeled = matching.Where(sample => sample.Partition == RuleCalibrationPartition.Holdout).ToArray();
        var metrics = CalculateMetrics(ruleId, labeled);
        var truePositive = metrics.TruePositive;
        var falsePositive = metrics.FalsePositive;
        var trueNegative = metrics.TrueNegative;
        var falseNegative = metrics.FalseNegative;
        var precision = Ratio(truePositive, truePositive + falsePositive);
        var recall = Ratio(truePositive, metrics.ExpectedPositiveCount);
        var falsePositiveRate = Ratio(falsePositive, falsePositive + trueNegative);
        var falseNegativeRate = Ratio(falseNegative, truePositive + falseNegative);
        var reasons = new List<string>();
        if (labeled.Length == 0 || corpus.Any(sample => sample.Partition == RuleCalibrationPartition.Unspecified || !Enum.IsDefined(sample.Partition)))
            reasons.Add("Default-blocking calibration requires explicit tuning/holdout partitions and held-out samples.");
        if (corpus.Any(sample => string.IsNullOrWhiteSpace(sample.SourceGroupId) || string.IsNullOrWhiteSpace(sample.ScenarioFamily) ||
            string.IsNullOrWhiteSpace(sample.ProtocolRevision) || string.IsNullOrWhiteSpace(sample.Transport)))
            reasons.Add("Calibration requires source-group, scenario-family, protocol and transport provenance.");
        var sourceGroups = GroupBySource(labeled);
        if (sourceGroups.Length < RuleCalibrationPolicy.MinimumSourceGroups)
            reasons.Add(FormattableString.Invariant($"Holdout source-group count {sourceGroups.Length} is below {RuleCalibrationPolicy.MinimumSourceGroups}."));
        if (metrics.ScenarioFamilyCount < RuleCalibrationPolicy.MinimumScenarioFamilies)
            reasons.Add(FormattableString.Invariant($"Holdout scenario-family count {metrics.ScenarioFamilyCount} is below {RuleCalibrationPolicy.MinimumScenarioFamilies}."));
        if (sourceGroups.Any(group => !group.Any(sample => sample.ExpectedPositive && sample.ObservedPositive.HasValue) ||
            !group.Any(sample => !sample.ExpectedPositive && sample.ObservedPositive.HasValue)))
            reasons.Add("Every holdout source group requires non-abstained expected-positive and expected-negative controls.");
        var scenarioFamilies = labeled.Where(sample => !string.IsNullOrWhiteSpace(sample.ScenarioFamily))
            .GroupBy(sample => sample.ScenarioFamily!.Trim(), StringComparer.OrdinalIgnoreCase);
        if (scenarioFamilies.Any(group => !group.Any(sample => sample.ExpectedPositive && sample.ObservedPositive.HasValue) ||
            !group.Any(sample => !sample.ExpectedPositive && sample.ObservedPositive.HasValue)))
            reasons.Add("Every holdout scenario family requires non-abstained expected-positive and expected-negative controls.");
        if (labeled.Length > 0 && sourceGroups.Any(group => group.Count() / (double)labeled.Length > RuleCalibrationPolicy.MaximumSourceGroupFraction))
            reasons.Add(FormattableString.Invariant($"A holdout source group exceeds the {RuleCalibrationPolicy.MaximumSourceGroupFraction:P1} concentration limit."));
        var tuningGroups = GroupBySource(corpus.Where(sample => sample.Partition == RuleCalibrationPartition.Tuning))
            .Select(group => group.Key).ToHashSet(StringComparer.OrdinalIgnoreCase);
        if (sourceGroups.Any(group => tuningGroups.Contains(group.Key)))
            reasons.Add("Source-group leakage between tuning and holdout partitions prevents qualification.");
        if (metrics.ExpectedPositiveCount == 0)
            reasons.Add("Calibration requires expected-positive samples.");
        if (metrics.ExpectedNegativeCount == 0)
            reasons.Add("Calibration requires expected-negative samples.");
        if (metrics.ExpectedPositiveCount > 0 && truePositive + falseNegative == 0)
            reasons.Add("Holdout requires non-abstained expected-positive controls.");
        if (metrics.ExpectedNegativeCount > 0 && trueNegative + falsePositive == 0)
            reasons.Add("Holdout requires non-abstained expected-negative controls.");
        if (labeled.Length > 0 && (labeled.Length - metrics.AbstainedCount) / (double)labeled.Length < RuleCalibrationPolicy.MinimumPredictionCoverage)
            reasons.Add(FormattableString.Invariant($"Holdout prediction coverage {labeled.Length - metrics.AbstainedCount}/{labeled.Length} is below {RuleCalibrationPolicy.MinimumPredictionCoverage:P1}."));
        if (matching.Any(sample => string.IsNullOrWhiteSpace(sample.SampleId)))
            reasons.Add("Calibration requires non-empty sample identities.");
        if (matching.Select(sample => sample.SampleId).Distinct(StringComparer.Ordinal).Count() != matching.Length)
            reasons.Add("Calibration requires unique sample identities within each rule.");
        if (metrics.LabeledSampleCount < RuleCalibrationPolicy.MinimumLabeledSamples)
            reasons.Add(FormattableString.Invariant($"Labeled sample count {metrics.LabeledSampleCount} is below {RuleCalibrationPolicy.MinimumLabeledSamples}."));
        if (truePositive + falsePositive > 0 && precision < RuleCalibrationPolicy.MinimumPrecision)
            reasons.Add(FormattableString.Invariant($"Precision {truePositive}/{truePositive + falsePositive} is below {RuleCalibrationPolicy.MinimumPrecision:P1}."));
        if (metrics.ExpectedPositiveCount > 0 && recall < RuleCalibrationPolicy.MinimumRecall)
            reasons.Add(FormattableString.Invariant($"Recall {truePositive}/{metrics.ExpectedPositiveCount} is below {RuleCalibrationPolicy.MinimumRecall:P1}."));
        if (falsePositiveRate > RuleCalibrationPolicy.MaximumFalsePositiveRate)
            reasons.Add(FormattableString.Invariant($"False-positive rate {falsePositive}/{falsePositive + trueNegative} exceeds {RuleCalibrationPolicy.MaximumFalsePositiveRate:P1}."));
        if (falseNegativeRate > RuleCalibrationPolicy.MaximumFalseNegativeRate)
            reasons.Add(FormattableString.Invariant($"False-negative rate {falseNegative}/{truePositive + falseNegative} exceeds {RuleCalibrationPolicy.MaximumFalseNegativeRate:P1}."));

        return new RuleCalibrationDecision
        {
            PolicyRevision = RuleCalibrationPolicy.Revision,
            Metrics = CalculateMetrics(ruleId, matching),
            HoldoutMetrics = labeled.Length == 0 ? null : metrics,
            DefaultBlockingEligible = reasons.Count == 0,
            Reasons = reasons
        };
    }

    private static RuleCalibrationMetrics CalculateMetrics(string ruleId, IReadOnlyList<RuleCalibrationSample> samples)
    {
        var truePositive = samples.Count(sample => sample.ExpectedPositive && sample.ObservedPositive == true);
        var falsePositive = samples.Count(sample => !sample.ExpectedPositive && sample.ObservedPositive == true);
        var trueNegative = samples.Count(sample => !sample.ExpectedPositive && sample.ObservedPositive == false);
        var falseNegative = samples.Count(sample => sample.ExpectedPositive && sample.ObservedPositive == false);
        var abstainedPositive = samples.Count(sample => sample.ExpectedPositive && sample.ObservedPositive == null);
        var abstainedNegative = samples.Count(sample => !sample.ExpectedPositive && sample.ObservedPositive == null);
        var expectedPositive = truePositive + falseNegative + abstainedPositive;
        var expectedNegative = trueNegative + falsePositive + abstainedNegative;
        var sourceGroups = GroupBySource(samples);
        return new RuleCalibrationMetrics
        {
            RuleId = ruleId,
            LabeledSampleCount = samples.Count,
            TruePositive = truePositive,
            FalsePositive = falsePositive,
            TrueNegative = trueNegative,
            FalseNegative = falseNegative,
            ExpectedPositiveCount = expectedPositive,
            ExpectedNegativeCount = expectedNegative,
            AbstainedCount = abstainedPositive + abstainedNegative,
            AbstainedPositiveCount = abstainedPositive,
            AbstainedNegativeCount = abstainedNegative,
            PredictionCoverage = RoundedRatio(samples.Count - abstainedPositive - abstainedNegative, samples.Count),
            SourceGroupCount = sourceGroups.Length,
            ScenarioFamilyCount = samples.Where(sample => !string.IsNullOrWhiteSpace(sample.ScenarioFamily))
                .Select(sample => sample.ScenarioFamily!.Trim()).Distinct(StringComparer.OrdinalIgnoreCase).Count(),
            MaximumSourceGroupFraction = samples.Count == 0 || sourceGroups.Length == 0
                ? null : Math.Round(sourceGroups.Max(group => group.Count()) / (double)samples.Count, 4),
            Precision = RoundedRatio(truePositive, truePositive + falsePositive),
            Recall = RoundedRatio(truePositive, expectedPositive),
            FalsePositiveRate = RoundedRatio(falsePositive, falsePositive + trueNegative),
            FalseNegativeRate = RoundedRatio(falseNegative, truePositive + falseNegative),
            ProtocolRevisions = samples.Select(sample => sample.ProtocolRevision).Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToArray(),
            Transports = samples.Select(sample => sample.Transport).Order(StringComparer.Ordinal).Distinct(StringComparer.OrdinalIgnoreCase).Order(StringComparer.OrdinalIgnoreCase).ToArray()
        };
    }

    private static IGrouping<string, RuleCalibrationSample>[] GroupBySource(IEnumerable<RuleCalibrationSample> samples) =>
        samples.Where(sample => !string.IsNullOrWhiteSpace(sample.SourceGroupId))
            .GroupBy(sample => sample.SourceGroupId!.Trim(), StringComparer.OrdinalIgnoreCase).ToArray();

    private static double? RoundedRatio(int numerator, int denominator) =>
        Ratio(numerator, denominator) is { } value ? Math.Round(value, 4) : null;

    private static double? Ratio(int numerator, int denominator) =>
        denominator == 0 ? null : numerator / (double)denominator;
}