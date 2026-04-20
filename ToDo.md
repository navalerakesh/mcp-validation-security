# MCP Validator Roadmap

## Goal

Make this repository credible as both:

- the validator for MCP server compliance, security, and AI safety
- a high-trust open source project with strong release, CI, and supply-chain hygiene

## Execution Order

| Step | Focus | Why first | Status |
| --- | --- | --- | --- |
| 1 | Repository trust fixes | Broken docs and policy drift reduce trust immediately | 🔵 |
| 2 | Boundary enforcement | Prevent host and engine coupling from growing while new work lands | 🔵 |
| 3 | Canonical rule model design | This is the base for scale, CI gates, and future UX surfaces | 🟡 |
| 4 | MCP guideline rule pack | Highest product-value expansion after boundaries are stable | 🟡 |
| 5 | CI-native outputs and policy modes | Turns the tool into a serious adoption-ready gate | ⬜ |

## Keep, Change, Remove

| Bucket | Items |
| --- | --- |
| Keep | Core contracts and models in Core, implementations in Infrastructure, CLI as a thin host, session bootstrap separation |
| Change | findings model, rule metadata, CI output contracts, documentation consistency, host-layer boundary enforcement |
| Remove over time | string-parsing-based scoring inputs, stale docs references, product naming drift, heuristic-only safety semantics |

## Status Legend

| Status | Meaning |
| --- | --- |
| ⬜ | Not started |
| 🟡 | Planned / needs design |
| 🔵 | In progress |
| 🟢 | Done |
| 🔴 | Blocked |
|

## Current Audit Snapshot

| Area | Snapshot | Status |
| --- | --- | --- |
| Core validation | Protocol, security, prompt, resource, tool, and performance validators exist | 🟢 |
| MCP guideline coverage | Tool annotations are partially parsed and partially reported, but guideline coverage is not yet a first-class rule pack | 🟡 |
| AI readiness | Parameter descriptions, vague schemas, token size, and LLM-friendliness are checked | 🟢 |
| CI gate readiness | Reports exist, but PR gate outputs are not yet optimized for SARIF, GitHub annotations, or policy-based blocking | 🟡 |
| Repo hardening | Dependabot, CODEOWNERS, templates, and release automation exist | 🟢 |
| Supply chain security | No visible CodeQL, dependency review, SBOM, provenance, or OSSF scorecard automation | ⬜ |
| Reproducible dev environment | No visible global.json, .editorconfig, or NuGet.config at repo root | ⬜ |
| Docs consistency | README references docs/Features/00_ToDo.md, but that path is currently missing | 🔴 |
| Security policy consistency | SECURITY.md still references older product/version wording and supported version ranges | 🔴 |

## Workstream A: Repository Hardening

| ID | Task | Why it matters | Priority | Status |
| --- | --- | --- | --- | --- |
| RH-01 | Add global.json pinned to .NET 8 | Makes local builds and CI reproducible and avoids SDK drift | P0 | ⬜ |
| RH-02 | Add root .editorconfig | Enforces consistent style and reduces review noise | P1 | 🟢 |
| RH-03 | Add CodeQL workflow | Baseline static security analysis for an open source security tool | P0 | ⬜ |
| RH-04 | Add GitHub dependency-review workflow for PRs | Blocks risky package changes before merge | P0 | ⬜ |
| RH-05 | Add npm audit and dotnet package audit as explicit CI steps with policy thresholds | Current audit coverage is too shallow for a security-focused repo | P0 | ⬜ |
| RH-06 | Generate SBOM artifacts for release builds | Required for serious supply-chain posture and enterprise adoption | P0 | ⬜ |
| RH-07 | Add build provenance and artifact attestations for release outputs | Improves trust for NuGet, npm, and standalone binaries | P0 | ⬜ |
| RH-08 | Add OSSF Scorecard workflow and badge | Gives external consumers objective repo trust signals | P1 | ⬜ |
| RH-09 | Add actionlint and workflow validation | The current workflow has expression diagnostics that should be eliminated | P0 | ⬜ |
| RH-10 | Fix SECURITY.md naming/version drift | Security policy must match the actual product and supported versions | P0 | 🟢 |
| RH-11 | Fix README roadmap link and align docs structure | Broken governance/docs links reduce project trust immediately | P0 | 🟢 |
| RH-12 | Add docs lint and link check workflow | Prevents future drift in README, SECURITY, and docs | P1 | ⬜ |

