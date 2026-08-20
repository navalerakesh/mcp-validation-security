# MCP Validator Enterprise Renewal Execution Plan

**Status:** Active on `Upgrade_18Aug2026`
**Started:** 2026-08-18
**Authority:** [DESIGN-PRINCIPLES.md](../../DESIGN-PRINCIPLES.md)
**Assessment baseline:** [ENTERPRISE-READINESS-AUDIT.md](../../ENTERPRISE-READINESS-AUDIT.md)

## North Star

MCP Validator is the deterministic evidence engine that tells a person, CI system, agent, registry, or governance service:

1. what MCP surface was actually reachable;
2. which protocol and security requirements were tested;
3. what was observed, with bounded and redacted evidence;
4. what passed, failed, was blocked, or remains unknown;
5. which client and deployment assumptions affect usability;
6. whether a release policy should pass;
7. how to reproduce and compare the decision.

The product wins through **trustworthy evidence**, not the number of checks or the appearance of certainty.

## Product Boundary

### In scope

- Current and supported historical MCP protocol conformance.
- HTTP and STDIO behavior under explicit execution policy.
- Authentication discovery, supplied credentials, and modern MCP authorization evaluation.
- Security, AI-facing safety, operational readiness, performance, and error behavior.
- Deterministic scoring, policy gates, compatibility profiles, reproducible reports, and CI consumption.
- Local CLI, local MCP adapter, GitHub Action, container, and reusable engine contracts.
- Signed and versioned evidence suitable for registry or governance ingestion.

### Not in scope

- Formal certification or accreditation without independent governance.
- Malware analysis, source-code SAST, dependency SCA, or container scanning already owned by specialist tools.
- Silent execution of arbitrary local server commands.
- A multi-tenant hosted service until worker isolation, durable jobs, tenant controls, and SLOs exist.
- LLM judgment as canonical verdict authority.

## Directions To Stop

1. **No score-first development.** Add evidence and classification semantics before weights or UI.
2. **No “latest” alias without current support.** A dated embedded profile is always disclosed.
3. **No missing-evidence pass.** Unreachable, auth-required, rate-limited, unsupported, truncated, and unimplemented are not success.
4. **No agent-triggered arbitrary STDIO by default.** Local execution requires a separate explicit authority.
5. **No application-only SSRF promise.** Hosted safety requires network-enforced egress.
6. **No shell invocation.** Structured process arguments are mandatory.
7. **No mutable singleton run state.** Every run owns its transport and policy state.
8. **No public contract by serializer accident.** Machine artifacts have schemas and versions.
9. **No broad hosted-service build before engine truth.** Current protocol, safe boundaries, and calibrated verdicts come first.
10. **No copied external implementation.** External research informs requirements and tests; repository code remains independently designed and attributed only where a dependency license requires it.

## 5/5 Release Standard

| Dimension | 5/5 exit criteria |
| --- | --- |
| Product usefulness | CLI, MCP adapter, Action, and container complete one documented local/CI workflow with equivalent canonical evidence |
| Protocol currency | Current stable revision plus declared supported historical revisions; modern/legacy era tests; no silent fallback |
| Evidence and reporting | Versioned JSON/SARIF/audit schemas; deterministic replay; every conclusion links to typed evidence and coverage |
| Validator security | No known high/critical dependency or boundary finding; DNS/redirect egress defense; no shell; explicit STDIO authority |
| Correctness/calibration | Public corpus with expected outcomes; no known wrong-behavior tests; measured false-positive/negative review |
| Test engineering | Unit, contract, integration, adversarial, fuzz, package, and live-certificate suites; coverage and mutation non-regression |
| Performance | Repeatable p50/p95/p99 benchmarks for engine overhead and probe workloads with recorded environment |
| Single-process scalability | Concurrent runs have isolated state; deterministic merge; bounded memory, I/O, requests, and processes |
| Hosted scalability | Durable idempotent jobs, isolated workers, quotas, backpressure, encrypted artifacts, and horizontal API nodes |
| Extensibility | Versioned pack manifests, deterministic conflict handling, stable out-of-process extension contract |
| Supply chain | Locked test/release graph, zero unaccepted high/critical findings, final-byte SBOM/provenance/signature |
| Governance | Explicit single-owner CODEOWNERS policy, signed sensitive-change approval, mandatory automated gates, private bypass reporting, schema/scoring change governance |

