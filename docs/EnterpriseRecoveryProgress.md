# Enterprise Recovery Implementation Status

Updated: 2026-11-10

Status: implementation in progress; not release-ready. The approved technical plan contains eight delivery slices. Passing foundation gates do not establish milestone acceptance. Local milestone commits are now authorized only after all applicable acceptance gates; pushing, publication and live enterprise probing remain unauthorized.

## Authoritative Checkpoint

- Current M0 acceptance: the [source-bound receipt](Resources/Acceptance/m0-local-receipt.json) qualifies AR-01 through AR-08 within their tested repair scope. It binds 73 candidate files and 71 evidence files; every digest was verified. The tracker and receipt are excluded from the source digest to avoid self-reference. No whole S1-S8 slice or native Windows milestone is accepted.
- Final local gate: `m0-post-review-repository-gate.log` records 1,222 .NET passes, zero failures, 23 explicit Windows-native skips, 150 wrapper tests, two actual-CLI lifecycle cases and three Node companion tests. Builds have zero warnings/errors; dependency audits and mandatory sensitive-artifact/distribution/retry gates pass. The documentation gate checked 45 Markdown files and 126 links with zero errors and five configured exclusions.
- Mutation completed at 92.11%, above the required 90%, in `wn-current-11-mutation.log`. All three report sources match current source. Scope is assurance, provider harness and calibration only. Subsequent exact-count tests killed all six surviving calibration arithmetic faults in an executed focused rerun at 98.59%; its one remaining case-representative mutation still preserves deterministic case-insensitive transport identity. The scores cover different scopes and must not be presented as a whole-product improvement.
- Installed qualification: correct-directory npm pack, NuGet pack, isolated installations and checked-in package smokes passed. Both installed lifecycle cases passed with observed exit 0. All five product DLLs and all 28 wrapper distribution files match the current build byte-for-byte. Same version 2.0.1 alone was not used as identity.
- Exact workflow lint: official native actionlint 1.7.7 passed after published archive-checksum and executable-version verification. The separate digest-pinned Docker command from the gate matrix also passed with exit 0 after the user authorized starting Docker Desktop. The earlier stopped-daemon failure remains retained. Corrected evidence is under `investigation/package-qualification/actionlint-1.7.7-verified/` and `m0-actionlint-docker-started.log`.
- Rejected evidence: the root-directory npm pack ENOENT, missing integration environment, incorrect spec assembly filename and receipt claiming actionlint 1.7.7 while its executable reported 1.7.12 are not passes. Separate corrected checks were executed; original logs remain retained. The corrected assembly identity is `Mcp.Compliance.Spec.dll`.
- Independent code, documentation/evidence and final calibration-test reviews returned READY FOR BUILD GATE with no concrete scoped blocker. Current local gates qualify M0 for a local commit after staged-byte verification. Next implementation work is the remaining S1 configuration contract. No push or publication is authorized. Native Windows/Linux, protected remote checks and external qualification remain open.

### Historical Checkpoints

The following checkpoints preserve the investigation sequence. Their pending and NOT READY labels describe earlier source states; they are superseded only within the qualified local scope above.