## Workstream B: MCP Rules And Guidelines Coverage

| ID | Task | Why it matters | Priority | Status |
| --- | --- | --- | --- | --- |
| MG-01 | Create a first-class MCP Guidelines rule pack separate from strict spec compliance | Needed to support the "Claude-style" expectations without mixing them into MUST failures | P0 | 🔵 |
| MG-02 | Validate tool annotations.title | Human-readable titles improve agent selection and UX consistency | P1 | 🟢 |
| MG-03 | Validate annotations.readOnlyHint and destructiveHint with direct evidence, not just name heuristics | This is one of the most important practical AI-safety signals | P0 | 🔵 |
| MG-04 | Validate annotations.openWorldHint and idempotentHint | Useful for agent planning, retry safety, and sandbox expectations | P1 | 🟢 |
| MG-05 | Add guideline checks for missing parameter descriptions, vague string schemas, required arrays, enum coverage, and format usage | Reduces hallucinated tool calls and improves cross-client reliability | P0 | 🟡 |
| MG-06 | Add checks for destructive tools requiring explicit confirmation / human-in-the-loop signaling | Needed for secure agent usage and enterprise review | P0 | ⬜ |
| MG-07 | Add checks for pagination support, stable cursor behavior, and large tools/list ergonomics | Important for real-world PR gate and agent scalability | P1 | ⬜ |
| MG-08 | Add checks for prompt metadata quality, message structure, and prompt safety guidance | Prompt surfaces are part of MCP quality, not just tools | P1 | ⬜ |
| MG-09 | Add checks for resource MIME quality, URI scheme expectations, and resource template ergonomics | Makes resource handling safer and easier for clients | P1 | ⬜ |
| MG-10 | Add optional checks for completions, roots, logging, sampling, and capability-specific expectations | Needed for a full-featured MCP gate beyond minimal tool support | P1 | ⬜ |
| MG-11 | Add "spec", "guideline", and "heuristic" labels to every rule and report item | Prevents false authority and keeps the validator fair | P0 | ⬜ |
| MG-12 | Add stable rule IDs and remediation text for every finding | Essential for suppression, baselining, dashboards, and CI automation | P0 | 🔵 |

## Workstream C: CI Gate Productization

| ID | Task | Why it matters | Priority | Status |
| --- | --- | --- | --- | --- |
| CI-01 | Add exit-code policy modes: strict, balanced, advisory | Teams need predictable gating behavior in CI | P0 | ⬜ |
| CI-02 | Add SARIF output | Lets GitHub code scanning surface validator findings natively | P0 | ⬜ |
| CI-03 | Add JUnit or xUnit-style output | Makes validator results easy to consume in existing CI systems | P1 | ⬜ |
| CI-04 | Add GitHub Check summary and inline annotations | Makes PR automation actually usable instead of artifact-only | P0 | ⬜ |
| CI-05 | Add baseline and suppression support with explicit expiry/owner metadata | Needed so teams can adopt the gate without permanent noise | P0 | ⬜ |
| CI-06 | Add reusable GitHub Action wrapper around mcpval | Critical for broad adoption as a standard PR gate | P0 | ⬜ |
| CI-07 | Publish a hardened Docker image for CI usage | Simplifies adoption for repos that do not want .NET bootstrapping | P1 | ⬜ |
| CI-08 | Add policy examples for public, authenticated, and enterprise MCP servers | Makes the tool immediately usable by other teams | P1 | ⬜ |
| CI-09 | Add regression fixtures for known-good and known-bad MCP servers | Prevents rule regressions and scoring drift | P0 | ⬜ |
| CI-10 | Add snapshot testing for Markdown, HTML, JSON, SARIF, and JUnit outputs | Reporting quality is part of the product contract | P1 | ⬜ |

## Workstream D: Architecture For Scale