A dimension is not 5/5 because code exists. It is 5/5 only when its evidence gate passes on the release candidate.

## Target Architecture

```text
Hosts
  CLI | local MCP adapter | GitHub Action | future API
                         |
                  IValidationEngine
                         |
     immutable ValidationRunRequest + OperationPolicySnapshot
                         |
  admission -> transport era -> session/auth -> evidence collectors
                         |
             immutable category outcomes
                         |
              deterministic result reducer
                         |
      verdict + policy + client interpretation
                         |
        canonical versioned result documents
                         |
               pure artifact renderers

Boundary implementations
  HTTP transport session -> redirect/DNS/egress policy
  STDIO transport session -> consent/sandbox/process policy
  Credential providers -> secret references and scoped resolution
  Artifact store -> atomic writes, retention, integrity
```

## Scalability Invariants

These rules apply to the local engine now and to hosted execution later.

1. **Topology is elastic.** No workflow assumes one process, one worker, one queue, one region, one artifact store, or one target at a time.
2. **Canonical identity is opaque and globally unique.** Run, attempt, request, probe, finding, artifact, and rule identities do not encode machine location or mutable state.
3. **The engine is stateless between runs.** Durable state is accessed through ports; caches and in-memory registries are disposable accelerators.
4. **Every operation is idempotent or explicitly non-replayable.** Durable idempotency keys and effect receipts protect retries and crash recovery.
5. **Admission precedes allocation.** Work receives a validated immutable policy snapshot, tenant/owner context, target classification, and capacity envelope before dispatch.
6. **Backpressure is explicit.** Queues and worker pools are bounded; saturation yields a typed retry/suspension outcome rather than memory growth or dropped work.
7. **Fairness is policy-owned.** Per-tenant, per-target, and global concurrency/rate controls prevent one workload from starving others.
8. **Limits are configuration-owned.** Request counts, concurrency, response sizes, stream duration, retries, artifact size, retention, and queue depth are named validated policies with provenance. Algorithms contain no unexplained operational literals.
9. **Provider limits and operator budgets remain distinct.** Physical request constraints do not become hidden total-work caps. Total cost/work limits require an explicit operator or tenant policy.
10. **Evidence is incrementally durable.** Large runs stream or batch bounded records; they do not require the full target catalog, payload corpus, or report document in memory.
11. **Reduction is deterministic and partitionable.** Category outcomes and findings merge by stable identity and ordering, producing the same canonical result regardless of worker count or completion order.
12. **Artifacts are content-addressable or integrity-stamped.** Large artifacts can move to object storage without changing canonical references.
13. **Observability cardinality is governed.** Metrics avoid unbounded target, tool, prompt, resource, tenant, or error text labels; detailed identity remains in traces/logs under retention policy.
14. **Configuration changes are revisioned.** A run consumes one immutable revision; scaling or policy changes affect new work unless an explicit emergency revocation is recorded.
15. **Capacity is measured.** Scale claims name workload mix, payload distribution, worker resources, concurrency, queue delay, throughput, p95/p99 latency, error rate, and cost.

### No-hardcoded-value rule

- Protocol constants and normative wire values belong to versioned specification owners.
- Security ranges and URL policy belong to a tested network policy owner.
- Operational defaults belong to typed configuration with documented rationale and source.
- Report strings and remediation text belong to resource/rule metadata.
- Test fixtures may use literals, but production admission and finality must not depend on fixture topology.
- A value that changes by environment, deployment, tenant, target, protocol revision, or capacity tier cannot be an inline production constant.

### Scale certification ladder

| Level | Required evidence |
| --- | --- |
| S1: isolated local runs | Concurrent runs share no mutable state and remain deterministic |
| S2: single worker saturation | Bounded memory/handles, backpressure, cancellation, and stable throughput under mixed targets |
| S3: multi-worker partitioning | Idempotent dispatch, deterministic merge, crash recovery, no duplicate active effects |
| S4: tenant scale | Fair scheduling, quotas, authorization, encrypted isolation, cardinality and cost controls |
| S5: regional resilience | Horizontal scale, failover, backup/restore, SLOs, load shedding, and disaster-recovery evidence |

No level may be claimed from architecture prose alone.

### Ownership map

