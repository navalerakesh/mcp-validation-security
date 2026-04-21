# MCP Schema Registry

This document explains how MCP Validator stores, versions, and resolves the vendored protocol schemas used during validation.

## Design Goals

- Keep protocol schemas version-aware and deterministic.
- Decouple schema evolution from validator logic.
- Avoid runtime network fetches from upstream specification repositories.
- Make every validator consume schemas through a single registry contract.

## Current Coverage

| Area | Current state |
| --- | --- |
| Library | `Mcp.Compliance.Spec` provides `ProtocolVersions`, `SchemaDescriptors`, `ISchemaRegistry`, and `EmbeddedSchemaRegistry`. |
| Supported protocol folders | `2024-11-05`, `2025-03-26`, `2025-06-18`, and `2025-11-25` are present under `schema/`. |
| Dependency model | Infrastructure consumes the registry for validation, while the CLI and reporting layers use protocol metadata for artifact labeling and profile selection. |
| Runtime behavior | Schema resolution is offline and deterministic; no production validation flow depends on fetching remote assets at runtime. |

## Layout

```text
Mcp.Compliance.Spec/
  schema/
    <version>/
      protocol/
      tools/
      resources/
      prompts/
      logging/
      completions/
  ProtocolVersions.cs
  SchemaDescriptors.cs
  ISchemaRegistry.cs
  EmbeddedSchemaRegistry.cs
```

Rules for the layout:

- Schemas live under `schema/<protocol-version>/<area>/<name>.json` and are embedded as resources.
- Version folders are immutable once introduced; add a new version rather than mutating an older one.
- `ProtocolVersions` is the typed catalog used by the rest of the solution.

## Registry Contract

```csharp
public interface ISchemaRegistry
{
    Stream GetSchema(ProtocolVersion version, string area, string name);
    IReadOnlyCollection<SchemaDescriptor> ListSchemas();
    IReadOnlyCollection<SchemaDescriptor> ListSchemas(ProtocolVersion version, string? area = null);
}
```

Registry expectations:

- Consumers do not read schema files directly from disk.
- `(version, area, name)` lookups must be deterministic and fail explicitly when an asset is missing.
- Returned streams must be read-only so validators can hand them to schema tooling without copying resources to temporary files.

## Add A New Protocol Version

1. Create `schema/<yyyy-mm-dd>/` with the full set of required feature areas.
2. Embed the JSON files through the project file so they are available at runtime.
3. Extend `ProtocolVersions` with the new constant and any alias metadata.
4. Register descriptors in `SchemaDescriptors` so rule registries can enumerate coverage.
5. Expose the version through the CLI profile catalog.
6. Add regression tests proving the new assets are embedded and resolvable.

## Consumers And Responsibilities

- Validators request schemas through `ISchemaRegistry` based on the effective protocol version in `McpValidatorConfiguration`.
- Reporting and CLI surfaces use `ProtocolVersions` to stamp the run with the profile that governed evaluation.
- Tests may use the registry to verify embedded assets and descriptor coverage, but should avoid direct file-system reads.

## Operational Guidance

- Production validation remains fully offline with respect to schema acquisition.
- Upstream schema comparison, if introduced, should remain a development or maintenance workflow outside the shipping binaries.
- Registry-backed resolution is part of the product boundary; ad hoc schema path usage should be treated as architectural drift.

## Future Work

- Layered registries for partner-specific overlays
- Tooling to diff schema versions and highlight breaking changes
- Static checks that verify validators only reference registered schema descriptors