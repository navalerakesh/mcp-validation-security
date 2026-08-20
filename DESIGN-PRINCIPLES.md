# MCP Validator Design Principles

> These principles govern MCP Validator (`mcpval`) as an open-source validation and security assessment product. They apply to the .NET engine and CLI, the local MCP wrapper, the GitHub Action, containers, reports, schemas, tests, and release automation.

Last reviewed: 2026-08-18

## Status And Interpretation

- **MUST**, **MUST NOT**, **SHOULD**, and **MAY** use RFC 2119 meanings.
- A principle describes the required product contract. Existing violations are defects or tracked architecture debt, not exceptions that silently redefine the principle.
- Security, verdict, and compatibility claims MUST be supported by reproducible evidence. Documentation and test count alone are not proof.
- MCP Validator is an assessment tool, not an accreditation authority. Reports MUST NOT imply formal certification unless an independently governed certification program exists.

## P0: Findings Stay Visible

When work reveals a correctness, security, privacy, reliability, or product-truth issue, it MUST be fixed in scope or recorded in the repository issue tracker with impact and reproduction evidence. A broad cleanup obligation MUST NOT turn a focused change into an unreviewable patch.

- Root causes are preferred over compatibility branches and repeated local workarounds.
- Disabled, incomplete, or heuristic checks MUST be represented as coverage gaps; they MUST NOT silently earn passing evidence.
- Tests that document a known bug MUST be skipped with a linked issue or made to fail until the bug is fixed. A passing assertion for known-wrong behavior is forbidden.

## P1: Evidence Before Verdict

Every verdict, score, trust level, policy outcome, and client-profile interpretation MUST derive from typed findings and explicit coverage evidence.

- Transport failure, authentication required, rate limiting, unavailable capability, unimplemented coverage, and a tested failure are distinct outcomes.
- Missing or inconclusive evidence MUST NOT be converted into a pass.
- Deterministic verdicts are the automation gate. Weighted scores and L1-L5 trust levels are summaries for review, not substitutes for blocking findings.
- Model-assisted evaluation remains advisory and in a separate artifact unless it becomes reproducible, versioned, and independently calibrated.
- Findings MUST include a stable rule ID, authority (`spec`, `guideline`, or `heuristic`), severity, tested scope, evidence, confidence, and remediation.

## P2: Protocol Fidelity And Versioning

MCP behavior MUST be evaluated against the protocol version negotiated or explicitly selected for the run.

- `Mcp.Compliance.Spec` is the repository source for embedded versioned schemas and protocol metadata.
- The official MCP SDK is the preferred wire implementation. Raw probes are acceptable when deliberately testing malformed or boundary behavior that an SDK cannot express.
- A raw probe MUST record the exact protocol expectation and expected response class.
- Rules MUST cite the applicable specification version and requirement level. A best practice MUST NOT be reported as an MCP MUST violation.
- Adding a protocol version requires schemas, registry metadata, applicability tests, and regression coverage for version-specific behavior.
- Unsupported protocol features or transports MUST be reported explicitly and excluded from positive coverage.

## P3: Clean Dependency Direction

The approved project dependency direction is:

```text
Mcp.Benchmark.Core        Mcp.Compliance.Spec
    ^                         ^
    |                         |
    +--- Mcp.Benchmark.Infrastructure
    |                ^
    |                |
Mcp.Benchmark.ClientProfiles   |
    ^                       |
    +---- Mcp.Benchmark.CLI-+

mcpval-mcp -> published mcpval CLI process
GitHub Action / container -> published mcpval host
Mcp.Benchmark.Fleet -> separate host-neutral fleet contracts; no existing product layer depends on Fleet
```

More precisely:

