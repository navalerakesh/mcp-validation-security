# MCP Validator Troubleshooting

This guide covers the most common operational failures and the fastest path to diagnosing them.

## Connection Failures

### Server not reachable

Typical causes:

- Incorrect URL or STDIO command
- The target process is not running
- Network routing, proxy, or firewall restrictions

Recommended actions:

- Re-run `mcpval health-check` before a full validation run.
- Confirm that the endpoint is the actual MCP entry point, not just a product landing URL.
- For STDIO targets, run the command directly outside `mcpval` once to confirm it starts cleanly.

### The server returns `429 Too Many Requests`

Remote MCP endpoints often protect discovery and tool surfaces with throttling.

Current validator behavior:

- Retryable `429`-style protocol or tool probes are treated as inconclusive transport pressure, not as hard spec failures.
- Remote functional probes self-calibrate concurrency downward before the full validator fan-out begins.
- Performance probes still measure pressure separately; those results remain visible in the performance section.

Recommended actions:

- Re-run the same command once before treating a transient probe note as a product defect.
- Lower `--max-concurrency` if the endpoint is known to be sensitive to burst traffic.
- Read the Markdown or HTML report to distinguish inconclusive probe notes from actual protocol violations.

## Authentication Failures

### The server returns `401` or `403`

Check the following first:

- `--access` matches the target's intended exposure model
- The supplied token is current and has the expected scopes
- Interactive login is supported for the target environment before relying on `--interactive`

If the run still fails, use `--verbose` and review the session-log hint printed by the CLI.

## Transport Mismatch

### STDIO target fails to start

Typical causes:

- The executable is missing from `PATH`
- The command needs additional environment variables
- The process exits before responding to the initialize handshake

Recommended actions:

- Run the STDIO command directly in a shell first.
- Use absolute executable paths if the environment differs between your shell and CI.
- Start with `mcpval health-check` to isolate startup issues from validator findings.

### `discover` does not work for STDIO commands

This is a documented product limitation, not a crash path. `discover` is HTTP-first today.

Use `validate` when you need transport-aware discovery behavior for a STDIO server.

## Policy Failures

### The run completed but exited non-zero

That usually means the validation succeeded technically, but the selected policy mode rejected the result.

Recommended actions:

- Read the executive summary in the Markdown report first.
- Inspect `PolicyOutcome` in the JSON result if you need machine-readable gating detail.
- Only relax policy thresholds deliberately; do not treat policy failures as transport failures.

### The run shows an inconclusive probe note

This means the validator saw retryable transport pressure while testing a specific probe, such as notification handling or `tools/call`.

Recommended actions:

- Treat the note as operational context, not as confirmed MCP non-compliance.
- Re-run during a lower-pressure window if you need a cleaner protocol-only read.
- Check whether the underlying raw finding is labeled as `guideline` or `heuristic` before escalating it as a spec defect.

## Report Rendering Issues

### `report` cannot produce the expected format

Check the following:

- The input points to a saved validation result or a sibling Markdown report from a completed run
- The output path is writable
- The requested format is one of `html`, `xml`, `sarif`, or `junit`

If in doubt, start from the `*-result.json` artifact because it is the canonical source for offline rendering.

## Need More Detail

When troubleshooting a complex failure:

- Re-run with `--verbose`
- Preserve the generated `*-result.json` artifact
- Capture the session-log path printed by the CLI
- Include sanitized artifacts when opening an issue

## When To Open An Issue

Open an issue when the behavior appears incorrect rather than merely unsupported. Include:

- The exact command line
- The `mcpval` version or distribution used
- The MCP endpoint or STDIO command under test
- A sanitized Markdown report, JSON result, or session-log excerpt
