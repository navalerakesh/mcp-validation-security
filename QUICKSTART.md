# MCP Validator Quick Start

This guide gets you from installation to a first validation run and explains what the CLI produces.

> **Breaking changes:** This guide describes 3.0 behavior. Existing users should follow the [3.0 upgrade checklist](docs/SchemaCompatibility.md#upgrading-to-30) and upgrade the CLI and local MCP wrapper together. Availability is determined by the published release, not this guide.

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

Start with an HTTP or HTTPS endpoint in explicit Safe inventory mode. Comprehensive execution additionally requires the [signed security baseline and independent run admission](Mcp.Benchmark.CLI/README.md#comprehensive-admission); it fails closed without prerequisites.

```bash
mcpval validate \
  --server https://example.test/mcp \
  --access public \
  --assurance inventory \
  --output ./mcp-reports
```

Host-specific compatibility interpretation is included by default. Add `--client-profile` only when you want to narrow the report to a smaller documented host subset.

```bash
mcpval validate \
  --server https://example.test/mcp \
  --access public \
  --assurance inventory \
  --client-profile github-copilot-cli \
  --output ./mcp-reports
```

## 3. Validate An Authenticated Server

Use `--access authenticated` when the target is expected to require credentials.

```bash
mcpval validate \
  --server https://example.test/mcp \
  --access authenticated \
  --assurance inventory \
  --token "$MCP_TOKEN" \
  --output ./mcp-reports
```

When a strategy supports it, you can use an interactive login flow instead of passing a token directly.

```bash
mcpval validate \
  --server https://example.test/mcp \
  --access authenticated \
  --assurance inventory \
  --interactive \
  --output ./mcp-reports
```

## 4. Validate A Local STDIO Server

`validate` and `health-check` both support process-backed STDIO targets.

```bash
mcpval validate \
  --server "npx -y @modelcontextprotocol/server-everything" \
  --access public \
  --assurance inventory \
  --output ./mcp-reports
```

The CLI automatically treats non-URL `--server` values as STDIO commands.

`discover` is the one exception today: it is HTTP-first and returns a clear not-supported error when pointed at a STDIO command.

## 5. Review The Generated Artifacts

An admitted validation run with `--output <folder>` publishes its artifacts together in a per-run `mcp-validation-*` subdirectory. Follow the paths returned by the CLI; admission failures before a run starts do not produce assessment evidence. A completed bundle includes:

- `mcp-validation-<timestamp>-report.md` - Markdown report for people
- `mcp-validation-<timestamp>-report.html` - HTML report for sharing
- `mcp-validation-<timestamp>-result.json` - canonical machine-readable result object
- `mcp-validation-<timestamp>-results.sarif.json` - SARIF feed for CI and code scanning
- `mcp-validation-<timestamp>-results.junit.xml` - JUnit feed for CI test reporting
- `mcp-validation-<timestamp>-audit.json` - execution audit manifest for the run

The CLI also writes `mcp-validation-<timestamp>-profile-summary.json` so CI or dashboards can consume the per-profile outcome rollup directly. Use `--client-profile` only to narrow the evaluated host set.

Generated reports use full detail by default. HTML starts with the target, selected scope and assessment decision, keeping execution, assurance, policy and certificate disposition separate. Coverage and priority actions precede supporting domain evidence and provenance. `--report-detail minimal` reduces supporting detail without dropping the canonical claims or glossary; it is not an executive-only summary. See [Reports And Evidence](docs/Reporting.md).

For regression-only CI gating:

```bash
mcpval validate \
  --server https://example.com/mcp \
  --assurance ci \
  --mode standard \
  --baseline ./approved-result.json \
  --regression-only \
  --output-format json \
  --output ./mcp-reports
```

Structured mode writes one versioned result envelope to stdout and typed failures to stderr. To emit a signed attestation, set `MCPVAL_ATTESTATION_PRIVATE_KEY_PEM` in the process environment and add `--sign-attestation`; the private key is never persisted.

To combine a run with pinned official conformance evidence, supply a wrapper that conforms to `docs/Schemas/mcpval-official-conformance-evidence.schema.json`:

```bash
mcpval validate \
  --server https://example.com/mcp \
  --assurance ci --mode standard \
  --conformance-results ./official-conformance-wrapper.json \
  --output ./mcp-reports
```

This active CI example requires an owned or explicitly authorized target. The wrapper target digest and protocol version must match the validation run. Official checks retain their own authority and affect only applicable conformance claims; extensions remain separate. Exact runner, source, package, requirement-set, scenario, and wrapper digests are retained for audit. Comprehensive assurance additionally requires the signed identity baseline and reserved run ID described above.

Advertised Apps and Skills extensions are detected automatically. Explicitly authorize Skills content reads only on a reviewed target:

```bash
mcpval validate \
  --server https://example.com/mcp \
  --assurance ci \
  --mode standard \
  --extension io.modelcontextprotocol/skills \
  --output ./mcp-reports
```

## 6. Render Additional Offline Formats

Use the saved JSON result when you need a different artifact format for downstream systems.

```bash
mcpval report \
  --input "$RESULT_JSON_PATH" \
  --format junit \
  --output ./mcp-reports/replayed-results.junit.xml
```

Set `RESULT_JSON_PATH` to the actual `*-result.json` path returned by validation, including its per-run subdirectory.

Supported offline formats are `html`, `xml`, `sarif`, and `junit`.

## 7. Pick The Right Policy Mode

| Policy | Intended use |
| --- | --- |
| `advisory` | Preserve findings without blocking on assurance debt; execution-integrity and required-baseline failures still block |
| `balanced` | Default gate; blocks rejected assurance assessments and critical policy signals |
| `strict` | Enterprise gate; also blocks review-required assessments, incomplete coverage, and high-severity policy signals |

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
