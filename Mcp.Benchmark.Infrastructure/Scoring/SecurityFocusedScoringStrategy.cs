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
            0.35,
            r => r.ProtocolCompliance?.ComplianceScore ?? 0,
            r => r.ProtocolCompliance?.Status),
        new(
            "Security",
            0.45,
            r => r.SecurityTesting?.SecurityScore ?? 0,
            r => r.SecurityTesting?.Status),
        new(
            "Tools",
            0.10,
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
            0.05,
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
            0.05,
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
        // Performance is INFORMATIONAL — does not impact compliance score.
        // A slow server is not a non-compliant server. Correctness > speed.
        // Performance data is still collected and reported in the Trust Assessment.
        new(
            "Performance",
            0.00,
            r => r.PerformanceTesting?.Status == TestStatus.Passed ? 100.0 : 0.0,
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

        // 1. Calculate Component Scores (0-100) and capture coverage metadata
        var evaluations = CategoryDefinitions
            .Select(def => def.Evaluate(result))
            .ToList();

        foreach (var evaluation in evaluations)
        {
            categoryScores[evaluation.Name] = evaluation.Score;

            if (!evaluation.IncludeInWeight && evaluation.Status == TestStatus.Skipped)
            {
                AppendSkipNote(evaluation.Name, notes);
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

        // Enterprise fairness: enforce minimum coverage threshold.
        // A server that only passes Security + Protocol (70% weight) should not score
        // higher than a server that passes all 6 categories. The coverage ratio
        // transparently communicates that untested categories reduce the ceiling.
        // Additionally, if fewer than 50% of categories were tested, cap score at 60%.
        if (coverageRatio < ScoringConstants.MinCoverageRatio && weightedScore > ScoringConstants.LowCoverageScoreCap)
        {
            notes.Add($"Coverage below {ScoringConstants.MinCoverageRatio:P0} ({coverageRatio:P0}). Score capped at {ScoringConstants.LowCoverageScoreCap}% \u2014 most categories were not validated.");
            weightedScore = Math.Min(weightedScore, ScoringConstants.LowCoverageScoreCap);
        }

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
                notes.Add($"Coverage multiplier applied ({coverageRatio:P0}). Missing or skipped categories: {missingList}.");
            }
        }

        // 3. Apply Rule Engine
        var context = new ScoringContext { Score = weightedScore * coverageRatio, Notes = notes };

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

        // Determine Status
        if (context.HasBlockingFailure)
        {
            scoringResult.Status = ValidationStatus.Failed;
        }
        else if (evaluations.All(e => !e.Status.HasValue || e.Status == TestStatus.Skipped))
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

    private static void AppendSkipNote(string categoryName, List<string> notes)
    {
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
            case "Performance":
                notes.Add("Performance testing skipped (Auth required). Score coverage reduced accordingly.");
                break;
        }
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
            var include = status.HasValue && status != TestStatus.Skipped;
            return new CategoryEvaluation(Name, Weight, score, status, include);
        }
    }
}
