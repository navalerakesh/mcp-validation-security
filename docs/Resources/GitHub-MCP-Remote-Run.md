# GitHub MCP Remote Run

This document shows a successful `mcpval` run against the GitHub remote MCP server and the artifacts the CLI produced.

## Command

```bash
DOTNET_ROLL_FORWARD=Major dotnet run --project Mcp.Benchmark.CLI -- validate \
  --server https://api.githubcopilot.com/mcp/ \
  --access authenticated \
  --token "$(gh auth token)" \
  --output ./PublicReports/github-mcp-remote \
  --verbose
```

> [!NOTE]
> `DOTNET_ROLL_FORWARD=Major` was used on this machine because the workspace has the .NET 10 runtime installed while the CLI targets `net8.0`.

## What The CLI Produces

> [!TIP]
> The CLI prints a console summary with server identity, negotiated protocol version, category scores, and next-step guidance.

> [!TIP]
> The CLI writes a Markdown report for human review:
> [mcp-validation-20260315-060145-report.md](../../PublicReports/github-mcp-remote/mcp-validation-20260315-060145-report.md)

> [!TIP]
> The CLI writes a JSON result snapshot for machine processing:
> [mcp-validation-20260315-060145-result.json](../../PublicReports/github-mcp-remote/mcp-validation-20260315-060145-result.json)

> [!TIP]
> The CLI can render the saved JSON into a shareable HTML report:
> [mcp-validation-20260315-060145-report.html](../../PublicReports/github-mcp-remote/mcp-validation-20260315-060145-report.html)

## Successful Run Summary

- Server: `https://api.githubcopilot.com/mcp/`
- Status: `PASSED`
- Score: `84.1%`
- Transport: `HTTP`
- Protocol: `2025-03-26`
- Duration: `66.82s`
- Security: `100%`
- Tools discovered: `43`

## Output Folder

All kept artifacts for this run are under:

```text
PublicReports/github-mcp-remote/
```

Files retained from the successful run:

- [mcp-validation-20260315-060145-report.md](../../PublicReports/github-mcp-remote/mcp-validation-20260315-060145-report.md)
- [mcp-validation-20260315-060145-result.json](../../PublicReports/github-mcp-remote/mcp-validation-20260315-060145-result.json)
- [mcp-validation-20260315-060145-report.html](../../PublicReports/github-mcp-remote/mcp-validation-20260315-060145-report.html)