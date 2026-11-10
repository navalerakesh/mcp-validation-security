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

`validate --assurance inventory|ci|provider` selects the V2 assurance contract. `validate --conformance-results <wrapper.json>` imports pinned official conformance evidence through the Core importer contract. Provider assurance additionally requires `--provider-harness <manifest.json>`, `--provider-authorization-key <public-key.pem>`, and `--sign-attestation`; the P-256-signed authorization and prior-run harness, target, protocol revision, dry-run execution-plan digest, repeated receipts, and internally produced current run must all verify. Repeatable `--extension <id>` selections flow through the existing applicability resolver; they do not create extension-specific top-level commands. Core, security, operations, extensions, clients, and external conformance remain independent typed assessments.

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
- `--allow-host`, `--allow-origin`, `--allow-private-addresses`, `--max-requests`, and `--timeout` define the outbound execution budget instead of leaving it implicit.
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

## Configuration Files

An explicitly supplied configuration file must exist, be a regular non-symlink file, and fit the 1 MiB input limit. Configuration JSON must be an object with at most 64 levels of nesting. Malformed JSON, unknown fields, duplicate fields (including casing aliases), and null required sections fail before target contact; omitting the file still uses defaults.

CLI binding and configuration normalization occur before host construction. Host transport selection and the selected command consume one invocation-owned normalized input; a later file change does not replace that input. Undefined enum values, including collection items, are rejected. Zero-length inputs are rejected before opening, preventing the observed FIFO blocking case. These checks are not a sandbox against hostile filesystem path replacement; keep configuration files and their parent directories under trusted ownership.

Configuration field names accept casing aliases, including `server`/`Server` and `headers`/`Headers`. This does not lowercase dictionary keys: environment variables and arbitrary parameter names retain their case. Duplicate HTTP header names are rejected case-insensitively. Nullable settings remain nullable; non-nullable settings and string dictionary values must not explicitly be null.

This is stricter than 2.0.1, which could silently ignore unknown fields or a missing file. Remove misspelled or duplicated fields instead of relying on last-value-wins behavior. The same reader is used by validation, health/discovery configuration and offline report configuration. Diagnostics do not include configuration values or unrecognized key text.

Configuration documents may declare `"schemaVersion": "1.0.0"` at the root. Files without this property retain legacy interpretation; adding the current version does not change normalized settings. Unsupported versions, null or non-string versions, and duplicate version aliases fail preflight. This version identifies the configuration format, not the MCP protocol revision or canonical result contract. UTF-8 files with one leading byte-order mark are accepted; invalid UTF-8 is a usage error.

A minimal versioned configuration:

```json
{
  "schemaVersion": "1.0.0",
  "server": {
    "endpoint": "https://example.invalid/mcp",
    "transport": "http"
  },
  "execution": {
    "mode": "safe",
    "timeoutSeconds": 30
  }
}
```

The [configuration schema](../docs/Schemas/mcpval-configuration-v1.schema.json) is generated from the same serializer metadata as the parser. Emit it without target contact using:

```bash
mcpval config-schema
```

For migration, first remove unknown or duplicate fields and explicit nulls rejected by the parser, then add `"schemaVersion": "1.0.0"`. Do not relabel an unsupported future version as current without reviewing its settings. The schema describes structural input shape; byte/depth limits, duplicate aliases, case-insensitive header collisions and runtime numeric representation checks remain parser responsibilities. The checked-in schema has a drift test and must be regenerated deliberately when the contract changes.

Validation, health and discovery plans and audit manifests record value-free setting origins: default, file, profile, CLI or governance. A same-value explicit CLI setting still records the override. Dictionary/collection settings are tracked as units without recording arbitrary keys or values. Offline-report session receipts track only their effective reporting settings.

For health and discovery, an explicit `--timeout` (milliseconds) wins; otherwise the configured `execution.timeoutSeconds` wins over legacy `server.timeoutMs`. If neither is supplied, health retains its 30-second default and discovery its 120-second default. Milliseconds are rounded up to whole seconds for the execution budget. Non-positive explicit timeouts fail with usage exit 64. Safe-mode restrictions and derived budgets are recorded after command overrides.

The full entrypoint qualification matrix remains in progress; see the [implementation status](../docs/EnterpriseRecoveryProgress.md). Schema generation uses the locked System.Text.Json exporter described by the [official documentation revision](https://github.com/dotnet/docs/blob/700169ff5b8d4936652749c196f76eb31a512bd1/docs/standard/serialization/system-text-json/extract-schema.md).

## Internal Windows Supervision

The local MCP adapter uses a versioned internal CLI channel on Windows to own CLI workers and their descendants in an unnamed Job Object. It is not a user-facing command. The worker is created suspended with creation-time job assignment and an explicit three-handle stdio inheritance list; the job handle is not inherited. Parent control and bounded output use owned overlapped pipes.

The protocol requires compatible capability negotiation and a post-creation ready/resume exchange. Deadlines include creation and admission. Cancellation, parent loss, output failure or worker exit trigger job cleanup; only observed zero active job processes and drained output can produce a confirmed terminal receipt. Kill-on-close is a safety mechanism, not completion evidence. Windows 10 / Server 2016 or later is required; nested-job restrictions fail closed without breakaway flags.

Native Windows CI is a release dependency and rejects missing, failed or skipped native evidence. Portable compilation and mock tests do not establish Windows acceptance. Job Objects are not a sandbox; externally brokered processes are outside the containment claim. See [implementation status](../docs/EnterpriseRecoveryProgress.md).

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
