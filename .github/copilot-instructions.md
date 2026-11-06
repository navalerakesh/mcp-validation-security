# MCP Validator Copilot Instructions

These instructions govern local Copilot agent behavior. They do not replace branch protection or required GitHub checks.

## Governing Authority

- Treat [DESIGN-PRINCIPLES.md](../DESIGN-PRINCIPLES.md) as the product and engineering authority.
- Use [docs/Design/Architecture.md](../docs/Design/Architecture.md), [docs/Design/TechnicalArchitecture.md](../docs/Design/TechnicalArchitecture.md), and [docs/Design/ComponentDesign.md](../docs/Design/ComponentDesign.md) for ownership boundaries.
- For MCP extension or official-conformance work, also follow [docs/Design/ExtensionIntegrationProposal.md](../docs/Design/ExtensionIntegrationProposal.md).
- Do not infer protocol behavior from memory. Verify current official MCP specifications, extension lifecycle pages, schemas, conformance scenarios, and changelogs. Record the dated revision or immutable source digest that governs the change.

## Evidence And Product Truth

- Evidence precedes findings, scoring, verdicts, compatibility interpretation, and presentation.
- Missing, skipped, unavailable, blocked, authentication-required, inconclusive, and not-applicable evidence are distinct and never earn pass credit.
- Keep MCP core conformance, official external conformance, extension readiness, client compatibility, heuristic advice, and model-assisted advice in separate typed lanes.
- Never blend optional extension results into the core compliance score or trust level unless the extension has been promoted into the selected dated core revision.
- Preserve source authority, requirement level, protocol/extension revision, rule or check ID, coverage, confidence, remediation, and redacted evidence.

## Ownership And Dependency Direction

- `Mcp.Benchmark.Core`: host-neutral contracts, configuration, canonical results, policy inputs, and abstractions. No Infrastructure or CLI dependencies.
- `Mcp.Compliance.Spec`: immutable embedded schemas, protocol versions, and source metadata. No host logic.
- `Mcp.Benchmark.Infrastructure`: transports, authentication, validators, scenario/rule packs, evidence collection, scoring implementations, and renderers.
- `Mcp.Benchmark.ClientProfiles`: dated interpretation of completed neutral evidence only. No target probes and no mutation of findings.
- `Mcp.Benchmark.CLI`: composition root, command binding, execution plans, artifact orchestration, terminal UX, and exit policy.
- `mcpval-mcp`, GitHub Action, container, and standalone hosts are adapters. They must not fork validation, scoring, or reporting semantics.
- `Mcp.Benchmark.Fleet` remains host-neutral fleet/governance contracts. Product layers do not depend on it.

## Single Source Of Truth

- Search for an owning constant, vocabulary, comparator, schema registry, rule catalog, pack descriptor, policy, renderer projection, fixture, or script before adding one.
- Do not duplicate protocol methods, version tables, rule IDs, severity mappings, lifecycle stages, scoring weights, report decisions, redaction rules, or release commands.
- Structured canonical evidence is the source for console, JSON, Markdown, HTML, XML, SARIF, JUnit, profile summaries, audit manifests, and attestations. Renderers never contact targets or recompute validation.
- Extend existing pack and applicability abstractions before creating parallel orchestration. Do not add dynamic in-process plugins without an approved integrity and isolation contract.
- When official conformance already owns wire behavior, import pinned evidence and add only non-duplicative security, safety, operational, policy, and reporting value.

### Canonical Definition Locations

- Protocol versions and embedded schemas: `Mcp.Compliance.Spec/` and `Mcp.Compliance.Spec/schema/`.
- Protocol methods/capability names: `Mcp.Benchmark.Core/Constants/McpSpecConstants.cs` and version-aware feature owners in `Mcp.Benchmark.Infrastructure/Registries/`.
- Validation methods and stable check/rule IDs: `Mcp.Benchmark.Core/Constants/ValidationConstants.cs` and `Mcp.Benchmark.Core/Models/ValidationFinding.cs`.
- Scoring weights, thresholds, and semantic marker sets: `Mcp.Benchmark.Core/Constants/ScoringConstants.cs` plus the versioned decision-policy manifest.
- Protocol feature lifecycle/applicability: `Mcp.Benchmark.Infrastructure/Registries/BuiltInProtocolFeaturePack.cs` and pack applicability models.
- Canonical artifact contracts and schemas: `Mcp.Benchmark.Core/Models/ValidationResults.cs`, `Mcp.Benchmark.Core/Models/ValidationArchitectureModels.cs`, and `docs/Schemas/`.
- Secret stripping/redaction: canonical clone/redaction services in Core/Infrastructure; search existing redaction owners before adding patterns.
- Release version: `Directory.Build.props`; distribution invariants: `scripts/verify-distribution-contracts.sh`.
- Build/test/audit entry point: `scripts/validate-repo.sh`; specialized gates remain in checked-in scripts and `.github/workflows/`.

## Reliability And Security Boundaries

- All target, config, saved-result, schema, HTML, URI, header, subprocess, and external-conformance data is untrusted and bounded before parsing or persistence.
- Keep mutable state run-scoped. Cancellation, concurrency, retries, and partial outcomes must preserve deterministic evidence semantics.
- Safe mode remains discovery/static analysis. Standard and elevated behavior require explicit policy admission; unknown tools, HTML, skills, tasks, or scripts are never executed automatically.
- Enforce outbound host/origin/address policy for every request, redirect, retry, resource read, and auth discovery path.
- Secrets remain transient and are never written to commands, logs, findings, reports, fixtures, snapshots, or exceptions.

## Change Workflow

1. Start from the controlling code path and one falsifiable hypothesis.
2. Identify the owning project, public contracts, protocol/extension applicability, and cheapest discriminating test.
3. Make the smallest coherent edit. After the first substantive edit, run the focused executable check before widening scope.
4. Add positive, negative, malformed, unavailable, and boundary tests proportional to risk. Tests prove specified behavior, not implementation accidents.
5. Update schemas, snapshots, documentation, pack/rule revisions, calibration fixtures, and changelog when their public contracts change.
6. Run [repo-precommit-review](./skills/repo-precommit-review/SKILL.md) before every agent-created commit.
7. Run [repo-build-release-gate](./skills/repo-build-release-gate/SKILL.md) after review and before commit. Do not commit with a failing or unobserved required gate.

## Commit And Review Rules

- Never commit unrelated changes, generated secrets, live target artifacts, or ignored failures.
- Findings lead code reviews, ordered by severity with exact file references and a reproducer or discriminating test.
- A local gate lowers risk but cannot guarantee remote infrastructure or registry availability. Required GitHub checks remain authoritative.
- Do not push, merge, publish, tag, or create a release unless the user explicitly requests that remote action.
- An agent cannot waive a required gate. A maintainer may explicitly direct an emergency commit, but the readiness receipt must remain `NOT READY`, name every skipped/failed gate, and record the maintainer direction without claiming release readiness.
