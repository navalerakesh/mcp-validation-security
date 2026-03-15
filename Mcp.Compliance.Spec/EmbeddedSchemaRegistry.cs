using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;

namespace Mcp.Compliance.Spec;

/// <summary>
/// Default schema registry backed by embedded JSON resources.
///
/// This implementation discovers schemas from the assembly's embedded resources
/// under the <c>schema/</c> folder and maps them by protocol version, area, and name.
/// </summary>
public sealed class EmbeddedSchemaRegistry : ISchemaRegistry
{
    private readonly IReadOnlyDictionary<(string Version, string Area, string Name), (SchemaDescriptor Descriptor, string ResourceName)> _schemas;

    /// <summary>
    /// Creates a registry that automatically discovers all embedded JSON schemas
    /// included in this assembly under the <c>schema/</c> folder.
    /// </summary>
    public EmbeddedSchemaRegistry()
        : this(BuildFromEmbeddedResources())
    {
    }

    /// <summary>
    /// Creates a registry using a precomputed schema map.
    /// Primarily intended for testing or custom bootstrapping.
    /// </summary>
    public EmbeddedSchemaRegistry(IReadOnlyDictionary<(string Version, string Area, string Name), (SchemaDescriptor Descriptor, string ResourceName)> schemas)
    {
        _schemas = schemas ?? throw new ArgumentNullException(nameof(schemas));
    }

    public Stream GetSchema(ProtocolVersion version, string area, string name)
    {
        if (string.IsNullOrWhiteSpace(area)) throw new ArgumentException("Area must be provided.", nameof(area));
        if (string.IsNullOrWhiteSpace(name)) throw new ArgumentException("Name must be provided.", nameof(name));

        var key = (version.Value, area, name);
        if (!_schemas.TryGetValue(key, out var entry))
        {
            throw new KeyNotFoundException($"Schema not found for version '{version.Value}', area '{area}', name '{name}'.");
        }

        var assembly = typeof(EmbeddedSchemaRegistry).Assembly;
        var stream = assembly.GetManifestResourceStream(entry.ResourceName);
        if (stream == null)
        {
            throw new InvalidOperationException($"Embedded resource '{entry.ResourceName}' for schema '{name}' could not be opened.");
        }

        return stream;
    }

    public IReadOnlyCollection<SchemaDescriptor> ListSchemas()
    {
        var list = _schemas.Values
            .Select(s => s.Descriptor)
            .ToList();

        return new ReadOnlyCollection<SchemaDescriptor>(list);
    }

    public IReadOnlyCollection<SchemaDescriptor> ListSchemas(ProtocolVersion version, string? area = null)
    {
        var list = _schemas
            .Where(kvp => kvp.Key.Version == version.Value &&
                         (area == null || string.Equals(kvp.Key.Area, area, StringComparison.OrdinalIgnoreCase)))
            .Select(kvp => kvp.Value.Descriptor)
            .ToList();

        return new ReadOnlyCollection<SchemaDescriptor>(list);
    }

    private static IReadOnlyDictionary<(string Version, string Area, string Name), (SchemaDescriptor Descriptor, string ResourceName)> BuildFromEmbeddedResources()
    {
        var assembly = typeof(EmbeddedSchemaRegistry).Assembly;
        var resources = assembly.GetManifestResourceNames();
        var map = new Dictionary<(string, string, string), (SchemaDescriptor, string)>();

        foreach (var resourceName in resources.Where(n => n.EndsWith(".json", StringComparison.OrdinalIgnoreCase)))
        {
            var marker = ".schema.";
            var markerIndex = resourceName.IndexOf(marker, StringComparison.OrdinalIgnoreCase);
            if (markerIndex < 0)
            {
                continue;
            }

            var pathPart = resourceName[(markerIndex + marker.Length)..];
            var parts = pathPart.Split('.');
            if (parts.Length < 3)
            {
                continue;
            }

            var version = NormalizeVersionSegment(parts[0]);
            var area = parts[1];
            var nameWithExt = string.Join('.', parts.Skip(2));
            if (!nameWithExt.EndsWith(".json", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var name = nameWithExt[..^5]; // trim ".json"
            var key = (version, area, name);

            if (!map.ContainsKey(key))
            {
                var descriptor = new SchemaDescriptor
                {
                    Version = new ProtocolVersion(version),
                    Area = area,
                    Name = name
                };

                map[key] = (descriptor, resourceName);
            }
        }

        return map;
    }

    private static string NormalizeVersionSegment(string segment)
    {
        if (string.IsNullOrWhiteSpace(segment))
        {
            return segment;
        }

        var trimmed = segment.TrimStart('_');
        return trimmed.Replace('_', '-');
    }
}
