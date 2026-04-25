using Mcp.Benchmark.Core.Abstractions;
using Mcp.Benchmark.Core.Models;

namespace Mcp.Benchmark.ClientProfiles;

public sealed class ClientProfileResolver : IClientProfileResolver
{
    private readonly IReadOnlyList<IClientProfilePack> _packs;
    private readonly IReadOnlyList<ClientProfileDescriptor> _supportedProfiles;
    private readonly IReadOnlyDictionary<string, IClientProfilePack> _packByProfileId;

    public ClientProfileResolver(IEnumerable<IClientProfilePack> packs)
    {
        _packs = packs?.ToArray() ?? Array.Empty<IClientProfilePack>();
        _supportedProfiles = _packs
            .SelectMany(pack => pack.GetProfiles())
            .DistinctBy(profile => profile.Id, StringComparer.OrdinalIgnoreCase)
            .ToArray();
        _packByProfileId = _packs
            .SelectMany(pack => pack.GetProfiles().Select(profile => new KeyValuePair<string, IClientProfilePack>(profile.Id, pack)))
            .ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.OrdinalIgnoreCase);
    }

    public string AllProfilesToken => ClientProfileCatalog.AllProfilesToken;

    public IReadOnlyList<ResolvedClientProfile> Resolve(ClientProfileOptions? options, ValidationApplicabilityContext applicabilityContext)
    {
        ArgumentNullException.ThrowIfNull(applicabilityContext);

        var requestedProfileIds = NormalizeRequestedProfiles(options);
        return requestedProfileIds
            .Select(profileId => GetDescriptor(profileId))
            .Select(descriptor => new ResolvedClientProfile
            {
                Descriptor = descriptor,
                Revision = new ValidationRevision(descriptor.Revision),
                Stability = ValidationStability.Stable
            })
            .ToArray();
    }

    public IReadOnlyList<ClientProfileDescriptor> GetSupportedProfiles()
    {
        return _supportedProfiles;
    }

    public IReadOnlyList<string> GetSupportedProfileIds()
    {
        return _supportedProfiles.Select(profile => profile.Id).ToArray();
    }

    public IClientProfilePack GetPack(string profileId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(profileId);

        if (_packByProfileId.TryGetValue(profileId, out var pack))
        {
            return pack;
        }

        throw new ArgumentException($"Unknown client profile '{profileId}'. Supported profiles: {string.Join(", ", GetSupportedProfileIds())}.", nameof(profileId));
    }

    private IReadOnlyList<string> NormalizeRequestedProfiles(ClientProfileOptions? options)
    {
        if (options?.Profiles == null || options.Profiles.Count == 0)
        {
            return GetSupportedProfileIds();
        }

        var requested = options.Profiles
            .SelectMany(value => value.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        if (requested.Count == 0 || requested.Any(value => string.Equals(value, AllProfilesToken, StringComparison.OrdinalIgnoreCase)))
        {
            return GetSupportedProfileIds();
        }

        return requested
            .Select(ResolveCanonicalProfileId)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private string ResolveCanonicalProfileId(string requestedProfileId)
    {
        if (_supportedProfiles.Any(profile => string.Equals(profile.Id, requestedProfileId, StringComparison.OrdinalIgnoreCase)))
        {
            return _supportedProfiles.First(profile => string.Equals(profile.Id, requestedProfileId, StringComparison.OrdinalIgnoreCase)).Id;
        }

        var canonicalId = ClientProfileCatalog.ResolveCanonicalProfileId(requestedProfileId);
        if (_supportedProfiles.Any(profile => string.Equals(profile.Id, canonicalId, StringComparison.OrdinalIgnoreCase)))
        {
            return canonicalId;
        }

        throw new ArgumentException($"Unknown client profile '{requestedProfileId}'. Supported profiles: {string.Join(", ", GetSupportedProfileIds())}.", nameof(requestedProfileId));
    }

    private ClientProfileDescriptor GetDescriptor(string profileId)
    {
        return _supportedProfiles.First(profile => string.Equals(profile.Id, profileId, StringComparison.OrdinalIgnoreCase));
    }
}