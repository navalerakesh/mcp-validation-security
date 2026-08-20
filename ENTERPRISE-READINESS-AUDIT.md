# MCP Validator Enterprise Readiness Audit

**Assessment date:** 2026-08-18
**Repository:** `navalerakesh/mcp-validation-security`
**Assessment type:** source, architecture, security, test, distribution, and market review

## Renewal Status Addendum

The findings below are the baseline that started the `Upgrade_18Aug2026` renewal. Current branch evidence as of 2026-08-18:

| Baseline area | Current branch status |
| --- | --- |
| Protocol currency | **Closed for declared local-engine scope:** official MCP C# SDK 2.2.0, verified `2026-07-28` schema, explicit era negotiation, typed discovery, request metadata, header/error checks, result/continuation semantics, cache/list determinism, subscriptions, extension/tasks negotiation, and lifecycle evidence are active; hosted and extension-specific behavior remains separately gated |
| Auth command injection | **Closed:** no shell invocation; structured arguments and cancellation/process-tree tests pass |
| Authentication assurance | **Closed for declared local-engine scope:** governed metadata transport/discovery, exact issuer/resource/redirect/state/RFC 9207 checks, PKCE S256, bounded challenge grammar, provider-backed wrong-resource and one-retry scope-union probes, typed registration/issuer binding, redacted evidence, and enterprise ID-JAG discovery are active; live provider certificates and downstream forwarding sinks remain separate deployment evidence and are not claimed |
| SSRF/DNS | **Closed for the local engine:** target admission and a shared raw/SDK handler enforce exact origins, disable redirects and ambient proxies, re-resolve each new connection, reject mixed/private/mapped/reserved/metadata answers, and connect directly to an approved address; hosted deployment still requires network-enforced egress |
| Agent-triggered STDIO | **Closed for local MCP adapter:** remote-only by default; explicit environment opt-in required for local execution |
| Tracked live artifacts | **Blocked for public release:** current-tree captures are removed and ignored, but historical reports/header/cookie captures remain reachable from public refs and require owner-led history remediation/session invalidation |
| Token exposure in Action | **Closed:** `MCPVAL_TOKEN` environment path, explicit masking, and no token argv construction |
| 503/security score inflation | **Closed:** unavailable attack evidence is typed inconclusive and cannot earn defense credit |
| Semaphore race | **Closed:** exact permit ownership with cancellation/concurrency regression coverage |
| Run request isolation | **Closed for the local engine:** mutable input is captured into an immutable `ValidationRunRequest`; HTTP/STDIO transports, validators, session builders, and SDK caches are run-scoped under an immutable `OperationPolicySnapshot`; categories return isolated typed outcomes and reduce in fixed order |
| Bounded transport reads | **Closed for the local engine:** streamed HTTP bodies, STDIO protocol lines, STDIO stderr, and auth CLI streams are byte-bounded; overflow is explicit, auth overflow terminates without a credential, and persisted stderr evidence is metadata-only |
| Release reproducibility/race | **Closed:** exact single-source version, serialized release, runtime-aware locked restore, and `--no-restore` publish |
| Incomplete malformed probe | **Closed:** missing-version raw probe executes with typed skipped/inconclusive handling |
| npm high advisories | **Closed:** current lockfile audits at zero vulnerabilities |
| Test dependency license warning | **Closed:** assertion dependency moved to the permissive 7.2 line; warning-free build |
| Scoring realism | **Improved:** evaluated dimensions are normalized, missing dimensions are named, incomplete evidence caps at L3, and coverage-only runs are partial rather than failed |
| Machine contracts | **Improved:** canonical result, audit manifest, client-profile summary, and model-evaluation companion carry versioned document identities and published JSON Schema contracts |
| Open-source hygiene | **Improved but blocked:** worktree/index checks are hardened and live certificates use OS temp; the gate intentionally fails until forbidden historical paths are removed from reachable refs |

Latest executable evidence: 912/912 .NET Release tests, 14/14 npm tests, TypeScript typecheck, warning-free builds, and a five-target HTTP/STDIO matrix with 30/30 subject hashes matched and zero GitHub-token matches. The sensitive-artifact gate is intentionally blocked by forbidden paths reachable in Git history.

## Executive Verdict

MCP Validator is a credible and unusually well-packaged **local and CI assessment tool**. Its strongest product idea is the separation of neutral evidence, deterministic verdicts, weighted trust summaries, host-specific compatibility interpretation, and human/machine reports. The repository also has stronger supply-chain automation than many open-source projects at this maturity.

It is **not ready to be described or operated as an enterprise certification authority, a safe scanner for arbitrary untrusted STDIO packages, or a multi-tenant hosted validation service**. The current release has five release-blocking concerns:

1. `latest` protocol support stops at `2025-11-25`, while the current MCP revision is `2026-07-28` and defines a new stateless protocol era.
2. Server-controlled OAuth metadata can reach a shell-concatenated Azure CLI invocation on Windows.
3. SSRF protection checks URL host strings but does not resolve DNS, pin addresses, or revalidate automatic redirects.
4. STDIO validation executes caller-supplied commands with the validator's privileges; the MCP wrapper exposes this capability to an AI host without an additional consent or sandbox boundary.
5. The npm security gate currently fails with three high-severity transitive vulnerabilities, including URI/IP parsing advisories relevant to SSRF boundaries.

**Recommended product status:** controlled beta for local/CI use against authorized HTTP targets. Use as one advisory gate, not the sole security approval. Do not run untrusted STDIO targets outside a disposable sandbox.

## Decision Summary