- Active workstream WN: internal Windows Job Object owner, overlapped I/O, host mode, wrapper negotiation and native CI fixtures are implemented. Atomic job-list/handle-list assignment precedes execution; a strict hello/launch/ready/resume protocol gates worker admission; parent-loss publication and final resume share a lock. No taskkill, PID-tree or Windows signal fallback exists. Current checks: 63 portable supervisor tests pass and 23 native tests explicitly skip on macOS (`windows-supervisor-portable-current.trx`); 129 wrapper supervisor/lifecycle tests pass. Exact next action: independently re-review the latest admission/receipt repairs, run current full and package gates, then continue remaining contracts. Native Windows acceptance is still blocked on actual Windows CI execution, not inferred from compilation.
- WN authorization: user explicitly selected the existing CLI internal supervisor, requiring a versioned capability contract, suspended/atomic assignment before execution, bounded cancellation/output/cleanup, safe native handles, native Windows descendant/escape tests and actual Windows CI qualification. macOS/Linux behavior must remain intact. No push/publication is authorized; Windows CI results cannot be claimed from local macOS checks.
- WN latest closure review: independent AI reviewer returned READY FOR BUILD GATE after the decoder-fault admission repair and correction of masked abnormal-close tests. The wrapper close-guard mutation is rejected by all three abnormal-close assertions. This is code-review closure, not native Windows acceptance.
- Superseded gate checkpoint: `wn-current-05-validate-repo.log` recorded 1,220 .NET passes, zero failures and 23 Windows-native skips, 150 wrapper tests, two actual-CLI lifecycle cases and three Node companion tests. At that checkpoint only local actionlint 1.7.12 had passed; exact 1.7.7 verification was still pending. The corrected native 1.7.7 execution and current repository gate are recorded above.
- Superseded package checkpoint: `wn-current-07-mcpval-pack-dry-run.log` recorded ENOENT from running npm pack in the repository root. Its execution summary incorrectly called that a pass. Packed installs and installed lifecycle had not yet run at that checkpoint; subsequent correct-directory packing, isolated installs, exact byte comparisons and installed lifecycle checks passed as recorded above.
- Superseded mutation checkpoint: the run under `Mcp.Benchmark.Tests/TestResults/stryker-wn-current` had scheduled 491 mutants without a final score at the earlier observation. It subsequently completed at 92.11%; the retained `wn-current-11-mutation.log` and current-source comparison supersede that in-flight observation.
- Branch: `fix/enhancements_2026-11-9`; base commit: `7e3584539cdbaee7cedf6b9de83d3bbb064dafdc`. The branch change was observed and preserved, not performed by this agent.
- Pending source ownership: CLI configuration commands/BoundedArtifactReader; Core configuration redaction, finding catalog and calibration; Infrastructure HTTP auth/client, schema advisory analyzer, orchestrator and BaseValidator-derived validators. Associated tests, JSON/SARIF snapshots, CLI README, calibration methodology and changelog are modified; BaseValidatorTests and this tracker are new. No files are staged.
- Active milestone M0: repair and qualify the existing uncommitted foundation diff. Four separate read-only AI adversarial reviews were performed. The fourth returned **READY FOR BUILD GATE** with no concrete M0 blocker; acceptance remains pending current gates and source-bound receipts.
- Exact next action: run mandatory and applicable conditional gates against frozen production code, retain and hash the results, reconcile the matrix, then commit a validated M0 locally. Continue remaining S1-S8 work after that checkpoint; no push or publication.
- Gate addition awaiting supplemental review: `mcpval-mcp/tests/cli-integration.test.ts`, package `test:integration`, fixture method-audit hook and full `validate-repo.sh` wiring. The SDK/actual-CLI/owned-STDIO flow passed locally with target-side Safe-method auditing and temporary-artifact cleanup; 19 existing process-fixture tests also passed. Actual packed-install qualification remains pending.
- Supplemental gate review returned NOT READY: CI did not invoke the test; Windows SDK environment merging retained profile paths; request/test timeout did not prove descendant cleanup. Repairs now include CI invocation, explicit Windows profile overrides, an owned CLI process supervisor, SDK cancellation forwarding and awaited SIGINT/SIGTERM/transport-close shutdown. Two real SDK/CLI/fixture lifecycle cases pass on macOS: request cancellation and host shutdown after an observed held tools/list request. Both require observed CLI/fixture PID exit and temporary-artifact removal. Twenty wrapper unit tests pass, including pre-launch cancellation, serialization failure cleanup and output-budget termination. Supplemental re-review and current full gates remain pending; do not commit based on the earlier production-only review verdict.
- Latest supplemental re-review remains NOT READY (source-transpiled in-memory fault probes, not native platform qualification): WR-01 fail-fast Promise.all can abandon another cleanup after rm failure; WR-02 termination failure can wait forever for close; WR-03 once-only signal handlers allow a repeated signal to bypass cleanup; WR-04 availability probes ignore request cancellation; WR-05 integration teardown deletes the root even when PID exit is unconfirmed. Exact next action: drain all cleanup promises, bound and expose unconfirmed process cleanup while retaining artifacts, persist idempotent signal listeners, forward cancellation into preflight, and reverify owned PID exit before deletion. Add executable regressions, then obtain independent closure review and run current gates.
- Historical evidence: the prior combined gate passed 1,099 .NET and 21 Node tests; mutation was 92.18% for its three-evaluator scope and 100% in the focused calibration rerun. These receipts predate the independent review and do not close its findings.
- Evidence locations: `BenchmarkDotNet.Artifacts/0_feedbacks/2026-11-09/investigation/implementation-combined-gate.log`, `implementation-links.log`, `implementation-mutation.log`, and `implementation-calibration-mutation.log`; mutation JSON under `Mcp.Benchmark.Tests/TestResults/stryker/reports/` and `stryker-calibration/reports/`. Logs may contain workstation paths and are not publishable support bundles.
- External dependencies: original customer artifacts (W00), approved production identities/oracles, manual assistive-technology/operator studies and protected platform/release checks. Synthetic local implementation and qualification remain actionable, not blocked by these dependencies.

