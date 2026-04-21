# Embedded MCP Schemas

This folder is the canonical home for the versioned MCP JSON Schemas embedded by `Mcp.Compliance.Spec`.

## Layout

```text
schema/
  2024-11-05/
    protocol/
    tools/
    resources/
    prompts/
    logging/
    completions/
  2025-03-26/
    protocol/
    tools/
    resources/
    prompts/
    logging/
    completions/
  2025-06-18/
    protocol/
    tools/
    resources/
    prompts/
    logging/
    completions/
  2025-11-25/
    protocol/
    tools/
    resources/
    prompts/
    logging/
    completions/
```

All `*.json` files under `schema/**` are embedded as resources and discovered automatically by `EmbeddedSchemaRegistry`.

## Source Of Truth

For each supported protocol version, this project vendors the official JSON Schema bundle from the upstream MCP specification repository:

- Repository: `modelcontextprotocol/modelcontextprotocol`
- Canonical source path: `schema/<version>/schema.json`

These assets are copied into the versioned folder structure used by this repository, primarily under `schema/<version>/protocol/schema.json`, along with the supporting area-specific files needed by validators.

## Registry Behavior

At build and runtime, `EmbeddedSchemaRegistry`:

- Discovers embedded `*.json` resources under `schema/`
- Maps each resource to `(protocolVersion, area, name)` using its folder path
- Serves deterministic streams through `ISchemaRegistry.GetSchema` and `ListSchemas`

This design keeps validation runs offline, reproducible, and independent of network access to upstream specification repositories.

## Rules For Adding A Version

- Create a complete `schema/<version>/` directory rather than mutating older version folders.
- Keep folder names aligned with the protocol version string.
- Register the version in `ProtocolVersions` and expose it through the CLI profile catalog.
- Add tests that prove the new assets are embedded and resolvable through the registry.