| Use case | Decision | Conditions |
| --- | --- | --- |
| Developer checks a server they own | **Use now** | Pin the tool version; start with dry-run and safe mode |
| CI validation of an authorized test endpoint | **Use with controls** | Pin action and CLI versions; use strict policy; archive JSON/SARIF; keep credentials ephemeral |
| Production endpoint change gate | **Pilot only** | Pair with DAST/SAST, OAuth tests, egress controls, and human review |
| Scan arbitrary public HTTP servers | **Do not use from a trusted network** | Run in an isolated worker with enforced egress policy |
| Scan arbitrary STDIO packages/commands | **Do not use on a workstation or shared runner** | Disposable VM/container, no ambient credentials, restricted network and filesystem |
| Sole enterprise security certification | **No-go** | Requires current conformance, calibrated corpus, independent rule governance, and external validation |
| Hosted multi-tenant scanner | **No-go** | Requires tenant isolation, durable jobs, sandbox workers, quotas, retention, SLOs, and network enforcement |

## Maturity Scorecard

These are audit judgments, not MCP Validator's own scores.

| Dimension | Rating | Assessment |
| --- | ---: | --- |
| Product usefulness | 4/5 | Strong local/CI workflow and report set |
| Protocol currency | 2/5 | Good legacy schema history, but missing the current modern protocol era |
| Evidence and reporting | 4/5 | One of the strongest areas; needs a versioned public result schema |
| Validator security | 2/5 | Good intent and policy surface, but critical process/network boundary gaps |
| Correctness/calibration | 3/5 | Strong typed direction; known wrong behavior can still pass tests |
| Test engineering | 3.5/5 | 552 .NET tests and 9 npm tests pass; no coverage gate and important assertions are weak |
| Performance engineering | 2.5/5 | Bounded concurrency and calibration exist; no repeatable benchmark baseline |
| Single-process scalability | 3/5 | Useful bounded category parallelism; shared mutable state needs correction |
| Hosted scalability | 1/5 | No durable queue, tenant model, worker isolation, or horizontal service architecture |
| Extensibility | 3.5/5 | Good interfaces, packs, strategies, and profiles; composition and public contracts need hardening |
| Supply-chain security | 3.5/5 | Strong controls, SBOMs and signing, but the current npm audit has high findings |
| Open-source governance | 3/5 | Good basic policies; single-owner review and limited independent calibration create concentration risk |

## What The Repository Is

### Product purpose

`mcpval` actively connects to an MCP server, captures protocol and operational evidence, evaluates security and AI-facing risk, derives a deterministic verdict and weighted L1-L5 posture, and emits reports for developers, CI systems, and governance workflows.

### Component map

| Component | Actual responsibility | Enterprise assessment |
| --- | --- | --- |
| `Mcp.Benchmark.Core` | Configuration, abstractions, canonical result and finding models | Correct home for neutral contracts; public result versioning is incomplete |
| `Mcp.Compliance.Spec` | Embedded protocol schemas and version metadata | Good design; currently stops at `2025-11-25` |
| `Mcp.Benchmark.ClientProfiles` | Interpret neutral evidence for named MCP hosts | Valuable differentiator; assumptions need dated provenance and expiry review |
| `Mcp.Benchmark.Infrastructure` | HTTP/STDIO transports, auth, validators, attacks, scenarios, scoring, reporting | Feature-rich but large; contains most security and concurrency risk |
| `Mcp.Benchmark.CLI` | Composition, command binding, policy, artifacts, console and exit behavior | Appropriate host role, but `Program.cs` is a large duplicated command/composition surface |
| `Mcp.Benchmark.Tests` | Unit, integration, architecture, fixtures and snapshots | Broad coverage and good fixtures; semantic quality is uneven |
| `mcpval-mcp` | Local MCP server that invokes the published CLI | Useful integration, but turns target selection into an agent-accessible process execution boundary |
| `action.yml` | Composite GitHub Action | Good distribution channel; version and secret handling need correction |
| `Dockerfile` | Distroless non-root runtime image | Strong baseline; still not a sandbox for arbitrary STDIO workloads by itself |
| `.github/workflows` | Build, release, analysis, fuzzing, Scorecard and dependency review | Strong breadth; release determinism and concurrency need correction |

### Runtime flow

1. CLI and optional JSON config are merged into `McpValidatorConfiguration`.
2. `ExecutionGovernanceService` creates a dry-run/contact plan, host set, limits, and artifact plan.
3. The session builder initializes the target, negotiates legacy protocol state, discovers authentication, and captures capability evidence.
4. Protocol, tool, resource, prompt, and security validators run in parallel.
5. Error-handling and performance checks run later; performance intentionally follows functional checks.
6. Applicability packs, scenarios, coverage, scoring, trust, verdict, policy, and client-profile interpretations are derived.
7. The CLI emits JSON, SARIF, Markdown, HTML, audit, and optional companion artifacts.

This lifecycle is a good foundation for a reusable engine. It is not yet a safe hosted worker model because mutable state and artifacts are process-local.

## Best Parts

### 1. Evidence architecture is directionally right

- Neutral findings are separated from client-profile interpretation.
- The result distinguishes deterministic verdict from weighted trust level.
- Coverage records include skipped, unavailable, auth-required, blocked, and inconclusive states.
- Experimental model evaluation is a companion artifact and does not mutate deterministic truth.
- Stable rule IDs, rule authority, remediation, evidence, and SARIF support are appropriate enterprise primitives.

### 2. Product packaging is excellent for an open-source project

- NuGet tool, npm MCP wrapper, GHCR image, standalone binaries, GitHub Action, checksums, SBOMs, attestations, and signatures cover major adoption paths.
- The container uses digest-pinned images, a chiseled runtime, and a non-root user.
- GitHub Actions are pinned to immutable commit SHAs.
- Dependency review, NuGet/npm audit, CodeQL, OpenSSF Scorecard, workflow linting, docs linting, and fuzzing are present.

