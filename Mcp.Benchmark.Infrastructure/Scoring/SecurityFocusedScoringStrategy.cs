using System.Globalization;
using Mcp.Benchmark.Core.Abstractions;
using Mcp.Benchmark.Core.Constants;
using Mcp.Benchmark.Core.Models;
using Mcp.Benchmark.Core.Services;

namespace Mcp.Benchmark.Infrastructure.Scoring;

/// <summary>
/// Implements a scoring strategy that prioritizes security and protocol compliance.
/// Score is descriptive; blocking status is reserved for true protocol MUST failures,
/// critical vulnerabilities, or exploitable unauthorized access.
/// </summary>
public class SecurityFocusedScoringStrategy : IAggregateScoringStrategy
{
    private readonly List<ScoringRule> _rules;
    private static readonly IReadOnlyList<ScoringCategoryDefinition> CategoryDefinitions = new List<ScoringCategoryDefinition>
    {
        new(
            "Protocol",
            ScoringConstants.WeightProtocol,
            r => r.ProtocolCompliance?.ComplianceScore ?? 0,
            r => r.ProtocolCompliance?.Status),
        new(
            "Security",
            ScoringConstants.WeightSecurity,
            r => r.SecurityTesting?.SecurityScore ?? 0,
            r => r.SecurityTesting?.Status),
        new(
            "Tools",
            ScoringConstants.WeightTools,
            r =>
            {
                if (r.ToolValidation == null)
                {
                    return 0;
                }

                if (r.ToolValidation.ToolsDiscovered > 0)
                {
                    return (double)r.ToolValidation.ToolsTestPassed / r.ToolValidation.ToolsDiscovered * 100.0;
                }

                return r.ToolValidation.Status == TestStatus.Passed ? 100.0 : 0.0;
            },
            r => r.ToolValidation?.Status),
        new(
            "Resources",
            ScoringConstants.WeightResources,
            r =>
            {
                if (r.ResourceTesting == null)
                {
                    return 0;
                }

                if (r.ResourceTesting.ResourcesDiscovered > 0)
                {
                    return (double)r.ResourceTesting.ResourcesAccessible / r.ResourceTesting.ResourcesDiscovered * 100.0;
                }

                return r.ResourceTesting.Status == TestStatus.Passed ? 100.0 : 0.0;
            },
            r => r.ResourceTesting?.Status),
        new(
            "Prompts",
            ScoringConstants.WeightPrompts,
            r =>
            {
                if (r.PromptTesting == null)
                {
                    return 0;
                }

                if (r.PromptTesting.PromptsDiscovered > 0)
                {
                    return (double)r.PromptTesting.PromptsTestPassed / r.PromptTesting.PromptsDiscovered * 100.0;
                }

                return r.PromptTesting.Status == TestStatus.Passed ? 100.0 : 0.0;
            },
            r => r.PromptTesting?.Status),
        new(
            "ErrorHandling",
            ScoringConstants.WeightErrorHandling,
            r => r.ErrorHandling?.Score ?? 0,
            r => r.ErrorHandling?.Status),
        // Performance is INFORMATIONAL — does not impact compliance score.
        // A slow server is not a non-compliant server. Correctness > speed.
        // Performance data is still collected and reported in the Trust Assessment.
        // Uses the nuanced score from PerformanceScoringStrategy (latency/error
        // penalties) so the category score in JSON matches human-readable reports
        // and TrustAssessment.OperationalReadiness.
        new(
            "Performance",
            0.00,
            r => r.PerformanceTesting?.Score ?? 0,
            r => r.PerformanceTesting?.Status)
    };

    private static readonly double TotalCategoryWeight = CategoryDefinitions.Sum(c => c.Weight);

