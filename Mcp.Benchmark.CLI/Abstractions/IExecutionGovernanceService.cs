using Mcp.Benchmark.CLI.Models;
using Mcp.Benchmark.CLI.Utilities;
using Mcp.Benchmark.Core.Models;

namespace Mcp.Benchmark.CLI.Abstractions;

public interface IExecutionGovernanceService
{
    ExecutionPlan BuildValidationPlan(CliSessionContext sessionContext, McpValidatorConfiguration configuration, string? outputDirectory);

    ExecutionPlan BuildCommandPlan(
        CliSessionContext sessionContext,
        string commandName,
        McpServerConfig serverConfig,
        ExecutionPolicy? executionPolicy,
        string? outputDirectory,
        IReadOnlyList<string> plannedChecks,
        IReadOnlyList<string> plannedArtifacts);

    Task<IReadOnlyList<string>> ValidateTargetResolutionAsync(
        ExecutionPlan plan,
        CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlyList<string>>([
            "The execution governance implementation does not provide target-resolution validation. Active contact is blocked."
        ]);

    AuditManifest BuildAuditManifest(
        ExecutionPlan plan,
        ValidationResult? result,
        IReadOnlyList<string> artifactPaths,
        ModelEvaluationArtifact? modelEvaluationArtifact);
}