### 3. The CLI has practical safety and governance controls

- Safe/standard/elevated modes, dry-run, host allowlist, private-address opt-in, request cap, timeout, concurrency cap, redaction, trace, and persistence controls are useful.
- Load testing runs after functional probes to avoid manufacturing false protocol failures.
- Transient/rate-limited outcomes are modeled separately in several paths.

### 4. The repository has meaningful automated evidence

- 252 tracked C# source files and 9 tracked TypeScript source files.
- 499 xUnit `[Fact]`/`[Theory]` declarations.
- Executed locally: 552/552 .NET tests passed using explicit runtime roll-forward.
- Executed locally: 9/9 npm wrapper tests passed.
- Architecture tests, HTTP fixtures, STDIO fixtures, report snapshots, property-based fuzzing, and package-oriented tests are all represented.

### 5. Extensibility seams already exist

- Protocol feature packs, rule packs, scenario packs, client profiles, auth strategies, scoring strategies, schema registry, and report renderers are better than a monolithic switch-based scanner.
- Client profiles consume neutral evidence rather than forking raw validation.
- The canonical JSON result supports offline rendering.

## Priority Findings

### ER-001: `latest` is one protocol era behind

**Severity:** Critical product correctness
**Evidence:** `Mcp.Compliance.Spec/ProtocolVersions.cs`, `Mcp.Compliance.Spec/schema/`, `Mcp.Benchmark.Infrastructure/Registries/BuiltInProtocolFeaturePack.cs`

The newest embedded revision is `2025-11-25`. The current MCP specification is `2026-07-28`. This is not a small additive update. The current revision:

- removes protocol-level sessions and `Mcp-Session-Id`;
- removes the `initialize` handshake and makes requests stateless;
- adds mandatory `server/discover`;
- moves version, identity, and capabilities to per-request `_meta`;
- adds `resultType` and multi-round-trip request behavior;
- replaces subscription mechanics;
- moves tasks to an extension;
- changes standard headers and error codes;
- adds caching fields and JSON Schema 2020-12 requirements;
- formalizes feature deprecation and extension negotiation.

**Impact:** modern-only servers cannot be correctly validated. A value named `latest` resolves to legacy behavior and creates a false product claim. Current protocol failures may be misclassified as target failures.

**Required action:** implement a dual-era transport/session abstraction and embed the `2026-07-28` schema before advertising current conformance. Until then, rename `latest` to `latest-embedded` in user-facing behavior and report a high-visibility stale-profile warning.

### ER-002: Server-controlled values reach a Windows shell

**Severity:** Critical security
**Evidence:** `Mcp.Benchmark.Infrastructure/Authentication/Strategies/BaseCliStrategy.cs`, `AzureAuthenticationStrategy.cs`

`BaseCliStrategy` changes execution to `cmd.exe /c` on Windows and concatenates executable and arguments. `AzureAuthenticationStrategy` interpolates `scope` and an extracted tenant path from target-provided authorization metadata.

**Impact:** a malicious server can potentially turn validation/auth discovery into command execution on the operator's Windows host.

**Required action:** never invoke a shell. Pass each argument with `ProcessStartInfo.ArgumentList`, validate tenant identifiers and scopes, bound subprocess output, kill the process tree on cancellation, and add adversarial tests for `&`, `|`, quotes, newlines, and Unicode separators. Treat the fix as a coordinated security release.

### ER-003: SSRF policy does not defend DNS or redirects

**Severity:** High security
**Evidence:** `Mcp.Benchmark.CLI/Services/ExecutionGovernanceService.cs`, `Mcp.Benchmark.Infrastructure/Http/McpHttpClient.cs`

Private-address checks call `IPAddress.TryParse(host)`. A hostname is therefore considered public without DNS resolution. Default `HttpClient` redirect handling can follow a public URL to an internal destination without re-entering `EnforceExecutionPolicy`.

The official MCP security guidance explicitly identifies attacker-controlled OAuth metadata URLs, redirects, private ranges, cloud metadata, DNS rebinding, and time-of-check/time-of-use as MCP client threats.

**Impact:** a target can make the validator access services reachable from the operator or CI network. In a hosted service this becomes a tenant-to-control-plane or tenant-to-private-network attack path.

**Required action:** centralize all HTTP creation behind a hardened handler; disable automatic redirects; validate every hop; resolve all addresses; reject private/reserved/metadata ranges including IPv4-mapped IPv6; pin or enforce at connect time; and use an external egress proxy/network policy for hosted workers.

### ER-004: STDIO validation is arbitrary local code execution

**Severity:** High security and product trust
**Evidence:** `Mcp.Benchmark.Infrastructure/Http/StdioMcpClientAdapter.cs`, `mcpval-mcp/src/tools.ts`, `mcpval-mcp/src/cli-runner.ts`

Any non-URL target is accepted as a STDIO command. The local MCP wrapper exposes that input as an AI-callable tool and launches the CLI, which launches the supplied command. Structured process APIs reduce shell injection, but they do not make an arbitrary executable safe.

**Impact:** an agent prompt, poisoned context, or mistaken user approval can execute `npx`, package managers, scripts, or binaries with the MCP wrapper's privileges and ambient environment.

**Required action:** remote URLs only by default in the MCP wrapper; require a separate explicit local-execution capability; show the exact command; require host-level approval; allowlist executable origins; strip the environment; and provide a documented sandbox worker profile. CI should require an unmistakable opt-in similar to `--dangerously-run-mcp-servers` used by market peers.

### ER-005: Tracked session and response artifacts

**Severity:** High privacy/secret hygiene
**Evidence:** root `github_headers.txt`, `learn_headers.txt`, `learn_cookies.txt`