| Owner | Required end state |
| --- | --- |
| Core | Immutable run request/context, canonical findings, coverage, outcomes, schema-versioned documents, policy ports |
| Compliance Spec | Current/historical schemas, protocol-era metadata, feature lifecycle and applicability |
| Infrastructure | Run-scoped transports, auth, probes, validators, reducers, scoring, renderers |
| Client Profiles | Dated and sourced interpretation rules over completed neutral evidence only |
| CLI | One command catalog, composition, terminal UX, exit codes, explicit artifacts |
| Local MCP adapter | Remote validation by default; explicit local-execution authority; structured MCP results |
| Tests | Corpus, boundary adversarial tests, contracts, packages, performance, live certificates |
| Workflows | Locked build/release, vulnerability gates, reproducible multi-channel publication |

## Implementation Order

Dependencies flow downward. A later milestone cannot compensate for an open earlier gate.

### M0: Baseline And Containment

**Goal:** make the branch safe to develop and make claims truthful.

- [ ] Commit design principles, audit, and this execution authority (operator-controlled; no commit created by the renewal agent).
- [x] Remove tracked operational session/cookie/live-report artifacts and replace permanent evidence with synthetic fixtures or secret-free summaries.
- [x] Add sensitive-artifact patterns and local/CI repository hygiene checks. Hosted push protection remains a repository-administration control.
- [x] Replace all auth CLI shell/string execution with structured arguments.
- [x] Redact Action command output and bind Action revision to CLI version.
- [x] Serialize release version production.
- [x] Upgrade npm dependency graph until high-severity audit passes.
- [x] Make `latest` resolve visibly to the current embedded protocol revision.
- [x] Make local STDIO unavailable from the MCP adapter unless explicit local execution is enabled.

**Tests:** command-injection corpus; canary token output test; npm/NuGet audit; Action shell tests; docs/help assertions.

**Exit:** Phase 0 exit gate in the audit passes.

### M1: Run Isolation And Boundary Safety

**Goal:** one run cannot affect another or escape its declared network/process envelope.

- [x] Introduce immutable `ValidationRunRequest` and `OperationPolicySnapshot`.
- [x] Create run-scoped HTTP and STDIO transport sessions.
- [x] Replace shared result mutation with immutable outcomes and deterministic merge.
- [x] Fix semaphore ownership and cancellation.
- [x] Implement typed bounded reads and explicit truncation evidence.
- [x] Disable automatic redirects and validate every destination.
- [x] Resolve and classify every address, including IPv4-mapped IPv6 and cloud metadata ranges.
- [x] Route raw OAuth metadata requests through the same outbound policy.
- [x] Allowlists cover scheme, host, port, resolved address, redirect, and auxiliary auth endpoints.
- [x] STDIO receives an allowlisted environment, bounded output, deadline, and process-tree cleanup. Disposable sandbox enforcement remains a deployment requirement and is not claimed by the local engine.

**Tests:** DNS/redirect/encoding adversarial matrix; concurrent-run isolation; cancellation at each await; response bombs; child-process cleanup.

**Exit:** no boundary bypass in the adversarial suite and no mutable cross-run singleton state.

### M2: Current Protocol And Era Support

**Goal:** validate current stable MCP and supported historical revisions without ambiguity.

- [x] Vendor and register the current stable schema with immutable blob verification.
- [x] Add explicit `legacy`, `modern`, and `auto` transport selection.
- [x] Add modern discovery and per-request version/capability/identity metadata.
- [x] Validate modern HTTP headers and typed protocol errors.
- [x] Add result-type and multi-round-trip request validation.
- [x] Add modern subscription behavior.
- [x] Add cache metadata and deterministic-list checks.
- [x] Add extension negotiation and tasks-extension coverage.
- [x] Add JSON Schema 2020-12 tool input/output validation and resource bounds.
- [x] Model deprecation separately from failure.
- [x] Publish an exact protocol/transport/feature coverage matrix.

**Tests:** composable legacy, modern, dual-era, malformed, auth-required, and partially implemented fixture servers over HTTP and STDIO.

**Exit:** current and historical era certificates pass and `latest` resolves truthfully.

### M3: Authentication Assurance

**Goal:** authenticate extensively without confusing possession of a token with proof of correct authorization.

