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