These tracked files contain real response metadata including MCP session identifiers, a user-linked claim inside a session value, and cookies. This audit did not prove that any value remains valid or independently authorizes access, so account-compromise claims would be speculative.

**Impact:** operational identity/session material is permanently distributed through repository history, teaches unsafe fixture practice, and may be reusable depending on server behavior.

**Required action:** revoke/expire related sessions, remove the files from current history, assess a history rewrite, add ignore/secret-scanning rules, replace with obviously synthetic fixtures, and enable push protection. Do not publish the values in an issue.

### ER-006: GitHub Action prints a token-bearing command

**Severity:** High credential handling
**Evidence:** `action.yml`

The action adds `--token "$INPUT_TOKEN"` and then prints every command argument with `printf %q`. GitHub masks values passed from a properly configured secret, but the action itself still handles and echoes the secret through argv, and masking is not a security boundary.

**Impact:** accidental log disclosure, process-list disclosure, and inconsistent behavior when callers pass tokens from non-secret expressions.

**Required action:** pass credentials through a protected temporary config or environment-backed secret reference, call `::add-mask::` as defense in depth, and print a redacted command plan.

### ER-007: A 503 can be counted as a successful defense

**Severity:** High verdict integrity
**Evidence:** `Mcp.Benchmark.Infrastructure/Validators/SecurityValidator.cs`, `Mcp.Benchmark.Tests/Unit/Validators/EdgeCaseAndBugTests.cs`

The exception path treats only text containing 500/Internal Server Error as server error. Other exceptions, including 503-style failures, become `DefenseSuccessful = true`. The test named `SecurityAttack_With503_ShouldNotReportAsBlocked` only checks that simulations are non-empty, so it passes without enforcing its stated behavior.

**Impact:** unavailable or broken targets can receive positive security evidence. This can inflate trust and weaken a release gate.

**Required action:** use typed response classification; represent 5xx, timeout, disconnect, and rate limiting as inconclusive/unavailable unless a probe proves defensive rejection; make the existing test assert exact outcome, defense flag, status, confidence, and verdict effect.

### ER-008: Parallel validators mutate shared non-thread-safe state

**Severity:** High reliability at scale
**Evidence:** `Mcp.Benchmark.Infrastructure/Services/McpValidatorService.cs`

Multiple `Task.Run` delegates update one mutable `ValidationResult` and may concurrently append to `CriticalErrors`, a `List<string>`. `List<T>` is not safe for concurrent writers. The shared HTTP client also contains mutable authentication, protocol version, session ID, request count, policy, and replaceable semaphore state.

**Impact:** rare lost/corrupted errors, nondeterministic reports, limiter races, and inability to reuse the engine safely for concurrent runs or hosted workers.

**Required action:** remove `Task.Run` around asynchronous I/O, let each validator return an isolated result/error record, and merge deterministically after `WhenAll`. Introduce immutable `ValidationRunContext` and run-scoped transports. Track semaphore ownership with an acquired flag and do not replace a limiter while requests are active.

### ER-009: Semaphore release uses a racy count check

**Severity:** High concurrency reliability
**Evidence:** `Mcp.Benchmark.Infrastructure/Http/McpHttpClient.cs`

The `finally` block checks `throttle.CurrentCount < _maxConcurrency` before `Release()`. The check and release are not atomic, and `_maxConcurrency` can belong to a newly installed semaphore.

**Impact:** over-release, starvation, or inconsistent limits under cancellation or concurrent reconfiguration.

**Required action:** set `acquired = true` only after `WaitAsync` succeeds and release exactly once when acquired. Make policy/limiter immutable per run.

### ER-010: Release artifacts are not restored under the tested lock contract

**Severity:** High supply-chain reproducibility
**Evidence:** `.github/workflows/ci.yml`

The test job uses `dotnet restore --locked-mode`, but standalone package publishing passes `RestoreLockedMode=false`.

**Impact:** published binaries can resolve a dependency graph different from the graph that passed tests and audit.

**Required action:** perform locked restore and publish with `--no-restore` from the tested graph. Attest final bytes from that graph.

### ER-011: Action tag and installed CLI version can drift

**Severity:** High distribution correctness
**Evidence:** `action.yml`, `README.md`

When `version` is omitted, an old or immutable action revision installs the latest NuGet package at runtime. Therefore `uses: ...@v1.1.24` does not identify the actual validator code that executes.

**Impact:** non-reproducible CI and unexpected behavior changes without a workflow change.

**Required action:** bind each action release to its matching CLI version. Require explicit opt-in for floating latest behavior.

### ER-012: Release version derivation can race

**Severity:** Medium-high release reliability
**Evidence:** `.github/workflows/ci.yml`

Patch version is computed from the latest tag independently in multiple jobs, and the workflow has no top-level concurrency serialization. Two main-branch runs can derive the same next version.

**Impact:** duplicate tags/packages, partial multi-channel releases, or mismatched aliases.

**Required action:** use one version-producing job/artifact, serialize releases, publish from an immutable signed tag, and make retries idempotent by channel.

### ER-013: Incomplete attack vector is presented as a complete check

**Severity:** Medium-high coverage truth
**Evidence:** `Mcp.Benchmark.Infrastructure/Attacks/JsonRpcErrorSmuggling.cs`

The class creates a missing-version payload but never sends it. A long comment explicitly skips the probe, while the check still reports JSON-RPC error-smuggling behavior.

**Impact:** overstated coverage and possible score inflation.

**Required action:** implement the raw authenticated probe or declare a typed coverage gap. Remove abandoned comments.

### ER-014: Discovery output includes synthetic capabilities

