using Mcp.Benchmark.Core.Models;

namespace Mcp.Benchmark.ClientProfiles;

public interface IClientProfileEvaluator
{
    ClientCompatibilityReport? Evaluate(ValidationResult validationResult, ClientProfileOptions? options);

    IReadOnlyList<ClientProfileDescriptor> GetSupportedProfiles();
}

public sealed class ClientProfileDescriptor
{
    public required string Id { get; init; }

    public required string DisplayName { get; init; }

    public required string Revision { get; init; }

    public required string DocumentationUrl { get; init; }

    public ClientProfileEvidenceBasis EvidenceBasis { get; init; } = ClientProfileEvidenceBasis.Documented;
}