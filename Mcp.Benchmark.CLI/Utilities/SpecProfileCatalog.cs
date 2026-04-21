using System;
using System.Collections.Generic;
using System.Linq;
using Mcp.Compliance.Spec;

namespace Mcp.Benchmark.CLI.Utilities;

/// <summary>
/// Builds a list of MCP spec profiles supported by the embedded schema registry.
/// </summary>
internal static class SpecProfileCatalog
{
    public static IReadOnlyList<SpecProfileInfo> GetProfiles(ISchemaRegistry schemaRegistry)
    {
        if (schemaRegistry == null)
        {
            throw new ArgumentNullException(nameof(schemaRegistry));
        }

        try
        {
            var versions = SchemaRegistryProtocolVersions
                .GetAvailableVersions(schemaRegistry)
                .Select(v => v.Value)
                .ToList();

            if (versions.Count == 0)
            {
                return Array.Empty<SpecProfileInfo>();
            }

            var profiles = new List<SpecProfileInfo>();

            // Add "latest" alias so users can always target the newest profile easily.
            var newest = SchemaRegistryProtocolVersions.GetLatestVersion(schemaRegistry).Value;
            profiles.Add(new SpecProfileInfo(
                Name: "latest",
                Description: $"Alias of {newest}",
                AliasOf: newest,
                IsAlias: true));

            foreach (var version in versions)
            {
                var description = version == newest
                    ? "Newest embedded MCP spec"
                    : "Embedded MCP spec";
                profiles.Add(new SpecProfileInfo(version, description));
            }

            return profiles;
        }
        catch
        {
            return Array.Empty<SpecProfileInfo>();
        }
    }
}

/// <summary>
/// Represents a user-selectable MCP spec profile (real version or alias).
/// </summary>
internal sealed record SpecProfileInfo(string Name, string Description, string? AliasOf = null, bool IsAlias = false);
