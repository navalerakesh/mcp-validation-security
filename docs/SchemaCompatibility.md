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

The `mcpval.official-conformance-evidence` wrapper has its own `1.x` contract. Import verifies the document contract, bounded structure, exact scenario check digests, pinned runner/package/requirement-set provenance, and target/protocol binding. Audit manifests bind the imported wrapper SHA-256 under `externalEvidenceSha256`.
