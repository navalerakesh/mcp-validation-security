using Mcp.Benchmark.Core.Abstractions;
using Mcp.Benchmark.Core.Constants;
using Mcp.Benchmark.Core.Models;

namespace Mcp.Benchmark.Infrastructure.Scoring;

/// <summary>
/// Implements a scoring strategy that prioritizes security and protocol compliance.
/// Uses a rule-based engine to distinguish between critical security breaches and protocol violations.
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
            // 1. CRITICAL: Data Breach (Unauthorized Access)
            // If a test that expects rejection (e.g. "No Auth") gets a 200 OK, it's a breach.
            new ScoringRule(
                "Security Breach",
                r => r.SecurityTesting?.AuthenticationTestResult?.TestScenarios?.Any(s => 
                    s.TestType != "Valid Token" && 
                    (s.StatusCode == "200" || s.StatusCode.StartsWith("2"))) == true,
                (ctx, r) =>
                {
                    if (RequiresStrictAuthentication(r))
                    {
                        ctx.Score = 0;
                        ctx.IsCriticalFailure = true;
                        ctx.Notes.Add($"CRITICAL: Security Breach - Unauthorized access allowed (HTTP 200 OK) for {r.ServerProfile} profile.");
                    }
                    else
                    {
                        ctx.Notes.Add("INFO: Security breach scenario observed but treated as informational for non-authenticated profile.");
                    }
                }
            ),

            // 2. CRITICAL: High Severity Vulnerabilities
            // For strict profiles (Authenticated/Enterprise), treat High+ as critical.
            // For public/anonymous profiles, only Critical vulns are treated as hard-fail;
            // High severity issues are reflected in the SecurityScore but do not zero the overall score.
            new ScoringRule(
                "Critical Vulnerabilities",
                r => r.SecurityTesting?.Vulnerabilities.Any(v =>
                        v.Severity >= VulnerabilitySeverity.Critical ||
                        (v.Severity >= VulnerabilitySeverity.High && RequiresStrictAuthentication(r))) == true,
                (ctx, r) => {
                    ctx.Score = 0;
                    ctx.IsCriticalFailure = true;
                    ctx.Notes.Add("CRITICAL: High/Critical security vulnerabilities detected.");
                }
            ),

            // 3. PENALTY: Auth Protocol Violations (Missing Headers)
            // If a test failed compliance but was NOT a breach (i.e., it was rejected with 4xx), it's a protocol issue.
            new ScoringRule(
                "Auth Protocol Violation",
                r => r.SecurityTesting?.AuthenticationTestResult?.TestScenarios?.Any(s => !s.IsCompliant && s.StatusCode != "200") == true,
                (ctx, r) =>
                {
                    if (!RequiresStrictAuthentication(r))
                    {
                        ctx.Notes.Add("INFO: Authentication protocol violations reported for non-authenticated profile; no score impact.");
                        return;
                    }

                    var failures = r.SecurityTesting!.AuthenticationTestResult!.TestScenarios
                        .Count(s => !s.IsCompliant && s.StatusCode != "200");

                    var penalty = Math.Min(20, failures * 5);
                    ctx.Score -= penalty;
                    ctx.Notes.Add($"WARNING: {failures} authentication protocol violations (missing headers). Score reduced by {penalty}%.");
                }
            ),

            // 4. PENALTY: JSON-RPC Format Violations
            new ScoringRule(
                "JSON-RPC Violation",
                r => r.ProtocolCompliance != null && 
                     (!r.ProtocolCompliance.JsonRpcCompliance.RequestFormatCompliant || 
                      !r.ProtocolCompliance.JsonRpcCompliance.ResponseFormatCompliant),
                (ctx, r) => {
                    ctx.Score -= 15;
                    ctx.Notes.Add("WARNING: JSON-RPC 2.0 format violations detected. Score reduced by 15%.");
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
        
        foreach (var rule in _rules)
        {
            if (rule.Condition(result))
            {
                rule.Apply(context, result);
                if (context.IsCriticalFailure) break; // Stop on critical failure
            }
        }

        scoringResult.OverallScore = Math.Max(0, Math.Round(context.Score, 2));
        scoringResult.CategoryScores = categoryScores;
        scoringResult.ScoringNotes = context.Notes;
        scoringResult.CoverageRatio = coverageRatio;

        // Determine Status
        if (context.IsCriticalFailure)
        {
            scoringResult.Status = ValidationStatus.Failed;
        }
        else if (scoringResult.OverallScore >= ScoringConstants.ExcellentThreshold)
        {
            scoringResult.Status = ValidationStatus.Passed;
        }
        else if (scoringResult.OverallScore >= ScoringConstants.PassThreshold)
        {
            // Warning / Acceptable range
            scoringResult.Status = ValidationStatus.Passed; 
            notes.Add("Score meets minimum requirements but improvements are recommended.");
        }
        else
        {
            scoringResult.Status = ValidationStatus.Failed;
        }

        return scoringResult;
    }

    private class ScoringContext
    {
        public double Score { get; set; }
        public List<string> Notes { get; set; } = new();
        public bool IsCriticalFailure { get; set; }
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

    private static bool RequiresStrictAuthentication(ValidationResult result)
    {
        return result.ServerProfile is McpServerProfile.Authenticated or McpServerProfile.Enterprise;
    }
}