### Independent Review Findings

The following independently identified findings have executed before/after regressions and reviewed repairs, as detailed below. Reviews were performed by separate AI reviewers without edits or target calls; they are not external human certification. The fourth review found no remaining concrete M0 blocker. The current source-bound receipt qualifies these local repair requirements, not their broader delivery slices.

| ID | Requirement / code owner | Discriminating acceptance check | Status |
| --- | --- | --- | --- |
| AR-01 | I04: McpHttpClient/McpAuthenticationHelper runtime-owned headers | Selected protocol/method/session identity wins over auth custom headers; ambient runtime defaults fail before dispatch | Verified complete: local repair scope |
| AR-02 | I02/I05: ToolValidator tool invocation outcomes | Cancel or return negative transport status after valid catalog; zero execution-pass credit, retained metadata and correct cancellation/unknown outcome in both APIs | Verified complete: local repair scope |
| AR-03 | I06/I14: canonical redaction and session comparison | Session canary absent from config/request evidence/canonical result; positive, negative and concurrent-session controls | Verified complete: local repair scope |
| AR-04 | I04: SendHttpTransportProbeAsync content headers | Explicit Content-Language reaches content headers; construction failures dispose owned content | Verified complete: local repair scope |
| AR-05 | I04/I07: raw RPC/JSON auth provenance | Custom Authorization yields correct 200/401 evidence without credential persistence; local rejection never blames target | Verified complete: local repair scope |
| AR-06 | I02/I03: catalog pagination accumulator | Malformed first-page violation survives later cancellation into claims/SARIF; failed later page never yields complete empty-catalog pass | Verified complete: local repair scope |
| AR-07 | I05/I06: partial-run finalization | Completed category/scenario/transport failures survive canonical projection; summaries agree; bootstrap/applicability faults finalize safely | Verified complete: local repair scope |
| AR-08 | I07/I10: calibration exact thresholds and determinism | Just-outside raw rates cannot qualify after rounding; equivalent permutations/cultures yield identical semantic results | Verified complete: local repair scope |

Additional locally qualified repairs: uncertain operations are not automatically replayed; selected error probes dispatch only their selected payloads in HTTP and STDIO. Remaining full-program gaps include aggregate catalog bounds, complete config parser/property matrix, complete entrypoint/artifact qualification, and enumeration of later packs that were never started after interruption. These remain actionable, not waived residuals.

### Executed Adversarial Repairs

