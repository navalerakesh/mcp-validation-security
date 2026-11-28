using System.Collections.Frozen;
using Mcp.Benchmark.Core.Constants;
using Mcp.Benchmark.Core.Services;

namespace Mcp.Benchmark.Core.Models;

public sealed class OperationPolicySnapshot
{
    private OperationPolicySnapshot(ExecutionPolicy policy)
    {
        Mode = policy.Mode;
        ConfirmElevatedRisk = policy.ConfirmElevatedRisk;
        AllowedHosts = policy.AllowedHosts
            .Where(host => !string.IsNullOrWhiteSpace(host))
            .Select(NetworkTargetPolicy.NormalizeHost)
            .ToFrozenSet(StringComparer.OrdinalIgnoreCase);
        AllowedOrigins = policy.AllowedOrigins
            .Where(origin => !string.IsNullOrWhiteSpace(origin))
            .Select(NetworkTargetPolicy.NormalizeOrigin)
            .ToFrozenSet(StringComparer.OrdinalIgnoreCase);
        AllowPrivateAddresses = policy.AllowPrivateAddresses;
        MaxRequests = ValidationRequestBudget.NormalizeLimit(policy.MaxRequests);
        MaxConcurrency = Math.Clamp(
            policy.MaxConcurrency,
            ExecutionPolicyDefaults.MinimumPositiveValue,
            ExecutionPolicyDefaults.MaximumConcurrency);
        TimeoutSeconds = Math.Max(ExecutionPolicyDefaults.MinimumPositiveValue, policy.TimeoutSeconds);
        MaxResponseBytes = Math.Clamp(
            policy.MaxResponseBytes,
            ExecutionPolicyDefaults.MinimumPositiveValue,
            ExecutionPolicyDefaults.MaximumResponseBytes);
        MaxCatalogPayloadCharacters = policy.MaxCatalogPayloadCharacters;
        if (MaxCatalogPayloadCharacters is < 1 or > ExecutionPolicyDefaults.MaximumCatalogPayloadCharacters)
            throw new ArgumentOutOfRangeException(nameof(policy), "Catalog payload budget is outside its supported range.");
    }

    public ExecutionMode Mode { get; }

    public bool ConfirmElevatedRisk { get; }

    public IReadOnlySet<string> AllowedHosts { get; }

    public IReadOnlySet<string> AllowedOrigins { get; }

    public bool AllowPrivateAddresses { get; }

    public int MaxRequests { get; }

    public int MaxConcurrency { get; }

    public int TimeoutSeconds { get; }

    public int MaxResponseBytes { get; }
    public int MaxCatalogPayloadCharacters { get; }

    public static OperationPolicySnapshot From(ExecutionPolicy policy)
    {
        ArgumentNullException.ThrowIfNull(policy);
        return new OperationPolicySnapshot(policy);
    }
}