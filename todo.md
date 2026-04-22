# MCP Validator — Improvement Tracker

Tracks report quality, scoring correctness, and architectural improvements identified from real-world validation runs.

## Status Legend

| Symbol | Meaning |
|:------:|---------|
| ✅ | Completed |
| 🚧 | In Progress |
| ⏳ | Planned |
| 🧭 | Future product candidate |

## Improvements

| # | Title | Status | Priority | Scope |
|---|-------|:------:|----------|-------|
| 1 | Machine vs human score inconsistency | ✅ | P0 — Bug | `SecurityFocusedScoringStrategy` |
| 2 | Null recommendations on protocol violations | ✅ | P0 — Bug | `ProtocolComplianceValidator` |
| 3 | Per-profile "why blocked" with rule IDs and remediation | ✅ | P1 | `MarkdownReportGenerator`, `ClientProfileEvaluator` |
| 4 | Tool-metadata completeness matrix | ✅ | P1 | `MarkdownReportGenerator` |
| 5 | Performance failure explanations | ✅ | P1 | `MarkdownReportGenerator`, `PerformanceScoringStrategy` |
| 6 | Aggregate summary for multi-profile runs | ✅ | P2 | `ValidateCommand`, artifact output |
| 7 | Cross-profile delta view | 🧭 | P3 | Report generation |
| 8 | Baseline diff mode | 🧭 | P3 | New feature |
| 9 | Ownership-oriented grouping | 🧭 | P4 | Report generation |
| 10 | Expand report command | 🧭 | P4 | `ReportCommand` |
| 11 | Public remote performance timeout should become advisory, not a hard zero | ✅ | P0 — Correctness | `ValidationCalibration`, `PerformanceValidator` |
| 12 | Distinguish 429/backoff from true server failure in performance score | ✅ | P0 — Correctness | `PerformanceScoringStrategy`, `PerformanceValidator` |
| 13 | Phase synthetic load for public remotes instead of starting at 100-way concurrency | ✅ | P1 — Reliability | `PerformanceValidator`, config model |
| 14 | Narrow MUST trust caps to protocol-blocking spec failures only | ✅ | P0 — Scoring Integrity | `McpTrustCalculator`, spec tier catalog |
| 15 | Replace trust-tier description string matching with structured rule IDs/findings | ✅ | P0 — Correctness | `McpTrustCalculator`, validators, finding model |
| 16 | Make auth challenge validation RFC/MCP exact for bare 401/403 vs valid challenge | ✅ | P1 — Spec Accuracy | `AuthenticationChallengeInterpreter`, auth validators |
| 17 | Surface retry/throttling events explicitly instead of hiding them behind final latency | ✅ | P1 — Observability | `McpHttpClient`, `PerformanceValidator`, reporting |
| 18 | Add public-remote calibration and trust-boundary unit tests | ✅ | P0 — Test Coverage | `Mcp.Benchmark.Tests` |
| 19 | Audit official MCP spec-rule ownership so guidance/heuristics never masquerade as spec | ✅ | P0 — Architecture | protocol registry, validators, scoring boundary |
| 20 | Constrain metadata enumeration to advertised same-family resources | ✅ | P0 — Truthfulness | `MetadataEnumeration`, `SecurityValidator` |
| 21 | Propagate skipped attack outcomes consistently across CLI and reports | ✅ | P1 — Reporting Integrity | `SecurityFormatter`, report generators |
| 22 | Apply central tone palette to report tiles and panels | ✅ | P1 — Report Polish | `ValidationHtmlReportTheme` |

### Latest Evidence Snapshot

- **Artifact sets:** `PublicReports/github-mcp-remote-live-20260422/mcp-validation-20260422-134617-*` and `PublicReports/microsoft-learn-mcp-live-20260422/mcp-validation-20260422-134401-*`
- **Live outcomes:** GitHub remote `Passed`, `73.44%`, `L4`, `Balanced policy requirements satisfied`; Microsoft Learn `Passed`, `92.34%`, `L4`, `Balanced policy requirements satisfied.`
- **Truthfulness checks holding:** GitHub keeps prompt scoring aligned with `2/2` successful prompt executions and reports `Tools/list = 41` consistently across capability snapshot and tool validation; Microsoft Learn renders the zero-prompt path as `No prompts were advertised; no prompt executions were required`; `MCP-SEC-002` still renders as `SKIPPED` rather than a vulnerability.
- **Client compatibility reporting:** `validate` now evaluates all documented host profiles by default; `--client-profile` narrows the evaluated set instead of acting as an enable switch, and both live runs now emit `*-profile-summary.json` by default.
- **Remaining live findings are protocol-only:** `MCP-PROTO-ERR` (parse error, missing `jsonrpc`, invalid params) and `MCP-PROTO-JSONRPC` (batch handling).

