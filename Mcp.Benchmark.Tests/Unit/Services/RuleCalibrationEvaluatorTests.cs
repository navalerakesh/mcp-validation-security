using System.Text.Json;
using System.Globalization;
using FluentAssertions;
using Mcp.Benchmark.Core.Constants;
using Mcp.Benchmark.Core.Models;
using Mcp.Benchmark.Core.Services;

namespace Mcp.Benchmark.Tests.Unit.Services;

public sealed class RuleCalibrationEvaluatorTests
{
    [Theory]
    [InlineData(512, false)]
    [InlineData(513, true)]
    public void Evaluate_RuleIdentityAdmission_ShouldIncludeExactLimit(int length, bool rejected)
    {
        var action = () => RuleCalibrationEvaluator.Evaluate(new string('r', length), []);

        if (rejected)
        {
            var exception = action.Should().Throw<ArgumentException>().Which;
            exception.ParamName.Should().Be("ruleId");
            exception.Message.Should().Contain("Calibration rule identity exceeds the admission limit.");
        }
        else action.Should().NotThrow();
    }

    [Fact]
    public void Evaluate_ProvenanceAtExactLimit_ShouldBeAdmitted()
    {
        var identifier = new string('x', 512);
        var sample = new RuleCalibrationSample
        {
            SampleId = identifier, RuleId = identifier, ExpectedPositive = true, ObservedPositive = true,
            ProtocolRevision = identifier, Transport = identifier, Partition = RuleCalibrationPartition.Holdout,
            SourceGroupId = identifier, ScenarioFamily = identifier
        };

        var decision = RuleCalibrationEvaluator.Evaluate(identifier, [sample]);

        decision.Metrics.LabeledSampleCount.Should().Be(1);
    }

    [Fact]
    public void Evaluate_CorpusAtExactLimit_ShouldBeAdmitted()
    {
        var samples = Enumerable.Range(0, 100_000).Select(index => Sample(
            $"limit-{index}", "RULE.LIMIT", index % 2 == 0, index % 2 == 0, "2026-07-28", "http"));

        var decision = RuleCalibrationEvaluator.Evaluate("RULE.LIMIT", samples);

        decision.Metrics.LabeledSampleCount.Should().Be(100_000);
    }

    [Theory]
    [InlineData("sourceGroupId")]
    [InlineData("scenarioFamily")]
    [InlineData("protocolRevision")]
    [InlineData("transport")]
    public void Evaluate_OneMissingProvenanceFieldAcrossRules_ShouldGiveIndependentReason(string field)
    {
        var document = JsonSerializer.SerializeToNode(new RuleCalibrationSample
        {
            SampleId = "other-rule", RuleId = "RULE.OTHER", ExpectedPositive = true, ObservedPositive = true,
            ProtocolRevision = "2026-07-28", Transport = "http", Partition = RuleCalibrationPartition.Tuning,
            SourceGroupId = "independent-tuning", ScenarioFamily = "scenario-0"
        }, new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase })!;
        document[field] = " ";
        var otherRule = document.Deserialize<RuleCalibrationSample>(
            new JsonSerializerOptions { PropertyNameCaseInsensitive = true })!;

        var decision = RuleCalibrationEvaluator.Evaluate("RULE.BOUNDARY", BuildSamples(50, 0, 50, 0).Append(otherRule));

