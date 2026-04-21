# MCP Validator Quick Start

This guide gets you from installation to a first validation run and explains what the CLI produces.

## 1. Install `mcpval`

Choose the distribution model that matches your environment.

### NuGet global tool

```bash
dotnet tool install --global McpVal
mcpval --help
```

### Standalone release binary

Download the platform-specific package from [GitHub Releases](https://github.com/navalerakesh/mcp-validation-security/releases). This option does not require a local .NET runtime.

### Container image

```bash
docker run --rm ghcr.io/navalerakesh/mcp-validation-security:latest --help
```

## 2. Validate A Remote MCP Server

Start with a standard HTTP or HTTPS endpoint.

```bash
mcpval validate \
  --server https://example.test/mcp \
  --access public \
  --output ./mcp-reports
```

If you want host-specific compatibility interpretation in the same run, add a documented client profile:

```bash
mcpval validate \
  --server https://example.test/mcp \
  --access public \
  --client-profile github-copilot-cloud-agent \
  --output ./mcp-reports
```

## 3. Validate An Authenticated Server

Use `--access authenticated` when the target is expected to require credentials.

```bash
mcpval validate \
  --server https://example.test/mcp \
  --access authenticated \
  --token "$MCP_TOKEN" \
  --output ./mcp-reports
```

When a strategy supports it, you can use an interactive login flow instead of passing a token directly.

```bash
mcpval validate \
  --server https://example.test/mcp \
  --access authenticated \
  --interactive \
  --output ./mcp-reports
```

## 4. Validate A Local STDIO Server

`validate` and `health-check` both support process-backed STDIO targets.

```bash
mcpval validate \
  --server "npx -y @modelcontextprotocol/server-everything" \
  --access public \
  --output ./mcp-reports
```

The CLI automatically treats non-URL `--server` values as STDIO commands.

`discover` is the one exception today: it is HTTP-first and returns a clear not-supported error when pointed at a STDIO command.

## 5. Review The Generated Artifacts

`validate --output <folder>` writes four files for every run:

- `mcp-validation-<timestamp>-report.md` - Markdown report for people
- `mcp-validation-<timestamp>-report.html` - HTML report for sharing
- `mcp-validation-<timestamp>-result.json` - canonical machine-readable result object
- `mcp-validation-<timestamp>-results.sarif.json` - SARIF feed for CI and code scanning

Generated reports are full by default, but stay compact by summarizing each section and adding short action hints. Use `--report-detail minimal` when you want the executive-only view.

## 6. Render Additional Offline Formats

Use the saved JSON result when you need a different artifact format for downstream systems.

```bash
mcpval report \
  --input ./mcp-reports/mcp-validation-20260421-031745-result.json \
  --format junit \
  --output ./mcp-reports/mcp-validation-20260421-031745.junit.xml
```

Supported offline formats are `html`, `xml`, `sarif`, and `junit`.

## 7. Pick The Right Policy Mode

| Policy | Intended use |
| --- | --- |
| `advisory` | Always complete the run and keep exit code `0` unless execution itself fails |
| `balanced` | Default mode for most teams; fail on non-passing results, MUST failures, low trust, or critical findings |
| `strict` | Enterprise gate; includes balanced conditions plus SHOULD failures, trust below `L4`, and high-severity findings |

## 8. Use The Other Commands

Run a fast connectivity probe before a full validation:

```bash
mcpval health-check --server https://example.test/mcp --access public
```

Capture a capability snapshot for a remote endpoint:

```bash
mcpval discover --server https://example.test/mcp --format json
```

Inside GitHub Actions, `mcpval` also writes a step summary and emits workflow annotations automatically. The repository root includes a reusable composite action if you want to standardize that workflow across multiple repositories.

## Troubleshooting

- If the server is unreachable, verify the URL or STDIO command first, then rerun with `--verbose` for extra console diagnostics.
- If authentication fails, confirm that `--access` matches the target's intended exposure model and that the supplied token has the right scopes.
- If the run fails a policy gate, inspect the Markdown report and the JSON result before changing thresholds.
- If you need deeper help, use [docs/Troubleshooting.md](docs/Troubleshooting.md).

## Next References

- [README.md](README.md) for the full product overview and package links
- [docs/FeatureMatrix.md](docs/FeatureMatrix.md) for current support boundaries and artifact behavior
- [docs/README.md](docs/README.md) for architecture, contributor, and operational documentation