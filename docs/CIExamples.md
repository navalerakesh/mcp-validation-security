# CI Policy Examples

These examples target the reviewed, checked-out Action revision (`uses: ./`), including unreleased contracts described below. Pin the checkout/Action to an immutable reviewed commit and the CLI to an exact compatible release in an approval path. A mutable major tag or an older published Action does not establish support for these contracts.

## Advisory

Collect evidence without blocking on validation findings:

```yaml
- uses: ./
  with:
    server: https://example.com/mcp
    policy: advisory
```

## Regression gate

Store an approved canonical result as a protected CI artifact, then block only regressions:

```yaml
- uses: ./
  with:
    server: https://example.com/mcp
    policy: balanced
    baseline: ./approved-result.json
    regression-only: 'true'
    output-format: json
```

Waivers belong in the checked-in validation configuration and require owner, reason, selector scope, and expiry. Raw findings remain unchanged.

## Strict admission

Require strict deterministic policy and preserve JSON, SARIF, JUnit, audit, Markdown, and HTML artifacts:

```yaml
- uses: ./
  with:
    server: https://example.com/mcp
    access: authenticated
    token: ${{ secrets.MCP_TOKEN }}
    policy: strict
    upload-artifacts: 'true'
```

Use environment protections for authenticated targets. Never place tokens in endpoint query strings, workflow summaries, or artifact names. The Action exposes typed inputs rather than free-form command arguments.

## Noninteractive comprehensive collection

The unreleased Action rejects `interactive: true` and malformed interactive values before launching the collector. Configuration alone cannot grant interactive authentication to CLI invocations: local interactive use requires explicit `--interactive`. CI credentials must already be available through an approved noninteractive source. The Action's default remains Safe inventory.

The current Action forwards the existing comprehensive evidence contract without implementing a second verifier. After checking out the reviewed Action revision and preparing target/run-bound, signed evidence in a protected job:

```yaml
- name: Collect comprehensive evidence
  id: validation
  uses: ./
  with:
    server: https://example.com/mcp
    assurance: comprehensive
    mode: standard
    interactive: 'false'
    validation-id: ${{ vars.MCPVAL_RESERVED_RUN_ID }}
    identity-baseline: ./approved-identity-baseline.json
    conformance-results: ./official-conformance.json
    output-format: json
```

This is not a self-contained evidence-generation recipe. The run ID must have been independently reserved, and both evidence inputs must match the admitted target, revision and run under the existing CLI checks. Active comprehensive execution rejects absent prerequisites and Safe mode before collector launch. Dry-run can plan without evidence but cannot establish assurance. Use `continue-on-error` only when a later explicit enforcement step consumes the real collector outcome; successful artifact publication never overrides it.

## Collector exit output

The same Action revision also forwards typed `max-requests`, `timeout`, `protocol-era`, `client-profiles`, `allowed-hosts`, `allowed-origins`, `allow-private-addresses`, `persistence-mode`, `redact-level` and `trace` inputs. List inputs, including `extensions`, share comma-separated parsing; empty entries, line breaks and option-shaped entries are rejected before launch. Extension identifiers retain their additional syntax check. The CLI remains the authority for supported values, limits and precedence. Whole-run timeout and aggregate catalog settings remain available through the validated configuration file; per-request timeout is not a whole-job deadline.

The unreleased Action revision declares `exit-code`, containing the actual `mcpval` collector process status when it was invoked and the output was published. The validation step exits with that same status after successful verification/publication. Missing artifacts after a non-planning exit `0`, corrupt/inconsistent artifacts, or output-publication failure make the step exit `70`; the separately published collector status is never rewritten. If the runner output file is unavailable or cannot be written, a sanitized diagnostic retains the collector's numeric status. Exposing outputs does not enable `continue-on-error` or turn a failed collection into success.

Read the value through `${{ steps.validation.outputs.exit-code }}` after assigning `id: validation` to the Action step. A downstream diagnostic step can use `if: always()` to attempt inspection even after validation failure. Do not treat an empty value as zero: Action preflight rejection, setup/install failure or runner interruption may prevent publication. Hosted runner output propagation and upload-on-failure remain separate qualification requirements from the executed local shell tests.

