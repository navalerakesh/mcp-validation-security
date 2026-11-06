# Organization-Grade Confidence

## Decision

MCP Validator is ready to be evaluated as an organization-grade local and CI dependency for authorized MCP targets within the scope below. This conclusion is evidence-bound: it is not formal accreditation, a universal live-provider certificate, a sandbox for arbitrary code, or a hosted multi-tenant service claim.

The product decision surface is V2 assurance only. Typed evidence projects into independent core conformance, security assurance, operational readiness, extension readiness, external conformance, and interpreted client compatibility. Certificate eligibility and process exits consume those assessments; descriptive category scores cannot override them.

## Supported Scope

| Use case | Confidence statement |
| --- | --- |
| Owned or explicitly authorized HTTP target | Supported with execution policy, outbound origin/address controls, bounded evidence, stable exits, and canonical reports |
| Reviewed local STDIO target | Supported with shell-free structured launch, environment allowlisting, bounded streams, cancellation, and process-tree cleanup; use a disposable sandbox for untrusted commands |
| CI change gate | Supported with `inventory`, `ci`, or `provider` assurance, baseline comparison, SARIF/JUnit/JSON, and deterministic exit semantics |
| Provider evidence | Supported when signed authorization, signed prior-run harness, exact plan/target/protocol binding, verified official conformance, current-run evidence, and final attestation all pass |
| Offline governance/reporting | Supported from canonical JSON without target contact or decision recomputation |

## Confidence Chain

### Canonical decisions

- Raw findings and coverage precede claims.
- Missing, unavailable, blocked, authentication-required, skipped, inconclusive, and not-applicable evidence remain distinct.
- Core, security, operations, extensions, clients, and external conformance remain separate typed lanes.
- The V1 trust calculator, global verdict engine, duplicate decision manifest, parallel policy branch, and report fallbacks are removed.
- Heuristic and model-assisted claims remain review-only. The current heuristic corpus is intentionally below admission thresholds and cannot silently become provider-blocking authority.

### Provider admission

Provider evidence uses two P-256/SHA-256 P1363 signatures verified against an operator-trusted public key:

1. The authorization grant binds authority, target identity, validity window, execution modes, exact planned probe classes, algorithm, and key ID.
2. The harness binds provider identity, protocol revision, grant digest/signature, required consistency, every prior run ID, result and execution-plan digest, timestamps, independent assessment outcomes, and sorted non-success claim IDs.
3. The CLI dry-run plan digest binds target, transport, protocol, mode, limits, allowed origins/hosts, and planned checks.
4. The current run is produced inside the admitted process and compared with signed prior runs.
5. The final canonical artifact records target and plan digests, claims, assessments, harness outcome, and current-run result, then receives a separate P-256 attestation.

A controlled end-to-end command test proves an eligible provider result, stable exit `0`, official conformance import, target/plan binding, and verifiable final signature. No production provider was contacted for this proof.

### Official conformance

The isolated companion verifies:

- `@modelcontextprotocol/conformance@0.2.0-alpha.12` tarball SHA-256 and npm integrity;
- source commit `f44482ba17df816d3176962a11cdf36aec9bda00`;
- the exact committed 120-entry transitive dependency lock with lifecycle scripts disabled;
- requirement set `2026-07-28` and its SHA-256;
- all 37 required and 13 not-scored server scenarios;
- exact bounded `checks.json` bytes before .NET parsing;
- target identity, transport, protocol, invocation, timestamps, exit, and run state.

Incomplete runs import as incomplete and earn no pass credit. Completed wrappers with missing, unexpected, empty, duplicate, or misclassified scenarios fail closed.

## Executable Evidence

The organization-confidence change set includes:

- full .NET and npm suites;
- focused positive, negative, malformed, unavailable, tampered, boundary, and end-to-end provider tests;
- frozen-scenario and exact-byte conformance importer tests;
- 50 parallel isolated plan/assurance runs with unique identities;
- 25 parallel byte-deterministic offline renders;
- 1,000 stateless provider-harness evaluations;
- real STDIO fixture, environment-isolation, response-bound, cancellation, and process cleanup tests;
- report snapshots and JSON Schema contract tests;
- mutation testing of assurance, provider harness, and calibration reducers;
- desktop and 390-pixel mobile browser inspection, keyboard disclosure, zero console errors, no document overflow, and computed WCAG AA contrast across 992 semantic text nodes;
- package, action, documentation, security-audit, and distribution gates listed in the final local readiness receipt.