- `Mcp.Benchmark.Core` owns host-neutral contracts, configuration, result models, abstractions, and invariants. It MUST NOT depend on CLI or Infrastructure.
- `Mcp.Compliance.Spec` owns embedded schemas and version metadata. It MUST NOT depend on a host.
- `Mcp.Benchmark.ClientProfiles` interprets neutral evidence and MUST NOT mutate validator findings.
- `Mcp.Benchmark.Infrastructure` owns transports, authentication, probes, validators, scenarios, scoring implementations, and report implementations.
- `Mcp.Benchmark.CLI` is the composition root and owns argument binding, terminal UX, artifacts, and process exit policy. Command handlers SHOULD depend on Core abstractions; Infrastructure construction belongs in composition.
- `mcpval-mcp`, the GitHub Action, and containers are adapters. They MUST NOT fork validation or scoring semantics.
- `Mcp.Benchmark.Fleet` owns hosted job/governance contracts only. Core, Compliance.Spec, ClientProfiles, Infrastructure, and CLI MUST NOT depend on Fleet. A future hosted composition root MAY depend on Fleet and the public engine abstractions.

Architecture tests MUST enforce these boundaries.

## P4: Run-Scoped State And Composition

Each validation run MUST have isolated mutable state.

- Authentication, MCP session ID, negotiated version, request count, execution policy, concurrency limiter, artifacts, and cancellation MUST NOT leak between runs.
- Services with mutable run state MUST be scoped per run or accept immutable run context explicitly. Singleton registration is allowed only for stateless, thread-safe services.
- Validators receive dependencies through interfaces. Concrete transport, storage, and reporting construction belongs in a host composition root.
- Library entry points MUST be asynchronous; process startup or network I/O MUST NOT be blocked synchronously during dependency construction.

## P5: Structured Findings Are The Canonical Contract

Validators MUST emit structured findings before rendering.

- Scoring, verdicts, SARIF, Markdown, HTML, JUnit, XML, console output, and client profiles consume the same canonical result.
- Business decisions MUST NOT parse human-readable finding text.
- Renderers MUST NOT contact the target or recompute validation.
- Report schemas require an explicit schema version and documented compatibility policy.
- Offline rendering from a saved canonical result MUST preserve verdict and evidence semantics.

## P6: Determinism And Reproducibility

Given the same canonical evidence, rule-pack revisions, protocol profile, policy, and client-profile revisions, derived outputs MUST be identical.

- Results record tool version, schema version, selected/negotiated MCP version, rule-pack revisions, configuration digest, target identity after redaction, start/end times, and environment facts relevant to interpretation.
- Wall-clock timing and live target behavior may vary; derived classification from captured evidence MUST not.
- Randomized and fuzz probes record their seed.
- Benchmark comparisons require a declared environment, workload, warm-up, sample count, percentiles, and raw measurements.

## P7: External Data Is Untrusted

Every target response, redirect, header, OAuth metadata document, schema, tool description, prompt, resource, config file, saved result, and subprocess output is untrusted.

- Validate shape and size before acting on data.
- Avoid broad type assertions and catch-all fallbacks that turn malformed input into plausible evidence.
- Parse failures produce typed blocked or inconclusive evidence, not empty successful values.
- Logs and reports use bounded, redacted previews rather than raw untrusted payloads.
- HTML and terminal renderers MUST encode untrusted content for their output context.

## P8: Network Target Isolation And SSRF Defense

Active validation intentionally contacts untrusted endpoints, so outbound policy is a security boundary.

- Remote targets default to HTTPS. Plain HTTP requires an explicit operator decision appropriate to the execution mode.
- Host allowlists apply to every initial request, SDK request, retry, redirect, OAuth discovery request, and resource fetch.
- Private, loopback, link-local, multicast, unspecified, and cloud-metadata address ranges are denied by default for IPv4 and IPv6.
- Hostname checks MUST resolve DNS and validate every returned address. Redirect destinations MUST be revalidated. DNS rebinding MUST NOT bypass the decision.
- URL user-info, ambiguous IP encodings, unsupported schemes, and unsafe ports are rejected or require elevated policy.
- Request budgets count actual outbound attempts, including retries and auxiliary discovery.

