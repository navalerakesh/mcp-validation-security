# V2 Dependency And Cryptography Strategy

## Decision

V2 uses the smallest dependency graph that preserves protocol fidelity and avoids implementing security-sensitive parsers or protocol clients from scratch. No package is admitted merely to reduce implementation effort.

The current V2 work adds no new runtime package. Existing mature packages remain where they own substantial standards or parser complexity. Cryptographic operations continue to use .NET platform primitives.

## Admission Gate

A new runtime dependency must satisfy every applicable requirement:

1. It closes a documented capability, correctness, or security gap that cannot be addressed safely with the platform or an existing package.
2. Its repository, package owner, release signing/provenance, issue history, security policy, and maintainer activity are verifiable.
3. It is an official protocol/vendor package, or demonstrates broad independent adoption over multiple years. Download counts or stars alone are insufficient.
4. It uses an approved permissive license. MIT is preferred for new packages; Apache-2.0 or BSD requires explicit license review.
5. It has no unresolved critical advisory, abandoned transitive dependency, install script, unexpected native payload, or undeclared network behavior.
6. Its API can be isolated behind an owned interface and removed without changing the canonical assurance contract.
7. Versions and transitive graphs are locked, audited, SBOM-visible, and updated through reviewed automation.
8. Failure or absence produces a typed unavailable/incomplete result. It never silently weakens validation.

Security and cryptography packages additionally require a public threat model, published algorithm choices, misuse-resistant APIs, test vectors, interoperability evidence, and active vulnerability handling. A package that only wraps a primitive already supplied by the platform is not admitted.

## Current Runtime Decisions

| Capability | Decision | Rationale |
| --- | --- | --- |
| MCP wire client and contracts | Keep official `ModelContextProtocol`/`ModelContextProtocol.Core` and `@modelcontextprotocol/sdk` | Official SDKs reduce protocol drift and are the correct integration boundary. They do not determine validator claims or policy. |
| Official conformance | Keep `@modelcontextprotocol/conformance` as a pinned, isolated tool input | Official scenario ownership is reused without making an npm runner an in-process .NET runtime dependency. Package, source, requirements, target, and result digests are verified before credit. |
| JSON Schema Draft 2020-12 | Keep JsonSchema.Net | Standards-complete evaluation is safer than a local schema engine. Unknown extension annotations remain non-failing under the declared dialect. |
| HTML parsing for MCP Apps | Keep AngleSharp | A mature HTML parser avoids security errors from regular-expression or string parsing. Target HTML remains untrusted and is never executed. |
| YAML parsing for MCP Skills | Keep YamlDotNet | A mature parser is used behind byte, depth, event, alias, and scalar limits. Parsed content remains data and is never executed. |
| OAuth client flows | Keep Microsoft.Identity.Client and owned protocol guards | Official Microsoft identity code handles supported interactive/token flows; validator-specific audience, origin, redirect, and evidence rules remain owned. |
| Telemetry | Keep OpenTelemetry packages | Open standard and exporter interoperability; telemetry remains optional and carries no target secrets. |
| CLI parsing and hosting | Keep System.CommandLine and Microsoft.Extensions | Official platform ecosystem packages with no validator semantics. |
| Cryptography | Keep `System.Security.Cryptography` | The platform provides CSPRNG, SHA-256, HMAC-SHA-256, AES-GCM, ECDSA P-256, PEM import/export, fixed-time comparison, and memory zeroing without another supply-chain or native-library dependency. |

All retained packages must pass the repository vulnerability, license, lockfile, distribution, and release gates on every update. This record approves roles, not arbitrary future versions.

## Cryptographic Boundary

V2 does not invent cryptographic algorithms or signature encodings.

- Artifact identity uses SHA-256 over deterministic canonical bytes.
- Local attestations use ECDSA P-256 with SHA-256 and reject other curves or algorithm labels.
- Fleet snapshots use AES-GCM with a unique random nonce and tenant-bound associated data.
- Approval and audit authentication uses platform HMAC and fixed-time verification.
- Keys are supplied through host secret boundaries, excluded from artifacts/logs, and zeroed where mutable key bytes are owned.
- Verification always binds signature, subject digest, algorithm, key identity, and expected document purpose.

New cryptographic formats require published test vectors and cross-implementation verification. The preferred ecosystem path for keyless/transparency-backed release attestations is optional Sigstore/GitHub artifact attestation executed by CI as a separate signed tool or action. V2 may verify such evidence through a bounded adapter, but does not embed an unneeded Sigstore or native cryptography library.

## Packages Not Added

| Candidate category | Decision |
| --- | --- |
| Bouncy Castle or alternate general cryptography provider | Do not add while platform primitives satisfy the approved algorithms. |
| libsodium/NSec wrapper | Do not add without an approved Ed25519/X25519 requirement and native distribution plan. |
| JWT library | Do not add until the validator must validate JWTs itself; current evidence checks must not become token-issuer logic accidentally. |
| HTML sanitizer | Do not add because target HTML is parsed for evidence, never rendered or executed; all report values are encoded. Reconsider only if product behavior changes. |
| Generic security scanner or AI classifier | Do not add. Such tools cannot replace typed evidence, calibrated rules, or official conformance. |
| Dynamic plugin framework | Do not add. Third-party validation code requires a signed, isolated process contract with explicit authority and resource limits. |

## Official MCP Reuse

Official MCP repositories are authoritative for schemas, lifecycle metadata, conformance scenarios, SDK wire behavior, and extension specifications. V2 imports pinned official artifacts rather than copying their logic.

The validator continues to own:

- bounded acquisition and provenance verification;
- security, safety, operations, policy, and reporting claims not owned by official conformance;
- conflict reconciliation between official and native evidence;
- certificate eligibility and host authorization;
- redaction, persistence, audit, and accessibility.

Official packages never receive implicit target credentials, expanded network authority, or pass credit merely because they executed.

## Supply-Chain Controls

- Commit lockfiles for every packaged project and npm workspace.
- Use exact versions for security- and protocol-critical packages.
- Generate SBOM and package provenance in release workflows.
- Run NuGet and npm vulnerability audits, secret scanning, dependency review, and distribution-contract verification.
- Verify package/source digests for imported official evidence.
- Reject lockfile drift, unexpected package scripts, mutable download URLs, and unreviewed native assets.
- Record dependency additions and removals in the changelog and this decision record.
- Remove a package when the platform supersedes it and migration reduces total risk.

## Reassessment Triggers

Reopen this decision when an approved algorithm is unavailable in the supported runtime, an official MCP SDK/conformance package changes its trust or license boundary, a retained parser becomes unmaintained, a critical advisory has no timely fix, or interoperability requires a standard attestation format that cannot be implemented safely with existing primitives.
