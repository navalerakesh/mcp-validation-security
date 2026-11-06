# Calibration Methodology

MCP Validator V2 separates deterministic observations, typed claims, evidence debt, independent assessments, client compatibility, and model-assisted advice. It does not use a global score as an assurance or certification gate.

## Rule Quality Gate

Heuristic rules are review-only unless a versioned labeled corpus demonstrates all default-blocking thresholds:

- at least 100 labeled samples;
- precision of at least 95%;
- recall of at least 90%;
- false-positive rate of at most 5%;
- false-negative rate of at most 10%.

`RuleCalibrationEvaluator` calculates the confusion matrix and metrics from individual labeled samples. Empty, undersized, or one-class corpora fail closed. Official specification failures and directly demonstrated deterministic security control failures are authority-based evidence and do not become heuristics merely because they are not statistically calibrated.

The initial `rule-quality-v2.json` corpus documents known behavior of `MCP.CONTENT.SystemImpact`, including benign destructive names and dangerous operations missed by name matching. It intentionally does not meet the minimum sample count, so the rule remains review-only.

## Versioned Decision Inputs

Every claim and assessment contains versioned authority and policy inputs with:

- closed outcome taxonomy version;
- stable finding-rule revisions;
- protocol feature and rule-pack revisions;
- assurance profile coverage and confidence thresholds;
- rule-calibration thresholds and measured metrics;
- semantic keyword sets and score-affecting regular expressions.

Every rule-backed decision records `ruleId` and `ruleRevision`. This allows a consumer to reconstruct which policy interpreted the evidence even after later releases change defaults.

## Authority And Lanes

Decision authority is ordered as:

1. MCP or referenced normative specification;
2. published implementation guideline;
3. deterministic local heuristic;
4. model-assisted advisory output.

Baseline, client-compatibility, and model-advisory lanes remain separate. Model evaluation is opt-in, provider-explicit, receives a purpose-built minimal projection rather than the canonical result, writes a separately versioned companion artifact, reports its own token/currency cost, and cannot alter canonical claims, independent assessments, or certificate eligibility.

## Outcome Taxonomy

All test, run, coverage, and probe statuses map exhaustively to the canonical taxonomy:

`notEvaluated`, `running`, `succeeded`, `failed`, `skipped`, `authRequired`, `inconclusive`, `notApplicable`, `unavailable`, `blocked`, `cancelled`, and `error`.

Unsupported enum values throw instead of falling through to pass or failure defaults. Explicit `Unknown` dispositions map to inconclusive evidence and never inherit legacy boolean pass flags.

## Public Calibration Corpus

The committed secret-free corpora cover two different questions:

- `assurance-corpus-v2.json` covers production protocol, authentication, security, availability, throttling, and protected-surface observations through V2 claims and independent assessments.
- `rule-quality-v2.json` contains individual expected/observed labels for measured heuristic quality.

`CalibrationTargetIntegrationTests` runs six target classes against deterministic local WireMock HTTP targets through production protocol, authentication, and security components. Live endpoints are unsuitable as goldens because deployment and policy change independently of validator code.

| Case | Purpose | Expected effect |
| --- | --- | --- |
| compliant | Complete high-confidence evidence with no blockers | Satisfied claims in evaluated dimensions |
| vulnerable | Confirmed critical security finding | Security `issuesFound` |
| malformed | High-severity protocol violation | Core `nonconformant` |
| unavailable | Transport evidence unavailable | Incomplete, not pass/fail |
| throttled | Transient/inconclusive evidence | Incomplete |
| auth-protected | Surface requires credentials | Incomplete until exercised |

The targets are deterministic local fixtures. Live endpoints are unsuitable as golden fixtures because availability, deployment, and policy change independently of validator code.

## Test Strategy

- Golden tests assert exact typed claims and independent assessment outcomes for V2 fixtures.
- Production differential tests compare compliant/vulnerable/malformed/unavailable/throttled/auth-protected claims and evidence outcomes.
- Rule-quality tests assert confusion matrices, threshold boundaries, and fail-closed empty/undersized corpora.
- Focused authority-lane tests verify specification, guideline, heuristic, and model decisions remain separate.
- Synthetic metamorphic tests prove adding blocking evidence cannot improve an assurance assessment; they are not golden calibration evidence.
- Stryker.NET 4.16.0 mutates the production `AssuranceAssessmentEngine`; CI enforces a 90% break threshold. The current score is recorded by the local readiness receipt rather than hard-coded in this document.
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

Any change to a rule, gate, threshold, outcome mapping, or pack revision must:

1. increment the owning version;
2. update the decision-policy manifest;
3. update or add corpus cases;
4. pass golden, monotonicity, production mutation, and snapshot tests;
5. document intentional classification changes.
