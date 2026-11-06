using System.Text.Json;
using FluentAssertions;
using Mcp.Benchmark.Core.Constants;
using Mcp.Benchmark.Core.Models;
using Mcp.Benchmark.Core.Services;

namespace Mcp.Benchmark.Tests.Unit.Services;

public sealed class RuleCalibrationEvaluatorTests
{
    [Fact]
    public void Evaluate_InvalidArguments_ShouldThrow()
    {
        var emptyRule = () => RuleCalibrationEvaluator.Evaluate("", []);
        var nullSamples = () => RuleCalibrationEvaluator.Evaluate("RULE", null!);

        emptyRule.Should().Throw<ArgumentException>();
        nullSamples.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void Evaluate_InitialSystemImpactCorpus_ShouldRemainNonBlocking()
    {
        var samples = JsonSerializer.Deserialize<List<RuleCalibrationSample>>(
            File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Fixtures", "Calibration", "rule-quality-v2.json")),
            new JsonSerializerOptions { PropertyNameCaseInsensitive = true })!;

        var decision = RuleCalibrationEvaluator.Evaluate("MCP.CONTENT.SystemImpact", samples);

        decision.DefaultBlockingEligible.Should().BeFalse();
        decision.Metrics.LabeledSampleCount.Should().Be(12);
        decision.Metrics.TruePositive.Should().Be(5);
        decision.Metrics.FalsePositive.Should().Be(2);
        decision.Metrics.TrueNegative.Should().Be(4);
        decision.Metrics.FalseNegative.Should().Be(1);
        decision.Reasons.Should().Contain(reason => reason.Contains("sample count", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Evaluate_MeasuredRuleMeetingEveryThreshold_ShouldBeEligible()
    {
        var samples = Enumerable.Range(0, 100)
            .Select(index => new RuleCalibrationSample
            {
                SampleId = $"sample-{index}",
                RuleId = "RULE.ELIGIBLE",
                ExpectedPositive = index < 50,
                ObservedPositive = index < 48 || index is 50 or 51,
                ProtocolRevision = "2026-07-28",
                Transport = index % 2 == 0 ? "http" : "stdio"
            });

        var decision = RuleCalibrationEvaluator.Evaluate("RULE.ELIGIBLE", samples);

        decision.DefaultBlockingEligible.Should().BeTrue();
        decision.Metrics.Precision.Should().Be(0.96);
        decision.Metrics.Recall.Should().Be(0.96);
        decision.Metrics.FalsePositiveRate.Should().Be(0.04);
        decision.Metrics.FalseNegativeRate.Should().Be(0.04);
    }

    [Theory]
    [InlineData(94, 6, 100, 0)]
    [InlineData(90, 0, 89, 11)]
    public void Evaluate_MetricOutsideBoundary_ShouldNotBeEligible(int truePositive, int falsePositive, int trueNegative, int falseNegative)
    {
        var samples = BuildSamples(truePositive, falsePositive, trueNegative, falseNegative);

        var decision = RuleCalibrationEvaluator.Evaluate("RULE.BOUNDARY", samples);

        decision.DefaultBlockingEligible.Should().BeFalse();
        decision.Reasons.Should().NotBeEmpty();
    }

    [Fact]
    public void Evaluate_EachFailedThreshold_ShouldEmitItsSpecificReason()
    {
        var undersized = RuleCalibrationEvaluator.Evaluate("RULE.BOUNDARY", BuildSamples(49, 0, 50, 0));
        var lowPrecision = RuleCalibrationEvaluator.Evaluate("RULE.BOUNDARY", BuildSamples(94, 6, 100, 0));
        var lowRecall = RuleCalibrationEvaluator.Evaluate("RULE.BOUNDARY", BuildSamples(89, 0, 100, 11));
        var highFalsePositiveRate = RuleCalibrationEvaluator.Evaluate("RULE.BOUNDARY", BuildSamples(100, 6, 94, 0));
        var highFalseNegativeRate = RuleCalibrationEvaluator.Evaluate("RULE.BOUNDARY", BuildSamples(89, 0, 100, 11));

        undersized.Reasons.Should().Contain(reason => reason.StartsWith("Labeled sample count", StringComparison.Ordinal));
        lowPrecision.Reasons.Should().Contain(reason => reason.StartsWith("Precision", StringComparison.Ordinal));
        lowRecall.Reasons.Should().Contain(reason => reason.StartsWith("Recall", StringComparison.Ordinal));
        highFalsePositiveRate.Reasons.Should().Contain(reason => reason.StartsWith("False-positive rate", StringComparison.Ordinal));
        highFalseNegativeRate.Reasons.Should().Contain(reason => reason.StartsWith("False-negative rate", StringComparison.Ordinal));
    }

    [Fact]
    public void Evaluate_NoSamples_ShouldFailClosed()
    {
        var decision = RuleCalibrationEvaluator.Evaluate("RULE.UNKNOWN", []);

        decision.DefaultBlockingEligible.Should().BeFalse();
        decision.Metrics.Precision.Should().Be(0);
        decision.Metrics.Recall.Should().Be(0);
    }

    [Fact]
    public void Evaluate_ShouldFilterRuleAndSortCoverageDimensions()
    {
        var samples = new[]
        {
            Sample("z", "RULE.TARGET", true, true, "2026-07-28", "stdio"),
            Sample("a", "RULE.TARGET", false, false, "2025-11-25", "HTTP"),
            Sample("other", "RULE.OTHER", true, false, "2099-01-01", "other")
        };

        var decision = RuleCalibrationEvaluator.Evaluate("RULE.TARGET", samples);

        decision.Metrics.LabeledSampleCount.Should().Be(2);
        decision.Metrics.ProtocolRevisions.Should().Equal("2025-11-25", "2026-07-28");
        decision.Metrics.Transports.Should().Equal("HTTP", "stdio");
    }

    [Fact]
    public void Evaluate_ExactPrecisionAndFalsePositiveBoundaries_ShouldBeEligible()
    {
        var samples = BuildSamples(truePositive: 95, falsePositive: 5, trueNegative: 95, falseNegative: 0);

        var decision = RuleCalibrationEvaluator.Evaluate("RULE.BOUNDARY", samples);

        decision.Metrics.Precision.Should().Be(0.95);
        decision.Metrics.FalsePositiveRate.Should().Be(0.05);
        decision.DefaultBlockingEligible.Should().BeTrue();
    }

    [Fact]
    public void Evaluate_ExactRecallAndFalseNegativeBoundaries_ShouldBeEligible()
    {
        var samples = BuildSamples(truePositive: 90, falsePositive: 0, trueNegative: 100, falseNegative: 10);

        var decision = RuleCalibrationEvaluator.Evaluate("RULE.BOUNDARY", samples);

        decision.Metrics.Recall.Should().Be(0.9);
        decision.Metrics.FalseNegativeRate.Should().Be(0.1);
        decision.DefaultBlockingEligible.Should().BeTrue();
    }

    [Fact]
    public void Evaluate_NoExpectedPositivesOrNegatives_ShouldReportZeroUndefinedRates()
    {
        var allNegative = BuildSamples(0, 0, 100, 0);
        var allPositive = BuildSamples(100, 0, 0, 0);

        RuleCalibrationEvaluator.Evaluate("RULE.BOUNDARY", allNegative).Metrics.Recall.Should().Be(0);
        RuleCalibrationEvaluator.Evaluate("RULE.BOUNDARY", allPositive).Metrics.FalsePositiveRate.Should().Be(0);
    }

    private static IReadOnlyList<RuleCalibrationSample> BuildSamples(
        int truePositive,
        int falsePositive,
        int trueNegative,
        int falseNegative)
    {
        var samples = new List<RuleCalibrationSample>();
        Add(truePositive, expected: true, observed: true, "tp");
        Add(falsePositive, expected: false, observed: true, "fp");
        Add(trueNegative, expected: false, observed: false, "tn");
        Add(falseNegative, expected: true, observed: false, "fn");
        return samples;

        void Add(int count, bool expected, bool observed, string prefix)
        {
            for (var index = 0; index < count; index++)
            {
                samples.Add(new RuleCalibrationSample
                {
                    SampleId = $"{prefix}-{index}",
                    RuleId = "RULE.BOUNDARY",
                    ExpectedPositive = expected,
                    ObservedPositive = observed,
                    ProtocolRevision = "2026-07-28",
                    Transport = "http"
                });
            }
        }
    }

    private static RuleCalibrationSample Sample(
        string id,
        string ruleId,
        bool expected,
        bool observed,
        string revision,
        string transport) => new()
    {
        SampleId = id,
        RuleId = ruleId,
        ExpectedPositive = expected,
        ObservedPositive = observed,
        ProtocolRevision = revision,
        Transport = transport
    };
}