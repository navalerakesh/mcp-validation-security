# Changelog

All notable changes to this project will be documented in this file.

The format is based on Keep a Changelog, and this project follows semantic versioning for published artifacts.

## Unreleased

## 1.1.26 - 2026-08-20

### Fixed

- Composite Action package smoke now installs from an explicit isolated local NuGet source configuration.
- Windows standalone archive smoke now extracts ZIP payloads with a ZIP-aware tool.

## 1.1.25 - 2026-08-19

### Added

- First-class MCP guideline rule pack metadata for tool, prompt, resource, and protocol guidance findings.
- Structured optional capability findings for `roots/list`, `logging/setLevel`, `sampling/createMessage`, and `completion/complete`, including declaration mismatches.
- JUnit report output for CI systems that ingest test-style artifacts.
- Snapshot approval tests for Markdown, HTML, XML, SARIF, JUnit, and JSON report outputs.
- Reusable compliant, partially compliant, and unsafe MCP fixture profiles for regression testing.
- Runnable compliant, partially compliant, and unsafe STDIO MCP fixture servers for process-backed regression and demo coverage.
- Hardened Docker distribution path with GHCR publishing, OCI SBOMs, and provenance metadata.
- Additional AI-readiness schema findings for required arrays, fixed-choice string parameters, and missing structured format hints.
- Destructive-tool confirmation guidance heuristics for tools that declare `destructiveHint` without describing confirmation or warning expectations.
- Prompt metadata guidance findings for missing prompt descriptions, missing argument descriptions, multi-input complexity guidance, and safety-sensitive prompts without warning language.
- Tool pagination findings for unstable cursors and large unpaginated `tools/list` catalogs.
- Resource guidance findings for missing MIME metadata, unclear URI schemes, and parameterized resource templates without ergonomic metadata.
- Embedded MCP `2026-07-28` schema support, modern `server/discover`, required per-request metadata and `Mcp-Method` HTTP headers, typed result envelopes, cache metadata, subscription acknowledgment, and modern/legacy protocol-era selection.
- Deterministic outcome, evidence-coverage, trust-limit, client-profile, operational-metric, and policy-result contracts across canonical JSON and companion formats.
- Explicit single-owner open-source governance, signed-evidence/recovery procedures, and fail-closed history/sensitive-artifact checks.

### Changed

- Capability negotiation now records optional capability support discovered during protocol validation.
- Contributor workflow now includes tracked repository validation scripts and fixture-profile guidance.
- Release distribution guidance now documents the GHCR image alongside NuGet, npm, and standalone binaries.
- HTML reports now surface Run Status, Deterministic Verdict, and Trust Level as separate top-page decision cards.
- Public documentation now describes the current explicit-output artifact set, including audit manifests and the HTML decision surface.
- Safe mode is discovery/static analysis only; reports now expose skipped and unevaluated dimensions instead of treating missing evidence as success.
- Deterministic baseline/protocol/coverage verdicts are authoritative. Weighted score and L1-L5 trust remain descriptive and are capped by blocking security evidence, MUST failures, incomplete evidence, and authoritative reject verdicts.
- L5 human-readable wording is now `High Assurance` and explicitly does not represent formal certification or accreditation.
- Canonical validation and client-profile artifacts use schema version `1.1.0`, include validator provenance and final policy/verdict/coverage fields, and preserve same-major ingestion compatibility for older producer metadata.
- SARIF invocation status and properties, JUnit policy/error test cases and durations, Markdown/HTML evidence language, and profile summaries now project the same final canonical gate semantics.
- Composite Action validation evidence uploads run even when strict/balanced policy returns a nonzero exit.
- NuGet/npm packages, package SBOMs, checksums, release SBOM, signatures, and standalone archives are durable GitHub Release assets; GHCR mutable aliases use OCI version metadata for monotonic promotion.
- Release checksum subjects use portable bare filenames so signature backfill can verify every generated asset.
- Build validation fetches complete Git history before applying the sensitive-artifact publication gate.

### Security

- Release outputs now include stronger supply-chain trust signals across standalone binaries, packages, and container images.
- STDIO commands remain fully redacted in plans/artifacts while execution uses the reviewed in-memory command; process environments, output, framing, and shutdown remain bounded.
- Canonical clones remove raw HTTP/STDIO payload previews, headers, response bodies, exceptions, and target output; session logs no longer print response bodies and redact cookies, MCP session IDs, OAuth fields, known GitHub token forms, JWTs, and private keys.
- Authenticated automation supports environment `SecretRef` credentials so tokens do not enter process arguments or persisted configuration.
- Container builds pin the immutable .NET SDK `8.0.419` and runtime `8.0.25` image manifests, preventing moving-base drift from bypassing the exact SDK contract.
- Test dependencies no longer include unused Testcontainers/SSH.NET packages, and WireMock resolves a patched Scriban release.

### Migration Notes

- Pin `--mcpspec` and `--protocol-era` when reproducibility across protocol revisions is required. `latest` resolves to `2026-07-28`; legacy-only servers should select a supported legacy profile explicitly.
- Consumers parsing canonical JSON, client-profile summaries, SARIF, or JUnit should accept additive `1.1` fields and use deterministic verdict/policy fields for admission decisions. Do not gate on compliance score or trust label alone.
- CI workflows using the composite Action should keep `upload-artifacts: 'true'` so reports are retained on policy failure. Treat a nonzero validation exit as a completed evidence-producing policy decision, not automatically as transport failure.
- L5 remains enum-compatible as `L5_CertifiedSecure` in the 1.x API, but its human label is `High Assurance`; integrations must not present it as independent certification.