## P9: STDIO Targets Are Arbitrary Code

A STDIO target is a local executable, not merely a protocol endpoint.

- The CLI and MCP wrapper MUST clearly state before execution that a STDIO command can run arbitrary code with the caller's privileges.
- Shells MUST NOT be used to launch target or authentication commands. Executable and arguments use structured process APIs.
- Server-provided values MUST never be concatenated into command strings.
- Environment variables passed to child processes are allowlisted; secrets are included only when explicitly configured for that target.
- Working directory, runtime limits, cancellation, process-tree termination, and stdout/stderr bounds are explicit.
- Enterprise automation SHOULD run active STDIO validation in a disposable sandbox/container with no ambient credentials, read-only filesystem where possible, and restricted network access.
- MCP Validator does not claim to sandbox a target unless that isolation is technically enforced and tested.

## P10: Authentication And Secret Safety

Credentials MUST remain transient and least-privileged.

- Tokens, cookies, session headers, authorization codes, and credential-bearing fixtures MUST never be committed, logged, printed in commands, stored in canonical results, uploaded as artifacts, or included in exception text.
- Configuration and result persistence use explicit secret-stripping methods with regression tests.
- OAuth/OIDC metadata is schema-validated. Callback state and PKCE are verified where applicable.
- Authentication strategy selection is explicit and auditable; a transport MUST NOT silently ignore configured authentication.
- CI adapters mask secrets before command construction and avoid echoing secret-bearing argv.
- The repository enables secret scanning and push protection where hosting permits it.

## P11: Active Security Tests Must Be Safe And Truthful

`safe` is the default execution mode.

- A probe declares whether it is read-only, state-changing, destructive, open-world, or potentially expensive.
- Unknown tools MUST NOT be invoked merely because they were discovered. Tool calls require a safe fixture, a verified read-only annotation plus suitable arguments, or explicit elevated operator consent.
- Attack payloads have bounded count, size, concurrency, and duration.
- A server error, timeout, disconnect, or rate limit is not proof that an attack was blocked. It is failed, unavailable, or inconclusive evidence according to the observed semantics.
- Elevated mode requires explicit acknowledgement and produces an audit record.
- The validator MUST preserve the distinction between observed behavior and inferred risk.

## P12: Concurrency Must Not Change Truth

Concurrency is an optimization and load-test input, not a source of nondeterminism.

- Shared collections and mutable result aggregates MUST NOT be written concurrently without synchronization or isolated per-task results followed by deterministic merge.
- Semaphore acquisition and release use ownership flags; mutable limiter replacement MUST NOT strand in-flight work.
- Functional probes may run concurrently only when they do not share protocol session state unsafely and the target permits it.
- Performance testing runs after functional validation unless an explicit scenario requires otherwise.
- Concurrency calibration and retry behavior are recorded in evidence.

## P13: Bound Resources End To End

Every untrusted or repeated operation has limits.

- Bound response headers, bodies, SSE events, JSON depth, collection cardinality, report size, subprocess output, request count, retries, concurrency, and per-request duration.
- Limits apply while streaming; a missing or dishonest `Content-Length` MUST NOT bypass them.
- Truncation is explicit in evidence and MUST NOT be parsed or scored as a complete response.
- Discard or dispose every `HttpRequestMessage`, `HttpResponseMessage`, content stream, process, semaphore, timer, and cancellation source at the correct lifecycle boundary.
- Defaults live in named policy/configuration owners and are documented.

## P14: Cancellation And Failure Semantics

Cancellation MUST propagate through every network, SDK, validator, report, and subprocess operation.

- `OperationCanceledException` caused by caller cancellation is not converted into a generic failed probe.
- Retries honor cancellation and a total operation deadline.
- Partial runs identify completed, skipped, blocked, and not-run checks.
- A framework exception MUST NOT be misreported as target noncompliance.
- Unexpected exceptions are logged with safe structured context and produce a typed framework-error outcome.

