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
            "2025-11-25" => CreateFeatureSet(context.NegotiatedProtocolVersion, schemaVersion, isHttpTransport, listChanged: true, tasks: true, deferred: true),
            "2025-06-18" => CreateFeatureSet(context.NegotiatedProtocolVersion, schemaVersion, isHttpTransport, listChanged: true, tasks: false, deferred: false),
            "2025-03-26" => CreateFeatureSet(context.NegotiatedProtocolVersion, schemaVersion, isHttpTransport, listChanged: true, tasks: false, deferred: false),
            _ => CreateFeatureSet(context.NegotiatedProtocolVersion, schemaVersion, isHttpTransport, listChanged: false, tasks: false, deferred: false)
        };
    }

    private static ProtocolFeatureSet CreateFeatureSet(
        string negotiatedProtocolVersion,
        string schemaVersion,
        bool isHttpTransport,
        bool listChanged,
        bool tasks,
        bool deferred)
    {
        return new ProtocolFeatureSet
        {
            NegotiatedProtocolVersion = negotiatedProtocolVersion,
            SchemaVersion = schemaVersion,
            RequiresHttpProtocolHeader = isHttpTransport,
            SupportsToolListChangedNotifications = listChanged,
            SupportsTasksSurface = tasks,
            SupportsDeferredWorkflows = deferred,
            OptionalCapabilities = new[] { "roots", "logging", "sampling", "completions" }
        };
    }
}