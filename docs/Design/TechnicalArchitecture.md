# MCP Validator Technical Architecture

This document is the detailed companion to [Architecture.md](Architecture.md) and [ComponentDesign.md](ComponentDesign.md). It focuses on stable entry points, execution lifecycle, and extension points for engineers working in the solution.

## Stable Code Map

| Project | Key responsibilities |
| --- | --- |
| `Mcp.Benchmark.CLI` | Command binding, dependency injection, console UX, session-log hints, artifact routing |
| `Mcp.Benchmark.Core` | `McpValidatorConfiguration`, `ValidationResult`, neutral contracts, shared enums and models |
| `Mcp.Benchmark.ClientProfiles` | `ClientProfileCatalog`, host-compatibility descriptors, evidence interpretation logic |
| `Mcp.Benchmark.Infrastructure` | Session bootstrap, transport clients, auth strategies, validators, scoring, report generators |
| `Mcp.Compliance.Spec` | `ProtocolVersions`, schema descriptors, embedded schema registry |

## Execution Lifecycle

1. The CLI binds command-line input and optional configuration into a `McpValidatorConfiguration`.
2. Session bootstrap resolves the target type, performs health and initialization checks, and establishes authentication context when needed.
3. Validators execute against a shared session context and collect neutral evidence for each category.
4. Scoring converts category evidence into overall score, trust assessment, and policy outcome.
5. Optional client profile evaluation interprets the completed result for specific hosts without mutating the raw findings.
6. Report generators render the chosen output formats and the CLI emits the final summary and exit code.

## Command And Transport Behavior

| Command | Behavior |
| --- | --- |
| `validate` | Full execution path for HTTP and STDIO targets |
| `health-check` | Lightweight bootstrap probe for HTTP and STDIO targets |
| `discover` | Remote capability discovery for HTTP targets; STDIO discovery is not implemented yet |
| `report` | Offline rendering path from saved artifacts only |

Transport detection and auth negotiation are shared infrastructure concerns. Command handlers should not duplicate those decisions.

## Observability And Reproducibility

- The output directory stores the artifact set intended for people, CI systems, and downstream tooling.
- The saved JSON result is the canonical record for offline rendering and post-run analysis.
- Session-log hints are emitted by the CLI so operators can inspect a single run in more detail when needed.
- Inside GitHub Actions, the host also writes step summaries and workflow annotations.

## Extension Points

### Add a new protocol version

- Vendor the schema set under `Mcp.Compliance.Spec/schema/<version>/`
- Register the version in `ProtocolVersions`
- Expose it through the CLI profile catalog
- Add regression coverage proving the registry can resolve the assets

### Add a new validation rule or validator

- Keep the rule and evidence collection in infrastructure
- Reuse the existing neutral result model where possible
- Add unit and integration coverage before exposing it in reports

### Add a new authentication flow

- Implement it as an authentication strategy in infrastructure
- Keep credential acquisition and retry logic out of command handlers
- Surface operator guidance through existing console and next-step services

### Add a new client profile

- Model it in `Mcp.Benchmark.ClientProfiles`
- Derive compatibility from existing evidence instead of introducing client-specific branches into validators
- Document the profile and its assumptions in user-facing docs

### Add a new report format

- Render from the saved `ValidationResult`
- Expose the format through `report`
- Preserve the JSON result as the source of truth

## Guardrails

- Keep `Core` host-neutral.
- Do not place client-specific compatibility rules inside neutral validators.
- Do not bypass the schema registry with direct file-system reads in validators.
- Keep reporting deterministic by rendering from saved results instead of re-contacting targets.