- [x] Model supplied bearer tokens as secret references and never persist them.
- [x] Validate protected-resource metadata and all authorization-server metadata discovery variants.
- [x] Validate HTTPS, issuer consistency, resource indicators, audience binding, PKCE S256, redirect exactness, and state behavior.
- [x] Evaluate 401/403 challenge syntax, required scopes, step-up behavior, retry limits, and least privilege.
- [x] Detect token passthrough indicators and confused-deputy conditions using safe, discriminating probes.
- [x] Support static client registration, metadata-document registration, and declared legacy registration behavior.
- [x] Add enterprise-managed authorization extension discovery/evaluation.
- [x] Separate auth acquisition from server conformance; inability to acquire credentials is not server failure.
- [x] Produce a redacted auth coverage document showing what was and was not exercised.
- [x] Expose noninteractive credential-provider contracts for CI and orchestrators.

**Tests:** mock authorization/resource servers; wrong issuer/audience/resource/state; scope step-up; rotation/expiry; metadata SSRF; no-secret artifact assertions.

**Exit:** every supported auth flow has positive, negative, cancellation, and redaction certificates.

### M4: Evidence, Verdict, And Realistic Scoring

**Goal:** make trust defensible and resistant to score inflation.

- [x] Define one closed outcome taxonomy (availability/inconclusive/cancellation corrections are active; full consolidation remains open).
- [x] Remove string-parsed decisions and implicit pass defaults.
- [x] Version every rule, weight, threshold, cap, and policy pack.
- [x] Prevent confirmed critical security/protocol blockers from being averaged away while avoiding caps from unevaluated dimensions.
- [x] Add trust evidence completeness, unevaluated dimensions, and an incomplete-evidence ceiling.
- [x] Build a public calibration corpus with compliant, vulnerable, malformed, unavailable, throttled, and auth-protected targets.
- [x] Add differential/golden scoring tests and mutation testing.
- [x] Publish calibration methodology, limitations, and reviewed false-positive/negative cases.
- [x] Keep model evaluation advisory, provider-explicit, redacted, and separately costed.

**Tests:** exact verdict tests for every outcome combination; metamorphic tests; score monotonicity; corpus snapshots; mutation threshold.

**Exit:** no known wrong classification and every score can be reconstructed from versioned evidence.

**Closure evidence:** 795/795 .NET tests; six production-backed HTTP calibration classes with exact verdict/score goldens; Stryker.NET 4.16.0 production reducer certificate 26/26 killed (100%, 90% gate); independent closure review approved with no blockers.

### M5: Stable Consumption Contracts

**Goal:** humans and automation consume reports without scraping prose.

- [x] Publish versioned schemas for canonical result, audit manifest, profile summary, and advisory companions.
- [x] Add schema compatibility and migration policy.
- [x] Add baseline comparison, waiver owner/reason/scope/expiry, and regression-only policy.
- [x] Include validator/rule/profile/config/target-artifact digests and evidence completeness.
- [x] Use atomic artifact writes and approved output-root enforcement.
- [x] Add structured stdout mode and stable typed stderr error envelopes.
- [x] Make local MCP tools return structured content in addition to concise text.
- [x] Keep Markdown/HTML executive views aligned with canonical data and accessible offline.
- [x] Add signed validation attestations for downstream governance.

**Tests:** JSON Schema validation; backward consumer fixtures; hostile HTML/text; atomic-write failure; deterministic replay; CLI exit-code matrix.

**Exit:** supported consumers operate across minor upgrades using documented schemas.

**Closure evidence:** 829/829 .NET tests; 11/11 local MCP tests and successful TypeScript build; real structured CLI matrix exits 0/2/64/64 with one JSON line on the correct stream; backward `1.0` consumer fixture; P-256 final-byte attestation/tamper certificate; independent closure review approved with no blockers.

### M6: CI/CD And Distribution Excellence

**Goal:** one tested commit produces every reproducible channel artifact.

- [x] Use one exact release version and serialize publication.
- [x] Restore runtime-aware locks and publish without graph changes.
- [x] Pin the Action to the matching CLI package version.
- [x] Smoke-test installed NuGet tool, npm adapter, container, standalone binaries, and Action.
- [x] Generate final-byte checksums, SBOMs, provenance, signatures, and verification instructions.
- [x] Publish stable and preview channels for protocol-era changes.
- [x] Add reusable CI examples for advisory, regression, and strict admission gates.
- [x] Emit summaries, annotations, SARIF, JUnit, and machine JSON consistently.

