using Mcp.Benchmark.Core.Models;

namespace Mcp.Benchmark.CLI.Abstractions;

public interface IGitHubActionsReporter
{
    void PublishValidationResult(ValidationResult validationResult, IEnumerable<string>? artifactPaths = null);

    void PublishOfflineReport(ValidationResult validationResult, string format, string outputPath);
}