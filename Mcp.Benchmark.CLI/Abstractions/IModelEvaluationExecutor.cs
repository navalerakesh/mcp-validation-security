using Mcp.Benchmark.CLI.Models;
using Mcp.Benchmark.Core.Models;

namespace Mcp.Benchmark.CLI.Abstractions;

public interface IModelEvaluationExecutor
{
    Task<ModelEvaluationArtifact> ExecuteAsync(
        ValidationResult validationResult,
        ExecutionPlan executionPlan,
        ModelEvaluationPolicy evaluationPolicy,
        CancellationToken cancellationToken);
}