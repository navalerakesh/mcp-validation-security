# MCP Schemas & Spec Project

## Goals

- Provide a **single, version-aware source of truth** for MCP protocol schemas across tools, resources, prompts, logging, and completions.
- Decouple schema evolution from validator logic so we can update spec assets without touching validation code.
- Ship deterministic assets (vendored JSON Schemas + descriptors) that never rely on runtime network fetches.

## Current State (December 2025)

| Area | Status |
| --- | --- |
| Library | `Mcp.Compliance.Spec` ships with `ProtocolVersions`, `SchemaDescriptors`, `ISchemaRegistry`, and the default `EmbeddedSchemaRegistry`. |
| Dependency graph | Infrastructure & CLI resolve `ISchemaRegistry` via DI; Core references `ProtocolVersions` for validation configs and reporting. |
| Assets | Folder scaffolding exists for 2024-11-05, 2025-03-26, and 2025-06-18 schema sets; vendored JSON files are being populated incrementally. |
| Next focus | Complete ingestion of the latest MCP schemas, enforce registry-backed lookups in every structural validator, and document alias handling for spec profiles. |

## Layout & Assets

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

- Schemas live under `schema/<protocol-version>/<area>/<name>.schema.json` and are embedded as resources.
- Version folders are immutable once released; new versions receive a full directory clone to preserve determinism.
- `ProtocolVersions` exposes strongly typed constants plus helpers for parsing/aliases.

## Registry Contract

```csharp
public interface ISchemaRegistry
{
    Stream GetSchema(ProtocolVersion version, string area, string name);
    IReadOnlyCollection<SchemaDescriptor> ListSchemas();
    IReadOnlyCollection<SchemaDescriptor> ListSchemas(ProtocolVersion version, string? area = null);
}
```

Key rules:

- Consumers never touch the file system; all access flows through `ISchemaRegistry`.
- `(version, area, name)` lookup must be deterministic and throw explicit errors if a schema is missing.
- Registry implementations must return read-only streams so validators can feed any JSON Schema engine without copying files to disk.

## Add a New Protocol Version

1. Add the schema folder `schema/<yyyy-mm-dd>/` with every required feature area.
2. Embed the JSON files using the `.csproj` `EmbeddedResource` or `Content` include.
3. Extend `ProtocolVersions` with the new constant and alias metadata.
4. Register descriptors in `SchemaDescriptors` (one per schema) so rule registries can enumerate coverage.
5. Update `SpecProfileCatalog` to expose the profile to CLI users.
6. Wire validators/rules to the new schemas and add regression tests.

## Consumers & Responsibilities

- **Infrastructure validators** call `GetSchema` based on the active `McpValidatorConfiguration.ProtocolVersion` and must not cache schema contents beyond the lifetime of a validation run.
- **CLI & reports** reference `ProtocolVersions` to stamp generated artifacts with the spec profile that governed the run.
- **Tests** may load schemas through the registry to validate that assets are embedded correctly; direct file reads are discouraged to keep parity with runtime behavior.

## Runtime Behavior

- Normal operation is fully offline—no HTTP fetches or schema discovery at runtime.
- Optional dev-time tooling (future) may compare vendored schemas against upstream spec repositories, but that workflow occurs outside the shipping binaries.

## Future Enhancements

- Layered registries (core + org-specific overlays) for partners who need custom schemas.
- CLI tooling to diff schema versions and highlight breaking changes.
- Analyzer to verify that every validator references registry descriptors (no stray magic strings).
