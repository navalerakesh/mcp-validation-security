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

    AuditManifest BuildAuditManifest(
        ExecutionPlan plan,
        ValidationResult? result,
        IReadOnlyList<string> artifactPaths,
        ModelEvaluationArtifact? modelEvaluationArtifact);
}