Rows below have independent review closure and current source-bound local qualification. BEFORE failures were retained, not skipped or weakened. The separate second AI review found additional response-session, nested-scenario and summary/finalization issues; those were also reproduced and repaired.

| Requirement / code | Discriminating executed tests | Retained evidence / result |
| --- | --- | --- |
| AR-02 / ToolValidator | Negative transport, caller cancellation and internal timeout through production discovery; alternate execution stops after first cancelled invocation; HTTP 400/404 does not earn execution credit | `ar02-before.log` (3 failures); `ar02-after.trx`; `tool-execution-before.trx` (6 failures), `tool-execution-after.trx` (65 passing tool tests) |
| AR-01/03/04/05 / HTTP client and configuration redaction | Runtime headers vs auth custom headers, content-language forwarding, 200/401 custom-Authorization evidence, session canaries; ambient defaults fail before dispatch | `ar-http-before.trx` (6 failures), `ar-http-after.trx`; `ar-http-defaults-before.trx` (4 failures/3 controls pass), `ar-http-defaults-after.trx` (158 tests pass) |
| AR-03 / protocol session handling | Real HTTP client + protocol validator: matched, missing and wrong session controls; no response-session canary in canonical clone | `ar03-response-before.trx` (failure), `ar03-response-after.trx`; expanded controls in `ar-http-defaults-after.trx` |
| AR-06 / pagination and typed findings | Malformed first page followed by cancellation retains violation in claims/SARIF; empty first page plus failed next page is inconclusive | `ar06-before.trx` (2 failures/1 control pass), `ar06-after.trx`, `ar06-canonical-after.trx` (65 tests pass) |
| AR-07 / service interpretation | Completed violation survives into layers, coverage, claims and six formats; opposite controlled completion schedules produce equal semantic projections | `ar07-before.trx` (failure), `ar07-after.trx`, `ar07-report-schedule.trx` (22 tests pass) |
| AR-07 / terminal summary and preparation | Correct partial failed-test count, generic bootstrap and applicability exceptions finalized without secret payloads | `ar07-summary-bootstrap-before.trx` (2 failures), corresponding after receipt; `applicability-failure-before.trx` (failure), `applicability-failure-after.trx` (25 tests pass) |
| AR-07 / nested evidence | Completed extension failure survives later scenario cancellation into claims/SARIF; completed HTTP transport failure survives later probe cancellation | `extension-partial-before.trx` and `protocol-partial-before.trx` (failures); corresponding after receipts (24 and 143 tests pass) |
| AR-08 / calibration | Just-outside thresholds fail despite display rounding; reversed sample order under en-US/fr-FR/tr-TR preserves serialized decision semantics | `ar08-before.trx` (4 failures), `ar08-after.trx` (23 tests pass) |
| I04 / retry safety | Synthetic accepted operations followed by timeout/503 execute exactly once; discovery retries retain request correlation | `uncertain-retry-before.trx` (6 failures/1 control pass), `uncertain-retry-after.trx` (94 tests pass) |
| I04 / exact error-probe admission | One selected error check sends one payload; empty/invalid selections do not send; real STDIO child and HTTP receiver count actual attempts | `selective-probes-before.trx` (failure), `selective-probes-after.trx`, `selective-probes-wire-matrix.trx` (10 tests pass) |
| I02/I04 / session correlation | Controlled in-flight session rotation preserves comparison against the request-local snapshot | `session-snapshot-before.trx` (failure), `session-snapshot-after.trx` (91 tests pass) |
| I02/I04/I07 / third-review closures | Local rejection has zero contact and no target violation; raw JSON Authorization evidence agrees with receiver; rejected construction disposes content | `review3-before.trx` (4 failures), `review3-after.trx` (110 tests pass) |
| I07 / mixed response attribution | Available valid/invalid error response plus unavailable probe retains both meanings without pass inflation | `error-mixed-qualification.trx` (10 tests pass) |
| C01-C12 subset / actual adapter path | SDK initializes built wrapper, invokes actual CLI health/inventory against owned STDIO fixture, checks target-side Safe audit, V2/profile/exit fields, explicit protocolEra forwarding and cleanup | Two built and two installed lifecycle cases pass; exact installed bytes verified; CI/full gate invocation added |

