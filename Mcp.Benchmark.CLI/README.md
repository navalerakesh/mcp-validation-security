# Mcp.Benchmark.CLI

This project hosts the `mcpval` command-line application. It is the composition root for command binding, dependency injection, console UX, artifact writing, and GitHub Actions integration.

If you are looking for installation or end-user usage guidance, start with the repository [README](../README.md) and [QUICKSTART](../QUICKSTART.md). This document focuses on what this source project owns inside the solution.

## Responsibilities

| Concern | Owned here | Lives elsewhere |
| --- | --- | --- |
| Command parsing and option validation | Yes | No |
| Console output, next-step hints, and session-log guidance | Yes | No |
| Dependency injection and runtime composition | Yes | No |
| Transport clients, validators, scoring, and reporting engines | No | `Mcp.Benchmark.Infrastructure` |
| Neutral domain models and abstraction contracts | No | `Mcp.Benchmark.Core` |
| Client-specific compatibility expectations | No | `Mcp.Benchmark.ClientProfiles` |
| Vendored protocol schemas and version catalog | No | `Mcp.Compliance.Spec` |

## Commands Hosted Here

| Command | Role |
| --- | --- |
| `validate` | Runs the full validation suite and writes the standard artifact set |
| `health-check` | Performs a fast connectivity and initialization probe |
| `discover` | Captures remote capability snapshots for documentation or debugging |
| `report` | Renders saved validation results into additional offline formats |

Transport support is intentionally command-specific: `validate` and `health-check` support HTTP and STDIO targets today, `discover` is HTTP-first, and `report` is offline only.

## Local Development

Run the CLI host directly from source:

```bash
dotnet run --project Mcp.Benchmark.CLI -- --help
```

Example commands:

```bash
dotnet run --project Mcp.Benchmark.CLI -- validate --server https://example.test/mcp --access public --output ./reports
dotnet run --project Mcp.Benchmark.CLI -- health-check --server https://example.test/mcp --access public
dotnet run --project Mcp.Benchmark.CLI -- discover --server https://example.test/mcp --format json
dotnet run --project Mcp.Benchmark.CLI -- report --input ./reports/mcp-validation-20260421-031745-result.json --format junit
```

## Execution Governance

`validate`, `health-check`, and `discover` now apply a safe-by-default execution contract before contacting a target.

- Default persistence is ephemeral. Session logs and session-state artifacts are only created when `--persistence-mode session` is selected.
- `--dry-run` renders the execution plan and exits without sending requests.
- `--mode elevated` requires `--confirm-elevated-risk` before the run starts.
- `--allow-host`, `--allow-private-addresses`, `--max-requests`, and `--timeout` define the outbound execution budget instead of leaving it implicit.
- Experimental model evaluation stays outside the canonical validation result and is emitted as a companion artifact only when a supported provider is configured.

Current model-evaluation support:

- `--enable-model-eval` without `evaluation.modelEvaluation.provider` now fails fast instead of emitting a placeholder `Skipped` artifact.
- This build currently ships the deterministic companion provider `builtin-rubric` for experimental side-lane summaries.

Typical governance-first invocation:

```bash
dotnet run --project Mcp.Benchmark.CLI -- validate \
  --server https://example.test/mcp \
  --mode safe \
  --dry-run \
  --allow-host example.test
```

The same contract applies to quick probes:

```bash
dotnet run --project Mcp.Benchmark.CLI -- health-check \
  --server https://example.test/mcp \
  --dry-run \
  --allow-host example.test

dotnet run --project Mcp.Benchmark.CLI -- discover \
  --server https://example.test/mcp \
  --format json \
  --dry-run \
  --allow-host example.test
```

## Design Guardrails

- Keep this project thin. Command handlers should translate CLI input into configuration and delegate real work to shared services.
- Keep protocol rules, transport logic, auth strategies, scoring, and report generation out of the CLI host.
- Keep client-specific compatibility logic out of Core and out of command handlers; it belongs in `Mcp.Benchmark.ClientProfiles`.
- Add new report surfaces by extending the shared reporting layer and wiring them here, not by duplicating rendering logic per command.

## Useful References

- [../README.md](../README.md) for installation, commands, and artifact behavior
- [../docs/FeatureMatrix.md](../docs/FeatureMatrix.md) for current command and transport support
- [../docs/Design/Architecture.md](../docs/Design/Architecture.md) for solution boundaries and runtime flow
- [../docs/Design/TechnicalArchitecture.md](../docs/Design/TechnicalArchitecture.md) for extension points and internal lifecycle details
