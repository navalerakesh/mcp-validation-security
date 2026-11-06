using System.Text.Json;
using System.Text.Json.Serialization;
using Mcp.Benchmark.Core.Constants;
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

        corpus.Version.Should().Be("2.0.0");
        corpus.Cases.Select(testCase => testCase.Kind).Should().BeEquivalentTo(
            "compliant", "vulnerable", "malformed", "unavailable", "throttled", "auth-protected");
    }

    [Fact]
    public void AddingBlockingEvidence_ShouldNeverImproveAssuranceOrScore()
    {
        foreach (var testCase in LoadCorpus().Cases)
        {
            var baseline = AssuranceAssessmentEngine.Evaluate(BuildResult(testCase), AssuranceProfileCatalog.Inventory);
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
            var mutated = AssuranceAssessmentEngine.Evaluate(mutatedResult, AssuranceProfileCatalog.Inventory);
            var baselineScore = new SecurityFocusedScoringStrategy().CalculateScore(BuildResult(testCase));
            var mutatedScore = new SecurityFocusedScoringStrategy().CalculateScore(mutatedResult);

            Rank(mutated.Assessments.CoreConformance.Status)
                .Should().BeLessThanOrEqualTo(Rank(baseline.Assessments.CoreConformance.Status), testCase.Id);
            mutated.Claims.Count(IsNonSuccess).Should().BeGreaterThanOrEqualTo(baseline.Claims.Count(IsNonSuccess), testCase.Id);
            mutatedScore.OverallScore.Should().BeLessThanOrEqualTo(baselineScore.OverallScore, testCase.Id);
        }
    }

    private static CalibrationCorpus LoadCorpus()
    {
        var path = Path.Combine(AppContext.BaseDirectory, "Fixtures", "Calibration", "assurance-corpus-v2.json");
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

    private static int Rank(CoreConformanceStatus status) => status switch
    {
        CoreConformanceStatus.Nonconformant => 0,
        CoreConformanceStatus.Incomplete => 1,
        CoreConformanceStatus.Conformant => 2,
        _ => throw new ArgumentOutOfRangeException(nameof(status), status, null)
    };

    private static bool IsNonSuccess(AssuranceClaim claim) =>
        claim.Outcome is not (AssuranceClaimOutcome.Satisfied or AssuranceClaimOutcome.NotApplicable);

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
        public double ExpectedScore { get; init; }
    }

}