| ID | Task | Why it matters | Priority | Status |
| --- | --- | --- | --- | --- |
| AR-01 | Define a canonical Rule model: ruleId, tier, category, severity, profile, appliesWhen, evidence, remediation | Single source of truth for all validators and reports | P0 | ⬜ |
| AR-02 | Move all scoring inputs to evidence-driven findings instead of validator-specific string parsing | Current report/scoring coupling should be more structured | P0 | ⬜ |
| AR-03 | Make capability-aware rule execution a core engine concept | Rules should only fire when the server surface makes them applicable | P0 | ⬜ |
| AR-04 | Separate transport concerns from rule concerns even more aggressively | Needed for HTTP, SSE, STDIO, remote, and future transports | P1 | ⬜ |
| AR-05 | Add versioned rule packs per MCP spec version and per guidance profile | Prevents drift as the protocol evolves | P0 | ⬜ |
| AR-06 | Add deterministic evidence capture for every finding | Required for CI explainability, diffing, and audits | P0 | ⬜ |
| AR-07 | Replace name-only destructive/exfiltration heuristics with annotation plus schema plus behavior evidence | Avoids subjective or fragile findings | P0 | ⬜ |
| AR-08 | Add a machine-readable policy schema for custom org rule thresholds | Enables enterprise adoption without code forks | P1 | ⬜ |

## Workstream E: Open Source Trust And DX

| ID | Task | Why it matters | Priority | Status |
| --- | --- | --- | --- | --- |
| DX-01 | Add a contributor environment guide for .NET 8 and Node 20 | Reduces setup friction and failed first impressions | P1 | ⬜ |
| DX-02 | Add a local "validate this repo" script/task for contributors | The project should validate itself easily | P1 | ⬜ |
| DX-03 | Add sample MCP fixture servers for compliant, partially compliant, and unsafe behaviors | Makes tests and demos much stronger | P0 | ⬜ |
| DX-04 | Add architecture decision records for rule engine, scoring, and CI gate direction | Important as the project grows | P1 | 🟢 |
| DX-05 | Add changelog/release notes discipline tied to rule or policy changes | Avoids silent breaking changes for adopters | P1 | ⬜ |

## Design Principles We Must Enforce

| Principle | What it means here |
| --- | --- |
| Single source of truth | Rule metadata, severity, evidence, remediation, and scoring inputs must come from one canonical rule definition |
| Capability-aware validation | Do not fail servers for features they do not advertise or that are outside the selected profile |
| Evidence before judgment | Findings must be backed by captured request/response evidence, not assumptions |
| Spec vs guideline separation | MUST and SHOULD failures must stay distinct from best-practice and vendor-style guidance |
| No hardcoded vendor bias | The validator must remain neutral across Claude, Copilot, OpenAI, internal enterprise clients, and custom agents |
| Deterministic output | Same server plus same config should produce the same findings and same trust outcome |
| Policy-driven gating | CI behavior must be configurable without changing validator code |
| Safe AI boundaries | Destructive actions, exfiltration paths, and ambiguous write tools must be flagged explicitly |
| Release trustworthiness | This repo must meet the same supply-chain and compliance bar it expects from MCP servers |

## Recommended Delivery Order

| Order | Focus | Outcome | Status |
| --- | --- | --- | --- |
| 1 | RH-01, RH-03, RH-04, RH-05, RH-09, RH-10, RH-11 | Make the repository itself trustworthy first | 🟡 |
| 2 | MG-01, MG-03, MG-04, MG-11, MG-12, AR-01, AR-02 | Turn guideline checks into a real rule system | 🟡 |
| 3 | CI-01, CI-02, CI-04, CI-05, CI-06 | Make the validator usable as a PR gate | 🟡 |
| 4 | MG-05 through MG-10, AR-03 through AR-08 | Expand protocol and guideline depth safely | 🟡 |
| 5 | DX-01 through DX-05 | Improve contributor experience and open source maturity | ⬜ |

## Notes From This Audit

- The repository already has a solid foundation: validators exist, testing exists, reporting exists, and release automation exists.
- Tool annotation support is only partially mature today. The code parses annotation fields and public reports mention readOnlyHint/destructiveHint, but guideline coverage is not yet complete enough to market as a full MCP best-practice gate.
- The current trust model still relies on some name-based heuristics for destructive and exfiltration risk. That is useful as a temporary safeguard, but it should not remain the primary source of truth.
- The repo should validate itself to the same bar it expects from other MCP projects. That means supply-chain controls, deterministic environments, cleaner workflow validation, and documentation consistency.
- The forward architecture target is documented in docs/Design/ForwardArchitecturePlan.md and should be treated as the boundary reference for future changes.