| Value | Collector interpretation |
| --- | --- |
| `0` | Collector completed under its selected profile/policy; not universal assurance or artifact-completeness proof |
| `1` | Recorded assurance or policy rejection |
| `2` | Incomplete required evidence or unrecorded execution termination |
| `64` | CLI usage/configuration error |
| `70` | Local runtime-integrity failure |
| `75` | Target unavailable |
| `124` | Timeout or cancellation |
| Absent | No collector status was published; do not infer a target result |

Dry-run exit `0` establishes planning only. Use the canonical result and digest-bound audit subjects to interpret completed validation evidence. Pin a revision containing this output before consuming it; older released Action revisions do not promise it.

## Typed artifact outputs without changing console output

The default human-readable console output is unchanged. `output-format: json` remains an explicit choice, independent of the typed outputs. The Action provisions its pinned Node runtime for local artifact inspection, not for target execution.

Each invocation creates a fresh `mcpval-action-*` directory under `output-dir`. It reads only that invocation's committed bundle; an older successful bundle cannot satisfy a new missing result. Consume the outputs rather than assuming a flat or fixed file layout. Artifact upload retains `always()` but uploads only `invocation-directory`, never unrelated/stale bundles from the shared parent, and a requested upload with no files is an error. If runner output propagation itself fails, no fallback to older output directories occurs. Empty or line-breaking output paths are rejected before collection.

| Output | Meaning |
| --- | --- |
| `invocation-directory` | Current collection directory, retained even if artifact inspection fails |
| `artifact-state` | `published`, `partial`, `plan-only`, `missing` or `corrupt`; artifact/execution state, not an assurance verdict |
| `artifact-directory` | Verified bundle directory, or the invocation directory when no verified bundle is available |
| `result-path`, `audit-path` | Matched canonical result and audit paths; empty when unavailable |
| `artifact-paths` | JSON array of digest-checked subjects and the matching audit manifest |
| `validation-id`, `target-identity-sha256` | Matching recorded run/target identities, not independent target authentication |
| `run-termination` | Canonical recorded execution termination |
| `policy-passed`, `assurance-profile` | Recorded policy decision and profile; no adapter recomputation |
| `core-conformance`, `security-assurance`, `operational-readiness` | Independent recorded assessments |

`published` means the admitted subjects match their recorded SHA-256 digests and required metadata is consistent. It does **not** mean all assessments passed, every optional capability was assessed, the full JSON schema was independently revalidated, or the source was authenticated. A Safe inventory run can be published with incomplete security/core assessments. A partial run retains its recorded termination and the collector's nonzero exit; a plan-only result never becomes collection credit. Missing/corrupt states expose no verified result identity or decision. Keep protected-artifact provenance, attestation verification, approval and platform publication controls in the appropriate jobs.

Artifact inspection bounds JSON to 16 MiB and depth 128, each subject to 64 MiB, total hashed subjects to 256 MiB, and directory/subject counts to 64. Hashing receives a ten-second cancellation signal; retain an independent workflow/job deadline for setup, collection, filesystem failures and upload. Symlinks, path traversal, duplicate subjects/bundles, inconsistent run/target/exit metadata and digest mismatches fail closed. These adapter bounds are not a whole-process memory guarantee.

Pass output values through quoted environment variables when using a downstream shell; do not interpolate untrusted metadata into executable code. Publication success alone must not override `exit-code`, the Action step outcome or the selected assurance policy.

Generate the CLI option manifest with `mcpval config-schema --commands` without target contact or host construction. It records actual names, aliases, value types, argument arity, multiple-values-per-token behavior, defaults and required metadata, with token defaults suppressed. A null command default can mean inheritance from configuration/profile/default owners; it is not a claim that the final setting is null. The configuration schema remains `mcpval config-schema`. Action inputs are separately declared in `action.yml` and tested against exact command-array forwarding; they do not expose unrestricted extra arguments.