TRX receipts are under `Mcp.Benchmark.Tests/TestResults/`; the AR-02 before log is under the investigation directory listed above. Retained TRX paths are hash-bound in the milestone acceptance receipt. The original two initialize-retry fixtures were preserved with corrected one-attempt/zero-retry expectations because initialization can create session state; a separate discovery retry control retains retry verification. The original timeout fixture was preserved with stronger inconclusive/no-credit assertions.

Determinism evidence excludes generated request IDs, run IDs, elapsed durations and timestamps from semantic schedule comparisons. Tests explicitly compare retained layers, coverage and claims; request retries must preserve the same request ID. The ten-second task waits are harness deadlock limits, not performance budgets. No real enterprise target, production credential or model inference was used.

Wrapper lifecycle qualification uses a bounded fixture method/PID audit and readiness polling, not elapsed-time success assertions. Generated-token properties use seed 5063504. POSIX cleanup now requires observed process-group disappearance. Windows taskkill was rejected and removed; the internal Job Object supervisor is the only Windows route, with no weaker fallback. Native Windows execution remains unverified locally. POSIX SIGKILL/host crash and deliberately escaping descendants require external job/container supervision and are not claimed covered by graceful-shutdown tests.

| WN requirement | Code / verification owner | Status |
| --- | --- | --- |
| Versioned bounded launch/capability protocol | WindowsSupervisorProtocol; portable parsing/bounds/quoting tests executed | Verified complete: portable contract scope |
| Atomic job assignment and handle ownership | WindowsJobProcess; ABI/flag/handle-contract tests pass locally, native execution pending | Implemented, qualification pending |
| Cancellation, parent loss, deadlines, output and cleanup receipt | WindowsSupervisorHost, WindowsSupervisorConsoleStreams and wrapper adapter; controlled fault tests pass, independent closure review complete | Portable scope qualified; native pending |
| Safe Windows fallback behavior | Strict compatible hello and ready/resume required; malformed/missing receipts remain unconfirmed; zero taskkill/signal fallback | Verified complete: portable fail-closed contract |
| Native descendants, leader exit, nested jobs and escape | 23 Windows-native tests plus fail-closed TRX gate; release-reservation depends on windows-supervision | Blocked: actual Windows CI execution pending |
| macOS/Linux regression and package parity | Built and installed macOS lifecycle gates pass; five product DLLs and 28 wrapper files match; Linux/Windows separate | macOS scope qualified; other platforms pending |

WN review history: the first independent native/host review found startup outside the deadline, parent EOF admission, a forbidden wrapper signal fallback, missing release dependency and detached synchronous I/O. Repairs added overlapped owned pipes, deadline-before-creation, two-phase resume admission, drained I/O, no signal fallback and the release dependency. A second review reproduced parent-loss publication racing the final resume and a successful receipt accepted without admission. Both were reproduced locally and repaired; `windows-admission-race-before.trx` fails and `windows-admission-race-after.trx` passes. The wrapper's two no-resume completion counterexamples also failed before repair. Latest closure review and local gates pass, including the full 150-test wrapper suite; native qualification remains pending. Native test oracles now release signaled process handles before requiring job accounting to reach zero.

Windows authority pins: UpdateProcThreadAttribute (2025-07-01, `fa53641576e3603fa7b66d3a4ad969d3ce49d6f3`), Job Objects (2025-07-16, `3aee48ac3f48e187d17bdfd56951741737331749`), and JOBOBJECT_BASIC_ACCOUNTING_INFORMATION (2024-02-22, `9267262487657894a8af112d7165006fed5035a7`). Job containment is not a sandbox and does not claim containment of externally brokered/WMI-created processes. No breaking-away job flag is enabled.

