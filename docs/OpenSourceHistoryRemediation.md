# Open-Source Git History Remediation

This repository previously committed live endpoint captures and generated reports. Deleting those files in a later commit does not remove them from branches, tags, forks, caches, or existing release source archives.

The scanner binds the known legacy history to an exact reviewed baseline of path and Git-object digests. Publication fails if that inventory changes or current content matches a sensitive pattern. Rewriting remains the preferred long-term remediation because the baseline does not remove historical objects from forks, caches, or source archives.

## Affected Path Classes

- `PublicReports/**`
- `github_headers.txt`
- `learn_headers.txt`
- `learn_cookies.txt`
- root `live-report-*` captures

Do not copy the historical file contents into issues, logs, pull requests, or remediation notes.

## Owner Procedure

History rewriting is destructive and must be performed by the repository owner from a fresh mirror clone after announcing a maintenance window.

1. Disable releases and mutable channel promotion.
2. Revoke or invalidate exposed sessions, cookies, and credentials where the provider supports revocation. Treat infrastructure/session identifiers as compromised even when they may have expired.
3. Create an offline backup of the original repository for incident evidence. Do not publish that backup.
4. In a fresh mirror clone, use a reviewed `git-filter-repo` invocation to remove every affected path from all branches and tags:

   ```bash
   git filter-repo --invert-paths \
     --path-glob 'PublicReports/**' \
     --path github_headers.txt \
     --path learn_headers.txt \
     --path learn_cookies.txt \
     --path-glob 'live-report-*'
   ```

5. Run object/path verification before any push:

   ```bash
   git rev-list --objects --all | grep -E '(^| )(PublicReports/|github_headers\.txt$|learn_headers\.txt$|learn_cookies\.txt$|live-report-)'
   scripts/check-sensitive-artifacts.sh
   ```

   Both commands must report no affected path.

6. Force-push rewritten branches and tags with explicit owner review. Coordinate with collaborators/forks because every existing clone must be replaced or carefully rebased.
7. Delete and recreate release source archives/tags as needed so public downloads reference only sanitized objects. Immutable package registry versions cannot be rewritten; publish a new patch if package contents were affected.
8. Re-run the exact Release test, audit, package, provenance, signing, and distribution gates from a clean clone.
9. Publish a security notice describing affected history ranges and remediation without reproducing sensitive values.

## Clean-Repository Alternative

If rewriting every public ref is not acceptable, create a new empty public repository from a sanitized source snapshot. Do not preserve the old Git object graph. Archive or make the old repository private only after considering forks and cached release archives.

## Ongoing Prevention

- Live captures and reports stay under the operating-system temporary directory or another ignored local directory.
- Only reviewed, secret-free summaries belong under `docs/Resources/`.
- Docker build contexts exclude local/private files.
- CI scans worktree, staged content, and reachable history paths before publication.