**Severity:** Medium product correctness
**Evidence:** `Mcp.Benchmark.Infrastructure/Services/McpValidatorService.cs`

HTTP discovery constructs generic entries such as `tools-validated`, `http://*`, and `validation-prompt` instead of returning only observed tools/resources/prompts.

**Impact:** consumers may interpret placeholders as actual server capabilities.

**Required action:** return the observed capability snapshot and mark unavailable fields explicitly. Do not fabricate semantic entities.

### ER-015: Oversized response handling is ambiguous

**Severity:** Medium correctness/security
**Evidence:** `Mcp.Benchmark.Infrastructure/Http/McpHttpClient.cs`

The client bounds content, which is good, but a declared oversized body returns an empty string and an unknown-length oversized stream returns a partial string. The result does not carry an explicit truncation state.

**Impact:** a resource-limit defense can become a malformed-response finding or partial JSON parse rather than a clear blocked/inconclusive outcome.

**Required action:** return a typed bounded-read result with `IsTruncated`, bytes read, declared length, and reason; never score partial content as complete.

### ER-016: Debug logging can persist raw target content

**Severity:** Medium privacy
**Evidence:** `Mcp.Benchmark.Infrastructure/Http/McpHttpClient.cs`

Debug logs include up to 1,000 characters of raw response content and multiple errors include target response bodies.

**Impact:** prompts, resources, personal data, tokens returned by broken servers, or attack payload reflections can reach session logs and CI artifacts.

**Required action:** log digests, sizes, classifications, and explicitly redacted previews. Add end-to-end canary-secret tests over console, log, JSON, SARIF, Markdown, HTML, and audit outputs.

### ER-017: Public machine contract lacks independent schema governance

**Severity:** Medium enterprise integration
**Evidence:** `Mcp.Benchmark.Core/Models/ValidationResults.cs`, report snapshot tests, `docs/Design/Schemas.md`

The result records the MCP schema selected for the target, but no independently versioned public JSON schema was found for the complete `ValidationResult` document.

**Impact:** dashboards and policy systems couple to serializer implementation and cannot negotiate breaking changes.

**Required action:** publish `mcpval-result.schema.json`, `profile-summary.schema.json`, and `audit-manifest.schema.json`; add semantic schema versions, compatibility policy, consumer contract tests, and migrations where needed.

### ER-018: No benchmark or code-coverage release gate

**Severity:** Medium engineering assurance
**Evidence:** test project includes Coverlet; CI runs tests but does not collect/enforce coverage; no dedicated benchmark project was found.

**Impact:** 552 passing tests can still leave critical paths unasserted, and performance claims cannot be compared across releases.

**Required action:** publish branch/line coverage by risk area, set a non-regression gate, add mutation testing for verdict/scoring rules, and create a repeatable benchmark corpus for framework overhead and target probes.

### ER-019: Test dependency raises commercial-use licensing questions

**Severity:** Medium adoption/legal
**Evidence:** `Mcp.Benchmark.Tests/Mcp.Benchmark.Tests.csproj`

The test run prints Fluent Assertions' Xceed license warning stating that commercial use requires a subscription. The production binaries are not thereby contaminated, but enterprises and commercial contributors running the test suite need a clear determination.

**Impact:** avoidable legal/procurement friction for contributors and enterprise forks.

**Required action:** obtain and document the required license or migrate tests to an unambiguously compatible assertion library/version. Confirm with legal counsel rather than inferring from this audit.

### ER-020: Single-owner governance requires explicit continuity controls

**Severity:** Closed governance control
**Evidence:** `.github/CODEOWNERS`, `docs/Design/SingleOwnerGovernance.md`, signed Fleet owner approvals, and mandatory CI/CD gates

MCP Validator declares one author and repository owner. Maintainer count is not treated as a readiness defect.

**Risk controlled:** enterprise users must not depend on unpublished owner knowledge, untracked credentials, mutable release history, or unrepeatable procedures.

**Resolution:** ownership is explicit; sensitive surfaces require signed owner approval and mandatory automated gates; release artifacts are reproducible and signed; and key rotation, compromise recovery, continuity, and ownership transfer are documented.

### ER-021: Security policy excludes false results too broadly

**Severity:** Medium governance
**Evidence:** `SECURITY.md`

False positives and false negatives are declared out of scope as security vulnerabilities. Most are product bugs, but a reproducible false negative that bypasses a documented strict security gate can be a security issue in the validator itself.

**Impact:** researchers may publicly disclose gate-bypass defects or have no appropriate private route.

**Required action:** accept privately reported validator bypasses, secret leaks, sandbox escapes, SSRF, command execution, and deterministic gate manipulation; route ordinary accuracy bugs to public issues after triage.

### ER-022: Current npm dependency graph fails the high-severity audit gate

**Severity:** High supply-chain security
**Evidence:** `mcpval-mcp/package-lock.json`, `mcpval-mcp/package.json`

The repository validation script reports six npm advisories: three high, one moderate, and two low. The traced paths are:

- `@modelcontextprotocol/sdk@1.29.0 -> ajv -> fast-uri@3.1.2`;
- `@modelcontextprotocol/sdk@1.29.0 -> express-rate-limit -> ip-address@10.2.0`;
- `@modelcontextprotocol/sdk@1.29.0 -> hono@4.12.18`;
- `@modelcontextprotocol/sdk@1.29.0 -> @hono/node-server@1.19.14`;
- `@modelcontextprotocol/sdk@1.29.0 -> express -> body-parser@2.2.2`;
- `tsx@4.22.2 -> esbuild@0.28.0`.

The high advisories include URI authority confusion, special-address misclassification, and Hono issues. Some affected web-server paths may not be reachable from this STDIO-only wrapper, but reachability has not been proved and the configured CI policy correctly fails at high severity.

