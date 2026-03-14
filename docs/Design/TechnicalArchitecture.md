# MCP Benchmark – Technical Architecture Details

This document is the detailed companion to **MCP Benchmark – Architecture & Design**. It focuses on concrete layers, key services, and extension points for engineers who want to understand or modify the internals.

If you just want to understand what the tool does and how the main pieces fit together, start with **Architecture & Design** and **Component Design**. This file is meant as a deeper reference once you are comfortable with the high‑level view.

## Code Map (Stable Entry Points)

This is a lightweight map of the most important building blocks. It intentionally avoids file paths and focuses on projects + key types so it stays stable over time.

- **Mcp.Benchmark.CLI**
	- `Program` – CLI entry point and dependency injection composition root.
	- `ValidateCommand` – handler for `mcpval validate`.
	- `ReportCommand` – handler for `mcpval report`.

- **Mcp.Benchmark.Core**
	- `McpValidatorConfiguration` – configuration and profile for a validation run.
	- `ValidationResult` – aggregate result and findings for a run.
	- `IMcpValidatorService`, `IValidationSessionBuilder`, `IValidator`, `IAggregateScoringStrategy` – core abstractions used across the solution.

- **Mcp.Benchmark.Infrastructure**
	- `McpValidatorService` – orchestrates validators and scoring.
	- `ValidationSessionBuilder` – builds the validation session and capability snapshot.
	- `McpHttpClient` and related transport types – HTTP / JSON-RPC client used by validators.
	- Category-specific validators (protocol, tools, resources, prompts, security, performance) – implement `IValidator`.

- **Mcp.Compliance.Spec**
	- `ISchemaRegistry`, `ProtocolVersions`, schema descriptors – hold vendored MCP schemas and protocol metadata used by validators and reports.

## Layered Solution

| Layer | Responsibilities | Notes |
| --- | --- | --- |
| `Mcp.Benchmark.Core` | Contracts, domain models (`ValidationResult`, `McpValidatorConfiguration`), abstraction interfaces, scoring contracts | Framework-agnostic, no external dependencies |
| `Mcp.Benchmark.Infrastructure` | Implementations for HTTP/SSE clients, validators, rule registries, authentication strategies, scoring engines, telemetry shims | Organized by functional domain to keep SRP intact |
| `Mcp.Benchmark.CLI` | System.CommandLine entrypoint, DI bootstrapper, console UX, session artifact plumbing | Hosts `validate`, `report`, `discover`, `health-check` commands |
| `Mcp.Compliance.Spec` | Vendored JSON Schemas, protocol version catalog, `ISchemaRegistry` and descriptors | Consumed by Infrastructure + reports to stay spec-aware |

## Execution Pipeline

1. **Input & configuration** – CLI merges command arguments, optional config JSON, and selected spec profile into a `McpValidatorConfiguration`.
2. **Pre-flight health** – `HealthCheckService` probes transports (HTTP streamable, SSE, STDIO placeholder) and captures soft failures (401/403) for retry once auth strategies kick in.
3. **Authentication negotiation** – `AuthenticationService` selects a strategy (GitHub, Azure, API Key, future flows). `INextStepAdvisor` surfaces actionable guidance if credentials are missing.
4. **Session fan-out** – `ValidationSessionBuilder` schedules validators. `McpValidatorService` executes protocol, prompt, resource, tool, security, and performance validators concurrently up to `TestExecution.MaxParallelThreads` while sharing the same `IMcpHttpClient`.
5. **Scoring & remediation** – Each validator emits findings mapped to rule IDs. `IAggregateScoringStrategy` aggregates raw scores into a 0–100 result and attaches remediation suggestions.
6. **Reporting & artifacts** – Console summaries stream live, Markdown/JSON snapshots land in the chosen output directory, and session logs plus sanitized config/results are written under `%LOCALAPPDATA%/McpCli/Sessions/<sessionId>` for replay or offline HTML/XML rendering.

## Key Services & Patterns

- **`McpValidatorService` (Facade)** – orchestrates validator lifecycles, wiring cancellation, concurrency, and progress telemetry into every category.
- **`McpClientFactory` (Transport abstraction)** – auto-detects streamable HTTP vs SSE, handles retries after 401s, and normalizes JSON-RPC messaging into a single `IMcpClient` surface.
- **`McpHttpClient` (Networking gateway)** – enforces timeouts, deterministic headers, request correlation, and per-request auth overrides.
- **`ValidationSessionBuilder` (Coordinator)** – materializes validator graph per run, injecting rule registries, schema references, and category-specific settings.
- **`IProtocolRuleRegistry` + `IValidationRule` (Rule engine)** – isolates spec diagnostics per rule, enabling versioned or profile-specific rule packs without altering validators.
- **`AuthenticationStrategy` implementations (Strategy pattern)** – pluggable flows for GitHub, Azure, and upcoming OAuth 2.1 profiles; each strategy declares prerequisites (token, interactive, scopes).
- **`IAggregateScoringStrategy` (Strategy pattern)** – controls weighting (strict/security/balanced) and severity labeling while keeping validator output raw.
- **`BaseValidator<T>` (Template method)** – standardizes logging, timing, and exception handling per validator type.

## Transport & Resilience

- **Auto-detect transports** – HTTP streamable is attempted first; 401s trigger SSE fallback; STDIO hooks exist for future versions.
- **Concurrency guards** – `--max-concurrency` or configuration caps propagate to both validator fan-out and performance load tests, preventing server overload.
- **Timeouts & cancellations** – `ValidateCommand` scopes every run with a default 10-minute CTS while per-request HTTP timeouts remain at 30 seconds.
- **Session artifacts** – Each run receives a globally unique session folder storing logs, sanitized configs, results, and report inputs for reproducibility.
- **Resilient error surfacing** – Soft failures (auth missing, schema gaps) are downgraded to advisories when the spec does not demand hard errors; transport failures before auth is established are retried automatically.

## Extensibility Playbook

1. **Add a new rule** – implement `IValidationRule`, register in `ProtocolRuleRegistry`, and include it in the appropriate validator constructor.
2. **Create a validator** – inherit from `BaseValidator<T>`, inject dependencies (HTTP, schema registry, scoring context), and register via DI.
3. **Introduce a spec profile** – vendor schemas in `Mcp.Compliance.Spec`, extend `ProtocolVersions`, and update `SpecProfileCatalog`.
4. **Plug in telemetry** – implement `ITelemetryService` or wrap OpenTelemetry exporters without touching validator logic.
