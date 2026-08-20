using System.Text.Json;
using System.Text.Json.Serialization;
using Mcp.Benchmark.Core.Models;
using Mcp.Benchmark.Core.Services;
using Mcp.Benchmark.Infrastructure.Scoring;

namespace Mcp.Benchmark.Tests.Unit.Scoring;

public sealed class CalibrationCorpusTests
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = false,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        Converters = { new JsonStringEnumConverter() }
    };

    [Fact]
    public void PublicCorpus_ShouldContainRequiredTargetClasses()
    {
        var corpus = LoadCorpus();

        corpus.Version.Should().Be("1.1.0");
        corpus.Cases.Select(testCase => testCase.Kind).Should().BeEquivalentTo(
            "compliant", "vulnerable", "malformed", "unavailable", "throttled", "auth-protected");
    }

    [Fact]
    public void AddingBlockingEvidence_ShouldNeverImproveAnyVerdict()
    {
        foreach (var testCase in LoadCorpus().Cases)
        {
            var baseline = ValidationVerdictEngine.Calculate(BuildResult(testCase));
            var mutatedResult = BuildResult(testCase);
            mutatedResult.ToolValidation ??= new ToolTestResult();
            mutatedResult.ToolValidation.Findings.Add(new ValidationFinding
            {
                RuleId = "CALIBRATION.MUTATION.BLOCKER",
                Source = ValidationRuleSource.Spec,
                Severity = ValidationFindingSeverity.Critical,
                GateOverride = GateOutcome.Reject,
                Category = "Calibration",
                Component = testCase.Id,
                Summary = "Injected deterministic blocker."
            });
            var mutated = ValidationVerdictEngine.Calculate(mutatedResult);
            var baselineScore = new SecurityFocusedScoringStrategy().CalculateScore(BuildResult(testCase));
            var mutatedScore = new SecurityFocusedScoringStrategy().CalculateScore(mutatedResult);

            Rank(mutated.BaselineVerdict).Should().BeLessThanOrEqualTo(Rank(baseline.BaselineVerdict), testCase.Id);
            Rank(mutated.ProtocolVerdict).Should().BeLessThanOrEqualTo(Rank(baseline.ProtocolVerdict), testCase.Id);
            Rank(mutated.CoverageVerdict).Should().BeLessThanOrEqualTo(Rank(baseline.CoverageVerdict), testCase.Id);
            mutatedScore.OverallScore.Should().BeLessThanOrEqualTo(baselineScore.OverallScore, testCase.Id);
        }
    }

    private static CalibrationCorpus LoadCorpus()
    {
        var path = Path.Combine(AppContext.BaseDirectory, "Fixtures", "Calibration", "corpus-v1.json");
        return JsonSerializer.Deserialize<CalibrationCorpus>(File.ReadAllText(path), JsonOptions)
            ?? throw new InvalidDataException("Calibration corpus was empty.");
    }

    private static ValidationResult BuildResult(CalibrationCase testCase)
    {
        var result = new ValidationResult
        {
            ProtocolCompliance = new ComplianceTestResult
            {
                Status = testCase.Id switch
                {
                    "compliant" => TestStatus.Passed,
                    "malformed" => TestStatus.Failed,
                    "unavailable" or "throttled" => TestStatus.Inconclusive,
                    "auth-protected" => TestStatus.AuthRequired,
                    _ => TestStatus.Passed
                },
                ComplianceScore = testCase.Id == "compliant" || testCase.Id == "vulnerable" ? 100 : 0,
                JsonRpcCompliance = new JsonRpcComplianceResult
                {
                    ErrorHandlingEvaluated = testCase.Id is "compliant" or "vulnerable",
                    RequestFormatCompliant = testCase.Id is "compliant" or "vulnerable",
                    ResponseFormatCompliant = testCase.Id is "compliant" or "vulnerable",
                    ErrorHandlingCompliant = testCase.Id is "compliant" or "vulnerable",
                    BatchProcessingCompliant = testCase.Id is "compliant" or "vulnerable",
                    ComplianceScore = testCase.Id is "compliant" or "vulnerable" ? 100 : 0
                }
            }
        };
        result.Evidence.Coverage.Add(new ValidationCoverageDeclaration
        {
            LayerId = "calibration",
            Scope = testCase.Id,
            Status = testCase.CoverageStatus,
            ObservedOutcome = testCase.CoverageStatus == ValidationCoverageStatus.Covered
                ? testCase.Id is "vulnerable" or "malformed" ? ValidationOutcome.Failed : ValidationOutcome.Succeeded
                : null,
            Blocker = testCase.CoverageStatus switch
            {
                ValidationCoverageStatus.Unavailable => ValidationEvidenceBlocker.TransportError,
                ValidationCoverageStatus.Inconclusive => ValidationEvidenceBlocker.TransientFailure,
                ValidationCoverageStatus.AuthRequired => ValidationEvidenceBlocker.AuthRequired,
                _ => ValidationEvidenceBlocker.None
            },
            Confidence = testCase.CoverageStatus == ValidationCoverageStatus.Covered
                ? EvidenceConfidenceLevel.High
                : EvidenceConfidenceLevel.Low
        });
        if (testCase.ProtocolViolationSeverity.HasValue)
        {
            result.ProtocolCompliance.Violations.Add(new ComplianceViolation
            {
                CheckId = $"CALIBRATION.{testCase.Id}.PROTOCOL",
                Category = "Protocol",
                Description = "Synthetic calibration protocol violation.",
                Severity = testCase.ProtocolViolationSeverity.Value
            });
        }
        if (testCase.FindingSeverity.HasValue)
        {
            result.ToolValidation = new ToolTestResult
            {
                Findings =
                [
                    new ValidationFinding
                    {
                        RuleId = $"CALIBRATION.{testCase.Id}.FINDING",
                        Category = "Calibration",
                        Component = testCase.Id,
                        Source = ValidationRuleSource.Heuristic,
                        Severity = testCase.FindingSeverity.Value,
                        GateOverride = testCase.FindingGate,
                        Summary = "Synthetic calibration finding."
                    }
                ]
            };
            result.SecurityTesting = new SecurityTestResult
            {
                Status = TestStatus.Failed,
                SecurityScore = 0,
                Vulnerabilities =
                [
                    new SecurityVulnerability
                    {
                        Id = $"CALIBRATION.{testCase.Id}.VULNERABILITY",
                        Severity = VulnerabilitySeverity.Critical,
                        IsExploitable = true,
                        Category = "Calibration",
                        AffectedComponent = testCase.Id,
                        Description = "Synthetic calibration vulnerability."
                    }
                ]
            };
        }

        return result;
    }

    private static int Rank(ValidationVerdict verdict) => verdict switch
    {
        ValidationVerdict.Reject => 0,
        ValidationVerdict.Unknown => 1,
        ValidationVerdict.ReviewRequired => 2,
        ValidationVerdict.ConditionallyAcceptable => 3,
        ValidationVerdict.Trusted => 4,
        _ => throw new ArgumentOutOfRangeException(nameof(verdict), verdict, null)
    };

    private sealed class CalibrationCorpus
    {
        public string Version { get; init; } = string.Empty;
        public List<CalibrationCase> Cases { get; init; } = new();
    }

    private sealed record CalibrationCase
    {
        public string Id { get; init; } = string.Empty;
        public string Kind { get; init; } = string.Empty;
        public ValidationCoverageStatus CoverageStatus { get; init; }
        public ValidationFindingSeverity? FindingSeverity { get; init; }
        public GateOutcome? FindingGate { get; init; }
        public ViolationSeverity? ProtocolViolationSeverity { get; init; }
        public ValidationVerdict ExpectedBaseline { get; init; }
        public ValidationVerdict ExpectedProtocol { get; init; }
        public ValidationVerdict ExpectedCoverage { get; init; }
        public double ExpectedScore { get; init; }
    }

}