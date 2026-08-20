# Calibration Methodology

MCP Validator separates deterministic observations, rule decisions, coverage debt, client compatibility, and model-assisted advice. A score is a summary of versioned evidence, not a substitute for the evidence or a certification claim.

## Versioned Decision Inputs

Every verdict contains a `policy` manifest with:

- closed outcome taxonomy version;
- stable finding-rule revisions;
- protocol feature and rule-pack revisions;
- aggregate and trust-dimension weights;
- pass, coverage, and trust thresholds;
- active score caps and every numeric scoring parameter;
- semantic keyword sets and score-affecting regular expressions.

Every rule-backed decision records `ruleId` and `ruleRevision`. This allows a consumer to reconstruct which policy interpreted the evidence even after later releases change defaults.

## Authority And Lanes

Decision authority is ordered as:

1. MCP or referenced normative specification;
2. published implementation guideline;
3. deterministic local heuristic;
4. model-assisted advisory output.

Baseline, client-compatibility, and model-advisory lanes remain separate. Model evaluation is opt-in, provider-explicit, receives a purpose-built minimal projection rather than the canonical result, writes a separately versioned companion artifact, reports its own token/currency cost, and cannot alter deterministic verdicts.

## Outcome Taxonomy

All test, run, coverage, and probe statuses map exhaustively to the canonical taxonomy:

`notEvaluated`, `running`, `succeeded`, `failed`, `skipped`, `authRequired`, `inconclusive`, `notApplicable`, `unavailable`, `blocked`, `cancelled`, and `error`.

Unsupported enum values throw instead of falling through to pass or failure defaults. Explicit `Unknown` dispositions map to inconclusive evidence and never inherit legacy boolean pass flags.

## Public Calibration Corpus

The committed secret-free corpus at `Mcp.Benchmark.Tests/Fixtures/Calibration/corpus-v1.json` contains exact verdict and score goldens. `CalibrationTargetIntegrationTests` runs all six target classes against real WireMock HTTP targets through the production protocol, authentication, security, verdict, and scoring components. The harness classifies the directly observed HTTP availability state into the canonical outcome taxonomy through the same `ValidationCoverageFactory` used by `McpValidatorService`; it does not synthesize protocol violations or security vulnerabilities. The vulnerable class establishes protocol conformance first and then runs the production security validator against a server that accepts every bearer token, so protocol mechanics and exploit evidence remain independently observable:

| Case | Purpose | Expected effect |
| --- | --- | --- |
| compliant | Complete high-confidence evidence with no blockers | Trusted |
| vulnerable | Confirmed critical security finding | Reject |
| malformed | High-severity protocol violation | Reject |
| unavailable | Transport evidence unavailable | Review required, not pass/fail |
| throttled | Transient/inconclusive evidence | Review required |
| auth-protected | Surface requires credentials | Review required until exercised |

The targets are deterministic local fixtures. Live endpoints are unsuitable as golden fixtures because availability, deployment, and policy change independently of validator code.

## Test Strategy

- Golden tests assert exact baseline, protocol, and coverage verdicts plus numeric score ranges for each corpus case.
- Production differential tests compare compliant/vulnerable/malformed/unavailable/throttled/auth-protected verdicts and scores.
- Focused authority-lane tests verify specification, guideline, heuristic, and model decisions remain separate.
- Synthetic metamorphic tests prove adding blocking evidence cannot improve a verdict; they are not golden calibration evidence.
- Stryker.NET 4.16.0 mutates the production `VerdictReducer`; CI enforces a 90% break threshold. The current certificate kills 26/26 tested mutants (100%).
- Snapshot tests certify stable human and machine representations.

## False-Positive Review

Reviewed false-positive controls include:

- unavailable, throttled, blocked, authentication-required, and parser-boundary evidence cannot earn pass credit;
- synthetic invalid JWT acceptance proves invalid-token acceptance, not audience bypass;
- controlled wrong-resource credentials are provider-backed and baseline-paired;
- discovery-only authentication probes avoid mutating target operations;
- heuristic findings cannot masquerade as specification authority;
- model output cannot enter the deterministic decision lane.

## False-Negative Review

Known residual risks include:

- an unobserved server path can remain vulnerable;
- downstream token forwarding requires a controlled sink to prove forwarding, not only wrong-resource acceptance;
- metadata-only AI-safety analysis cannot prove runtime consent or policy enforcement;
- local application egress controls do not replace hosted network enforcement;
- a synthetic corpus cannot represent every provider, proxy, or deployment topology.
- the vulnerable calibration class uses separate deterministic protocol-conformance and credential-bypass phases because authenticated active protocol probes do not yet forward target credentials;
- the harness owns only deterministic WireMock behavior and direct HTTP availability classification; category findings, vulnerabilities, verdicts, and scores are production-owned;
- production mutation scope currently covers the pure verdict reducer, while projection and scoring remain protected by focused, golden, differential, and monotonicity tests.

These limits produce explicit unevaluated dimensions or coverage debt rather than implicit passes.

## Change Policy

Any change to a rule, gate, weight, threshold, cap, outcome mapping, or pack revision must:

1. increment the owning version;
2. update the decision-policy manifest;
3. update or add corpus cases;
4. pass golden, monotonicity, production mutation, and snapshot tests;
5. document intentional classification changes.
