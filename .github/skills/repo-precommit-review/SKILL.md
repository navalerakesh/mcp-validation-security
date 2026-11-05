---
name: repo-precommit-review
description: "Review MCP Validator changes before any commit or pull request. Use when: preparing to commit, reviewing a diff, checking architecture ownership, preventing duplicate contracts, validating MCP protocol or extension fidelity, or deciding whether code is commit-ready."
argument-hint: "Optional changed path, commit, or diff range"
user-invocable: true
disable-model-invocation: false
---

# MCP Validator Pre-Commit Review

Use this workflow before every agent-created commit. Review is semantic and repository-aware; it is not replaced by a green build.

## Procedure

1. Establish the review boundary.
   - Record the branch, base commit, worktree status, staged/unstaged files, and requested diff range.
   - Preserve user changes and exclude unrelated files from the proposed commit.
2. Load governing context.
   - Read `DESIGN-PRINCIPLES.md`, `docs/Design/Architecture.md`, `docs/Design/TechnicalArchitecture.md`, and the nearest design owner for touched code. Paths are workspace-root relative and are indexed from `docs/README.md`.
   - For protocol, extensions, auth, schemas, or conformance, verify the current official source and dated revision. Do not rely on remembered semantics.
   - Read [the review checklist](./references/review-checklist.md).
3. Map ownership and contracts.
   - Identify the controlling abstraction and all consumers of changed public models, schemas, rule IDs, pack descriptors, policies, report projections, CLI options, and artifacts.
   - Search for existing constants, comparators, registries, scripts, fixtures, and helpers before accepting a new definition.
   - Reject reverse dependencies, host logic in Core/Spec, probes in client profiles, or renderer-owned decisions.
4. Review behavior and risk.
   - Check missing-evidence semantics, protocol/extension applicability, authority labels, safe/standard/elevated admission, redaction, SSRF, subprocess, cancellation, concurrency, bounds, and deterministic merge.
   - Check every output consumer: console, canonical JSON, Markdown, HTML, XML, SARIF, JUnit, profile summary, audit manifest, baseline comparison, and attestation.
   - Check compatibility and versioning requirements for schemas, rules, packs, policies, snapshots, and changelog.
5. Review tests as product evidence.
   - Require the cheapest focused test plus risk-scaled negative, malformed, unavailable, cancellation, and boundary cases.
   - Reject tests that encode known-wrong behavior, rely on wall-clock luck, fetch mutable upstream fixtures, or turn missing prerequisites into passes.
6. Produce findings first.
   - Order findings by severity.
   - Include exact file references, impact, reproducer/discriminating check, and required fix.
   - Separate blockers from residual risks and optional improvements.
   - Use [the review receipt template](./references/receipt-template.md) so evidence is consistent across commits.
7. Hand off to the executable gate.
   - Invoke the `repo-build-release-gate` skill only after review blockers are resolved.
   - Do not commit until both skills produce a passing readiness receipt.

## Review Receipt

Report:

- review boundary and governing sources;
- blocking findings and resolutions;
- single-source-of-truth/ownership verdict;
- public contract and output impact;
- test gaps and residual risk;
- `READY FOR BUILD GATE` or `NOT READY`.

## Exceptions And Escalation

- The agent cannot waive review findings or required evidence.
- If a maintainer explicitly directs an emergency commit, record that direction and every unresolved finding. The receipt remains `NOT READY`; do not represent the commit as build/release ready.
