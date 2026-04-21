# MCP Validator Forward Architecture Plan

## Intent

This document defines the target architecture for MCP Validator as a product, not just as a CLI.

The CLI remains the current host, but the validation engine must stay reusable for future hosts such as:

- a GitHub Action
- a hosted API or worker
- a web UI or dashboard
- an IDE extension
- an MCP-native wrapper

Backward compatibility is not the primary constraint for this planning phase. The goal is to reduce future architectural drag.

## Target State

| Layer | Owns | Must not own |
| --- | --- | --- |
| Core | Contracts, configuration, canonical findings, rule metadata, validation session models, aggregate result models | CLI concerns, transport implementation, host-specific policy wiring |
| Infrastructure | Transport clients, auth flows, validators, rule execution, evidence capture, scoring implementations, reporting implementations | CLI argument parsing, terminal UX, host-specific exit code behavior |
| Host layer | Composition root, arguments, environment-specific output, artifact paths, workflow-specific policy selection | Validation business logic, rule definitions, transport negotiation logic |

## What We Keep

| Decision | Why |
| --- | --- |
| Keep Core as the home for abstractions and shared models | This already enables reuse outside the CLI |
| Keep Infrastructure as the implementation layer | Validators, auth, and transport are correctly placed here |
| Keep CLI as a thin host | This is the right product shape for current distribution |
| Keep session bootstrap separate from validator execution | The ValidationSessionBuilder seam is valuable for future hosts |

## What Must Change

| Change | Reason |
| --- | --- |
| Introduce a canonical finding model with rule ID, tier, severity, evidence, remediation, and source classification | Scoring and reporting should not parse free-form issue strings |
| Make guideline checks a first-class rule pack distinct from spec compliance | Best-practice guidance must not be conflated with MCP MUST requirements |
| Push exit-code policy and gating modes into structured policy config | Future hosts should share behavior without duplicating decision logic |
| Reduce name-based destructive and exfiltration heuristics over time | These are useful stopgaps, but not durable product semantics |
| Make machine-readable outputs first-class | CI, dashboards, and future UX surfaces need stable contracts |

## What Should Be Removed Over Time

| Remove | Why |
| --- | --- |
| String-parsing based scoring inputs | Fragile, hard to test, and hard to reuse |
| Implicit host-specific behavior embedded in validators | Makes alternate UX surfaces expensive |
| Broken or stale documentation references | Undermines trust in an open source security tool |
| Product naming drift across docs and policy files | Creates ambiguity for contributors and adopters |

## Boundary Rules

1. Commands and host-facing handlers should depend on Core abstractions, not Infrastructure implementations.
2. Infrastructure may depend on Core, but never on CLI.
3. Program or host bootstrapping code may reference Infrastructure for composition only.
4. Findings must be produced as structured data before any Markdown, HTML, SARIF, or console formatting happens.
5. Report rendering must consume canonical results, not validator-specific strings.

## Priority Sequence

### Phase 1: Stabilize The Repository

- Fix documentation drift and policy drift.
- Add repo-level formatting and architecture guardrails.
- Keep the current CLI product stable while reducing future ambiguity.

### Phase 2: Lock In Boundaries

- Strengthen architecture tests around host versus implementation boundaries.
- Keep commands thin and orchestration reusable.
- Identify all places where string parsing is used as a data contract.

### Phase 3: Move To A Canonical Rules Engine

- Define rule metadata and structured findings in Core.
- Make Infrastructure emit findings instead of loosely formatted issues.
- Make scoring and reporting consume those findings.

### Phase 4: Productize For Multiple Hosts

- Add CI-native outputs and policy modes.
- Add reusable GitHub Action and machine-readable report contracts.
- Keep CLI, MCP wrapper, and future hosts on the same core engine.

## Today Scope

Today the safe, high-value work is:

1. clarify and document the target architecture
2. tighten boundary enforcement with architecture tests
3. clean repo inconsistencies that reduce trust
4. prepare the codebase for later rule-model refactoring without large churn

## Near-Term Implementation Decisions

| Decision | Action |
| --- | --- |
| Do not split projects aggressively yet | Preserve momentum and avoid refactor churn |
| Do enforce host versus engine boundaries now | Add tests and docs so drift stops immediately |
| Do not preserve shaky legacy semantics if they block a cleaner contract | Favor forward consistency |
| Do keep the CLI as the primary shipping surface for now | It is the strongest current distribution model |
