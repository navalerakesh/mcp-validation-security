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
| Library | `Mcp.Compliance.Spec` provides `ProtocolVersions`, `SchemaRegistryProtocolVersions`, `SchemaDescriptors`, `ISchemaRegistry`, and `EmbeddedSchemaRegistry`. |
| Supported protocol folders | `2024-11-05`, `2025-03-26`, `2025-06-18`, and `2025-11-25` are present under `schema/`. |
| Dependency model | Infrastructure consumes the registry for validation, while the CLI and reporting layers use protocol metadata for artifact labeling and spec-profile selection. |
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
  SchemaRegistryProtocolVersions.cs
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

## Version Resolution Behavior

- Requested protocol version `latest`, `null`, or empty resolves to the newest embedded schema version for outbound initialize handshakes.
- Negotiated versions that do not exist in the embedded registry fall back to the backwards-compatible embedded default, currently `2025-03-26`.
- Validators and reporting surfaces should use `SchemaRegistryProtocolVersions` for normalization and fallback logic instead of re-implementing version parsing.
- The run record keeps both the negotiated protocol version and the resolved schema version so reports can explain which bundle actually governed validation.

## Add A New Protocol Version

1. Create `schema/<yyyy-mm-dd>/` with the full set of required feature areas.
2. Embed the JSON files through the project file so they are available at runtime.
3. Extend `ProtocolVersions` with the new constant and ensure `SchemaRegistryProtocolVersions` can resolve it.
4. Register descriptors in `SchemaDescriptors` so rule registries can enumerate coverage.
5. Expose the version through CLI spec-profile handling and any user-facing list commands.
6. Add regression tests proving the new assets are embedded and resolvable.

## Consumers And Responsibilities

- Session bootstrap and validators normalize requested and negotiated versions through `SchemaRegistryProtocolVersions`, then request schemas through `ISchemaRegistry`.
- Reporting and CLI surfaces use the resolved protocol metadata to stamp the run with the profile that governed evaluation.
- Tests may use the registry to verify embedded assets and descriptor coverage, but should avoid direct file-system reads.

## Operational Guidance

- Production validation remains fully offline with respect to schema acquisition.
- Upstream schema comparison, if introduced, should remain a development or maintenance workflow outside the shipping binaries.
- Registry-backed resolution is part of the product boundary; ad hoc schema path usage should be treated as architectural drift.

## Future Work

- Layered registries for partner-specific overlays
- Tooling to diff schema versions and highlight breaking changes
- Static checks that verify validators only reference registered schema descriptors