## P15: Errors Are Observable, Typed, And Redacted

- Empty catch blocks and blanket exception swallowing are forbidden in production paths.
- Catch the narrow exception that represents an expected alternate path.
- Developer logs use `ILogger` structured templates; production code does not rely on direct console output outside host bootstrap/terminal presentation.
- Error records identify origin: target, transport, authentication, policy, validator, schema, persistence, renderer, or framework.
- User-facing errors provide a safe next action without exposing secrets, sensitive paths, raw payloads, or stack traces by default.

## P16: Performance Claims Require Benchmarks

- Performance thresholds MUST name the workload and authority. A universal latency threshold without deployment context is advisory only.
- Report p50, p95, p99, throughput, error rate, throttling, concurrency, and sample count where meaningful.
- Separate target latency from validator overhead, queue time, retry backoff, and report generation.
- Use a dedicated benchmark project or repeatable harness for framework microbenchmarks; unit-test elapsed-time assertions are not reliable benchmarks.
- CI regression gates use stable, controlled runners or statistically robust tolerances.
- The engine MUST support bounded parallel execution across independent targets without shared mutable run state before a hosted multi-tenant service is claimed.

## P17: Scoring And Calibration Are Governed Product Logic

- Rule weights, hard caps, trust thresholds, and policy gates are versioned and reviewable.
- Critical protocol or security blockers cannot be averaged away by unrelated passing checks.
- Heuristics disclose false-positive/false-negative limitations and do not claim exploit confirmation without a discriminating probe.
- Calibration uses a maintained corpus of compliant, vulnerable, malformed, authenticated, rate-limited, and partially implemented servers.
- Enterprise release gates SHOULD baseline accepted findings and compare regressions, while preserving raw current findings.
- Changes to scoring require golden fixtures demonstrating intended movement and protection against score inflation.

## P18: Reports Are Durable Enterprise Interfaces

- JSON and SARIF are first-class public contracts with schema/version documentation and compatibility tests.
- Reports include coverage gaps, confidence, policy, tool/rule versions, and enough redacted evidence for independent review.
- Human reports prioritize deterministic verdict, blocking findings, tested scope, and remediation before weighted summaries.
- Artifact filenames are collision-resistant. Writes are atomic, and output paths stay inside the operator-approved directory.
- Reports never label an endpoint “secure” based only on absence of detected findings.
- Accessibility and safe offline viewing are required for HTML output; external active content is avoided.

## P19: Extensibility Uses Versioned Packs

Protocol feature packs, rule packs, scenario packs, client profiles, authentication strategies, scoring strategies, and report renderers are the supported extension seams.

- A pack has a stable ID, semantic revision, applicability declaration, authority, owner, and tests.
- Extension discovery is deterministic. Duplicate IDs or incompatible revisions fail closed with a clear error.
- Third-party code is not dynamically loaded in-process until package integrity, trust policy, API compatibility, and isolation are defined.
- Client profiles interpret existing neutral evidence. They MUST NOT cause target probes or alter raw findings.
- A new host reuses the engine through abstractions rather than invoking CLI parsing in-process.

## P20: Tests Prove Behavior At Boundaries

Required coverage includes:

- unit tests for rules, policy, scoring, redaction, and parsers;
- contract tests for canonical JSON/SARIF schemas and compatibility;
- integration tests for HTTP, Streamable HTTP, SSE behavior where supported, and STDIO process lifecycle;
- adversarial tests for SSRF, redirects, DNS resolution, command argument injection, oversized/streaming input, malformed JSON-RPC, hostile HTML, and secret leakage;
- concurrency, cancellation, timeout, retry, rate-limit, and resource-disposal tests;
- architecture tests for project dependency direction;
- end-to-end package tests for NuGet tool, npm wrapper, GitHub Action, container, and standalone binaries.

Tests verify correct semantics, not current implementation accidents. Live-provider tests are separate from deterministic CI and identify their cost and credentials.