**Impact:** the current main dependency graph does not satisfy its own release gate. URI/IP parser advisories are especially sensitive for a security product that accepts target endpoints.

**Required action:** update the SDK and affected direct/transitive dependencies under lock, rerun npm tests/build/audit, document any unreachable advisory with a time-bounded accepted-risk record, and do not weaken the high-severity threshold to obtain green CI.

## Architecture Critique

### What should remain

- Core/Infrastructure/CLI project split.
- Schema registry as protocol-version authority.
- Neutral evidence followed by interpretation.
- Pack/strategy/profile extension model.
- Canonical result followed by pure report rendering.
- Model evaluation isolated from deterministic results.
- Functional probes before load generation.

### What should change

1. **Create an engine facade.** Move the complete run lifecycle behind an `IValidationEngine` that accepts an immutable request/context and returns a canonical result. CLI, Action wrapper, MCP wrapper, and future service call the same API contract.
2. **Split run state from transport implementation.** Authentication, negotiated era/version, session state, request budget, and limiter belong to a run-scoped transport session, not a reusable singleton.
3. **Replace parallel mutation with collect-and-reduce.** Validators return immutable category outcomes. A deterministic reducer builds `ValidationResult`.
4. **Split the orchestrator.** `McpValidatorService` owns bootstrap, parallel execution, applicability, coverage, scenarios, scoring, trust, verdict, discovery, and result population. Separate these into lifecycle stages with explicit inputs/outputs.
5. **Unify command definitions.** Lightweight help and full command construction duplicate the CLI surface and can drift. Build one declarative command catalog and bind handlers only for execution.
6. **Make boundary policy infrastructural.** DNS/redirect/egress checks must be enforced by the HTTP connection layer, not only by plan validation.
7. **Add protocol-era adapters.** Legacy initialization/session behavior and modern stateless/discover behavior should implement one transport-era abstraction.
8. **Publish result schemas.** Reports are already a product API; govern them as one.

## Security Coverage: Present And Missing

| Security area | Current posture | Required enterprise posture |
| --- | --- | --- |
| Protocol malformed input | Partial active probes | Versioned modern+legacy conformance corpus |
| Prompt/tool description risk | Deterministic heuristics | Calibrated multilingual/Unicode corpus plus semantic optional lane |
| Authentication behavior | Bearer and provider strategies | Full OAuth 2.1/RFC 9728/8707/9207, PKCE, issuer, audience, scope and enterprise extension tests |
| SSRF | Host-string allow/private checks | DNS/connect-time enforcement, redirects, metadata ranges, egress proxy |
| STDIO process safety | Structured argument parsing | Consent, environment allowlist, sandbox, resource/process-tree controls |
| Tool mutation risk | Annotations and attack checks | No unknown invocation in safe mode; explicit fixture/consent policy |
| Session security | Legacy session handling | Legacy hijack tests plus modern sessionless behavior |
| Supply-chain risk of target | Not a primary feature | Package provenance, dependency/CVE, source/binary scan or integrations |
| Tool poisoning/shadowing | Some heuristics | Cross-server namespace and dynamic tool-change analysis |
| Secret leakage | Config clone/redaction controls | Canary tests across every output and log path |
| HTML/report injection | Report generators and snapshots | Dedicated hostile payload/XSS/CSP tests |
| Hosted tenant isolation | Not implemented | Sandbox workers, egress, identity, quotas, encrypted storage, retention |

## Performance And Scalability

### Current effectiveness

- Functional categories can execute concurrently.
- Concurrency is capped from 1 to 256.
- Request count, retries, timeouts, response size, and some stream preview behavior are bounded.
- Remote probe concurrency is calibrated below the user cap in some conditions.
- Performance tests run after functional checks.

### Current limitations

- `Task.Run` adds scheduler overhead to asynchronous I/O.
- One mutable HTTP client carries run/session state.
- No trustworthy framework-overhead benchmark was found.
- No p50/p95/p99 regression baseline or controlled runner gate was found.
- Reports and artifacts are local filesystem outputs.
- Telemetry is NoOp by default; no OpenTelemetry operational contract was found for a hosted deployment.
- There is no durable job queue, lease, idempotency key, worker heartbeat, resume/retry state, or cross-process cancellation.
- There is no target-level fair scheduling or organization quota model.

### Enterprise scaling target

```text
API / CLI / Action
       |
Admission + tenant policy + idempotency
       |
Durable queue -------------------- Audit/event store
       |
Isolated workers (HTTP or STDIO sandbox)
       |
Egress proxy / network policy
       |
Encrypted artifact store + result index
       |
Dashboards, SARIF, webhooks, registry admission
```

Workers should be disposable and single-run. API nodes should be stateless. Results should be addressed by tenant/run IDs. STDIO workers should use a stricter sandbox profile than HTTP workers.

## Extensibility Assessment

### Strong extension points

- Protocol feature packs and applicability matching.
- Protocol rule registry.
- Scenario packs.
- Authentication strategies.
- Aggregate scoring strategy.
- Client-profile pack/resolver/evaluator.
- Report renderers.
- Embedded schema registry.

### Gaps before a plugin ecosystem

- No stable plugin ABI/API version.
- No package signature or publisher trust policy.
- No out-of-process isolation for third-party rules.
- No duplicate rule/pack conflict policy visible to operators.
- No independent result schema version.
- No resource budget per extension.
- No deterministic extension manifest with owner, revision, authority, and supported protocol era.

**Recommendation:** do not add arbitrary in-process plugins yet. First make built-in packs manifest-driven and versioned. Later support external packs as signed data or isolated processes with a narrow JSON contract.

## Open-Source Package Assessment

### What is done well

