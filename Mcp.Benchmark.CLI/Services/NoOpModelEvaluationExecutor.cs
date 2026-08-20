using Mcp.Benchmark.CLI.Abstractions;
using Mcp.Benchmark.CLI.Exceptions;
using Mcp.Benchmark.CLI.Models;
using Mcp.Benchmark.Core.Models;

namespace Mcp.Benchmark.CLI.Services;

public sealed class NoOpModelEvaluationExecutor : IModelEvaluationExecutor
{
    public Task<ModelEvaluationArtifact> ExecuteAsync(
        ModelEvaluationInput input,
        ExecutionPlan executionPlan,
        ModelEvaluationPolicy evaluationPolicy,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(input);
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

        var advisoryNotes = BuildAdvisoryNotes(input);
        var relatedFindings = BuildRelatedDeterministicFindings(input);
        var blockingCount = input.BlockingDecisions.Count;

        return Task.FromResult(new ModelEvaluationArtifact
        {
            ValidationId = input.ValidationId,
            SessionId = executionPlan.SessionId,
            Provider = "builtin-rubric",
            Model = string.IsNullOrWhiteSpace(evaluationPolicy.Model) ? "builtin-rubric-v1" : evaluationPolicy.Model,
            PromptSet = string.IsNullOrWhiteSpace(evaluationPolicy.PromptSet) ? "builtin-default" : evaluationPolicy.PromptSet,
            Status = ModelEvaluationArtifactStatus.Completed,
            Cost = new ModelEvaluationCost
            {
                InputTokens = 0,
                OutputTokens = 0,
                Amount = 0,
                Currency = "USD",
                Estimated = false
            },
            Summary = BuildSummary(input, blockingCount),
            BaselineVerdict = input.BaselineVerdict,
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

    private static string BuildSummary(ModelEvaluationInput input, int blockingCount)
    {
        return $"Built-in rubric evaluation completed. Baseline verdict={input.BaselineVerdict}; blocking decisions={blockingCount}. Experimental companion output only.";
    }

    private static IReadOnlyList<string> BuildAdvisoryNotes(ModelEvaluationInput input)
    {
        var notes = new List<string>
        {
            "Experimental model evaluation is stored as a companion artifact and never alters the deterministic baseline verdict."
        };

        foreach (var decision in input.BlockingDecisions.Take(3))
        {
            notes.Add($"Decision {decision.DecisionId}: {decision.Category}/{decision.Component}; gate={decision.Gate}; severity={decision.Severity}.");
        }

        foreach (var assessment in input.ClientProfiles.Take(2))
        {
            notes.Add($"Client profile {assessment.ProfileId}: {assessment.Status}.");
        }

        foreach (var finding in input.AiFindings.Take(3))
        {
            notes.Add($"Related deterministic AI-readiness finding {finding.RuleId}: {finding.EvidenceKind} for {finding.Component}.");
        }

        if (notes.Count == 1)
        {
            notes.Add("No blocking decisions were present in the deterministic baseline result.");
        }

        return notes;
    }

    private static IReadOnlyList<ModelEvaluationFindingLink> BuildRelatedDeterministicFindings(ModelEvaluationInput input)
    {
        return input.AiFindings
            .Where(finding => !string.IsNullOrWhiteSpace(finding.RuleId))
            .OrderByDescending(finding => finding.Severity)
            .ThenBy(finding => finding.RuleId, StringComparer.OrdinalIgnoreCase)
            .Take(20)
            .Select(finding => new ModelEvaluationFindingLink
            {
                RuleId = finding.RuleId,
                Category = finding.Category,
                Component = finding.Component,
                EvidenceKind = finding.EvidenceKind,
                Summary = $"Deterministic finding {finding.RuleId} ({finding.Severity}).",
                Recommendation = null
            })
            .ToList();
    }
}