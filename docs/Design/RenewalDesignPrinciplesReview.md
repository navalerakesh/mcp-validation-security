# Renewal Design Principles Review

Reviewed: 2026-08-19

Release candidate: `1.1.25`

This review maps the enterprise renewal to `DESIGN-PRINCIPLES.md`. Repository readiness and deployed hosted readiness are separate claims.

## Principle Map

| Principle | Repository status | Evidence |
| --- | --- | --- |
| P0 Findings stay visible | Met | Milestone tracker retains deployment and external blockers; hosted checkboxes remain open. |
| P1 Evidence before verdict | Met | Closed outcomes, typed coverage/findings, pure verdict reducer, canonical verdict in CLI and MCP previews. |
| P2 Protocol fidelity | Met for declared versions | Embedded versioned schemas, era negotiation, applicability matrix, raw-probe expectations, regression fixtures. |
| P3 Clean dependency direction | Met | Architecture tests enforce Core/Infrastructure/CLI/ClientProfiles/Fleet boundaries. |
| P4 Run-scoped state | Met for local engine | Immutable run request, scoped transports, isolated correlation/metrics, parallel-run tests. |
| P5 Canonical structured findings | Met | JSON, SARIF, JUnit, XML, Markdown, HTML, policy, profiles, and MCP consume the same canonical result. |
| P6 Determinism | Met for captured evidence | Immutable policy manifest, fixed artifact timestamps/culture, sorted maps, byte-stable replay tests, recorded benchmark environment. |
| P7 External data untrusted | Met | Bounded contract-first ingestion, streaming limits, deep canonical sanitization, hostile renderer tests, MCP untrusted-content framing. |
| P8 Network isolation | Met for local engine | Shared governed SDK/raw HTTP chain, DNS/connect-time address enforcement, exact origins, disabled redirects/proxy, budgets. Hosted egress remains external. |
| P9 STDIO arbitrary code | Met for local contract | Structured process launch, allowlisted environment, bounded UTF-8 framing/output, cancellation/tree termination, redacted commands. Hosted sandbox remains external. |
| P10 Secret safety | Met | Secret references, stripped configuration, raw transport/observation removal, URI/flag/session-log redaction, canary tests. |
| P11 Safe active tests | Met | Safe default, bounded attacks, typed inconclusive outcomes, no error-as-defense inference. |
| P12 Concurrency truth | Met | Deterministic merge, limiter ownership tests, bounded concurrent telemetry, functional-before-performance ordering. |
| P13 Resource bounds | Met | Request/body/frame/output/cardinality/depth/retry/concurrency/time bounds with truncation evidence. |
| P14 Cancellation semantics | Met | Caller cancellation propagates; internal timeout/error remains typed; transport and stage regression tests. |
| P15 Typed observable errors | Met | Typed origins/outcomes, redacted logs/artifacts, structured CLI errors, no target prose in decisions. |
| P16 Performance claims | Met for repository workloads | BenchmarkDotNet baseline, target/local metric separation, Release regression budgets, controlled fixtures. |
| P17 Scoring governance | Met | Versioned constants/rules, immutable manifest, production-backed calibration, blocker caps, 100% reducer mutation score. |
| P18 Durable reports | Met | Versioned schemas, compatibility checks, bounded offline ingestion, atomic writes, signatures/digests, deterministic renderers. |
| P19 Versioned extension packs | Met for built-ins | Deterministic versioned protocol/rule/scenario/profile registries; duplicate/incompatible registrations fail closed. |
| P20 Boundary tests | Met for repository scope | Unit, contract, HTTP/STDIO integration, adversarial, fuzz, concurrency, package, schema, snapshot, and architecture suites. |
| P21 Reproducible supply chain | Met in workflow definition | Exact SDK/Node/version, locks, pinned Actions/base images, pre-publication tag reservation, deterministic archives, final registry-byte checks, signed dual-arch OCI promotion. Repository Actions are currently disabled, so the workflow controls cannot execute. |
| P22 Open-source governance | Blocked for publication | CODEOWNERS and single-owner procedures are defined, but historical live captures must be purged. The verified host state lacks administrator enforcement, required CodeQL/dependency checks, tag protection, `Ghcr`, secret scanning, and push protection. |

## Pipeline Review

- Immutable `v1.1.25` reservation occurs before registry publication.
- NuGet publication verifies repository-signed final bytes, payload identity, SHA-256, and provenance.
- npm publishes under a run candidate tag, verifies final bytes and registry signatures/provenance, then promotes a monotonic stable/preview tag.
- Standalone ZIP/tar archives have normalized metadata and are downloaded, extracted, and executed on all supported target runners.
- AMD64 and ARM64 container images are built for matching .NET RIDs, QEMU-smoked, assembled into an exact two-platform candidate manifest, signed/attested, then promoted monotonically.
- Existing release retries use a closed asset inventory and verify bytes plus Sigstore bundle and detached signature forms.
- Backfill requires an immutable tag, independently supplied manifest digest, tag/source/version equality, and manifest-bound checksum/SBOM digests.
- CodeQL and dependency review fail closed when repository features are unavailable.

## Validation Evidence

- Exact SDK: .NET `8.0.419`, roll-forward disabled.
- Full repository gate: 912/912 .NET Release tests, 14/14 npm tests, and TypeScript typecheck.
- Build: zero warnings and zero errors.
- Audits: no high/critical NuGet vulnerabilities; npm audit clean.
- Release performance and architecture: 11/11.
- Mutation: 26/26 production verdict mutants killed, 100%.
- Static pipeline checks: Actionlint, ShellCheck, composite metadata parsing, distribution contracts, deterministic archive comparison, and full diff hygiene pass.
- Open-source publication remains blocked by forbidden live-capture paths reachable in Git history and the need for fresh protected pull-request CodeQL results recorded in `docs/ReleaseHostConfiguration.md`.

## Enterprise Hosted Blockers

Repository primitives do not constitute a hosted deployment. M8 remains open until independent evidence proves:

1. Shared transactional API/job/artifact/index stores and tenant isolation.
2. Disposable HTTP/STDIO worker sandboxes with default-deny network egress.
3. Atomic deployed RBAC, quota, retention, legal-hold, deletion, and audit enforcement.
4. Backpressure, fairness, per-target rate/cost controls under load and failure injection.
5. Backup/restore/DR exercises, SLO dashboards, alerts, and runbooks.
6. Enforced branch/environment/release controls for the declared owner and required automated gates.
7. Signed external threat model, penetration test, and independent calibration review bound to the exact build/environment.

No hosted or certification-authority claim is permitted before those gates pass.
