using Mcp.Benchmark.Core.Models;

namespace Mcp.Benchmark.Core.Abstractions;

public interface IValidationPack
{
    ValidationPackDescriptor Descriptor { get; }

    ValidationApplicability Applicability { get; }
}

public interface IValidationPackRegistry<TPack> where TPack : IValidationPack
{
    IReadOnlyList<TPack> GetAll();

    IReadOnlyList<TPack> Resolve(ValidationApplicabilityContext context);
}

public interface IValidationApplicabilityResolver
{
    ValidationApplicabilityContext Build(
        ValidationSessionContext session,
        McpValidatorConfiguration configuration,
        IReadOnlyList<string> selectedClientProfiles);

    ValidationApplicabilityContext Build(
        McpServerConfig serverConfig,
        string? protocolVersion,
        IReadOnlyList<string> selectedClientProfiles,
        TransportResult<CapabilitySummary>? capabilitySnapshot = null,
        bool isAuthenticated = false,
        string? accessMode = null,
        string? serverProfile = null);
}

public interface IProtocolFeaturePack : IValidationPack
{
    ProtocolFeatureSet BuildFeatureSet(ValidationApplicabilityContext context);
}

public interface IProtocolFeatureResolver
{
    ProtocolFeatureSet Resolve(ValidationApplicabilityContext context);
}

public interface IVersionedValidationRule<TContext> : IValidationRule<TContext>
{
    ValidationRuleDescriptor Descriptor { get; }

    ValidationApplicability Applicability { get; }
}

public interface IValidationRulePack<TContext> : IValidationPack
{
    IReadOnlyList<IVersionedValidationRule<TContext>> GetRules();
}

public interface IValidationScenario
{
    ValidationScenarioDescriptor Descriptor { get; }

    Task<ValidationScenarioExecutionResult> ExecuteAsync(
        ValidationScenarioContext context,
        CancellationToken cancellationToken);
}

public interface IValidationScenarioPack : IValidationPack
{
    IReadOnlyList<IValidationScenario> GetScenarios();
}

public sealed class ValidationScenarioDescriptor
{
    public required ValidationDescriptorKey Key { get; init; }

    public required ValidationRevision Revision { get; init; }

    public required string DisplayName { get; init; }

    public required string LayerKey { get; init; }

    public required ValidationStability Stability { get; init; }

    public bool MutatesState { get; init; }
}

public interface IClientProfilePack : IValidationPack
{
    IReadOnlyList<ClientProfileDescriptor> GetProfiles();

    ClientProfileAssessment Evaluate(
        ClientProfileDescriptor profile,
        ValidationResult validationResult,
        ValidationApplicabilityContext applicabilityContext);
}

public interface IClientProfileResolver
{
    string AllProfilesToken { get; }

    IReadOnlyList<ResolvedClientProfile> Resolve(
        ClientProfileOptions? options,
        ValidationApplicabilityContext applicabilityContext);

    IReadOnlyList<ClientProfileDescriptor> GetSupportedProfiles();

    IReadOnlyList<string> GetSupportedProfileIds();
}