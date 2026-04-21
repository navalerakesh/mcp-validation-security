# GitHub MCP Remote Example Run

This document shows a representative `mcpval validate` invocation against GitHub's remote MCP endpoint and explains the artifact set the CLI produces.

## Command

```bash
DOTNET_ROLL_FORWARD=Major dotnet run --project Mcp.Benchmark.CLI -- validate \
  --server https://api.githubcopilot.com/mcp/ \
  --access authenticated \
  --token "$(gh auth token)" \
  --output ./PublicReports/github-mcp-remote-live \
  --verbose
```

`DOTNET_ROLL_FORWARD=Major` was required on the machine used for this example because the local runtime inventory differed from the target framework used by the CLI project. That environment-specific setting is not part of normal product usage.

## What The CLI Produces

For every successful `validate --output <folder>` run, the CLI writes the following timestamped artifacts:

| Artifact | Purpose |
| --- | --- |
| `mcp-validation-<timestamp>-report.md` | Human-readable Markdown report |
| `mcp-validation-<timestamp>-report.html` | Shareable HTML report |
| `mcp-validation-<timestamp>-result.json` | Canonical machine-readable validation result |
| `mcp-validation-<timestamp>-results.sarif.json` | SARIF findings feed for CI and code-scanning systems |

Inside GitHub Actions, the same run also emits a step summary and workflow annotations derived from policy failures, structured findings, protocol violations, and security vulnerabilities.

## Example Output Directory

A representative output set is kept under:

- [../../PublicReports/github-mcp-remote-live](../../PublicReports/github-mcp-remote-live)

The exact filenames change on every run because they are timestamped. The folder is preserved as an example of the default artifact contract rather than as a normative reference for scores or findings.

## Why This Example Matters

- It demonstrates an authenticated remote validation flow against a widely recognized MCP endpoint.
- It shows the full artifact set produced by `validate` without requiring a separate rendering step.
- It provides a realistic example of the type of evidence teams can archive in CI or attach to change reviews.

## Reusable Action

Other repositories can adopt the validator through the composite action shipped at the repository root:

```yaml
- name: Validate MCP server
  uses: navalerakesh/mcp-validation-security@main
  with:
    server: https://example.test/mcp
    access: authenticated
    token: ${{ secrets.MCP_TOKEN }}
    policy: strict
    output-dir: ./mcp-validation-results
```
