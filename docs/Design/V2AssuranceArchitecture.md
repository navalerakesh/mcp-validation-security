# MCP Validator V2 Assurance Architecture

## Status And Intent

This document governs the V2 redesign. V2 is intentionally breaking because the project has no production compatibility obligation and the current `1.x` result conflates protocol conformance, security risk, evidence completeness, compatibility interpretation, and descriptive scoring.

V2 keeps one evidence collection pipeline. It does not preserve the V1 decision contract, score semantics, result schema, baselines, or exit policy.

## Official Authority

The design is grounded in sources reviewed on 2026-11-05:

| Source | Governing fact |
| --- | --- |
| MCP core `2026-07-28` | The TypeScript schema is protocol source of truth; BCP 14 requirement levels govern normative claims; the wire is stateless and capability-aware per request. |
| Official conformance requirements `2026-07-28` | Server and client legs are separate; frozen required scenarios determine conformance; not-scored, pending, extension, missing, and skipped evidence do not earn pass credit. |
| MCP extension lifecycle | Experimental, Beta, and Stable are distinct; extensions are opt-in and not core conformance unless promoted into a dated core revision. |
| MCP security best practices | Token audience, consent, SSRF, redirect, state, scope, and local execution are distinct security claims requiring directly relevant evidence. |
| MCP Apps Stable `2026-01-26` | Server content and metadata are observable; host sandbox, CSP enforcement, consent, and lifecycle behavior require host-side evidence. |
| Skills Beta at `649f7aee87d631ac4d1fdd3b12eef0c6264fdb3a` | Catalog/content consistency is server-observable; activation, approval, origin binding, cache isolation, and local execution prevention are host responsibilities. |

Immutable source revisions and SHA-256 digests remain owned by existing Core catalogs. Updating a source requires a reviewed digest change and fixture diff.

## Product Offerings

V2 defines three shipped assurance products over the same evidence and one planned product layer:

| Offering | Intended use | Required authority |
| --- | --- | --- |
| Inventory | Passive discovery, metadata, schema, and risk inventory | Safe mode |
| CI Assurance | Repeatable release gating on an authorized target | Standard mode plus policy |
| Provider Certificate | Core conformance, security, operations, extensions, and reproducibility | Provider profile plus controlled fixtures |
| Continuous Monitor (planned) | Signed repeated runs, drift, expiry, and baseline governance | Future scheduled provider profile |

Inventory output is never certificate-eligible. A report can be detailed while its assurance remains incomplete.

## Canonical Contract

The V2 canonical result is the only semantic source for all renderers and adapters:

```text
AssuranceResult
├── document identity and producer
├── target identity
├── immutable execution plan
├── claims[]
├── evidence[]
├── assessments
│   ├── coreConformance
│   ├── securityAssurance
│   ├── operationalReadiness
│   ├── extensionReadiness[]
│   ├── clientCompatibility[]
│   └── externalConformance
├── certificateEligibility
├── policyDecision
├── baselineComparison
└── audit references
```

V2 removes the global compliance score, L1-L5 headline trust level, `BaselineVerdict`, and the generic overall status as automation gates.

## Claim Model

Every conclusion is a typed claim:

```text
claimId
subject
dimension
requirementLevel
authority
source revision and digest
applicability
outcome
confidence
evidence references
remediation
```

Rules:

- Claims reference immutable evidence; they never embed target payloads.
- Specification authority requires an official source reference and applicable revision.
- A heuristic cannot claim specification nonconformance.
- Missing, skipped, blocked, unavailable, auth-required, inconclusive, and not-applicable remain distinct outcomes.
- Not-applicable requires a recorded applicability reason.
- Unknown values fail closed as review-required contract errors.

## Independent Assessments

### Core Conformance

Values: `conformant`, `nonconformant`, `incomplete`.

- Only applicable specification claims and pinned official conformance determine this assessment.
- A failed required MUST claim makes it nonconformant.
- Missing required evidence makes it incomplete.
- SHOULD and MAY claims remain visible but follow the selected dated requirement set.

### Security Assurance

Values: `verified`, `issuesFound`, `incomplete`.

- Demonstrated exploit or deterministic control failure can produce `issuesFound`.
- Metadata/name/content heuristics produce review claims by default.
- Organizations may promote a calibrated heuristic through explicit versioned policy.

### Operational Readiness

Values: `ready`, `notReady`, `incomplete`.

- Uses authorized repeated measurements against declared SLOs.
- A single best-effort public run is never operational certification.

### Extension Readiness

One assessment per extension, bound to lifecycle stage, revision, digest, applicability, and policy behavior. Extension results never modify core conformance.

### Client Compatibility

One interpretation per executable client profile. Documentation-only profiles are labeled `interpreted`; executable profiles are labeled `verified`. Client results never modify neutral assessments.

## Certificate Eligibility

Values: `eligible`, `ineligible`, `reviewRequired`.

Eligibility is a deterministic contract, not a score. A provider certificate requires:

- selected assurance profile is `provider`;
- core conformance is `conformant`;
- security assurance is `verified`;
- operational readiness is `ready`;
- all required extensions meet their selected policy;
- required official conformance evidence is verified and conflict-free;
- applicable evidence coverage and confidence meet the profile thresholds;
- repeated-run stability meets the profile threshold;
- no expired or invalid waiver exists;
- final artifacts are audit-bound and signed.