### Requirement Traceability

Statuses are **Verified complete**, **Implemented, qualification pending**, **Blocked**, **Incomplete**, or **Explicitly out of scope**. No whole delivery slice is verified complete. The original proposal/technical/HTML/CI documents remain the detailed acceptance definitions; grouped requirements below remain individually open until expanded with their own receipt. A test name is a verification method, not an acceptance receipt.

| Requirement | Production owner / current extent | Verification method and retained evidence | Classification |
| --- | --- | --- | --- |
| S1 / I01 | BoundedArtifactReader and CLI loaders: strict parsing implemented; version/provenance/schema work absent | Reader and public-command tests; combined gate log. Boundary/all-entrypoint qualification still required | Incomplete |
| S2 / I02 | BaseValidator and McpValidatorService: shared accumulators and fixed merge; AR-02/06/07 locally qualified | Source-bound before/after regressions and final gate; complete obligation/catalog surfaces remain open | Incomplete |
| S2 / I03 | ToolAiReadinessAnalyzer/ToolValidator: crash repairs and local recovery; bounded dialect/ref/composition inspector and complete counts absent | Schema-shape and malformed-neighbor tests; actual new inspector acceptance receipt absent | Incomplete |
| S3 / I04; G01-G05 | HTTP client: runtime header ownership, auth provenance, selected probes and uncertain retry repair qualified | Source-bound receiver/retry regressions close AR-01/03/04/05; typed request context and complete version/direction matrix remain open | Incomplete |
| S4 / I05 | Existing evidence/assurance reducers: full obligation ledger and frozen interpretation contract incomplete | Schedule permutation, cancellation finalization, baseline compatibility and cross-format assertions required | Incomplete |
| S4/S6 / I06 | Existing reporting projection/AtomicFileWriter: no complete artifact transaction or redesigned document | Fault injection before/during/after persistence, canonical parity and R01-R20 receipts required | Incomplete |
| S5 / I07/I10 | RuleCalibrationEvaluator: class/identity/exact-rate/culture guards qualified; AR-08 closed locally | Source-bound calibration tests and scoped mutation receipt; diversity, holdout and abstention contracts remain open | Incomplete |
| S7 / I08 | Existing scenario packs: controlled identity/state/relay pilot not implemented | Approved synthetic positive/negative/unavailable controls required; no acceptance receipt | Incomplete |
| S8 / I09 | Existing execution policy: aggregate budgets and planned request-demand accounting incomplete | Boundary exhaustion, cleanup and equivalent-obligation cost accounting required | Incomplete |
| S8 / I11; K01-K09 | Existing benchmark project: scale suites not implemented | Existing six-workload contract check is not execution of K01-K09 or performance evidence | Incomplete |
| S5 / I12 | ValidationObservability: numeric/windowed metric interpretation not repaired | Released synthetic receipts T01-T05 are counterexamples, not corrected-product acceptance | Incomplete |
| All slices / I13 | Existing ownership reused; no new platform introduced | Architecture tests passed historically; independent review must reconfirm each changed milestone | Implemented, qualification pending |
| S6 / I14; R01-R20 | Stable actionable support/report experience not implemented | Uncoached tasks, static/offline/print, keyboard/assistive, privacy and cross-format tests required | Incomplete |
| S6 / C01-C12; G06-G07 | CLI/wrapper/Action pipeline contracts not completed | Real local entrypoint/exit/artifact and workflow forwarding checks required | Incomplete |
| S3/S8 / M01-M24; B01-B10 | Capability-depth and adoption matrix remains a proposal | Pinned requirement applicability, official differential and qualified optional-feature receipts required; no core credit for Preview | Incomplete |
| W00 original incident reconciliation | Customer package/config/result/report/route records unavailable | Approved redacted customer bundle required; synthetic runs cannot substitute | Blocked |
| External enterprise/platform qualification | Production identity/oracle approval, human studies and protected release infrastructure unavailable | Named approvals and observed platform/reviewer receipts required; local work continues | Blocked |

