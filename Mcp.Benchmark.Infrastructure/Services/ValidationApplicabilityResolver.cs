using Mcp.Benchmark.Core.Abstractions;
using Mcp.Benchmark.Core.Models;
using Mcp.Compliance.Spec;

namespace Mcp.Benchmark.Infrastructure.Services;

public sealed class ValidationApplicabilityResolver : IValidationApplicabilityResolver
{
    private readonly ISchemaRegistry _schemaRegistry;

    public ValidationApplicabilityResolver(ISchemaRegistry schemaRegistry)
    {
        _schemaRegistry = schemaRegistry ?? throw new ArgumentNullException(nameof(schemaRegistry));
    }

    public ValidationApplicabilityContext Build(
        ValidationSessionContext session,
        McpValidatorConfiguration configuration,
        IReadOnlyList<string> selectedClientProfiles)
    {
        ArgumentNullException.ThrowIfNull(session);
        ArgumentNullException.ThrowIfNull(configuration);

        return Build(
            session.EffectiveServer,
            session.ProtocolVersion ?? session.EffectiveServer.ProtocolVersion ?? configuration.Server.ProtocolVersion,
            selectedClientProfiles,
            session.CapabilitySnapshot,
            session.AuthDiscovery != null,
            session.ServerProfile.ToString(),
            session.ServerProfile.ToString());
    }

    public ValidationApplicabilityContext Build(
        McpServerConfig serverConfig,
        string? protocolVersion,
        IReadOnlyList<string> selectedClientProfiles,
        TransportResult<CapabilitySummary>? capabilitySnapshot = null,
        bool isAuthenticated = false,
        string? accessMode = null,
        string? serverProfile = null)
    {
        ArgumentNullException.ThrowIfNull(serverConfig);

        var normalizedVersion = SchemaRegistryProtocolVersions.NormalizeRequestedVersion(protocolVersion ?? serverConfig.ProtocolVersion, _schemaRegistry);
        var schemaVersion = SchemaRegistryProtocolVersions.ResolveSchemaVersion(normalizedVersion, _schemaRegistry).Value;
        var transport = string.IsNullOrWhiteSpace(serverConfig.Transport) ? "stdio" : serverConfig.Transport;
        var normalizedProfiles = selectedClientProfiles?.Where(value => !string.IsNullOrWhiteSpace(value)).Distinct(StringComparer.OrdinalIgnoreCase).ToArray()
            ?? Array.Empty<string>();

        return new ValidationApplicabilityContext
        {
            NegotiatedProtocolVersion = normalizedVersion,
            SchemaVersion = schemaVersion,
            Transport = transport,
            AccessMode = string.IsNullOrWhiteSpace(accessMode) ? serverConfig.Profile.ToString() : accessMode,
            ServerProfile = string.IsNullOrWhiteSpace(serverProfile) ? serverConfig.Profile.ToString() : serverProfile,
            IsAuthenticated = isAuthenticated || !string.IsNullOrWhiteSpace(serverConfig.Authentication?.Token),
            AdvertisedCapabilities = ExtractCapabilities(capabilitySnapshot),
            AdvertisedSurfaces = ExtractSurfaces(capabilitySnapshot),
            SelectedClientProfiles = normalizedProfiles,
            EnvironmentHints = BuildEnvironmentHints(serverConfig)
        };
    }

    private static IReadOnlyList<string> ExtractCapabilities(TransportResult<CapabilitySummary>? capabilitySnapshot)
    {
        if (capabilitySnapshot?.Payload == null)
        {
            return Array.Empty<string>();
        }

        var capabilities = new List<string>();
        if (capabilitySnapshot.Payload.CapabilityDeclarationsAvailable)
        {
            return capabilitySnapshot.Payload.AdvertisedCapabilities;
        }

        if (capabilitySnapshot.Payload.ToolListingSucceeded)
        {
            capabilities.Add("tools");
        }

        if (capabilitySnapshot.Payload.ResourceListingSucceeded)
        {
            capabilities.Add("resources");
        }

        if (capabilitySnapshot.Payload.PromptListingSucceeded)
        {
            capabilities.Add("prompts");
        }

        return capabilities;
    }

    private static IReadOnlyList<string> ExtractSurfaces(TransportResult<CapabilitySummary>? capabilitySnapshot)
    {
        if (capabilitySnapshot?.Payload == null)
        {
            return Array.Empty<string>();
        }

        var surfaces = new List<string>();
        if (capabilitySnapshot.Payload.DiscoveredToolsCount > 0)
        {
            surfaces.Add("tools");
        }

        if (capabilitySnapshot.Payload.DiscoveredResourcesCount > 0)
        {
            surfaces.Add("resources");
        }

        if (capabilitySnapshot.Payload.DiscoveredPromptsCount > 0)
        {
            surfaces.Add("prompts");
        }

        return surfaces;
    }

    private static IReadOnlyDictionary<string, string> BuildEnvironmentHints(McpServerConfig serverConfig)
    {
        var hints = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        if (!string.IsNullOrWhiteSpace(serverConfig.Endpoint))
        {
            hints["endpointKind"] = Uri.TryCreate(serverConfig.Endpoint, UriKind.Absolute, out _) ? "remote" : "local";
        }

        if (!string.IsNullOrWhiteSpace(serverConfig.Transport))
        {
            hints["transport"] = serverConfig.Transport;
        }

        return hints;
    }
}