- MIT product license and standard contribution/security documents.
- Reproducible dependency lock files in normal CI.
- Multiple install channels and non-root container.
- SHA-pinned Actions and broad security automation.
- SBOM, provenance, checksum, and signing workflows.
- Architecture and troubleshooting documentation.

### What blocks stronger adoption

- Single-maintainer review ownership.
- Current protocol lag despite a `latest` selector.
- No published compatibility policy for machine result schemas.
- Floating CLI version in the GitHub Action.
- Test-suite licensing warning for commercial use.
- Tracked operational headers/cookies.
- No public calibration dataset with expected findings and false-positive/negative metrics.
- No formal release channels (stable/preview) for large protocol-era changes.

## Market Position As Of 2026-08-18

### Market reality

- MCP is now governed under the Linux Foundation's Agentic AI Foundation.
- The Linux Foundation reported more than 10,000 published MCP servers and adoption across Claude, Cursor, Microsoft Copilot, Gemini, VS Code, ChatGPT, AWS, Google Cloud, and Azure.
- Microsoft documents MCP across Azure MCP Server, Foundry, Copilot Studio, Security Copilot, Dynamics, and other services. Some offerings remain preview, which shows strong adoption but uneven production maturity.
- The official Registry authenticates publisher namespaces and metadata but explicitly delegates security scanning to package registries and downstream aggregators.
- The current protocol has modern/legacy eras, official extensions, an enterprise-managed authorization extension, a registry, governance groups, deprecation policy, and conformance requirements.

This creates a real market need for an independent, deterministic admission and regression gate. It also raises the bar: a validator must update quickly, handle extensions, and prove its own isolation.

### Competitive benchmark

| Product | Leads in | Relative gap/opportunity for `mcpval` |
| --- | --- | --- |
| Official protocol tooling | Protocol debugging, modern/legacy era behavior, interactive inspection, single-method CLI | Not a broad deterministic security/governance scorecard; `mcpval` should complement official tooling while matching current protocol behavior |
| Security scanning platforms | Rule, semantic, package, dependency, malware, source, and offline scanning | `mcpval` has stronger deterministic protocol/evidence reporting but should integrate specialist supply-chain scanners rather than duplicate them |
| Fleet posture platforms | Agent/MCP inventory, cross-component flow analysis, background monitoring, local-execution consent | `mcpval` needs fleet inventory and central posture integrations while preserving an open deterministic evidence contract |
| Official MCP Registry | Publisher namespace authenticity and ecosystem metadata | Registry deliberately delegates security scanning, creating an integration opportunity for signed `mcpval` attestations |
| General DAST/SAST/SCA tools | Mature web, code, dependency, container, and policy ecosystems | They do not understand MCP semantics; `mcpval` should integrate rather than reimplement all of them |

### Defensible product position

The strongest position is:

> A deterministic MCP conformance, behavior, compatibility, and deployment-readiness evidence engine for CI and registry admission.

Do not position it as a universal malware scanner or formal security certification. Integrate package/source scanners and retain deterministic MCP-specific authority.

### High-value differentiators to build

1. First-class legacy/modern conformance with dated rule packs.
2. Signed, versioned validation attestations for registry/marketplace ingestion.
3. Baseline, waiver, expiry, and regression policy for enterprise CI.
4. Cross-client compatibility backed by executable profile fixtures.
5. Air-gapped deterministic mode from captured protocol evidence.
6. Safe isolated worker image and Kubernetes job profile.
7. Differential validation across two server versions.
8. Organization policy packs mapped to NIST AI RMF, OWASP guidance, internal controls, and MCP normative requirements without conflating their authority.

## Recommended Enterprise Workflow

1. Build and scan the MCP server package with normal SAST, SCA, secret, container, and malware tools.
2. Deploy the server to an isolated test environment.
3. Run `mcpval --dry-run` and review the target, mode, hosts, request count, and planned active probes.
4. Run deterministic validation with a pinned CLI, protocol profile, strict policy, conservative concurrency, and explicit egress allowlist.
5. Store canonical JSON, SARIF, audit manifest, tool version, config digest, and server artifact digest.
6. Compare with the approved baseline; require owner and expiry for waivers.
7. Run optional semantic/model analysis separately and never let it overwrite deterministic evidence.
8. Require human review for destructive/open-world tools, auth changes, new scopes, new domains, or trust-level changes.
9. Promote only if deterministic blockers are closed and required evidence is complete.
10. Revalidate periodically and on server, dependency, protocol, client-profile, or policy revision changes.

## Remediation Roadmap

### Phase 0: Immediate containment (0-72 hours)

- Privately triage ER-002 and ER-003 as validator security issues.
- Remove or disable shell-based Azure auth until structured arguments ship.
- Revoke and remove tracked session/cookie artifacts; add synthetic fixtures and push protection.
- Stop printing token-bearing commands in the Action.
- Make Action-to-CLI version binding deterministic.
- Add a top-level release concurrency group.
- Upgrade or explicitly remediate the npm dependency graph until the high-severity audit gate passes.
- Change documentation so `latest` clearly means latest embedded legacy profile, not current MCP.
- Disable STDIO targets in `mcpval-mcp` by default.

**Exit gate:** no known direct command-injection path, no raw credentials/session artifacts in current history, no token echo, no high-severity dependency finding, and no misleading current-protocol claim.

### Phase 1: Restore product truth (1-3 weeks)

- Implement `2026-07-28` schemas and dual-era transport behavior.
- Add `server/discover`, per-request metadata, modern errors/headers, `resultType`, caching, MRTR, subscriptions, and extension negotiation coverage.
- Fix 503/inconclusive security classification and strengthen semantic assertions.
- Complete or explicitly mark skipped attack probes.
- Replace synthetic discovery entities with observed results.
- Publish a protocol coverage matrix by version and transport.