**Tests:** clean-runner install tests across Linux/macOS/Windows; artifact digest comparison; rollback and retry simulation.

**Exit:** every channel is traceable to the same tested source and dependency graph.

**Closure evidence:** exact version `1.1.24` synchronized across .NET/npm/Action/container contracts; repository gate 829/829 .NET and 11/11 npm plus build/audits; installed standalone, local-only NuGet, and packed npm initialize certificates; four native standalone and three-OS package/Action smoke jobs; serialized byte-aware NuGet → npm → OCI → tag/release graph; checksums/SBOM/provenance/Sigstore/cross-channel manifest; actionlint, shellcheck, retry simulations, and independent closure review approved. Container execution is certified in the CI Docker job because the local Docker daemon was unavailable.

### M7: Performance, Reliability, And Observability

**Goal:** measure the validator separately from the target and make failures diagnosable without leaking data.

- [x] Add a benchmark project for startup, planning, reduction, scoring, and rendering overhead.
- [x] Record p50/p95/p99 target latency, validator overhead, queue time, retry delay, throughput, and error rate.
- [x] Add controlled performance fixtures and non-flaky regression budgets.
- [x] Add OpenTelemetry traces/metrics with redacted attributes and stable semantic conventions.
- [x] Add run correlation, rule timing, request budget, retry, truncation, and coverage metrics.
- [x] Define health/readiness behavior for future workers without treating health as conformance.
- [x] Add soak, high-cardinality, slow-stream, cancellation, and resource-exhaustion tests.

**Exit:** performance claims are reproducible and every stalled/failed stage is attributable from safe telemetry.

**Closure evidence:** production-backed BenchmarkDotNet project and reviewed baseline (production host construction 66.488 ms / 151.52 KB); bounded canonical operational metrics and safe OpenTelemetry correlation across governed HTTP SDK/direct and real STDIO paths; exact cross-transport transport-failure, retry, queue, response-rejection, cancellation, and evidence/rule timing semantics; complete schema/Markdown/HTML projection; ten-times-baseline per-operation Release CI budgets; repository gate 852/852 .NET and 11/11 npm with zero build warnings/errors and clean audits; actionlint clean; independent closure review approved with no blockers.

### M8: Enterprise Scale And Governance

**Goal:** make hosted/fleet use a distinct, correctly isolated product mode.

**Progress:** the repository-side Fleet foundation is complete: isolated dependency direction; tenant/principal-bound state transitions; encrypted atomic single-node persistence with idempotency, leases, retries, cancellation, dead-letter and recovery; persistence-bound RBAC and mutation audit; quota/retention/legal-hold evaluators; fair/rate-aware scheduling; exact transport-pool dispatch; keyed audit checkpoints; SLO evaluation; signed owner approvals; and ECDSA-signed deployment/review evidence bound to exact build/environment digests. Hosted readiness deliberately remains false until a deployed composition proves atomic quota/retention/audit enforcement and the external evidence below exists; see `docs/Design/FleetHostedMode.md`.

- [ ] Durable idempotent job model with leases, retries, cancellation, and crash recovery.
- [ ] Tenant-scoped identity/RBAC, quotas, encryption, retention, deletion, legal hold, and audit.
- [ ] Separate disposable HTTP and STDIO worker pools with network-enforced egress.
- [ ] Queue backpressure, fairness, per-target rate policy, and cost controls.
- [ ] Horizontally scalable API and encrypted artifact/index stores.
- [ ] SLOs, alerts, dashboards, runbooks, backup, restore, and disaster-recovery exercises.
- [x] Explicit single-owner governance with CODEOWNERS, signed owner approval for auth/transports/scoring/schemas/releases, mandatory automated gates, and documented recovery.
- [ ] External threat model, penetration test, and independent calibration review.

**Exit:** hosted claims begin only after tenant isolation, recovery, and independent review certificates pass.

**Repository evidence:** exact version `1.1.25`; SDK `8.0.419` with roll-forward disabled; repository gate 912/912 .NET Release tests and 14/14 npm tests with zero build warnings/errors; Release performance/architecture 11/11; verdict mutation 26/26 killed (100%); Fleet dependency, authorization, persistence, audit, scheduling, signed-evidence, and adversarial tests; bounded/culture-independent canonical artifacts; fail-closed pipelines with immutable tag reservation, monotonic mutable channels, exact registry-byte checks, deterministic standalone archives, and AMD64/ARM64 container contracts. The known 156-path legacy history is bound to a reviewed 181-object digest baseline, and protected pull-request CodeQL results are required before merge.

