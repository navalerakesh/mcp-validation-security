# CI Policy Examples

Pin the Action to a released major/minor or immutable commit according to your governance policy.

## Advisory

Collect evidence without blocking on validation findings:

```yaml
- uses: navalerakesh/mcp-validation-security@v1
  with:
    server: https://example.com/mcp
    policy: advisory
```

## Regression gate

Store an approved canonical result as a protected CI artifact, then block only regressions:

```yaml
- uses: navalerakesh/mcp-validation-security@v1
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
- uses: navalerakesh/mcp-validation-security@v1
  with:
    server: https://example.com/mcp
    access: authenticated
    token: ${{ secrets.MCP_TOKEN }}
    policy: strict
    upload-artifacts: 'true'
```

Use environment protections for authenticated targets. Never place tokens in endpoint query strings, workflow summaries, or artifact names. The Action exposes typed inputs rather than free-form command arguments.

## Collector exit output

The unreleased Action revision declares `exit-code`, containing the actual `mcpval` collector process status when it was invoked and the output was published. The validation step exits with that same status after successful publication. If the runner output file is unavailable or cannot be written, the step instead exits `70` and emits a sanitized diagnostic containing the collector's numeric status; it does not pretend publication succeeded or expose the output path. Exposing the output does not enable `continue-on-error` or turn a failed collection into success. The existing artifact-upload step retains its `always()` condition.

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