**Exit gate:** current and legacy fixture servers pass era-specific conformance; unsupported features cannot earn positive evidence.

### Phase 2: Harden the validator (2-6 weeks)

- Add DNS/connect-time/redirect SSRF enforcement and an egress-proxy deployment profile.
- Introduce immutable run context and run-scoped transport sessions.
- Replace shared mutation with deterministic merge.
- Fix semaphore ownership and resource disposal.
- Make response truncation typed.
- Add canary-secret and hostile-report payload tests.
- Add explicit STDIO consent, environment allowlist, limits, and sandbox documentation/image.

**Exit gate:** adversarial boundary tests pass; two concurrent runs cannot share state; cancellation leaves no child process or permit leak.

### Phase 3: Stabilize enterprise contracts (1-2 months)

- Publish versioned JSON schemas for all machine artifacts.
- Add baseline/diff, waiver reason, approver, scope, and expiry.
- Record target artifact digest and rule/profile revisions.
- Add migration and compatibility tests.
- Add code-coverage non-regression, mutation tests for verdict logic, and reproducible performance benchmarks.
- Resolve test dependency licensing.

**Exit gate:** a downstream consumer can safely automate against documented schemas across minor releases.

### Phase 4: Build ecosystem integrations (2-4 months)

- Emit signed in-toto/SLSA-style validation attestations.
- Integrate official Registry metadata and downstream marketplace ingestion.
- Add adapters for package/source scanners rather than duplicating every SCA/malware engine.
- Add executable client-profile conformance fixtures.
- Establish a public vulnerable/compliant calibration corpus and publish accuracy measurements.

**Exit gate:** independent users can reproduce verdicts and assess false-positive/negative behavior.

### Phase 5: Hosted enterprise service (only after Phases 0-4)

- Tenant identity/RBAC, quotas, encryption, retention, deletion, audit, and legal/privacy controls.
- Durable jobs, idempotency, leases, crash recovery, and backpressure.
- Isolated HTTP and STDIO worker pools with enforced egress.
- Object storage, result index, webhooks, dashboards, OpenTelemetry, SLOs, alerts, runbooks, backup, and disaster recovery.
- External penetration test and architecture threat model.

**Exit gate:** independent security review plus demonstrated tenant isolation and failure recovery.

## Verification Performed

### Executed checks

```text
DOTNET_ROLL_FORWARD=Major dotnet test mcp-benchmark-validation.sln \
  --no-restore --configuration Debug --verbosity minimal

Result: 552 passed, 0 failed, 0 skipped.
```

The first attempt without roll-forward compiled but could not start the .NET 8 test host because this workstation has only the .NET 10 runtime installed. Explicit major roll-forward allowed the tests to execute. Four nullable warnings were observed in test source during the first build.

```text
cd mcpval-mcp && npm test

Result: 9 passed, 0 failed.
```

The repository's complete validation script was also executed with network access:

```text
DOTNET_ROLL_FORWARD=Major scripts/validate-repo.sh

.NET locked restore/build/tests: passed
NuGet high/critical audit: passed (none found)
npm install/build: passed
npm audit --audit-level=high: failed
Advisories: 6 total (3 high, 1 moderate, 2 low)
```

Because the npm audit failed, the complete repository validation did **not** pass. This is recorded as ER-022.

### Not performed

- No hostile target was contacted.
- No live OAuth flow was executed.
- No untrusted STDIO command was launched for this audit.
- No cloud-hosted multi-tenant deployment exists to load or penetration test.
- GitHub repository controls were remediated and re-inspected through read-only administration APIs on 2026-08-20. Actions with SHA pinning, secret scanning and push protection, administrator-enforced branch checks, an immutable full-version tag ruleset, and owner-reviewed `NuGet`, `Npm`, and `Ghcr` environments are enabled. Publication remains blocked by 156 forbidden artifact paths reachable in Git history and by the need for fresh pull-request CodeQL results. Published attestation verification was not performed.
- This was not a formal legal/license opinion or independent certification.

## Source Notes

Primary external sources reviewed on 2026-08-18:

- MCP `2026-07-28` specification and changelog: <https://modelcontextprotocol.io/specification/2026-07-28/changelog>
- MCP versioning and dual-era compatibility: <https://modelcontextprotocol.io/specification/2026-07-28/basic/versioning>
- MCP security best practices: <https://modelcontextprotocol.io/specification/2025-11-25/basic/security_best_practices>
- MCP enterprise-managed authorization: <https://modelcontextprotocol.io/extensions/auth/enterprise-managed-authorization>
- Official MCP Inspector CLI and protocol eras: <https://modelcontextprotocol.io/docs/2026-07-28/tools/inspector/cli>
- Official MCP Registry trust model: <https://modelcontextprotocol.io/registry/about>
- Linux Foundation AAIF formation and MCP adoption: <https://www.linuxfoundation.org/press/linux-foundation-announces-the-formation-of-the-agentic-ai-foundation>
- Microsoft Azure MCP documentation: <https://learn.microsoft.com/azure/developer/azure-mcp-server/>
- Microsoft Foundry custom MCP server guidance: <https://learn.microsoft.com/azure/foundry/mcp/build-your-own-mcp-server>

## Bottom Line

The repository has a valuable product core and better-than-average open-source engineering discipline. Its strongest path to enterprise relevance is not adding more score labels. It is making every claim current, reproducible, isolated, and governable.

Fix the process/network execution boundaries first. Add the modern protocol era second. Then stabilize machine contracts and calibration. After those steps, MCP Validator can credibly become a strong open-source CI and registry admission standard. Hosted scanning should remain a later product with a separate threat model and architecture.
