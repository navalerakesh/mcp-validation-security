namespace Mcp.Compliance.Spec;

public static class ProtocolEraVersions
{
    public static bool IsModern(string? protocolVersion)
    {
        return string.Equals(protocolVersion, ProtocolVersions.V2026_07_28.Value, StringComparison.Ordinal);
    }

    public static ProtocolVersion GetLatestLegacyVersion(ISchemaRegistry? schemaRegistry = null)
    {
        var legacyVersion = SchemaRegistryProtocolVersions
            .GetAvailableVersions(schemaRegistry)
            .FirstOrDefault(version => !IsModern(version.Value));
        return string.IsNullOrWhiteSpace(legacyVersion.Value)
            ? SchemaRegistryProtocolVersions.BackwardCompatibilityDefault
            : legacyVersion;
    }
}