    public SecurityFocusedScoringStrategy()
    {
        _rules = new List<ScoringRule>
        {
            // 1. BLOCKER: Unauthorized success on sensitive protected operations.
            new ScoringRule(
                "Blocking Authentication Exposure",
                r => r.SecurityTesting?.AuthenticationTestResult?.TestScenarios?.Any(s =>
                    ValidationCalibration.IsBlockingAuthenticationFailure(s, r.ServerProfile)) == true,
                (ctx, r) =>
                {
                    ctx.HasBlockingFailure = true;
                    ctx.Score = Math.Min(ctx.Score, 20);
                    ctx.Notes.Add($"BLOCKER: Unauthorized success was observed on a sensitive operation for the {r.ServerProfile} profile.");
                }
            ),

            // 2. BLOCKER: Critical vulnerabilities.
            new ScoringRule(
                "Critical Vulnerabilities",
                r => r.SecurityTesting?.Vulnerabilities.Any(v => v.Severity >= VulnerabilitySeverity.Critical) == true,
                (ctx, r) => {
                    ctx.HasBlockingFailure = true;
                    ctx.Score = Math.Min(ctx.Score, 30);
                    ctx.Notes.Add("BLOCKER: Critical security vulnerabilities detected.");
                }
            ),

            // 2b. BLOCKER: security posture collapsed to near-zero.
            new ScoringRule(
                "Severe Security Degradation",
                r => (r.SecurityTesting?.SecurityScore ?? 100) < 25,
                (ctx, r) =>
                {
                    ctx.HasBlockingFailure = true;
                    ctx.Score = Math.Min(ctx.Score, 25);
                    ctx.Notes.Add("BLOCKER: Security posture score is critically low.");
                }
            ),

            // 3. PENALTY: Secure-but-noncanonical auth challenge behavior.
            new ScoringRule(
                "Auth Guidance Gap",
                r => r.SecurityTesting?.AuthenticationTestResult?.TestScenarios?.Any(s =>
                    s.AssessmentDisposition == AuthenticationAssessmentDisposition.SecureCompatible) == true,
                (ctx, r) =>
                {
                    if (!ValidationCalibration.RequiresStrictAuthentication(r.ServerProfile))
                    {
                        ctx.Notes.Add("INFO: Authentication challenge behavior is informational for the declared public profile.");
                        return;
                    }

                    var failures = r.SecurityTesting!.AuthenticationTestResult!.TestScenarios
                        .Count(s => s.AssessmentDisposition == AuthenticationAssessmentDisposition.SecureCompatible);

                    var penalty = Math.Min(20, failures * 5);
                    ctx.Score -= penalty;
                    ctx.Notes.Add($"GUIDANCE: {failures} protected-endpoint authentication scenarios were secure but not fully aligned with the preferred MCP/OAuth challenge flow. Score reduced by {penalty}%.");
                }
            ),

            // 4. BLOCKER/PENALTY: critical protocol violations.
            new ScoringRule(
                "Critical Protocol Violation",
                r => r.ProtocolCompliance?.Violations.Any(v => v.Severity == ViolationSeverity.Critical) == true,
                (ctx, r) =>
                {
                    ctx.HasBlockingFailure = true;
                    ctx.Score = Math.Min(ctx.Score, 40);
                    ctx.Notes.Add("BLOCKER: Critical MCP/JSON-RPC requirement violation detected.");
                }
            ),

            // 5. PENALTY: JSON-RPC format deviations.
            new ScoringRule(
                "JSON-RPC Violation",
                r => r.ProtocolCompliance != null && 
                     (!r.ProtocolCompliance.JsonRpcCompliance.RequestFormatCompliant || 
                      !r.ProtocolCompliance.JsonRpcCompliance.ResponseFormatCompliant),
                (ctx, r) => {
                    ctx.Score -= 15;
                    ctx.Notes.Add("SPEC: JSON-RPC 2.0 format deviations detected. Score reduced by 15%.");
                }
            )
        };
    }

