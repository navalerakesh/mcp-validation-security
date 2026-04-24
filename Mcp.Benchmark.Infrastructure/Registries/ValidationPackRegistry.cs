using Mcp.Benchmark.Core.Abstractions;
using Mcp.Benchmark.Core.Models;

namespace Mcp.Benchmark.Infrastructure.Registries;

public sealed class ValidationPackRegistry<TPack> : IValidationPackRegistry<TPack> where TPack : IValidationPack
{
    private readonly IReadOnlyList<TPack> _packs;

    public ValidationPackRegistry(IEnumerable<TPack> packs)
    {
        _packs = packs?.ToArray() ?? Array.Empty<TPack>();
    }

    public IReadOnlyList<TPack> GetAll()
    {
        return _packs;
    }

    public IReadOnlyList<TPack> Resolve(ValidationApplicabilityContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        return _packs
            .Where(pack => ValidationPackApplicabilityMatcher.Matches(pack.Applicability, context))
            .ToArray();
    }
}

internal static class ValidationPackApplicabilityMatcher
{
    public static bool Matches(ValidationApplicability applicability, ValidationApplicabilityContext context)
    {
        ArgumentNullException.ThrowIfNull(applicability);
        ArgumentNullException.ThrowIfNull(context);

        return MatchesAny(applicability.ProtocolVersions, context.NegotiatedProtocolVersion)
            && MatchesAny(applicability.SchemaVersions, context.SchemaVersion)
            && MatchesAny(applicability.Transports, context.Transport)
            && MatchesAny(applicability.AccessModes, context.AccessMode)
            && MatchesAll(applicability.RequiredCapabilities, context.AdvertisedCapabilities)
            && MatchesAll(applicability.RequiredSurfaces, context.AdvertisedSurfaces)
            && MatchesRequestedProfiles(applicability.ClientProfiles, context.SelectedClientProfiles)
            && MatchesAuthentication(applicability.RequiresAuthentication, context.IsAuthenticated);
    }

    private static bool MatchesAny(IReadOnlyList<string> allowed, string actual)
    {
        return allowed.Count == 0 || allowed.Contains(actual, StringComparer.OrdinalIgnoreCase);
    }

    private static bool MatchesAll(IReadOnlyList<string> required, IReadOnlyList<string> available)
    {
        if (required.Count == 0)
        {
            return true;
        }

        var availableSet = new HashSet<string>(available, StringComparer.OrdinalIgnoreCase);
        return required.All(availableSet.Contains);
    }

    private static bool MatchesRequestedProfiles(IReadOnlyList<string> requiredProfiles, IReadOnlyList<string> selectedProfiles)
    {
        if (requiredProfiles.Count == 0 || selectedProfiles.Count == 0)
        {
            return true;
        }

        var selectedSet = new HashSet<string>(selectedProfiles, StringComparer.OrdinalIgnoreCase);
        return requiredProfiles.Any(selectedSet.Contains);
    }

    private static bool MatchesAuthentication(bool? requiredAuthentication, bool isAuthenticated)
    {
        return requiredAuthentication == null || requiredAuthentication.Value == isAuthenticated;
    }
}