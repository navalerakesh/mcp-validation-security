# Schema Compatibility And Migration

MCP Validator public JSON artifacts identify both `documentType` and `documentSchemaVersion`. Consumers must validate both values before reading an artifact.

Schema versions use semantic versioning:

- Patch releases clarify schemas without changing accepted document shapes.
- Minor releases may add optional properties or enum members. Consumers must ignore unknown properties and handle unknown non-gating enum values explicitly.
- Major releases may remove, rename, or change required properties and require a consumer migration.

The validator supports all well-formed artifact versions with the same major version as the current contract. Unknown document types, malformed versions, and future major versions are rejected by `ArtifactContracts.IsCompatible`.

Published schemas are under `docs/Schemas`. A major schema release must add migration notes and retain a rejection fixture for incompatible artifacts. V1 score/trust/verdict artifacts cannot be converted losslessly into V2 claims and certificate evidence, so the runtime does not provide a compatibility converter.

Audit manifests hash the final bytes of every subject artifact listed in `artifactPaths`. The manifest does not list or hash itself, avoiding a recursive self-digest contract. Signed validation attestations independently bind the canonical result bytes to an ECDSA P-256 public key.

Assurance-result schema `2.0.0` uses document type `mcpval.assurance-result`. It requires typed claims, independent assessments, evidence summary, certificate eligibility, and a V2 policy decision. It removes serialized global score, L1-L5 trust, baseline verdict, and generic overall status fields. Baselines must use the same assurance profile and profile revision.

Client-profile summaries, model-evaluation companions, and audit manifests also use `2.x` contracts. V1 companions are incompatible by design.

Audit manifest `2.1.0` adds optional `configurationSources`. Each entry identifies a canonical setting path, source (`default`, `file`, `profile`, `commandLine`, or `governance`), non-secret pointer, replaced source and override reason. It contains no setting values, raw file paths or arbitrary dictionary-key text. Existing `2.0.0` manifests remain readable and are not assumed to contain this evidence. Execution plans carry the same projection; offline-report session receipts include only their reporting settings. This operational metadata does not change canonical findings or scoring.

Configuration input has a separate `schemaVersion` contract, currently `1.0.0`. Omitting it preserves legacy interpretation; adding it does not alter normalized settings. Unsupported explicit versions fail preflight rather than being interpreted as current. The [published schema](Schemas/mcpval-configuration-v1.schema.json) is generated from CLI serializer metadata with `mcpval config-schema`; the [configuration guidance](../Mcp.Benchmark.CLI/README.md#configuration-files) describes migration and parser-only limits. This input version is independent of artifact and MCP protocol versions.

The `mcpval.official-conformance-evidence` wrapper has its own `1.x` contract. Import verifies the document contract, bounded structure, exact scenario check digests, pinned runner/package/requirement-set provenance, and target/protocol binding. Audit manifests bind the imported wrapper SHA-256 under `externalEvidenceSha256`.