### Milestone Closure

- **Current state:** The validator correctness and truthfulness milestone is closed.
- **Active internal backlog:** None. Items `1–22` that affect validator correctness, trust calibration, attack truthfulness, or current report quality are complete.
- **Items `7–10`:** These remain intentionally parked product enhancements, not unresolved defects or release blockers.
- **Endpoint findings below:** These are retained as external observations against the evaluated Microsoft Learn server, not remaining repo work.

### Completed Details

#### 1. Machine vs human score inconsistency

- **Root Cause:** `CategoryScores["Performance"]` used a binary lambda (`Passed ? 100 : 0`) while human reports and `TrustAssessment.OperationalReadiness` used the nuanced `PerformanceScoringStrategy` score.
- **Fix:** Replaced binary lambda with `result.PerformanceTesting?.Score ?? 0` so all consumers see the same score.

#### 2. Null recommendations on protocol violations

- **Root Cause:** `ComplianceViolation` construction sites did not set `Recommendation`.
- **Fix:** Added centralized recommendation mapping and surfaced remediation directly in reports.

#### 3. Per-profile "why blocked" with rule IDs and remediation

- **Root Cause:** Client compatibility details mixed rule IDs, components, and prose into one column.
- **Fix:** Split the report into dedicated fields and added remediation guidance per requirement.

#### 4. Tool-metadata completeness matrix

- **Root Cause:** Missing tool annotations were only visible in per-tool prose.
- **Fix:** Added a matrix view so coverage gaps are explicit and comparable.

#### 5. Performance failure explanations

- **Root Cause:** Performance failures surfaced without threshold or penalty detail.
- **Fix:** Added explicit score breakdowns, request counts, and bottleneck evidence.

#### 6. Aggregate summary for multi-profile runs

- **Root Cause:** `--client-profile all` had no dedicated machine-readable aggregate artifact.
- **Fix:** Emit `*-profile-summary.json` with per-profile status and blockers.

#### 11. Public remote performance timeout should become advisory, not a hard zero

- **Outcome:** Zero-measurement public-remote probes are now advisory instead of forcing a synthetic failure.
- **Evidence:** The latest Microsoft Learn live run reports `Performance | ✅ Passed | 100.0% | -` while still preserving calibration notes in the performance section.

#### 12. Distinguish 429/backoff from true server failure in performance score

- **Outcome:** Rate limiting and retry pressure are tracked separately from genuine stability failures.
- **Evidence:** Performance telemetry now surfaces cumulative rate-limit and transient-failure observations without collapsing them into a false failure signal.

#### 13. Phase synthetic load for public remotes instead of starting at 100-way concurrency

- **Outcome:** Public remote probing now ramps safely instead of opening with the most aggressive concurrency.
- **Evidence:** The latest Microsoft Learn report records stabilization at concurrency `20` before ramping to target concurrency `100`.

#### 14. Narrow MUST trust caps to protocol-blocking spec failures only

- **Outcome:** Trust caps now stay tied to actual protocol-blocking spec evidence rather than broad MUST string heuristics.
- **Evidence:** The latest live result remains `L4` despite non-blocking SHOULD/MAY gaps.

#### 15. Replace trust-tier description string matching with structured rule IDs/findings

- **Outcome:** Trust logic now consumes structured findings instead of prose matching.
- **Evidence:** Remaining blockers in the live artifact are carried as concrete rule IDs such as `MCP-PROTO-ERR` and `MCP-PROTO-JSONRPC`.

#### 16. Make auth challenge validation RFC/MCP exact for bare 401/403 vs valid challenge

- **Outcome:** Authentication interpretation now uses canonical scenario fields and shared challenge parsing.
- **Evidence:** Public-profile runs show `Informational (public profile)` expectations and actual behavior in the auth table instead of hardcoded `400/401 + WWW-Auth` text.

#### 17. Surface retry/throttling events explicitly instead of hiding them behind final latency

- **Outcome:** Retry and throttling telemetry is persisted and rendered separately from final latency.
- **Evidence:** The performance calibration section reports executed rounds plus observed rate-limited and retryable transient counts.

