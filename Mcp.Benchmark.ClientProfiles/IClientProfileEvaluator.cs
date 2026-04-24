using Mcp.Benchmark.Core.Models;

namespace Mcp.Benchmark.ClientProfiles;

public interface IClientProfileEvaluator
{
    ClientCompatibilityReport? Evaluate(ValidationResult validationResult, ClientProfileOptions? options);

    IReadOnlyList<ClientProfileDescriptor> GetSupportedProfiles();
}