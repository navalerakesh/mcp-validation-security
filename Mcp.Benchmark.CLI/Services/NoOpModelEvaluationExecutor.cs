using Mcp.Benchmark.CLI.Abstractions;
using Mcp.Benchmark.CLI.Exceptions;
using Mcp.Benchmark.CLI.Models;
using Mcp.Benchmark.Core.Models;

namespace Mcp.Benchmark.CLI.Services;

public sealed class NoOpModelEvaluationExecutor : IModelEvaluationExecutor
{
    public Task<ModelEvaluationArtifact> ExecuteAsync(
        ValidationResult validationResult,
        ExecutionPlan executionPlan,
        ModelEvaluationPolicy evaluationPolicy,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(validationResult);
        ArgumentNullException.ThrowIfNull(executionPlan);
        ArgumentNullException.ThrowIfNull(evaluationPolicy);

        var provider = string.IsNullOrWhiteSpace(evaluationPolicy.Provider)
            ? "none"
            : evaluationPolicy.Provider.Trim();

        if (string.Equals(provider, "none", StringComparison.OrdinalIgnoreCase))
        {
            throw new CliUsageException("Model evaluation is enabled, but no provider was configured. Set evaluation.modelEvaluation.provider to 'builtin-rubric' or another supported provider.");
        }

        if (!IsBuiltInProvider(provider))
        {
            throw new CliUsageException($"Model evaluation provider '{provider}' is not registered in this build. Supported providers: builtin-rubric.");
        }

        var advisoryNotes = BuildAdvisoryNotes(validationResult);
        var relatedFindings = BuildRelatedDeterministicFindings(validationResult);
        var blockingCount = validationResult.VerdictAssessment?.BlockingDecisions.Count ?? 0;

        return Task.FromResult(new ModelEvaluationArtifact
        {
            ValidationId = validationResult.ValidationId,
            SessionId = executionPlan.SessionId,
            Provider = "builtin-rubric",
            Model = string.IsNullOrWhiteSpace(evaluationPolicy.Model) ? "builtin-rubric-v1" : evaluationPolicy.Model,
            PromptSet = string.IsNullOrWhiteSpace(evaluationPolicy.PromptSet) ? "builtin-default" : evaluationPolicy.PromptSet,
            Status = ModelEvaluationArtifactStatus.Completed,
            Summary = BuildSummary(validationResult, blockingCount),
            BaselineVerdict = validationResult.VerdictAssessment?.BaselineVerdict,
            AdvisoryNotes = advisoryNotes,
            RelatedDeterministicFindings = relatedFindings
        });
    }

    private static bool IsBuiltInProvider(string provider)
    {
        return string.Equals(provider, "builtin-rubric", StringComparison.OrdinalIgnoreCase)
            || string.Equals(provider, "builtin", StringComparison.OrdinalIgnoreCase)
            || string.Equals(provider, "rubric", StringComparison.OrdinalIgnoreCase);
    }

    private static string BuildSummary(ValidationResult validationResult, int blockingCount)
    {
        var verdictLabel = validationResult.VerdictAssessment?.BaselineVerdict.ToString() ?? "Unknown";
        return $"Built-in rubric evaluation completed. Baseline verdict={verdictLabel}; blocking decisions={blockingCount}. Experimental companion output only.";
    }

    private static IReadOnlyList<string> BuildAdvisoryNotes(ValidationResult validationResult)
    {
        var notes = new List<string>
        {
            "Experimental model evaluation is stored as a companion artifact and never alters the deterministic baseline verdict."
        };

        if (!string.IsNullOrWhiteSpace(validationResult.VerdictAssessment?.Summary))
        {
            notes.Add(validationResult.VerdictAssessment.Summary);
        }

        foreach (var decision in validationResult.VerdictAssessment?.BlockingDecisions.Take(3) ?? Array.Empty<DecisionRecord>())
        {
            notes.Add($"{decision.Category}/{decision.Component}: {decision.Summary}");
        }

        if (validationResult.ClientCompatibility?.Assessments is { Count: > 0 } assessments)
        {
            foreach (var assessment in assessments.Take(2))
            {
                notes.Add($"Client profile {assessment.ProfileId}: {assessment.Status}.");
            }
        }

        var aiFindings = validationResult.ToolValidation?.AiReadinessFindings ?? new List<ValidationFinding>();
        foreach (var finding in aiFindings.Take(3))
        {
            var evidenceKind = finding.Metadata.TryGetValue(AiReadinessEvidenceKinds.MetadataKey, out var value)
                ? value
                : null;
            notes.Add($"Related deterministic AI-readiness finding {finding.RuleId}: {AiReadinessEvidenceKinds.ToDisplayLabel(evidenceKind, finding.RuleId)} for {finding.Component}.");
        }

        if (notes.Count == 1)
        {
            notes.Add("No blocking decisions were present in the deterministic baseline result.");
        }

        return notes;
    }

    private static IReadOnlyList<ModelEvaluationFindingLink> BuildRelatedDeterministicFindings(ValidationResult validationResult)
    {
        return (validationResult.ToolValidation?.AiReadinessFindings ?? new List<ValidationFinding>())
            .Where(finding => !string.IsNullOrWhiteSpace(finding.RuleId))
            .OrderByDescending(finding => finding.Severity)
            .ThenBy(finding => finding.RuleId, StringComparer.OrdinalIgnoreCase)
            .Take(20)
            .Select(finding =>
            {
                var evidenceKind = finding.Metadata.TryGetValue(AiReadinessEvidenceKinds.MetadataKey, out var value)
                    ? value
                    : AiReadinessEvidenceKinds.Infer(null, finding.RuleId);

                return new ModelEvaluationFindingLink
                {
                    RuleId = finding.RuleId,
                    Category = finding.Category,
                    Component = finding.Component,
                    EvidenceKind = evidenceKind,
                    Summary = finding.Summary,
                    Recommendation = finding.Recommendation
                };
            })
            .ToList();
    }
}