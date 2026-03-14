# MCP Schemas Folder

This folder is the canonical home for versioned MCP JSON Schemas used by `Mcp.Compliance.Spec`.

## Layout

Expected structure:

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
```

All `*.json` files placed under `schema/**` are embedded as resources and automatically discovered by `EmbeddedSchemaRegistry`.

## Source of Official Schemas

For each supported protocol version, this project vendors the official MCP
JSON Schema bundle from the MCP specification repository:

- Repository: `modelcontextprotocol/modelcontextprotocol`
- Path per version: `schema/<version>/schema.json`

These bundles are placed under:

- `schema/<version>/protocol/schema.json`

On build, the `EmbeddedSchemaRegistry` will:

- Discover all embedded `*.json` resources under `schema/`.
- Map each resource to `(protocolVersion, area, name)` based on its folder path.
- Serve them via `ISchemaRegistry.GetSchema` and `ListSchemas`.

This ensures the validator uses **authentic upstream schemas** from the
official MCP specification while keeping this repository deterministic and
avoiding runtime network fetches.
