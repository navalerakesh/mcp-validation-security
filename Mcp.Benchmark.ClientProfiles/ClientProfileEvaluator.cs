using Mcp.Benchmark.Core.Abstractions;
using Mcp.Benchmark.Core.Models;

namespace Mcp.Benchmark.ClientProfiles;

public sealed class ClientProfileEvaluator : IClientProfileEvaluator
{
    private readonly IClientProfileResolver _resolver;
    private readonly IReadOnlyDictionary<string, IClientProfilePack> _packByProfileId;

    public ClientProfileEvaluator()
        : this(CreateDefaultResolver(), CreateDefaultPacks())
    {
    }

    public ClientProfileEvaluator(IClientProfileResolver resolver, IEnumerable<IClientProfilePack> packs)
    {
        _resolver = resolver ?? throw new ArgumentNullException(nameof(resolver));

        var packList = packs?.ToArray() ?? Array.Empty<IClientProfilePack>();
        _packByProfileId = packList
            .SelectMany(pack => pack.GetProfiles().Select(profile => new KeyValuePair<string, IClientProfilePack>(profile.Id, pack)))
            .ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.OrdinalIgnoreCase);
    }

    public ClientCompatibilityReport? Evaluate(ValidationResult validationResult, ClientProfileOptions? options)
    {
        ArgumentNullException.ThrowIfNull(validationResult);

        var applicabilityContext = validationResult.Run.ApplicabilityContext ?? BuildFallbackApplicabilityContext(validationResult, options);
        var requestedProfiles = _resolver.Resolve(options, applicabilityContext);
        if (requestedProfiles.Count == 0)
        {
            return null;
        }

        var resolvedProfiles = requestedProfiles
            .Select(profile => (Profile: profile, Pack: ResolvePack(profile.Descriptor.Id)))
            .ToList();

        RecordAppliedPacks(validationResult, resolvedProfiles.Select(entry => entry.Pack));

        var assessments = resolvedProfiles
            .Select(entry => entry.Pack.Evaluate(entry.Profile.Descriptor, validationResult, applicabilityContext))
            .ToList();

        RecordCompatibilityLayer(validationResult, requestedProfiles, assessments);

        return new ClientCompatibilityReport
        {
            RequestedProfiles = requestedProfiles.Select(profile => profile.Descriptor.Id).ToList(),
            Assessments = assessments
        };
    }

    public IReadOnlyList<ClientProfileDescriptor> GetSupportedProfiles()
    {
        return _resolver.GetSupportedProfiles();
    }

    private IClientProfilePack ResolvePack(string profileId)
    {
        if (_packByProfileId.TryGetValue(profileId, out var pack))
        {
            return pack;
        }

        throw new ArgumentException($"Unknown client profile '{profileId}'. Supported profiles: {string.Join(", ", _resolver.GetSupportedProfileIds())}.", nameof(profileId));
    }

    private static ValidationApplicabilityContext BuildFallbackApplicabilityContext(ValidationResult result, ClientProfileOptions? options)
    {
        var surfaces = new List<string>();
        if ((result.ToolValidation?.ToolsDiscovered ?? result.ToolValidation?.ToolResults.Count ?? 0) > 0)
        {
            surfaces.Add("tools");
        }

        if ((result.PromptTesting?.PromptsDiscovered ?? result.PromptTesting?.PromptResults.Count ?? 0) > 0)
        {
            surfaces.Add("prompts");
        }

        if ((result.ResourceTesting?.ResourcesDiscovered ?? result.ResourceTesting?.ResourceResults.Count ?? 0) > 0)
        {
            surfaces.Add("resources");
        }

        return new ValidationApplicabilityContext
        {
            NegotiatedProtocolVersion = result.ProtocolVersion ?? result.ServerConfig.ProtocolVersion ?? "unknown",
            SchemaVersion = result.Run.SchemaVersion ?? result.ProtocolVersion ?? result.ServerConfig.ProtocolVersion ?? "unknown",
            Transport = string.IsNullOrWhiteSpace(result.ServerConfig.Transport) ? "stdio" : result.ServerConfig.Transport,
            AccessMode = result.ServerProfile.ToString(),
            ServerProfile = result.ServerProfile.ToString(),
            IsAuthenticated = !string.IsNullOrWhiteSpace(result.ServerConfig.Authentication?.Token),
            AdvertisedCapabilities = surfaces,
            AdvertisedSurfaces = surfaces,
            SelectedClientProfiles = options?.Profiles?.Where(value => !string.IsNullOrWhiteSpace(value)).Distinct(StringComparer.OrdinalIgnoreCase).ToArray()
                ?? Array.Empty<string>(),
            EnvironmentHints = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        };
    }

    private static IClientProfilePack[] CreateDefaultPacks()
    {
        return new IClientProfilePack[] { new BuiltInClientProfilePack() };
    }

    private static IClientProfileResolver CreateDefaultResolver()
    {
        return new ClientProfileResolver(CreateDefaultPacks());
    }

    private static void RecordAppliedPacks(ValidationResult validationResult, IEnumerable<IClientProfilePack> packs)
    {
        foreach (var descriptor in packs
            .Select(pack => pack.Descriptor)
            .DistinctBy(descriptor => (descriptor.Key, descriptor.Revision)))
        {
            if (validationResult.Evidence.AppliedPacks.Any(existing => existing.Key.Equals(descriptor.Key) && existing.Revision.Equals(descriptor.Revision)))
            {
                continue;
            }

            validationResult.Evidence.AppliedPacks.Add(descriptor);
        }
    }

    private static void RecordCompatibilityLayer(
        ValidationResult validationResult,
        IReadOnlyList<ResolvedClientProfile> requestedProfiles,
        IReadOnlyList<ClientProfileAssessment> assessments)
    {
        validationResult.Assessments.Layers.RemoveAll(layer => string.Equals(layer.LayerId, "client-profiles", StringComparison.OrdinalIgnoreCase));
        validationResult.Evidence.Coverage.RemoveAll(coverage => string.Equals(coverage.LayerId, "client-profiles", StringComparison.OrdinalIgnoreCase));

        var incompatibleCount = assessments.Count(assessment => assessment.Status == ClientProfileCompatibilityStatus.Incompatible);
        var warningCount = assessments.Count(assessment => assessment.Status == ClientProfileCompatibilityStatus.CompatibleWithWarnings);
        var status = incompatibleCount > 0 ? TestStatus.Failed : TestStatus.Passed;

        validationResult.Assessments.Layers.Add(new ValidationLayerResult
        {
            LayerId = "client-profiles",
            DisplayName = "Client Profile Compatibility",
            Status = status,
            Summary = $"Evaluated {assessments.Count} profile(s); {warningCount} warning profile(s); {incompatibleCount} incompatible profile(s)."
        });

        validationResult.Evidence.Coverage.Add(new ValidationCoverageDeclaration
        {
            LayerId = "client-profiles",
            Scope = string.Join(",", requestedProfiles.Select(profile => profile.Descriptor.Id)),
            Status = ValidationCoverageStatus.Covered,
            Reason = null
        });
    }
}