        decision.DefaultBlockingEligible.Should().BeFalse();
        decision.Reasons.Should().Equal("Calibration requires source-group, scenario-family, protocol and transport provenance.");
    }

    [Fact]
    public void Evaluate_OversizedProvenance_ShouldRejectBeforeGrouping()
    {
        var sample = new RuleCalibrationSample
        {
            SampleId = "oversized", RuleId = "RULE.BOUNDED", ExpectedPositive = true, ObservedPositive = true,
            ProtocolRevision = "2026-07-28", Transport = "http", Partition = RuleCalibrationPartition.Holdout,
            SourceGroupId = new string('x', 513), ScenarioFamily = "family"
        };

        var exception = FluentActions.Invoking(() => RuleCalibrationEvaluator.Evaluate("RULE.BOUNDED", [sample]))
            .Should().Throw<ArgumentException>().Which;
        exception.ParamName.Should().Be("samples");
        exception.Message.Should().Contain("Calibration provenance exceeds the identifier admission limit.");
    }

    [Theory]
    [InlineData(RuleCalibrationPartition.Tuning)]
    [InlineData(RuleCalibrationPartition.Unspecified)]
    [InlineData((RuleCalibrationPartition)999)]
    public void Evaluate_SourcePartitionIsolation_ShouldApplyAcrossRules(RuleCalibrationPartition partition)
    {
        var samples = BuildSamples(50, 0, 50, 0).Append(new RuleCalibrationSample
        {
            SampleId = "other-rule-tuning", RuleId = "RULE.OTHER", ExpectedPositive = true, ObservedPositive = true,
            ProtocolRevision = "2026-07-28", Transport = "http", Partition = partition,
            SourceGroupId = " SOURCE-0 ", ScenarioFamily = "scenario-0"
        });

        var decision = RuleCalibrationEvaluator.Evaluate("RULE.BOUNDARY", samples);

        decision.Metrics.LabeledSampleCount.Should().Be(100);
        decision.DefaultBlockingEligible.Should().BeFalse();
        decision.Reasons.Should().Contain(reason => reason.Contains("partition", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Evaluate_NullSample_ShouldRejectMalformedCorpus()
    {
        var action = () => RuleCalibrationEvaluator.Evaluate("RULE.MALFORMED", [null!]);

        var exception = action.Should().Throw<ArgumentException>().Which;
        exception.ParamName.Should().Be("samples");
        exception.Message.Should().Contain("Calibration corpus contains a null sample.");
    }

    [Fact]
    public void Evaluate_OversizedCorpus_ShouldStopAtAdmissionBoundary()
    {
        var observed = 0;
        IEnumerable<RuleCalibrationSample> Corpus()
        {
            for (var index = 0; index < 100_002; index++)
            {
                observed++;
                if (observed > 100_001) throw new InvalidOperationException("Enumeration crossed admission boundary.");
                yield return Sample($"large-{index}", "RULE.LARGE", index % 2 == 0, index % 2 == 0, "2026-07-28", "http");
            }
        }
        var action = () => RuleCalibrationEvaluator.Evaluate("RULE.LARGE", Corpus());

        var exception = action.Should().Throw<ArgumentException>().Which;
        exception.ParamName.Should().Be("samples");
        exception.Message.Should().Contain("Calibration corpus exceeds the sample admission limit.");
        observed.Should().Be(100_001);
    }

    [Theory]
    [InlineData(true, false)]
    [InlineData(false, false)]
    [InlineData(true, true)]
    [InlineData(false, true)]
    public void Evaluate_AbstainedDiversityControl_ShouldNotQualifyThatGroupOrFamily(bool sourceControl, bool positiveControl)
    {
        var samples = Enumerable.Range(0, 100).Select(index => new RuleCalibrationSample
        {
            SampleId = $"diversity-abstention-{index}", RuleId = "RULE.DIVERSITY.ABSTENTION",
            ExpectedPositive = positiveControl ? index >= 95 : index < 95,
            ObservedPositive = index < 95 ? !positiveControl : (sourceControl ? index % 3 == 2 : index % 2 == 0) ? (bool?)null : positiveControl,
            Partition = RuleCalibrationPartition.Holdout, ProtocolRevision = "2026-07-28", Transport = "http",
            SourceGroupId = $"source-{index % 3}", ScenarioFamily = $"family-{index % 2}"
        });

        var decision = RuleCalibrationEvaluator.Evaluate("RULE.DIVERSITY.ABSTENTION", samples);

        decision.HoldoutMetrics!.PredictionCoverage.Should().BeGreaterThanOrEqualTo(0.95);
        decision.DefaultBlockingEligible.Should().BeFalse();
        decision.Reasons.Should().Contain(reason => reason.Contains(sourceControl ? "source group" : "scenario family", StringComparison.OrdinalIgnoreCase));
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void Evaluate_NegativeAbstentions_ShouldNotInventOrDiluteFalsePositiveEvidence(bool allNegativesAbstained)
    {
        var count = allNegativesAbstained ? 100 : 1000;
        var positives = allNegativesAbstained ? 95 : 900;
        var samples = Enumerable.Range(0, count).Select(index => new RuleCalibrationSample
        {
            SampleId = $"negative-abstention-{index}", RuleId = "RULE.NEGATIVE.ABSTENTION",
            ExpectedPositive = index < positives,
            ObservedPositive = index < positives ? true : index >= count - 5 ? (bool?)null : index < positives + 5,
            Partition = RuleCalibrationPartition.Holdout, ProtocolRevision = "2026-07-28", Transport = "http",
            SourceGroupId = $"source-{index % 3}", ScenarioFamily = $"family-{index % 2}"
        });

        var decision = RuleCalibrationEvaluator.Evaluate("RULE.NEGATIVE.ABSTENTION", samples);

        decision.DefaultBlockingEligible.Should().BeFalse();
        decision.HoldoutMetrics!.AbstainedNegativeCount.Should().Be(5);
        decision.HoldoutMetrics.AbstainedCount.Should().Be(5);
        decision.HoldoutMetrics.ExpectedNegativeCount.Should().Be(count - positives);
        decision.HoldoutMetrics.PredictionCoverage.Should().Be((count - 5) / (double)count);
        if (allNegativesAbstained)
        {
            decision.HoldoutMetrics.FalsePositiveRate.Should().BeNull();
            decision.Reasons.Should().Contain("Holdout requires non-abstained expected-negative controls.");
        }
        else
        {
            decision.HoldoutMetrics.FalsePositiveRate.Should().Be(0.0526);
            decision.Reasons.Should().Contain(reason => reason.StartsWith("False-positive rate 5/95", StringComparison.Ordinal));
        }
    }

    [Theory]
    [InlineData(0, true)]
    [InlineData(5, true)]
    [InlineData(6, false)]
    [InlineData(50, false)]
    public void Evaluate_Abstentions_ShouldRemainSeparateAndReduceHeldOutCoverage(int abstentions, bool expectedEligible)
    {
        var json = JsonSerializer.Serialize(Enumerable.Range(0, 100).Select(index => new
        {
            sampleId = $"abstention-{index}", ruleId = "RULE.ABSTENTION", expectedPositive = index < 50,
            observedPositive = index < abstentions ? (bool?)null : index < 50,
            partition = RuleCalibrationPartition.Holdout, protocolRevision = "2026-07-28", transport = "http",
            sourceGroupId = $"source-{index % 3}", scenarioFamily = $"family-{index % 2}"
        }));
        var samples = JsonSerializer.Deserialize<RuleCalibrationSample[]>(json,
            new JsonSerializerOptions { PropertyNameCaseInsensitive = true })!;

        var decision = RuleCalibrationEvaluator.Evaluate("RULE.ABSTENTION", samples);

        decision.DefaultBlockingEligible.Should().Be(expectedEligible);
        if (abstentions > 5)
            decision.Reasons.Should().Contain($"Holdout prediction coverage {100 - abstentions}/100 is below 95.0 %.");
        else
            decision.Reasons.Should().NotContain(reason => reason.StartsWith("Holdout prediction coverage", StringComparison.Ordinal));
        if (abstentions == 50)
            decision.Reasons.Should().Contain("Holdout requires non-abstained expected-positive controls.");
        decision.HoldoutMetrics!.FalseNegative.Should().Be(0, "an abstention is not an observed negative prediction");
        decision.HoldoutMetrics.TruePositive.Should().Be(50 - abstentions);
        decision.HoldoutMetrics.Recall.Should().Be((50 - abstentions) / 50.0);
        var document = JsonSerializer.SerializeToNode(decision, new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase })!;
        document["holdoutMetrics"]!["abstainedCount"]!.GetValue<int>().Should().Be(abstentions);
        document["holdoutMetrics"]!["predictionCoverage"]!.GetValue<double>().Should().Be((100 - abstentions) / 100.0);
    }

    [Theory]
    [InlineData("single-source")]
    [InlineData("source-aliases")]
    [InlineData("single-family")]
    [InlineData("source-class")]
    [InlineData("family-class")]
    [InlineData("tuning-leakage")]
    public void Evaluate_WeakOrLeakedProvenance_ShouldRemainNonBlocking(string fault)
    {
        var samples = Enumerable.Range(0, 100).Select(index => new RuleCalibrationSample
        {
            SampleId = $"holdout-{index}", RuleId = "RULE.DIVERSITY", Partition = RuleCalibrationPartition.Holdout,
            ExpectedPositive = index % 2 == 0, ObservedPositive = index % 2 == 0,
            ProtocolRevision = "2026-07-28", Transport = "http",
            SourceGroupId = fault switch
            {
                "single-source" => "source",
                "source-aliases" => new[] { "source", " SOURCE ", "Source" }[index % 3],
                "source-class" => index % 2 == 0 ? "positive-source" : $"negative-source-{index % 4}",
                _ => $"source-{index % 3}"
            },
            ScenarioFamily = fault == "single-family" ? "family" : fault == "family-class"
                ? (index % 2 == 0 ? "positive-family" : "negative-family") : $"family-{index / 2 % 2}"
        }).ToList();
        if (fault == "tuning-leakage") samples.Add(new RuleCalibrationSample
        {
            SampleId = "tuning-distinct-id", RuleId = "RULE.DIVERSITY", Partition = RuleCalibrationPartition.Tuning,
            ExpectedPositive = true, ObservedPositive = true, ProtocolRevision = "2026-07-28", Transport = "http",
            SourceGroupId = " SOURCE-0 ", ScenarioFamily = "family-0"
        });

        var decision = RuleCalibrationEvaluator.Evaluate("RULE.DIVERSITY", samples);

        decision.DefaultBlockingEligible.Should().BeFalse();
        decision.Reasons.Should().NotBeEmpty();
        if (fault is "single-source" or "source-aliases")
            decision.Reasons.Should().Contain("Holdout source-group count 1 is below 3.");
        if (fault == "tuning-leakage") decision.Reasons.Should().Contain(reason => reason.Contains("leakage", StringComparison.OrdinalIgnoreCase));
    }

    [Theory]
    [InlineData(50, 100, true)]
    [InlineData(51, 100, false)]
    [InlineData(10001, 20001, false)]
    public void Evaluate_SourceConcentration_ShouldUseUnroundedHoldoutFraction(int dominant, int count, bool expectedEligible)
    {
        var samples = Enumerable.Range(0, count).Select(index => new RuleCalibrationSample
        {
            SampleId = $"concentration-{index}", RuleId = "RULE.CONCENTRATION", Partition = RuleCalibrationPartition.Holdout,
            ExpectedPositive = index % 2 == 0, ObservedPositive = index % 2 == 0,
            ProtocolRevision = "2026-07-28", Transport = "http",
            SourceGroupId = index < dominant ? "source-0" : index < dominant + (count - dominant) / 2 ? "source-1" : "source-2",
            ScenarioFamily = $"family-{index / 2 % 2}"
        });

        var decision = RuleCalibrationEvaluator.Evaluate("RULE.CONCENTRATION", samples);

        decision.DefaultBlockingEligible.Should().Be(expectedEligible);
        decision.HoldoutMetrics!.MaximumSourceGroupFraction.Should().Be(Math.Round(dominant / (double)count, 4));
        if (!expectedEligible) decision.Reasons.Should().Contain(reason => reason.Contains("concentration", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Evaluate_HoldoutWithoutProvenance_ShouldRemainNonBlocking()
    {
        var samples = Enumerable.Range(0, 100).Select(index => new RuleCalibrationSample
        {
            SampleId = $"missing-provenance-{index}", RuleId = "RULE.PROVENANCE",
            ExpectedPositive = index < 50, ObservedPositive = index < 50,
            Partition = RuleCalibrationPartition.Holdout, ProtocolRevision = "2026-07-28", Transport = "http"
        });

        var decision = RuleCalibrationEvaluator.Evaluate("RULE.PROVENANCE", samples);

        decision.DefaultBlockingEligible.Should().BeFalse();
        decision.Reasons.Should().Contain(reason => reason.Contains("provenance", StringComparison.OrdinalIgnoreCase));
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void Evaluate_MixedPartitions_ShouldUseOnlyHoldoutForEligibility(bool holdoutCorrect)
    {
        var samples = new[] { RuleCalibrationPartition.Tuning, RuleCalibrationPartition.Holdout }
            .SelectMany(partition => Enumerable.Range(0, 100).Select(index => new RuleCalibrationSample
            {
                SampleId = $"{partition}-{index}", RuleId = "RULE.PARTITIONED", Partition = partition,
                SourceGroupId = $"{partition}-source-{index % 3}", ScenarioFamily = $"scenario-{index % 2}",
                ExpectedPositive = index < 50,
                ObservedPositive = (partition == RuleCalibrationPartition.Holdout ? holdoutCorrect : !holdoutCorrect)
                    ? index < 50 : index >= 50,
                ProtocolRevision = "2026-07-28", Transport = "http"
            }));

        var decision = RuleCalibrationEvaluator.Evaluate("RULE.PARTITIONED", samples);

        decision.DefaultBlockingEligible.Should().Be(holdoutCorrect);
        decision.Metrics.LabeledSampleCount.Should().Be(200);
        decision.Metrics.Precision.Should().Be(0.5);
        var document = JsonSerializer.SerializeToNode(decision, new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase })!;
        document["holdoutMetrics"].Should().NotBeNull();
        document["holdoutMetrics"]!["labeledSampleCount"]!.GetValue<int>().Should().Be(100);
        document["holdoutMetrics"]!["precision"]!.GetValue<double>().Should().Be(holdoutCorrect ? 1 : 0);
    }

    [Fact]
    public void Evaluate_PerfectUnpartitionedSamples_ShouldNotQualifyAsHoldoutEvidence()
    {
        var samples = Enumerable.Range(0, 100).Select(index => new RuleCalibrationSample
        {
            SampleId = $"unpartitioned-{index}", RuleId = "RULE.HOLDOUT",
            ExpectedPositive = index < 50, ObservedPositive = index < 50,
            ProtocolRevision = "2026-07-28", Transport = "http"
        });

        var decision = RuleCalibrationEvaluator.Evaluate("RULE.HOLDOUT", samples);

        decision.DefaultBlockingEligible.Should().BeFalse();
        decision.Reasons.Should().Contain(reason => reason.Contains("holdout", StringComparison.OrdinalIgnoreCase));
    }

    [Theory]
    [InlineData(1899, 100, 1900, 0)]
    [InlineData(8999, 0, 10000, 1000)]
    [InlineData(10000, 100, 1899, 0)]
    public void Evaluate_JustOutsideThreshold_ShouldNotQualifyAfterDisplayRounding(int truePositive, int falsePositive, int trueNegative, int falseNegative)
    {
        var decision = RuleCalibrationEvaluator.Evaluate("RULE.BOUNDARY", BuildSamples(truePositive, falsePositive, trueNegative, falseNegative));

        decision.DefaultBlockingEligible.Should().BeFalse();
        decision.Reasons.Should().NotBeEmpty();
    }

    [Fact]
    public void Evaluate_ShouldBeInvariantUnderCultureAndSampleOrder()
    {
        var samples = BuildSamples(20, 30, 40, 10).Concat(
        [
            Sample("upper", "RULE.BOUNDARY", false, false, "2025-11-25", "HTTP"),
            Sample("lower", "RULE.BOUNDARY", false, false, "2025-11-25", "http")
        ]).ToArray();
        var originalCulture = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = CultureInfo.InvariantCulture;
            var expected = JsonSerializer.Serialize(RuleCalibrationEvaluator.Evaluate("RULE.BOUNDARY", samples));
            foreach (var culture in new[] { "en-US", "fr-FR", "tr-TR" })
            {
                CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo(culture);
                JsonSerializer.Serialize(RuleCalibrationEvaluator.Evaluate("RULE.BOUNDARY", samples.Reverse())).Should().Be(expected);
            }
        }
        finally
        {
            CultureInfo.CurrentCulture = originalCulture;
        }
    }

    [Fact]
    public void Evaluate_InvalidArguments_ShouldThrow()
    {
        var emptyRule = () => RuleCalibrationEvaluator.Evaluate("", []);
        var nullSamples = () => RuleCalibrationEvaluator.Evaluate("RULE", null!);

        emptyRule.Should().Throw<ArgumentException>();
        nullSamples.Should().Throw<ArgumentNullException>().Which.ParamName.Should().Be("samples");
    }

    [Fact]
    public void Evaluate_InitialSystemImpactCorpus_ShouldRemainNonBlocking()
    {
        var samples = Fixtures.RuleQualityCorpus.ObserveSystemImpact().Select(sample => sample.Observed).ToArray();

        var decision = RuleCalibrationEvaluator.Evaluate("MCP.CONTENT.SystemImpact", samples);

        decision.DefaultBlockingEligible.Should().BeFalse();
        decision.Metrics.LabeledSampleCount.Should().Be(12);
        decision.HoldoutMetrics.Should().BeNull("these illustrative examples are not independent held-out evidence");
        decision.Metrics.TruePositive.Should().Be(4);
        decision.Metrics.FalsePositive.Should().Be(3);
        decision.Metrics.TrueNegative.Should().Be(3);
        decision.Metrics.FalseNegative.Should().Be(2);
        decision.Metrics.Precision.Should().Be(0.5714);
        decision.Metrics.Recall.Should().Be(0.6667);
        decision.Metrics.FalsePositiveRate.Should().Be(0.5);
        decision.Metrics.FalseNegativeRate.Should().Be(0.3333);
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
                Transport = index % 2 == 0 ? "http" : "stdio",
                Partition = RuleCalibrationPartition.Holdout,
                SourceGroupId = $"source-{index % 3}", ScenarioFamily = $"scenario-{index % 2}"
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
    public void Evaluate_OneClassCorpora_ShouldNeverBeEligible()
    {
        var positiveOnly = RuleCalibrationEvaluator.Evaluate("RULE.BOUNDARY", BuildSamples(100, 0, 0, 0));
        var negativeOnly = RuleCalibrationEvaluator.Evaluate("RULE.BOUNDARY", BuildSamples(0, 0, 100, 0));

        positiveOnly.DefaultBlockingEligible.Should().BeFalse();
        positiveOnly.Reasons.Should().Contain("Calibration requires expected-negative samples.");
        negativeOnly.DefaultBlockingEligible.Should().BeFalse();
        negativeOnly.Reasons.Should().Contain("Calibration requires expected-positive samples.");
    }

    [Theory]
    [InlineData(20, 30, 40, 10, 50, 30, 70)]
    [InlineData(0, 30, 40, 30, 30, 30, 70)]
    public void Evaluate_FailedRates_ShouldReportObservedNumeratorsAndDenominators(
        int truePositive, int falsePositive, int trueNegative, int falseNegative,
        int predictedPositiveCount, int expectedPositiveCount, int expectedNegativeCount)
    {
        var decision = RuleCalibrationEvaluator.Evaluate("RULE.BOUNDARY",
            BuildSamples(truePositive, falsePositive, trueNegative, falseNegative));

        decision.DefaultBlockingEligible.Should().BeFalse();
        decision.Reasons.Should().HaveCount(4);
        decision.Reasons.Should().Contain(reason => reason.StartsWith(
            $"Precision {truePositive}/{predictedPositiveCount} is below ", StringComparison.Ordinal));
        decision.Reasons.Should().Contain(reason => reason.StartsWith(
            $"Recall {truePositive}/{expectedPositiveCount} is below ", StringComparison.Ordinal));
        decision.Reasons.Should().Contain(reason => reason.StartsWith(
            $"False-positive rate {falsePositive}/{expectedNegativeCount} exceeds ", StringComparison.Ordinal));
        decision.Reasons.Should().Contain(reason => reason.StartsWith(
            $"False-negative rate {falseNegative}/{expectedPositiveCount} exceeds ", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData("duplicate")]
    [InlineData("")]
    [InlineData(" ")]
    public void Evaluate_InvalidSampleIdentities_ShouldNotQualifyByRepeatingEvidence(string identity)
    {
        var samples = Enumerable.Range(0, 100).Select(index => Sample(identity, "RULE.TARGET",
            index < 50, index < 50, "2025-11-25", "http"));

        var decision = RuleCalibrationEvaluator.Evaluate("RULE.TARGET", samples);

        decision.DefaultBlockingEligible.Should().BeFalse();
        decision.Reasons.Should().Contain(reason => reason.Contains("sample identities", StringComparison.Ordinal));
    }

    [Fact]
    public void Evaluate_BothClassesWithEqualCorrectAndIncorrectLabels_ShouldNotReportMissingClasses()
    {
        var decision = RuleCalibrationEvaluator.Evaluate("RULE.BOUNDARY", BuildSamples(50, 50, 50, 50));

        decision.DefaultBlockingEligible.Should().BeFalse();
        decision.Reasons.Should().NotContain("Calibration requires expected-positive samples.");
        decision.Reasons.Should().NotContain("Calibration requires expected-negative samples.");
        decision.Reasons.Should().NotContain("Holdout requires non-abstained expected-positive controls.");
        decision.Reasons.Should().NotContain("Holdout requires non-abstained expected-negative controls.");
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData(" ")]
    public void Evaluate_OneMissingIdentityAmongUniqueSamples_ShouldFailClosed(string? identity)
    {
        var samples = Enumerable.Range(0, 100).Select(index => Sample(index == 0 ? identity! : $"sample-{index}",
            "RULE.TARGET", index < 50, index < 50, "2025-11-25", "http"));

        var decision = RuleCalibrationEvaluator.Evaluate("RULE.TARGET", samples);

        decision.DefaultBlockingEligible.Should().BeFalse();
        decision.Reasons.Should().Contain("Calibration requires non-empty sample identities.");
        decision.Reasons.Should().NotContain("Calibration requires unique sample identities within each rule.");
    }

    [Fact]
    public void Evaluate_NoSamples_ShouldFailClosed()
    {
        var decision = RuleCalibrationEvaluator.Evaluate("RULE.UNKNOWN", []);

        decision.DefaultBlockingEligible.Should().BeFalse();
        decision.HoldoutMetrics.Should().BeNull();
        decision.Metrics.Precision.Should().BeNull();
        decision.Metrics.Recall.Should().BeNull();
        decision.Reasons.Should().NotContain(reason => reason.StartsWith("Precision", StringComparison.Ordinal) ||
            reason.StartsWith("Recall", StringComparison.Ordinal));
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
    public void Evaluate_NoExpectedPositivesOrNegatives_ShouldKeepUndefinedRatesMissing()
    {
        var allNegative = BuildSamples(0, 0, 100, 0);
        var allPositive = BuildSamples(100, 0, 0, 0);

        RuleCalibrationEvaluator.Evaluate("RULE.BOUNDARY", allNegative).Metrics.Recall.Should().BeNull();
        RuleCalibrationEvaluator.Evaluate("RULE.BOUNDARY", allPositive).Metrics.FalsePositiveRate.Should().BeNull();
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
                    Transport = "http",
                    Partition = RuleCalibrationPartition.Holdout,
                    SourceGroupId = $"source-{samples.Count % 3}", ScenarioFamily = $"scenario-{index % 2}"
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
        Transport = transport,
        Partition = RuleCalibrationPartition.Holdout,
        SourceGroupId = "fixture-source", ScenarioFamily = "fixture-scenario"
    };
}