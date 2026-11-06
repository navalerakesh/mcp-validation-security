---
name: repo-pr-merge-release-readiness
description: "Prepare MCP Validator changes for commit, pull request, merge, and release with high confidence. Use when: finalizing a branch, updating README/SECURITY/docs after product changes, checking mcpval-mcp STDIO drift, proving PR or merge readiness, preventing CI failures, or publishing synchronized NuGet CLI and npm MCP packages. Orchestrates repo-precommit-review and repo-build-release-gate; verifies protected checks and release publication without bypassing them."
argument-hint: "Optional target phase: audit, commit, pr, merge, release, or all"
user-invocable: true
disable-model-invocation: false
---

# PR, Merge, And Release Readiness

Use this workflow to move a reviewed MCP Validator change from local source through documentation, package parity, protected PR checks, merge, and observable release completion.

This skill maximizes evidence-backed confidence; it never promises that remote infrastructure, registries, protected environments, or signing services cannot fail.

## Authority

- Treat `DESIGN-PRINCIPLES.md` and `.github/copilot-instructions.md` as governing product rules.
- Reuse [`repo-precommit-review`](../repo-precommit-review/SKILL.md) for semantic review.
- Reuse [`repo-build-release-gate`](../repo-build-release-gate/SKILL.md) for executable local gates.
- Treat `Directory.Build.props` as the release-version source of truth.
- Treat `.github/workflows/ci.yml` and checked-in scripts as build, package, publish, and release truth.
- Never create parallel scoring, release, documentation, or package contracts in this skill.

## Admission Rules

1. Do not commit with unresolved review blockers or failed/unobserved required local gates.
2. Do not push, open a PR, merge, or trigger a release unless the user explicitly requested that remote phase.
3. Do not bypass branch protection, required reviews, required checks, environments, immutable tags, or package verification.
4. Do not claim `READY` when a required gate is skipped, unavailable, stale, or run against a different source tree.
5. Do not print or persist tokens, authorization headers, signing keys, package credentials, or environment secrets.
6. Preserve unrelated user changes and generated live artifacts outside the commit.
7. A historical document may retain old contracts only when clearly labeled historical and linked to current guidance.

## Phase 1: Establish The Change Boundary

1. Record branch, HEAD, base branch, worktree status, staged/unstaged/untracked paths, and remote repository.
2. Classify every changed path:
   - Core contracts, evidence, claims, assessment, policy, scoring, or schemas;
   - protocol/version/extension behavior;
   - HTTP/auth/security or STDIO/process execution;
   - reports/renderers/accessibility;
   - CLI/config/action inputs and exits;
   - `mcpval-mcp` STDIO wrapper;
   - packaging/workflows/version/release;
   - documentation only.
3. Identify unrelated changes and exclude them from the proposed payload.
4. Name one falsifiable behavioral hypothesis and the cheapest discriminating check for each changed product surface.

## Phase 2: Documentation And Security Drift

Review current user-facing documentation whenever behavior, terminology, options, output, security boundaries, versions, or packages change.

### Required Drift Search

Search current docs for retired or contradictory terms, including:

- V1 L1-L5 trust as active guidance;
- global score or verdict as a V2 gate;
- stale supported versions, package names, test counts, exits, options, profiles, or protocol revisions;
- safe mode described as active security certification;
- inventory described as failed provider certification;
- HTML/report order or artifact claims that no longer match renderers;
- STDIO described as sandboxed;
- wrapper claims that generated HTML is returned through MCP;
- old client compatibility outcomes or live inventory counts presented as current.

### Documentation Impact Matrix

Review these owners when applicable:

| Change | Documentation owners |
| --- | --- |
| Product overview/options | `README.md`, `QUICKSTART.md`, `Mcp.Benchmark.CLI/README.md` |
| Security or execution boundary | `SECURITY.md`, `docs/AuthenticationCoverage.md`, `docs/Troubleshooting.md` |
| Reports/artifacts/schemas | `docs/Reporting.md`, `docs/FeatureMatrix.md`, `docs/SchemaCompatibility.md`, `docs/Schemas/` |
| Protocol/extensions | `docs/ProtocolCoverageMatrix.md`, `docs/OfficialConformanceAndExtensions.md` |
| Architecture/contracts | `docs/Design/`, `DESIGN-PRINCIPLES.md` only when the governing design changed |
| Local MCP wrapper | `mcpval-mcp/README.md`, root `README.md`, `docs/FeatureMatrix.md` |
| Release-visible behavior | `CHANGELOG.md` |
| Confidence receipt | `docs/OrganizationGradeConfidence.md` only with observed, reproducible evidence |

Do not rewrite historical evidence. Add a visible historical-contract banner when old semantics could be mistaken for current guidance.

Run the exact Markdown and link commands from `.github/workflows/docs-lint.yml` after the final documentation edit.

## Phase 3: CLI And `mcpval-mcp` Parity

Run this phase when CLI arguments, assurance profiles, exits, canonical results, report behavior, security policy, or wrapper source changes.

1. Compare current `mcpval validate`, `health-check`, `discover`, and `report` help with wrapper input schemas and handlers.
2. Verify wrapper behavior remains an adapter:
   - canonical V2 JSON is returned in `structuredContent`;
   - target-derived content is explicitly marked untrusted;
   - no scoring, claim reduction, compatibility decision, or report recomputation is forked;
   - durable HTML/Markdown is generated through the CLI, not inserted into model context;
   - temporary wrapper artifacts are deleted;
   - local STDIO execution remains disabled unless the operator enables it;
   - provider paths remain operator-owned environment configuration, never agent arguments;
   - provider assurance selects the admitted execution mode and fails closed before contact;
   - CLI exits `0`, `1`, `2`, `64`, `70`, `75`, and `124` remain numeric and meaningful.