Initial provider thresholds are 100% required MUST coverage, at least 90% total applicable coverage, at least 90% evidence confidence, and 99% repeated-run consistency. These values live in a versioned profile manifest, not renderer code.

## Assurance And Execution Profiles

Execution authority and assurance intent are orthogonal:

```text
--mode safe|standard|elevated
--assurance inventory|ci|provider
```

- `inventory` requires safe mode and cannot issue a certificate.
- `ci` requires an authorized target and selected deterministic checks.
- `provider` requires controlled active evidence and signed output.

A future `monitor` profile will require a previously approved provider certificate and drift policy. It is not accepted by the 2.0.0 CLI.

The execution plan names every request class, mutation risk, selected pack, required evidence declaration, and output before target contact.

## Pack Contract

All protocol, security, extension, compatibility, and provider packs implement one contract:

```text
Descriptor
Applicability
Evidence requirements
Execution-plan contribution
Evidence collection
Claim projection
```

Packs do not calculate global scores, render reports, determine process exit codes, or contact undeclared targets. Dynamic in-process plugins remain prohibited. Future third-party packs require signed isolated processes, declared capabilities, resource limits, and explicit network/mutation authority.

## Dependency And Cryptography Policy

[V2DependencyStrategy.md](V2DependencyStrategy.md) governs package admission, official MCP reuse, cryptographic boundaries, supply-chain controls, and dependency failure behavior. V2 adds no package unless it closes a documented security or standards gap and passes the repository's provenance, adoption, maintenance, licensing, isolation, and vulnerability gate.

## Reliability And Non-Happy Paths

The following are first-class outcomes and test obligations:

- target unavailable, throttled, redirected, rebound, or authentication-required;
- malformed, oversized, deeply nested, duplicate, stale, or contradictory evidence;
- cancellation before, during, and after a target request;
- partial official conformance output and harness failure;
- stale baseline, stale waiver, changed policy, changed source digest, and changed extension lifecycle;
- conflicting native and external evidence;
- report replay from a saved canonical result with no target access;
- interrupted artifact persistence and signature failure.

No retry, fallback, or renderer is allowed to convert these states into a pass.

## Output And Accessibility Contract

- Canonical JSON is authoritative.
- Console presents certificate eligibility, independent assessments, coverage, confidence, and next action before scores or inventory.
- HTML is offline, keyboard-readable, structurally headed, table-labeled, printable, and contains no target-controlled executable content.
- Markdown mirrors the assessment order.
- SARIF contains actionable non-success claims only.
- JUnit uses separate suites per assessment lane and represents incomplete evidence as skipped/review, not success.
- XML preserves claim IDs, authority, outcome, and evidence references.
- GitHub Action and MCP wrapper consume the same canonical result and exit contract.

## Exit Contract

| Exit | Meaning |
| ---: | --- |
| `0` | Selected policy and certificate contract satisfied |
| `1` | Deterministic assessment or certificate failure |
| `2` | Incomplete or review-required evidence |
| `64` | Usage or configuration error |
| `70` | Validator/runtime integrity failure |
| `75` | Target unavailable |
| `124` | Timeout or cancellation |

## Accuracy And Calibration

Every default-blocking rule publishes precision, recall, false-positive rate, false-negative rate, labeled sample count, supported revisions/transports, and known blind spots. A rule cannot become a default blocker without meeting the selected policy's calibration threshold.

The corpus includes official SDKs, provider-shaped schemas, benign destructive tools, malicious tools, auth gateways, proxies, unavailable/throttled targets, Apps, Skills, Tasks, and protocol-revision fixtures. Mutation testing covers reducers, claim projection, certificate eligibility, and security-sensitive classifiers.

## Ownership And Code Map

| Ownership | V2 responsibility |
| --- | --- |
| Core | Claims, evidence references, independent assessment contracts, certificate profile/eligibility, policy inputs, exit semantics |
| Compliance.Spec | Immutable official schemas and source metadata |
| Infrastructure | Evidence collection, pack implementations, claim projectors, reducers, official-run import/execution, parsers |
| ClientProfiles | Compatibility interpretation of completed neutral evidence only |
| CLI | Composition, plan admission, persistence, rendering orchestration, final exit mapping |
| Fleet | Host-neutral scheduling, quotas, leases, retention, and governance contracts |
| Adapters | Canonical contract projection only; no forked semantics |

## Delivery Order

1. Publish V2 contracts, schemas, exit policy, and breaking-change record.
2. Add neutral claims while preserving existing probes.
3. Implement independent reducers and certificate eligibility.
4. Replace V1 canonical result and report projections.
5. Add official conformance execution and provenance wrapper generation.
6. Add provider harness manifests and controlled security/auth/operations certificates.
7. Expand labeled calibration and publish accuracy metrics.
8. Add executable client compatibility.
9. Run full release, accessibility, package, live-safe, and controlled-provider gates.

## Definition Of Done

V2 is complete only when the old global decision contract is absent from public artifacts and automation, every output consumes independent canonical assessments, provider certification fails closed on incomplete evidence, official sources are pinned, accuracy thresholds are measured, and the full repository/release gate passes.
