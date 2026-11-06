---
name: repo-build-release-gate
description: "Validate MCP Validator changes before commit for local parity with GitHub build, test, security, documentation, packaging, and release-contract checks. Use when: preparing a commit or PR, diagnosing CI failures, changing protocol/scoring/reporting/distribution code, or proving a branch is build- and release-ready."
argument-hint: "Optional changed path or gate level"
user-invocable: true
disable-model-invocation: false
---

# MCP Validator Build And Release Gate

Run only after the `repo-precommit-review` skill reports no blockers. This workflow minimizes avoidable CI/release failures; remote required checks remain authoritative.

## Procedure

1. Confirm prerequisites and branch state.
   - Record branch, base, staged/unstaged paths, configured .NET/Node versions, and whether the commit includes unrelated changes.
   - Read [the gate matrix](./references/gate-matrix.md) and classify the changed surface.
2. Run focused checks first.
   - Classify every changed path with [the gate matrix](./references/gate-matrix.md) and run all conditional checks for each affected surface.
   - Execute the narrow test/build that can falsify the change.
   - Repair local defects and rerun the same check before expanding.
3. Run mandatory repository gates.
   - These five gates are locally executable repository prerequisites. GitHub-only and environment-dependent gates are listed separately in the gate matrix and remain required before merge/release.
   - `git diff --check`
   - `scripts/check-sensitive-artifacts.sh`
   - `scripts/verify-distribution-contracts.sh`
   - `scripts/test-release-retry.sh`
   - `./scripts/validate-repo.sh`
4. Run all conditional gates from the matrix.
   - Use checked-in scripts and workflow commands as the source of truth.
   - Do not replace a failed gate with a narrower command or rerun until random success.
5. Verify the commit payload.
   - Review `git diff --cached --check`, staged names/stat, generated files, line-ending warnings, package locks, and ignored/untracked outputs.
   - Confirm every changed source has its intended test/doc/schema/changelog companion.
6. Produce a readiness receipt.
   - List every command, exit code, test count, audit result, package smoke, skipped/unavailable gate, and residual environment difference from GitHub.
   - `READY TO COMMIT` requires all applicable local gates observed and passing.
   - Any required unobserved or failing gate yields `NOT READY`.
   - Use [the readiness receipt template](./references/receipt-template.md).

## Rules

- Never claim 100% certainty: GitHub infrastructure, registries, signing, protected environments, and platform-specific runners can still fail. State the observed confidence boundary.
- Do not commit/push merely to discover a deterministic local failure.
- Do not modify tests, snapshots, locks, or release contracts only to silence a gate without proving the intended behavior.
- Do not run live external targets for deterministic CI. Live certificates use owned or explicitly authorized targets, frozen builds, bounded modes, and separate reporting.

## Exceptions And Escalation

- The agent cannot skip or waive a required local or remote gate.
- A maintainer may explicitly direct an emergency commit before all gates complete. Record the direction and every skipped, failed, or unobserved gate; the receipt remains `NOT READY` and must not claim build/release readiness.

## Readiness Receipt

Report:

- changed-surface classification;
- focused checks;
- mandatory gates;
- conditional gates;
- tests passed/failed/skipped;
- NuGet/npm audit results;
- package/distribution/documentation/workflow results;
- live validation scope when applicable;
- residual remote-only checks;
- `READY TO COMMIT` or `NOT READY`.