Determinism qualification must cover culture/time zones, assessment clocks, ordering and concurrency, retries, IDs/temp paths, seeds, numeric thresholds, dependency/protocol pins and environment. Document excluded volatile metadata explicitly; compare semantic projections under varied permitted schedules rather than serializing valid protocol concurrency.

## Implemented Foundation

| Area | Implemented behavior | Evidence |
| --- | --- | --- |
| Configuration | Shared bounded strict reader; explicit missing-file rejection; unknown/duplicate field and required-null rejection; field aliases with dictionary case preservation | BoundedArtifactReaderTests and ValidateCommandTests |
| Category result ownership | All production BaseValidator callbacks use the base-owned accumulator; late error/cancellation/timeout retains observations | BaseValidatorTests and ToolValidatorComprehensiveTests |
| Orchestration | Sequential immediate merge and fixed-order parallel terminal merge retain completed siblings on cancellation | McpValidatorServiceUnitTests |
| Catalog recovery | Malformed names/descriptions/titles/annotations do not discard valid neighbors; rejected catalog entries prevent a successful empty-list result; earlier-page inventory survives cancellation | ToolValidatorComprehensiveTests and ToolValidatorIntegrationTests |
| Schema heuristics | Union/boolean shapes no longer crash; exact required-name matching; absent minItems does not imply an invalid required array | ToolAiReadinessAnalyzerTests |
| Calibration | Both expected classes and unique non-empty sample IDs required; policy revision updated | RuleCalibrationEvaluatorTests |
| Raw request headers | Selected authentication custom headers applied across RPC/POST/transport probes; explicit overrides, no-auth isolation, safe malformed-header diagnostics and canonical credential redaction | McpHttpClientCapabilitiesTests and existing HTTP/redaction suites |
| Disabled error probes | Error-code probing requires an enabled built-in error-code check; custom-only configurations report unsupported scenarios without transport calls | ErrorHandlingValidatorUnitTests |

The legacy protected BaseValidator overload remains compatible. External derived validators must use the accumulator-aware overload to retain callback-owned evidence; an arbitrary old callback's private local objects cannot be recovered after it throws.

## Authority And Compatibility

