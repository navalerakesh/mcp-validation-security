# Schema Compatibility And Migration

MCP Validator public JSON artifacts identify both `documentType` and `documentSchemaVersion`. Consumers must validate both values before reading an artifact.

Schema versions use semantic versioning:

- Patch releases clarify schemas without changing accepted document shapes.
- Minor releases may add optional properties or enum members. Consumers must ignore unknown properties and handle unknown non-gating enum values explicitly.
- Major releases may remove, rename, or change required properties and require a consumer migration.

The validator supports all well-formed artifact versions with the same major version as the current contract. Unknown document types, malformed versions, and future major versions are rejected by `ArtifactContracts.IsCompatible`.

Published schemas are under `docs/Schemas`. A major schema release must add migration notes, retain the previous schema and a backward-consumer fixture for the documented support window, and provide an explicit conversion path where lossless conversion is possible.

Audit manifests hash the final bytes of every subject artifact listed in `artifactPaths`. The manifest does not list or hash itself, avoiding a recursive self-digest contract. Signed validation attestations independently bind the canonical result bytes to an ECDSA P-256 public key.