3. Verify MCP metadata:
   - tool titles/descriptions match V2 terminology;
   - annotations reflect actual side effects and open-world behavior;
   - input and output schemas match emitted structured content;
   - initialize instructions describe safe-mode limitations truthfully.
4. Run wrapper package gates:
   - clean `npm ci`;
   - tests, typecheck, build, and high audit;
   - `npm pack --dry-run`;
   - actual packed-install smoke through `scripts/smoke-npm-package.mjs`.
5. Run SDK-over-STDIO integration with the built wrapper and actual local CLI:
   - initialize and list surfaces;
   - verify schemas/annotations;
   - perform an owned safe fixture `health_check`;
   - perform an owned safe fixture `validate` when validation behavior changed;
   - assert canonical V2 document type/schema, profile, execution mode, claims, exits, trust metadata, redaction, and zero unexpected violations.
6. Never contact an external target for deterministic CI evidence.

## Phase 4: Version And Release Contract

1. Read the latest published immutable GitHub release/tag and compare it with `Directory.Build.props`.
2. If the exact version is already published, stop and require an explicit semantic-version bump before release.
3. Confirm the selected version is appropriate for breaking, feature, or patch scope.
4. Verify all distribution consumers derive the same version:
   - NuGet `McpVal`;
   - npm `mcpval-localmcp` via workflow version synchronization;
   - standalone binaries, container labels, action tags, manifest, and GitHub release.
5. Review `CHANGELOG.md` for the target version or Unreleased content.
6. Run distribution-contract and release-retry scripts; reject mutable or duplicate release identity.

## Phase 5: Mandatory Local Review And Build Gate

1. Invoke `repo-precommit-review` on the final source and documentation diff.
2. Resolve every blocker and rerun focused checks.
3. Invoke `repo-build-release-gate` on the final source tree.
4. Run every conditional row applicable to all changed paths, including mutation when reducers/scoring/calibration changed and report/browser checks when HTML changed.
5. Run `./scripts/validate-repo.sh` after the final substantive edit.
6. Re-run fast mandatory gates on the final staged tree.
7. Verify generated artifacts, snapshots, package locks, schemas, docs, and changelog are intentional.
8. The local receipt must be `READY TO COMMIT`; otherwise stop.

## Phase 6: Commit And PR

1. Stage only reviewed files and run `git diff --cached --check`, staged names/stat, and sensitive-artifact checks.
2. Commit with a release-relevant message; do not amend unrelated published work.
3. Confirm the worktree is clean.
4. Before creating a PR:
   - authenticate the GitHub identity;
   - read `.github/pull_request_template.md`;
   - search open PRs for the branch/title to avoid duplicates;
   - push only the reviewed branch when explicitly requested.
5. Build the PR description from the template and include:
   - behavioral summary and breaking changes;
   - security and evidence semantics;
   - documentation/report/wrapper impact;
   - exact local gate counts and package audits;
   - live/safe validation scope and explicit limits;
   - release version and expected package channels.
6. Create the PR against the repository default branch. Request only valid reviewers required by policy.

## Phase 7: Protected Check And Merge Admission

1. Monitor every required PR check, including build/test, CodeQL, dependency review, workflow/docs lint, fuzz/security checks, package matrices, and review state.
2. A queued or in-progress check is not passing. Wait for terminal conclusions.
3. Investigate failures from logs; fix locally, rerun focused and required gates, commit, push, and wait again.
4. Do not dismiss review findings or merge with unresolved conversations.
5. Merge only when:
   - all required checks are successful;
   - required review/conversation policies are satisfied;
   - the branch is up to date if protection requires it;
   - mergeability is clean;
   - the user explicitly requested merge.
6. Record the merge commit SHA and verify the default branch contains it.

## Phase 8: Release Kickoff And Publication Verification

A merge is not a release success.

1. Verify the default-branch push started `.github/workflows/ci.yml` (`Build & Release`) for the merge commit.
2. Confirm its computed version equals `Directory.Build.props` and is greater than the latest immutable release.
3. Monitor all required jobs, especially:
   - build/test/security analysis;
   - package, standalone, container, and install smoke;
   - NuGet and npm package preparation;
   - protected NuGet/npm deployment jobs;
   - release reservation, signing, attestation, checksums, SBOM, manifest, and publish.
4. If an environment approval is required, report the exact waiting job. Do not bypass it.
5. After completion, verify independently:
   - immutable Git tag points to the merged source revision;
   - GitHub release exists and contains expected assets, checksums, SBOM, and distribution manifest;
   - NuGet exposes `McpVal` at the exact version;
   - npm exposes `mcpval-localmcp` at the exact version;
   - container/action/standalone outputs match the release contract when applicable;
   - package install/version and MCP initialize smokes pass from public channels.
6. If registry indexing is delayed, report `PUBLISHING/INDEXING`, not success.
7. Release readiness becomes `RELEASED` only after every required channel is independently observed.

## Readiness Receipt

Report each phase with one of:

- `NOT READY`: blocker, failed gate, unresolved drift, missing approval, or unobserved requirement;
- `READY TO COMMIT`: local review and build gate passed;
- `PR READY`: branch pushed and PR created with local receipt;
- `MERGE READY`: all protected checks/reviews passed;
- `MERGED / RELEASE RUNNING`: merge observed and release workflow started;
- `RELEASED`: immutable tag, GitHub release, NuGet, npm, and required distribution channels verified.

Include exact commands, exits, test/audit/package counts, commit/PR/run/tag identifiers, skipped or remote-only gates, and residual risk. Never replace evidence with a confidence adjective.