Schema inspection corrections are grounded in JSON Schema draft 2020-12: [Core sections 4.3.2 and 10.3.1.2](https://json-schema.org/draft/2020-12/json-schema-core) and [Validation sections 6.1.1, 6.4.2 and 6.5.3](https://json-schema.org/draft/2020-12/json-schema-validation), published 2022-06-16. Boolean schemas, type alternatives, omitted minItems and exact property names are not new MCP requirements. Advisory inspection remains distinct from schema conformance and does not resolve arbitrary references or prove model accuracy.

Configuration acceptance is intentionally stricter than 2.0.1. The root canonical contract version is unchanged; raw optional transport session identity is now excluded from serialization for privacy, while typed session validity/propagation outcomes remain. Selective error probing is an additive transport API whose default implementation rejects unsupported selection instead of dispatching a broader suite. Finding-catalog and calibration-policy revisions change because interpretation changed. Historical diagnostic receipts against the released package remain historical evidence and are not rewritten to look repaired. No SDK, protocol-version pin or package version is changed.

## Remaining Requirements

| Slice | Open work |
| --- | --- |
| S1 / W01 | Versioned public configuration schema, explicit migration policy, per-setting provenance, complete wrapper/example compatibility matrix |
| S2 / W02/W06 | Shared bounded dialect/reference/composition inspection; raw/recovered/rejected/analyzed counts; complete pagination disposition; recovery across remaining catalog surfaces and all nested stages |
| S3 / W03/W04/W15 | Typed SDK/raw request identity and routing contract beyond the repaired custom-header forwarding; exact operation admission; protocol role/revision/header/result corrections and full receiver matrix |
| S4 / W05/W10 | Complete obligation ledger, attribution and frozen evaluator inputs; baseline migration; atomic artifact-set completeness |
| S5 / W06/W11 | Calibration diversity/holdout/abstention contracts; not-measured/windowed metric semantics; actual CPU/resource attribution |
| S6 / W07/W14 | Document-first HTML implementation, cross-format and CI interface parity, accessibility and operator-comprehension qualification |
| S7 / W08/W09/W13 | Approved identity/state oracles and two-boundary relay pilot with independent negative controls |
| S8 / W11/W12/W16 | Aggregate budgets, declared scale envelope, benchmark suites, adoption candidates, cross-platform package and independent qualification |

W00 remains open: the original customer package identity, effective config, canonical result, report and trusted target/identity evidence are unavailable. Synthetic regressions cannot reconstruct those incidents. These residuals must remain visible and be transferred to the repository issue tracker before release; remote issue creation has not been requested.

## Historical Foundation Verification

Foundation environment: macOS arm64, .NET SDK 8.0.419, Node 24.19.0. Focused checks ran after each edit. Review kept configuration parsing in the CLI, collection and deterministic merging in Infrastructure, and calibration policy in Core. Snapshot review confirmed only eight rule-revision fields changed across JSON and SARIF.

| Check | Result |
| --- | --- |
| `git diff --check` | Pass |
| `scripts/check-sensitive-artifacts.sh` | Pass |
| `scripts/verify-distribution-contracts.sh` | Pass; package version remains 2.0.1 |
| `scripts/test-release-retry.sh` | Pass |
| `scripts/validate-repo.sh` | Pass; 1,087 .NET tests, 3 companion Node tests, 18 wrapper tests; no skips/failures; builds have zero warnings/errors |
| Dependency audits | No high/critical NuGet findings; zero npm vulnerabilities |
| Benchmark contract check | Six existing workloads verified; not a new performance measurement |
| `scripts/validate-mutation.sh` | Pass at 92.18% against the required 90%; scope is assurance, provider harness and calibration |
| Focused calibration mutation rerun | Pass at 100%; all 63 tested mutants killed after adding independent missing-ID/class-support tests; not a whole-product mutation score |
| Documentation workflow commands | 45 Markdown files lint-clean; 125 links checked, zero errors, five configured exclusions |
| Built CLI isolated dry-run smoke | Casing aliases accepted with exit 0; unknown and duplicate fields rejected with usage exit 64; no target contact |

The full mutation run also reports surviving and uncovered cases in unchanged assurance/provider-harness code; the aggregate gate does not imply exhaustive detection. Full-run and focused mutation JSON reports are retained under the test-results directories. Local build/link/mutation logs are investigation artifacts and may contain workstation paths; they are not public support bundles.

Historical decision: foundation-batch local gates passed; the enterprise recovery program remained **NOT READY**. See the authoritative checkpoint for current qualification. A passing first-batch receipt does not close the remaining requirements above. Remote protected checks, platform matrices, signing and registry publication remain separate release requirements.

Routing follow-up: 93 focused HTTP/redaction tests and eight error-validator tests pass, including disabled/custom-only no-contact cases. The final combined mandatory gates pass with **1,099 .NET tests and 21 Node tests**, no skips/failures, zero build warnings/errors, no high/critical NuGet findings and zero npm vulnerabilities. Documentation lint, sensitive-artifact, distribution and release-retry checks also pass. The mutation scores above cover their declared Core evaluator scope, not the HTTP routing code.

An arbitrary custom header cannot be safely classified as routing versus authentication solely by its name. Explicit auth selection therefore does not inherit default authentication custom headers; operators should keep stable non-secret route headers in server headers. Typed header roles and broader identity/route assertion remain open. The canonical classifier redacts known credential-bearing names, not arbitrary undisclosed secret names. These follow-ups do not complete S3 or the overall recovery program.