#### 18. Add public-remote calibration and trust-boundary unit tests

- **Outcome:** Focused tests now cover the trustfulness-critical slices touched by the calibration and attack-outcome work.
- **Evidence:** Focused regression runs passed for `AttackVectorIntegrationTests`, `MarkdownReportGeneratorTests`, `ValidationFormatterTests`, and `ReportSnapshotTests`.

#### 19. Audit official MCP spec-rule ownership so guidance/heuristics never masquerade as spec

- **Outcome:** Spec, guideline, and heuristic ownership stay explicit instead of being inferred from presentation text.
- **Evidence:** The latest report labels the remaining protocol violations with source `spec`, while heuristic AI-boundary findings remain separate.

#### 20. Constrain metadata enumeration to advertised same-family resources

- **Outcome:** `MCP-SEC-002` no longer compares unrelated URI schemes or manufactures exploitable vulnerabilities without an advertised comparison surface.
- **Evidence:** The latest live report records `MCP-SEC-002` as `⏭️ SKIPPED` with the note that no advertised concrete resource URI was available.

#### 21. Propagate skipped attack outcomes consistently across CLI and reports

- **Outcome:** Attack outcomes now preserve `SKIPPED` explicitly instead of being collapsed into blocked or detected by renderer-specific heuristics.
- **Evidence:** Markdown/HTML reporting and CLI formatting now share the same attack-outcome resolution path, and focused reporting regressions passed.

#### 22. Apply central tone palette to report tiles and panels

- **Outcome:** Success, info, and neutral report tiles now use the existing centralized theme palette instead of collapsing back to generic gray surfaces.
- **Evidence:** `ValidationHtmlReportTheme` now applies tone-aware borders, washes, and supporting text color to panels, score pills, metric cards, and adjacent ledger-style surfaces from the shared `--tone-*` variables.

### Future Product Candidates

These items are intentionally parked outside the closed correctness milestone. They are not active defects and should only be resumed as explicit product-scope work.

#### 7. Cross-profile delta view

- **Gap:** Six profile reports still repeat shared findings instead of emphasizing only profile-specific differences.
- **Plan:** Revisit after the current correctness slice; this remains a reporting feature, not a trustfulness blocker.

#### 8. Baseline diff mode

- **Gap:** There is still no first-class diff against a prior validation baseline.
- **Plan:** Add later with explicit baseline storage rather than ad hoc report comparisons.

#### 9. Ownership-oriented grouping

- **Gap:** Reports are still category-oriented rather than team/component-oriented.
- **Plan:** Defer until there is a clear triage workflow that justifies the extra abstraction.

#### 10. Expand report command

- **Gap:** `mcpval report` remains format conversion rather than a broader triage/diff surface.
- **Plan:** Keep constrained until there is concrete demand for rollups or filtering.

### External Endpoint Observations

These are current Microsoft Learn server findings from the `2026-04-22 13:44:01 UTC` artifact set. They are external endpoint issues, not current validator truthfulness defects or open repo backlog.

#### A. `MCP-PROTO-ERR` (3 instances)

- **Evidence:** The latest report still records parse-error, missing-`jsonrpc`, and invalid-params error-code mismatches.
- **Source:** `spec`
- **Status:** Open on the evaluated endpoint.

#### B. `MCP-PROTO-JSONRPC`

- **Evidence:** The latest report still records incomplete or inconsistent batch processing behavior.
- **Source:** `spec`
- **Status:** Open on the evaluated endpoint.

---

## Architecture Notes

### Scoring dual-path (by design)

Two independent scoring systems exist intentionally:

- **Compliance Score** (`SecurityFocusedScoringStrategy`): weighted aggregate with rule-based caps for CI gating
- **Trust Assessment** (`McpTrustCalculator`): 4-dimension model with L1–L5 levels for human interpretation

The Performance dimension was the only one treated radically differently between the two (binary vs nuanced). Item #1 aligned them.

### Recommendation data flow

- `ComplianceViolation.Recommendation` → consumed by `ReportActionHintBuilder` and serialized to JSON
- `ValidationFinding.Recommendation` → consumed by `ClientProfileEvaluator` for per-profile "why blocked"
- `ValidationResult.Recommendations` → top-level generic list from `GenerateRecommendations()` (5 threshold-based entries)
- `SecurityVulnerability.Remediation` → separate field name, used by security sections

Item #2 fixed the violation gap. Item #3 surfaces finding-level recommendations in reports.