## P21: Supply Chain And Releases Are Reproducible

- Dependencies are lock-file restored in both test and release jobs.
- GitHub Actions are pinned to immutable commit SHAs.
- Pull requests run dependency review, static analysis, tests, and secret detection.
- Release artifacts are built from the tested commit and receive checksums, SBOMs, provenance attestations, and signatures where supported.
- NuGet, npm, container, GitHub Action tags, and standalone binaries share one release version and changelog entry.
- Mutable major/minor action tags are documented aliases; immutable full-version tags are the audit reference.
- Release workflows MUST prevent two concurrent main-branch runs from deriving or publishing the same version.
- Provenance covers the final published bytes, not an intermediate artifact.

## P22: Open-Source Governance Is Part Of Enterprise Readiness

- `SECURITY.md` defines private vulnerability reporting, supported versions, and response expectations.
- `CONTRIBUTING.md` defines build/test commands, rule contribution requirements, disclosure expectations, and review standards.
- Maintainer ownership for security-sensitive areas is explicit through `CODEOWNERS` or equivalent review policy.
- A single-owner open-source project MAY satisfy governance through explicit ownership, mandatory automated gates, signed releases/approvals, documented key rotation and recovery, and reproducible public procedures; a second maintainer is not intrinsically required.
- Releases use semantic versioning and document breaking machine-contract changes.
- Roadmap items and known limitations are public and linked from product documentation.
- Enterprise adoption MUST NOT depend on one maintainer's unpublished knowledge or credentials.

## P23: Multi-Tenant And Hosted Use Requires Additional Isolation

The current CLI architecture does not automatically constitute a safe hosted service.

Before claiming hosted or multi-tenant readiness, the product MUST add:

- tenant-scoped identity, authorization, quotas, encryption, retention, deletion, and audit policy;
- worker isolation for untrusted targets and STDIO processes;
- durable job state, idempotency, leases, cancellation, and crash recovery;
- outbound network egress enforcement outside the application process;
- queue backpressure, fair scheduling, per-tenant concurrency, and cost controls;
- horizontally scalable artifact storage and stateless API nodes;
- service-level metrics, tracing, alerting, SLOs, runbooks, backup, and disaster recovery;
- abuse prevention and legal/privacy review for stored target evidence.

Until these exist, MCP Validator SHOULD be positioned as a local/CI single-run tool and reusable engine.

## P24: Documentation Must Match Shipped Behavior

- README, CLI help, feature matrix, architecture docs, schemas, examples, action inputs, and package metadata are one reviewed product surface.
- Stable, experimental, partial, and unsupported features are labeled consistently.
- A command example is tested or exercised in CI where practical.
- Claims such as “full,” “secure,” “certified,” “all clients,” or “enterprise” require an explicit tested scope.
- Product naming is `MCP Validator` for the product and `mcpval` for the command. Historical `Benchmark` namespaces may remain internal until migrated, but user-facing naming MUST be consistent.

## P25: Validation Cadence Follows Risk

After the first substantive edit, run the cheapest executable check that can falsify the local hypothesis. Then expand according to blast radius:

1. focused unit or contract test;
2. affected project build/analyzers;
3. full solution tests and npm tests for shared contracts or adapters;
4. package/container/action smoke tests for distribution changes;
5. live target tests only on a frozen build with explicit authorization.

Record commands and failures. Do not hide unrelated failures, but do not silently broaden a focused change to repair unrelated defects.

## Required Pull Request Questions

Every behavior-changing pull request MUST answer:

1. What evidence or public contract changes?
2. Can missing evidence be mistaken for a pass?
3. Does this contact or execute an untrusted target differently?
4. Are secrets, payloads, paths, and HTML still safely redacted/encoded?
5. Is mutable state isolated per run and safe under cancellation/concurrency?
6. Are protocol authority and version applicability explicit?
7. Do machine-readable schemas or scoring revisions require versioning?
8. Which focused and end-to-end checks prove the change?
