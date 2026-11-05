# Schema Compatibility And Migration

MCP Validator public JSON artifacts identify both `documentType` and `documentSchemaVersion`. Consumers must validate both values before reading an artifact.

Schema versions use semantic versioning:

- Patch releases clarify schemas without changing accepted document shapes.
- Minor releases may add optional properties or enum members. Consumers must ignore unknown properties and handle unknown non-gating enum values explicitly.
- Major releases may remove, rename, or change required properties and require a consumer migration.

The validator supports all well-formed artifact versions with the same major version as the current contract. Unknown document types, malformed versions, and future major versions are rejected by `ArtifactContracts.IsCompatible`.

Published schemas are under `docs/Schemas`. A major schema release must add migration notes, retain the previous schema and a backward-consumer fixture for the documented support window, and provide an explicit conversion path where lossless conversion is possible.

Audit manifests hash the final bytes of every subject artifact listed in `artifactPaths`. The manifest does not list or hash itself, avoiding a recursive self-digest contract. Signed validation attestations independently bind the canonical result bytes to an ECDSA P-256 public key.

Validation-result schema `1.2.0` adds optional official-conformance and extension-readiness assessments plus evidence lane/origin metadata. Existing `1.x` consumers must continue to ignore unknown properties and enum values. Core score, trust, and policy fields retain their `1.1` meaning; external conformance and extensions are separate typed lanes.

The `mcpval.official-conformance-evidence` wrapper has its own `1.x` contract. Import verifies the document contract, bounded structure, exact scenario check digests, pinned runner/package/requirement-set provenance, and target/protocol binding. Audit manifests bind the imported wrapper SHA-256 under `externalEvidenceSha256`.
