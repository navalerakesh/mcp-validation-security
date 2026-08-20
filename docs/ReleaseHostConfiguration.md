# Release Host Configuration

These GitHub settings are mandatory for production publication. Workflow YAML cannot enforce repository-administration settings by itself.

Owner: `@navalerakesh`

Repository: `navalerakesh/mcp-validation-security`

## Verified State (2026-08-20)

An owner-authorized configuration update followed by a read-only GitHub API audit confirmed:

- Actions are enabled for SHA-pinned actions, and the workflows use full commit references;
- `main` enforces rules for administrators, blocks force pushes and deletion, and requires `Build & Test`, both in-workflow CodeQL matrix checks, and `Dependency Review`;
- an active no-bypass ruleset blocks deletion and non-fast-forward updates for full-version release tags;
- `NuGet`, `Npm`, and `Ghcr` require owner approval and protected-branch deployment;
- dependency review, Dependabot security updates, private vulnerability reporting, secret scanning, and push protection are enabled;
- the repository has zero open secret-scanning alerts; non-provider-pattern scanning and validity checks remain unavailable or disabled;
- release `1.1.25` is absent from NuGet, npm, GHCR, GitHub Releases, and Git tags;
- the latest visible CodeQL analysis is dated 2026-05-25 and must be replaced by successful results for the release-candidate pull request.

The current index contains no forbidden path or sensitive-content match. The 156-path legacy history is locked to a reviewed 181-object digest baseline; any inventory drift fails CI. Full history removal remains tracked in `docs/OpenSourceHistoryRemediation.md`.

## Main Branch

Protect `main` with a branch ruleset that:

- requires pull requests or an explicit reviewed owner merge process;
- requires the `Build & Test`, both in-workflow `CodeQL` matrix checks, and dependency-review check for pull requests;
- requires branches to be current before merge;
- blocks force pushes and deletion;
- applies to administrators, including the sole owner;
- prevents bypass except a documented emergency recovery procedure.

Self-review is owner review, not independent review. The public record must retain the exact diff and successful checks.

## Release Tags

Protect immutable full-version tags matching `v[0-9]+.[0-9]+.[0-9]+*`:

- disallow update and deletion after creation;
- allow creation only through the protected release workflow or explicit owner recovery;
- protect the tag rule from administrator bypass where GitHub supports it.

Mutable compatibility aliases (`v1`, `v1.1`) are intentionally separate. Their workflow promotion is monotonic and must never be treated as immutable provenance.

## Environments

Create and protect these environments before enabling publication:

- `NuGet`
- `Npm`
- `Ghcr`

Each environment must:

- restrict deployment branches/tags to the release workflow from `main`;
- store only the minimum channel-specific credentials;
- require owner approval where supported;
- prevent untrusted pull-request code from receiving credentials;
- retain deployment history.

The `Ghcr` environment must exist explicitly. Referencing a missing environment is not evidence of protection.

## Repository Security Features

Enable and verify:

- dependency graph and Dependabot alerts;
- dependency review on pull requests;
- code scanning with CodeQL for C# and JavaScript/TypeScript;
- secret scanning and push protection;
- private vulnerability reporting;
- Actions restricted to reviewed SHA-pinned actions.

## Pre-Publication Evidence

Before the owner freezes a release commit:

1. Confirm the sensitive-artifact gate passes from a fresh clone and the reviewed legacy-history baseline has not drifted.
2. Capture a secret-free export or screenshot of branch, tag, and environment rules under an ignored/private evidence directory.
3. Run the full Release test, npm, typecheck, audit, mutation, package, container, and distribution gates.
4. Confirm the full-version tag does not exist, or points to the exact retry commit with byte-identical release subjects.
5. Confirm NuGet/npm/GHCR mutable aliases pass monotonic version checks.
6. Publish only from the protected workflow; do not publish from a workstation.

If any setting cannot be verified, publication is blocked.