    public ScoringResult CalculateScore(ValidationResult result)
    {
        var scoringResult = new ScoringResult();
        var notes = new List<string>();
        var categoryScores = new Dictionary<string, double>();
        var evidenceSummary = ValidationEvidenceSummarizer.Summarize(result.Evidence.Coverage);

        // 1. Calculate Component Scores (0-100) and capture coverage metadata
        var evaluations = CategoryDefinitions
            .Select(def => def.Evaluate(result))
            .ToList();

        foreach (var evaluation in evaluations)
        {
            categoryScores[evaluation.Name] = evaluation.Score;

            if (!evaluation.IncludeInWeight && evaluation.Status.HasValue)
            {
                AppendLimitedCoverageNote(evaluation.Name, evaluation.Status.Value, notes);
            }
        }

        // 2. Weighted Calculation with Dynamic Adjustment for Coverage
        var includedWeight = evaluations.Where(e => e.IncludeInWeight).Sum(e => e.Weight);
        double weightedScore = 0;

        if (includedWeight > 0)
        {
            foreach (var evaluation in evaluations.Where(e => e.IncludeInWeight))
            {
                double normalizedWeight = evaluation.Weight / includedWeight;
                weightedScore += evaluation.Score * normalizedWeight;
            }
        }
        else
        {
            notes.Add("All categories skipped. Score is 0.");
        }

        var coverageRatio = TotalCategoryWeight > 0 ? includedWeight / TotalCategoryWeight : 0;

        if (coverageRatio < 1 && TotalCategoryWeight > 0)
        {
            var limitedCategories = evaluations
                .Where(e => !e.IncludeInWeight)
                .Select(e => e.Name)
                .Distinct()
                .ToList();

            if (limitedCategories.Count > 0)
            {
                var missingList = string.Join(", ", limitedCategories);
                notes.Add($"Score coverage is partial ({FormatPercent(coverageRatio, "F0")}). Missing, blocked, or inconclusive categories: {missingList}.");
            }
        }

        if (evidenceSummary.ConfidenceLevel < EvidenceConfidenceLevel.High)
        {
            notes.Add($"Evidence confidence is {evidenceSummary.ConfidenceLevel} ({FormatPercent(evidenceSummary.EvidenceConfidenceRatio, "F0")}); score should be interpreted with the coverage summary.");
        }

        // 3. Apply Rule Engine
        var context = new ScoringContext { Score = weightedScore, Notes = notes };

        if (!ValidationCalibration.RequiresStrictAuthentication(result.ServerProfile) &&
            result.SecurityTesting?.AuthenticationTestResult?.TestScenarios?.Count > 0)
        {
            context.Notes.Add("INFO: Authentication challenge observations are informational for the declared public profile.");
        }
        
        foreach (var rule in _rules)
        {
            if (rule.Condition(result))
            {
                rule.Apply(context, result);
                if (context.HasBlockingFailure) break;
            }
        }

        scoringResult.OverallScore = Math.Max(0, Math.Round(context.Score, 2));
        scoringResult.CategoryScores = categoryScores;
        scoringResult.ScoringNotes = context.Notes;
        scoringResult.CoverageRatio = coverageRatio;
        scoringResult.EvidenceSummary = evidenceSummary;

        // Determine Status
        if (context.HasBlockingFailure)
        {
            scoringResult.Status = ValidationStatus.Failed;
        }
        else if (evaluations.All(e => !e.Status.HasValue || IsNotExecuted(e.Status.Value)))
        {
            scoringResult.Status = ValidationStatus.Failed;
        }
        else if (scoringResult.OverallScore >= ScoringConstants.ExcellentThreshold)
        {
            scoringResult.Status = ValidationStatus.Passed;
        }
        else
        {
            scoringResult.Status = ValidationStatus.Passed;
            if (scoringResult.OverallScore < ScoringConstants.PassThreshold)
            {
                notes.Add("Score is below the preferred target, but no blocking failure was observed in this run.");
            }
            else
            {
                notes.Add("Score meets the preferred target with non-blocking improvement opportunities.");
            }
        }

        return scoringResult;
    }

    private class ScoringContext
    {
        public double Score { get; set; }
        public List<string> Notes { get; set; } = new();
        public bool HasBlockingFailure { get; set; }
    }

    private static void AppendLimitedCoverageNote(string categoryName, TestStatus status, List<string> notes)
    {
        if (status == TestStatus.AuthRequired)
        {
            notes.Add($"{categoryName} validation blocked by authentication. Score confidence reduced; rerun with credentials for authoritative coverage.");
            return;
        }

        if (status == TestStatus.Inconclusive)
        {
            notes.Add($"{categoryName} validation was inconclusive. Score confidence reduced; inspect probe evidence before relying on the result.");
            return;
        }

        if (status != TestStatus.Skipped)
        {
            return;
        }

        switch (categoryName)
        {
            case "Tools":
                notes.Add("Tools validation skipped (Auth required). Score coverage reduced accordingly.");
                break;
            case "Resources":
                notes.Add("Resources validation skipped (Auth required). Score coverage reduced accordingly.");
                break;
            case "Prompts":
                notes.Add("Prompts validation skipped (Auth required). Score coverage reduced accordingly.");
                break;
            case "ErrorHandling":
                notes.Add("Error-handling validation skipped. Score coverage reduced accordingly.");
                break;
            case "Performance":
                notes.Add("Performance testing skipped (Auth required). Score coverage reduced accordingly.");
                break;
        }
    }

    private static bool IsNotExecuted(TestStatus status)
    {
        return status is TestStatus.Skipped or TestStatus.AuthRequired or TestStatus.Inconclusive or TestStatus.NotRun;
    }

    private static string FormatPercent(double ratio, string format)
    {
        return $"{(ratio * 100).ToString(format, CultureInfo.InvariantCulture)}%";
    }

    private record ScoringRule(
        string Name, 
        Func<ValidationResult, bool> Condition, 
        Action<ScoringContext, ValidationResult> Apply
    );

    private sealed record CategoryEvaluation(
        string Name,
        double Weight,
        double Score,
        TestStatus? Status,
        bool IncludeInWeight);

    private sealed record ScoringCategoryDefinition(
        string Name,
        double Weight,
        Func<ValidationResult, double> ScoreAccessor,
        Func<ValidationResult, TestStatus?> StatusAccessor)
    {
        public CategoryEvaluation Evaluate(ValidationResult result)
        {
            var score = ScoreAccessor(result);
            var status = StatusAccessor(result);
            var include = status is TestStatus.Passed or TestStatus.Failed;
            return new CategoryEvaluation(Name, Weight, score, status, include);
        }
    }
}
