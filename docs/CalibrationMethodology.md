# Calibration Methodology

MCP Validator V2 separates deterministic observations, typed claims, evidence debt, independent assessments, client compatibility, and model-assisted advice. It does not use a global score as an assurance or certification gate.

## Rule Quality Gate

Heuristic rules are review-only unless a versioned labeled corpus demonstrates all default-blocking thresholds:

- at least 100 explicitly held-out labeled samples;
- both expected-positive and expected-negative samples, with non-empty, unique sample IDs within each rule;
- at least three source groups and two scenario families, each containing classified positive and negative controls;
- no source group contributing more than 50% of holdout observations, and no holdout source group used for tuning any rule in the submitted corpus;
- at least 95% held-out prediction coverage; abstentions remain explicit and cannot substitute for negative predictions;
- precision of at least 95%;
- recall of at least 90%;
- false-positive rate of at most 5%;
- false-negative rate of at most 10%.

`RuleCalibrationEvaluator` retains whole-rule diagnostics in `Metrics` and uses only `HoldoutMetrics` for eligibility. Every submitted row must declare its partition and provenance, including rows for other rules; unknown partitions cannot establish source isolation. `Partition` is explicit (`Unspecified`, `Tuning`, `Holdout`); `SourceGroupId` and `ScenarioFamily` are required provenance, along with protocol and transport labels. Group/family identity is trimmed and compared ordinally without case sensitivity. Empty, undersized, unpartitioned, concentrated, leaked or class-confounded evidence fails closed. Admission is bounded to 100,000 total samples and 512 characters per identifier. Official specification failures and directly demonstrated deterministic security control failures remain authority-based evidence.

The initial `rule-quality-v2.json` corpus documents known behavior of `MCP.CONTENT.SystemImpact`, including bounded operations with destructive names and higher-impact operations missed by name matching. Each row now contains an explicit `toolName` input. The test-owned corpus observer runs the production analyzer in its default metadata-only context, checks stored golden observations for drift, and supplies actual observations to the calibration evaluator.

Connecting the corpus to executable behavior exposed four stale observation labels. With the original expected labels preserved, the twelve examples produce 4 true positives, 3 false positives, 3 true negatives and 2 false negatives. These illustrate why name-only signals cannot establish default-blocking safety decisions; they do not prove whether a real operation is safe. Transport/protocol labels provide context, not evidence that a transport was exercised. No tools are invoked by this corpus.

The examples remain explicitly unpartitioned and have no reviewed independent source groups. They do not meet the minimum sample count or holdout/provenance requirements, so the rule remains review-only. Do not duplicate these examples or relabel them as holdout data to cross the quality gate.

Calibration policy revision `2026-11-10.1` adds holdout, provenance, diversity and abstention admission to the class/identity protections introduced in `2026-11-09.1`. Each decision records its policy revision. Earlier `2026-11-05.1` could accept a positive-only corpus. Declared groups do not prove real-world independence or trustworthy labels: provenance still requires review. Synthetic tests qualify the gate implementation, not a heuristic rule's production accuracy; the original small corpus remains non-blocking.

Eligibility is a versioned point-estimate policy, not a statistical confidence interval, independent certification or automatic rule promotion. Human-reviewed corpus provenance and an approved rule revision remain necessary.

A null `ObservedPositive` is an abstention, not a false negative or true negative. Recall divides true positives by all expected positives, including abstentions. False-positive and false-negative rates use their classified class denominators; abstentions cannot dilute those observed rates. Both classes must have classified evidence. Undefined rates are null, with explicit counts and eligibility reasons, never invented zero accuracy. Source partition isolation applies across rule IDs even though quality metrics remain rule-local.

Eligibility compares unrounded ratios; serialized metrics remain rounded to four decimal places for compatibility. A displayed threshold value does not override an underlying just-outside result. Failure reasons retain the exact numerator/denominator and use culture-invariant formatting. Undefined precision/recall are described by missing-evidence reasons rather than numeric threshold claims. Equivalent input permutations select stable transport labels before case-insensitive deduplication.

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

The protected-target controls keep one server configuration throughout protocol and security assessment. The fixture configures the transport's run-scoped authentication just as session bootstrap does; explicit no-credential and invalid-credential security probes remain independent of that default. Four combinations of credential availability and invalid-token acceptance verify that protocol success cannot conceal a security failure, and missing credentials remain incomplete. Receiver-side request records verify whether protocol probes carried authorization. This is component integration evidence, not a complete CLI comprehensive-profile certificate or an OAuth authorization-server test.

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
- Rule-quality tests assert confusion matrices, threshold boundaries, and fail-closed empty/undersized/one-class corpora and repeated or missing sample IDs.
- Focused authority-lane tests verify specification, guideline, heuristic, and model decisions remain separate.
- Synthetic metamorphic tests prove adding blocking evidence cannot improve an assurance assessment; they are not golden calibration evidence.
- The production mutation scope and thresholds are owned by [the mutation gate](../scripts/validate-mutation.sh). Scores are source-bound qualification evidence, not an inference from corpus size or ordinary test counts.
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
- authenticated calibration targets now retain the same configuration across protocol and credential-bypass checks, but the harness does not qualify a complete OAuth deployment or full same-target comprehensive-profile execution;
- the harness owns only deterministic WireMock behavior and direct HTTP availability classification; category findings, vulnerabilities, verdicts, and scores are production-owned;
- corpus replay protects against stale predictions, but synthetic labels and declared provenance cannot replace independent real-world calibration or source-specific mutation qualification.

These limits produce explicit unevaluated dimensions or coverage debt rather than implicit passes.

## Change Policy

Any change to a rule, gate, threshold, outcome mapping, or pack revision must:

1. increment the owning version;
2. update the decision-policy manifest;
3. update or add corpus cases;
4. pass golden, monotonicity, production mutation, and snapshot tests;
5. document intentional classification changes.