## Observed Local Readiness Receipt

Observed on macOS arm64 with .NET SDK `8.0.419`, .NET runtime `8.0.25`, Node.js `24.19.0`, npm `11`, and Docker Desktop. Commands used checked-in scripts and the exact workflow pins where available.

| Evidence | Observed result |
| --- | --- |
| Debug build | 0 warnings, 0 errors |
| .NET suite | 1,026 passed, 0 failed, 0 skipped |
| Companion Node tests | 3 passed, 0 failed |
| npm wrapper tests | 15 passed, 0 failed |
| Dependency audits | 0 npm vulnerabilities; no high/critical NuGet vulnerabilities |
| Mutation | 92.90%, 432 killed, 27 survived, 3 no coverage, 0 timeouts; 90% break threshold passed |
| Parallel/soak proof | 50 isolated parallel plans/reductions, 25 deterministic parallel renders, 1,000 harness reductions |
| Browser/accessibility | 992 semantic text samples, 0 WCAG AA contrast violations, 0 console errors, 0 unsafe anchors/scripts/ARIA gaps, desktop/mobile overflow contained |
| Markdown/docs | 43 files linted with 0 errors; 118 links checked, 108 OK, 5 excluded, 5 redirects, 0 errors |
| GitHub Action | Digest-pinned actionlint `1.7.7` passed |
| npm package | Typecheck/build/audit passed; 26 files, 21.9 kB packed, 88.7 kB unpacked |
| NuGet tool | `McpVal.2.0.0.nupkg`, 4,689,329 bytes; clean local-tool install and version smoke passed |
| Standalone | `osx-arm64` self-contained single file, 83,138,032 bytes; version smoke passed |
| Container | arm64 image `sha256:7755d07a93fff1da8f4f3ead97e6492a47d80719172aa9930efb65b7e56fdc59`, 36,279,408 bytes; binary and OCI label both `2.0.0` |
| BenchmarkDotNet short run | 6 workloads completed; assurance reduction 1.858 microseconds, scoring 1.249 microseconds, Markdown rendering 5.916 microseconds |
| Mandatory repository gates | diff, sensitive-artifact baseline, distribution contracts, release retry, and repository validation passed |

The mutation row records the last isolated frozen-source receipt with zero timeouts. Overlapping exploratory mutation runs were discarded and are not evidence.

## Stable Exit Contract

| Exit | Meaning |
| ---: | --- |
| `0` | Selected assurance and policy requirements satisfied |
| `1` | Deterministic assurance or policy failure |
| `2` | Incomplete evidence or review required |
| `64` | Usage or configuration error |
| `70` | Runtime or artifact integrity failure |
| `75` | Target unavailable |
| `124` | Timeout or cancellation |

## Reproduction

```bash
# Standard repository gate
./scripts/validate-repo.sh

# Reducer quality gate
./scripts/validate-mutation.sh

# Controlled scale and provider proofs
dotnet test Mcp.Benchmark.Tests/Mcp.Benchmark.Tests.csproj \
  --filter 'FullyQualifiedName~OrganizationScaleProofTests|FullyQualifiedName~ProviderCertificationCommandTests'

# Isolated official conformance producer contract
node --test scripts/tests/run-official-conformance.test.mjs
```

Generate official evidence only for a target you own or are explicitly authorized to test:

```bash
node scripts/run-official-conformance.mjs \
  --url https://provider.example.com/mcp \
  --output /secure/path/official-conformance.json \
  --results-dir /secure/path/official-results
```

## Explicit Limits

- Built-in client profiles are documentation-derived and labeled `Interpreted`; executable client verification is planned.
- The rule-quality corpus does not satisfy the 100-sample blocking threshold, so heuristics remain review-only.
- STDIO process controls are not an operating-system sandbox. Arbitrary untrusted commands require an externally enforced disposable worker, restricted network, filesystem policy, and no ambient credentials.
- The repository does not claim hosted multi-tenant isolation, durable fleet SLOs, or universal live-provider coverage.
- Local evidence cannot replace protected GitHub CodeQL, dependency review, multi-OS package matrices, registry indexing, signing identity, or environment approval checks.
- A passing local gate lowers risk; it does not guarantee remote infrastructure or third-party registry availability.