**External/deployment blockers:** no shared transactional Fleet API/job/artifact/index stores; no disposable sandboxed worker deployment with network-enforced egress; no atomic deployed quota/retention/audit integration or load/failure-injection certificate; no backup/restore/DR exercise, dashboards, alerts, or runbooks; no signed external threat model, penetration test, or independent calibration review for the exact build/environment. Seven checkboxes remain open by design.

**Design review:** `docs/Design/RenewalDesignPrinciplesReview.md` maps P0-P22, pipeline controls, executable evidence, and hosted non-claims. `docs/Design/SingleOwnerGovernance.md` defines owner approval, CI/CD controls, key rotation, compromise recovery, and continuity.

## Live Milestone Certification

Live tests run only from a frozen build and only after deterministic fixtures pass.

### Public remote certificate

Latest reviewed summary: `docs/Resources/LiveCertificate-1.1.25-MicrosoftLearn.md`.

Cross-transport release-candidate matrix: `docs/Resources/ReleaseCandidateMatrix-1.1.25.md`.

- Target: Microsoft Learn MCP public endpoint.
- Run health check, discovery, then full safe validation.
- Record exact command, validator version, protocol profile, source revision, time, and environment.
- Review canonical JSON first; verify report/verdict/coverage consistency.
- Confirm no target payload or identifier is leaked beyond intended redacted evidence.
- Delete generated live artifacts after recording a secret-free summary in the milestone evidence.

### Authenticated remote certificate

- Use only an already-authorized, noninteractive credential provider.
- Never request credentials through chat, print them, or use VS Code account login.
- Confirm target authorization and scope before active probes.
- Run health/discovery/validation with conservative safe-mode policy.
- Verify token absence from process output, logs, JSON, SARIF, Markdown, HTML, and audit artifacts.
- Revoke temporary credentials where applicable and delete local artifacts.

If safe credential acquisition or target authorization cannot be proved, the authenticated certificate is blocked, not bypassed.

## Documentation Synchronization

Every milestone updates in the same change:

- CLI help and examples;
- README, Quickstart, Feature Matrix, troubleshooting, and architecture;
- configuration and result schemas;
- security guidance and limitations;
- Action, npm adapter, container, and package documentation;
- changelog and migration guidance;
- this plan's status and evidence links.

## Open-Source Artifact Hygiene

Generated and live-test material is untrusted until proven otherwise.

- Temporary downloads, package probes, live reports, HTTP captures, auth metadata, benchmark raw data, and generated credentials use the operating system temporary directory or a dedicated ignored workspace path.
- Live headers, cookies, tokens, authorization codes, session IDs, account identifiers, private endpoint names, and machine-specific paths are never committed, even when believed expired or redacted.
- Reports intended as permanent fixtures are regenerated from synthetic inputs and reviewed for identity, path, time, host, and secret leakage.
- Build outputs, test results, coverage, benchmark output, package archives, container exports, logs, and validation artifacts remain ignored unless a specific synthetic golden file is an intentional reviewed contract.
- Research downloads and external source trees never enter the repository.
- Cleanup uses explicit known paths. Broad destructive cleanup commands are forbidden.
- No commit is created by the renewal agent unless the operator explicitly requests it.

### Mandatory pre-commit gate

1. Record `git status --short` before and after every live or package-producing certificate.
2. List every new/modified tracked path and classify it as source, test, documentation, configuration, schema, or intentional synthetic fixture.
3. Confirm no ignored artifact has been force-added.
4. Scan the candidate diff for tokens, authorization headers, cookies, session IDs, private keys, credentials, absolute home paths, and live response payloads.
5. Verify generated temporary paths no longer exist or remain only outside the repository/under ignored directories.
6. Review the complete diff for unrelated user changes and preserve them.
7. Run the milestone's executable tests from the clean candidate tree.

Any unexplained file blocks the milestone and any future commit.

## Completion Rule

The renewal is complete only when all applicable 5/5 exit criteria have current executable evidence. A passing test count, polished report, high trust score, or successful live target run cannot close an unmet security, protocol, reliability, or contract gate.
