# Changelog

All notable changes to this project will be documented in this file.

The format is based on Keep a Changelog, and this project follows semantic versioning for published artifacts.

## Unreleased

## 2.0.0 - 2026-11-06

### Added

- Added bounded import of pinned official MCP conformance evidence with runner, package, requirement-set, scenario, target, and wrapper digest verification; contradictory native evidence produces a separate `EvidenceConflict` review decision.
- Added extension-aware applicability and provenance, automatic passive discovery, repeatable `--extension`, and separate extension assessments that do not inflate core score or trust.
- Added the Stable MCP Apps server-security pack for tool/UI linkage, extension settings, UI resource MIME/content, HTML5, CSP origin, permission, and executable-content analysis without rendering server HTML.
- Added the Beta MCP Skills security Preview for bounded catalog/manifest analysis and explicitly authorized SKILL.md digest, size, YAML frontmatter, prompt-injection, secret-request, and code-execution risk analysis without executing instructions or scripts.
- Added Official Conformance and Extension Readiness projections across Markdown, HTML, XML, SARIF, JUnit, canonical JSON, and audit manifests.
- Added signed ECDSA P-256 provider authorization grants and signed prior-run harness manifests, target/protocol/execution-plan/current-run binding, repeated-run admission, and a controlled end-to-end provider certificate proof.
- Added an isolated official-conformance companion with exact runner, requirement-set, frozen-scenario, raw-result, and transitive npm dependency locks.
- Added deterministic 50-run parallel isolation, 25-render concurrency, 1,000-evaluation soak, structural HTML accessibility, WCAG AA token, and browser desktop/mobile/keyboard proofs.
- Added a repository PR/merge/release readiness skill that audits documentation and STDIO-wrapper drift, composes existing local gates, and verifies protected checks plus NuGet/npm publication.

### Changed

- **Breaking:** replaced the V1 global score, L1-L5 trust, baseline verdict, and generic overall gate with V2 typed claims, independent core/security/operations assessments, assurance profiles, explicit certificate eligibility, and stable incomplete-evidence exit semantics.
- **Breaking:** canonical validation artifacts now use document type `mcpval.assurance-result` and schema version `2.0.0`; V1 artifacts are not accepted as V2 baselines or offline report inputs.
- Added `--assurance inventory|ci|provider` consistently across the CLI, GitHub Action, and local MCP wrapper.
- Reports and automation now consume the canonical V2 assurance object; SARIF emits actionable non-success claims and JUnit groups claims by assurance dimension.
- Removed the dormant V1 trust calculator, verdict engine, duplicate decision manifest, policy branch, report fallbacks, and calibration corpus so V2 claims and independent assessments are the only decision pipeline.
- Provider assurance, baselines, policy, console output, reports, audit identities, and exit codes now consume the final V2 assurance state after external evidence and current-run receipts are attached.
- HTML reports now lead with tested scope and the assurance interpretation, use neutral surfaces with semantic status rails, keep run metadata secondary on mobile, and move terminology into a collapsed reference appendix.
- The local MCP STDIO wrapper now describes safe-mode evidence limits explicitly, declares trust metadata in tool output schemas, and selects standard mode only for operator-configured provider assurance.

### Security

- Updated the local MCP wrapper to patched MCP SDK and Zod releases and refreshed transitive dependencies so the production npm audit reports zero vulnerabilities.

### Fixed

- Canonical results no longer persist SDK runtime tool handles, and offline reporting tolerates older `1.x` artifacts that contain them.
- Draft 2020-12 tool schemas now accept unknown extension keywords such as GitHub MCP's `x-mcp-header` as annotations while continuing to validate known constraints.
- Client-profile compatibility coverage now remains in its own lane and cannot alter the neutral core score, evidence summary, or deterministic verdict.
- Advisory policy now returns a successful exit for expected coverage-limited safe-mode results while execution errors, cancellation, and incomplete execution remain blocking.
- Modern stateless bootstrap evidence now records healthy disposition and discovery duration, and reports no longer describe `server/discover` as an initialize handshake.
- Remediation reports preserve finding-specific recommendations and classify missing active protocol evidence with protocol work instead of advertised-capability fixes.
- AI-readiness schema markers now match semantic tokens instead of substrings, avoiding false enum recommendations for prose such as `scoped` and `TypeScript`.
- Official conformance import now fails closed on missing, unexpected, empty, or misclassified frozen scenarios and verifies the exact bounded `checks.json` bytes before parsing.
- Normative security violations now contribute to deterministic security assurance instead of remaining outside the security reducer.
- HTML reports now use assurance terminology, provide console-clean offline icons, meet WCAG AA text contrast, and retain bounded mobile table scrolling.
- Safe-mode reports now distinguish catalog validation from tool invocation, resource reads, and prompt execution; omitted tool annotations retain their MCP-defined defaults, and coverage exclusions remain provider-policy evidence rather than specification failures.
- GitHub Copilot Cloud Agent compatibility now treats missing explicit destructive hints as an advisory because MCP defaults omitted write-tool hints to destructive and GitHub does not document explicit hints as an admission requirement.
- AI-readiness enum and format advisories now derive schema intent from parameter names and titles instead of incidental words in free-form descriptions.
- Offline HTML rendering now displays the canonical persisted MCP schema version instead of a renderer-default profile.
- The local MCP wrapper now preserves numeric V2 exits for CLI outcomes and maps wrapper launch failures and timeouts to stable runtime exits.
- Canonical assurance threshold reasons now use culture-invariant percentage formatting so report snapshots remain byte-stable across operating systems and CI runners.
- Local and GitHub build gates now share one benchmark-catalog contract using the V2 `AssuranceReduction` workload name.

## 1.1.33 - 2026-08-23

### Security

- npm publishing now uses GitHub Actions trusted publishing directly to the final channel tag without a long-lived npm token.

### Fixed

- npm publication verifies the final `latest` or `preview` tag without unsupported post-publish OIDC tag mutations.

## 1.1.32 - 2026-08-22

### Security

- Pull-request package jobs now use read-only permissions; package attestations and all deployments are isolated to guarded main-push jobs.

### Fixed

- Immutable release tags are reserved only after every package, container, standalone, and cross-platform install smoke succeeds.

## 1.1.31 - 2026-08-21

### Fixed

- Legacy GHCR channel aliases without OCI version metadata are migrated only when their digest exactly matches an immutable release tag.

## 1.1.30 - 2026-08-20

### Fixed

- Registry publication now waits for NuGet/npm indexing before verification and audits signatures from an installed npm package.
- Successful npm promotion removes stale temporary candidate tags.

## 1.1.29 - 2026-08-20

### Fixed

- Packed npm smoke now launches the installed server directly with Node and awaits process exit before deleting temporary files.

## 1.1.28 - 2026-08-20

### Fixed

- Windows clean-install smoke now invokes the platform-specific npm command when installing packed artifacts.

## 1.1.27 - 2026-08-20

### Fixed

- Windows clean-install smoke now converts Git Bash paths before embedding a local NuGet feed in configuration.

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
