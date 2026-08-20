using Mcp.Benchmark.Core.Abstractions;
using Mcp.Benchmark.Core.Models;
using Mcp.Compliance.Spec;

namespace Mcp.Benchmark.Infrastructure.Registries;

public sealed class BuiltInProtocolFeaturePack : IProtocolFeaturePack
{
    private readonly ISchemaRegistry _schemaRegistry;

    public BuiltInProtocolFeaturePack(ISchemaRegistry schemaRegistry)
    {
        _schemaRegistry = schemaRegistry ?? throw new ArgumentNullException(nameof(schemaRegistry));
    }

    public ValidationPackDescriptor Descriptor => new()
    {
        Key = new ValidationDescriptorKey("protocol-features/mcp-embedded"),
        Kind = ValidationPackKind.ProtocolFeatures,
        Revision = new ValidationRevision("2026-04"),
        DisplayName = "Embedded MCP Protocol Features",
        Stability = ValidationStability.Stable,
        DocumentationUrl = "https://spec.modelcontextprotocol.io/"
    };

    public ValidationApplicability Applicability => new();

    public ProtocolFeatureSet BuildFeatureSet(ValidationApplicabilityContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        var schemaVersion = SchemaRegistryProtocolVersions.ResolveSchemaVersion(context.SchemaVersion, _schemaRegistry).Value;
        var isHttpTransport = string.Equals(context.Transport, "http", StringComparison.OrdinalIgnoreCase)
            || string.Equals(context.Transport, "https", StringComparison.OrdinalIgnoreCase);

        return schemaVersion switch
        {
            "2026-07-28" => CreateModernFeatureSet(context.NegotiatedProtocolVersion, schemaVersion, isHttpTransport),
            "2025-11-25" => CreateLegacyFeatureSet(context.NegotiatedProtocolVersion, schemaVersion, isHttpTransport, listChanged: true, tasks: true, deferred: true, batchJsonRpc: false),
            "2025-06-18" => CreateLegacyFeatureSet(context.NegotiatedProtocolVersion, schemaVersion, isHttpTransport, listChanged: true, tasks: false, deferred: false, batchJsonRpc: false),
            "2025-03-26" => CreateLegacyFeatureSet(context.NegotiatedProtocolVersion, schemaVersion, isHttpTransport, listChanged: true, tasks: false, deferred: false, batchJsonRpc: true),
            _ => CreateLegacyFeatureSet(context.NegotiatedProtocolVersion, schemaVersion, isHttpTransport, listChanged: false, tasks: false, deferred: false, batchJsonRpc: false)
        };
    }

    private static ProtocolFeatureSet CreateModernFeatureSet(
        string negotiatedProtocolVersion,
        string schemaVersion,
        bool isHttpTransport)
    {
        return new ProtocolFeatureSet
        {
            NegotiatedProtocolVersion = negotiatedProtocolVersion,
            SchemaVersion = schemaVersion,
            Era = McpProtocolEra.Modern,
            RequiresHttpProtocolHeader = isHttpTransport,
            SupportsToolListChangedNotifications = true,
            SupportsTasksSurface = true,
            SupportsDeferredWorkflows = true,
            SupportsBatchJsonRpc = false,
            SupportsServerDiscovery = true,
            UsesPerRequestMetadata = true,
            IsSessionless = true,
            SupportsSubscriptionsListen = true,
            RequiresCacheMetadata = true,
            SupportsMultiRoundTripRequests = true,
            SupportsExtensionNegotiation = true,
            UsesTasksExtension = true,
            OptionalCapabilities = new[] { "completions", "extensions" },
            FeatureLifecycle = new Dictionary<string, ProtocolFeatureLifecycle>(StringComparer.Ordinal)
            {
                ["logging/setLevel"] = ProtocolFeatureLifecycle.Removed,
                ["resources/subscribe"] = ProtocolFeatureLifecycle.Removed,
                ["resources/unsubscribe"] = ProtocolFeatureLifecycle.Removed
            }
        };
    }

    private static ProtocolFeatureSet CreateLegacyFeatureSet(
        string negotiatedProtocolVersion,
        string schemaVersion,
        bool isHttpTransport,
        bool listChanged,
        bool tasks,
        bool deferred,
        bool batchJsonRpc)
    {
        return new ProtocolFeatureSet
        {
            NegotiatedProtocolVersion = negotiatedProtocolVersion,
            SchemaVersion = schemaVersion,
            Era = McpProtocolEra.Legacy,
            RequiresHttpProtocolHeader = isHttpTransport,
            SupportsToolListChangedNotifications = listChanged,
            SupportsTasksSurface = tasks,
            SupportsDeferredWorkflows = deferred,
            SupportsBatchJsonRpc = batchJsonRpc,
            OptionalCapabilities = new[] { "roots", "logging", "sampling", "completions" },
            FeatureLifecycle = new Dictionary<string, ProtocolFeatureLifecycle>(StringComparer.Ordinal)
            {
                ["logging/setLevel"] = schemaVersion == "2025-11-25"
                    ? ProtocolFeatureLifecycle.Deprecated
                    : ProtocolFeatureLifecycle.Active,
                ["resources/subscribe"] = ProtocolFeatureLifecycle.Active,
                ["resources/unsubscribe"] = ProtocolFeatureLifecycle.Active
            }
        };
    }
}