# MCP Validator Feature Matrix

This document summarizes the current product surface for the public `mcpval` CLI.

## Command Surface

| Command | Purpose | Status |
| --- | --- | --- |
| `mcpval validate` | Full validation run across protocol, tools, prompts, resources, security, and performance checks | Stable |
| `mcpval health-check` | Fast connectivity and initialization probe | Stable |
| `mcpval discover` | Capability discovery output for debugging and documentation | Stable for HTTP transport |
| `mcpval report` | Offline rendering from saved validation results | Stable |
| `mcpval --list-spec-profiles` | Show embedded MCP spec profiles supported by the current build | Stable |

## Transport Support

| Command | HTTP / HTTPS | STDIO |
| --- | --- | --- |
| `validate` | Supported | Supported |
| `health-check` | Supported | Supported |
| `discover` | Supported | Not yet supported |
| `report` | Offline only | Offline only |

When you need discovery-like evidence for a local STDIO server, use `validate`; it already performs transport-aware bootstrap and capability analysis.

## Protocol Profiles

| Profile | Era | Embedded schema | Current implementation status |
| --- | --- | --- | --- |
| `2026-07-28` / `latest` | Modern, stateless | Supported | Schema registry, official SDK foundation, era-aware planning, and feature applicability are active; remaining feature-specific certificates are tracked in the enterprise renewal plan |
| `2025-11-25` | Legacy, initialized | Supported | Supported |
| `2025-06-18` | Legacy, initialized | Supported | Supported |
| `2025-03-26` | Legacy, initialized | Supported | Supported |
| `2024-11-05` | Legacy, initialized | Supported | Supported |

The CLI prints `requested -> resolved (era)` before active execution. Unsupported or incomplete modern feature evidence must remain visible as unavailable/inconclusive rather than earning a pass.

`--protocol-era auto|legacy|modern` controls negotiation. `legacy` with `latest` resolves to the newest embedded legacy profile; `modern` forces Streamable HTTP; the oldest legacy profile uses SSE; explicit selections fail on unsupported or cross-era negotiation instead of silently downgrading.

Modern runs call `server/discover` before capability collection, validate supported versions/capabilities/result type/cache scope/TTL, and store typed discovery evidence. Normal HTTP and STDIO requests include required per-request protocol version, capability, and client identity metadata; raw malformed probes remain intentionally unenriched.

Modern HTTP transport checks require matching `_meta` and `MCP-Protocol-Version`, reject mismatches with HTTP 400, and validate unsupported versions as HTTP 400 plus JSON-RPC error `-32022` with requested/supported version data. Legacy initialize/session/SSE checks are not applied to modern stateless runs.

Modern JSON-RPC results are typed as `complete`, `input_required`, or invalid. `input_required` is valid only with a non-empty `requestState` or `inputRequests` map; continuation evidence remains unevaluated when a target returns only complete results.

For every modern list surface advertised by `server/discover`, the validator requires `resultType`, `cacheScope`, non-negative `ttlMs`, and the expected collection. It repeats the first page under the same run context and compares canonical SHA-256 fingerprints of ordered items and cursor without persisting target payloads.

Modern subscription checks require `notifications/subscriptions/acknowledged` as the first subscription message with a matching subscription ID. STDIO validation then cancels the long-lived request and requires a correlated complete close result; HTTP validates the first SSE event and closes the bounded probe stream.

Modern discovery records extension identifiers as typed evidence. The tasks extension (`io.modelcontextprotocol/tasks`) receives a dedicated stable finding; the validator does not invent task RPCs because the embedded protocol schema defines tasks as negotiated extension metadata.

Tool input/output schemas and returned `structuredContent` are evaluated with JSON Schema Draft 2020-12 semantics, including `unevaluatedProperties` and `prefixItems`. Resource catalogs have a configuration-owned processing limit; exceeding it produces explicit truncation evidence and an inconclusive result rather than silently scoring an incomplete catalog.

## Validation Coverage

| Area | Coverage |
| --- | --- |
| Protocol | JSON-RPC structure, initialization, capability negotiation, error handling, and response-shape validation |
| Tools | `tools/list`, `tools/call`, pagination, annotations, destructive-action signaling, and AI-readiness schema checks |
| Prompts | `prompts/list` and `prompts/get` structure, metadata quality, argument guidance, and prompt-safety checks |
| Resources | `resources/list`, `resources/read`, `resources/templates/list`, URI clarity, MIME guidance, and template ergonomics |
| Security | Authentication behavior, access-aware enforcement, content safety analysis, and attack simulation coverage |
| Performance | Latency, throughput, concurrency handling, and execution stability |
| Reporting | Console summaries plus Markdown, HTML, JSON, SARIF, audit manifests, and optional client-profile summary/model-evaluation companions from `validate`; HTML, XML, SARIF, and JUnit from `report`. HTML reports open with Run Status, Deterministic Verdict, and Trust Level decision cards. |
| CI gating | Advisory, balanced, and strict policy modes with structured policy outcomes |

## Execution Modes

| Mode | Active target behavior | Intended use |
| --- | --- | --- |
| `safe` | Discovery and metadata/schema analysis only; no tool calls, resource reads, prompt execution, malformed requests, attack simulation, or load | Default for unknown/public targets and first contact |
| `standard` | Configured active functional and conformance probes | Authorized non-production targets |
| `elevated` | Broadest configured active surface with explicit acknowledgement | Isolated environments with reviewed target ownership |

Incomplete safe-mode evidence produces a partial/review disposition and an L3 maximum, not a fabricated failure or enterprise-trusted rating.

HTTP response bodies are bounded by `execution.maxResponseBytes` (1 MiB by default, 16 MiB maximum). The normalized value is captured in immutable operation policy state and emitted in the audit manifest; oversized bodies produce explicit failed transport evidence rather than silent truncation or an empty response.

Each CLI command owns an asynchronously disposed dependency-injection scope. HTTP clients, SDK client caches, validators, session builders, and STDIO processes are isolated per command run. STDIO starts only after execution-policy admission, inherits a minimal platform environment, accepts target variables only from explicit server configuration, and terminates its process tree when startup is cancelled or fails.

Subprocess stdout/stderr is byte-bounded while streaming. Authentication CLI overflow terminates the process and cannot yield a credential. STDIO stderr is continuously drained into a fixed-capacity ring, but reports expose only byte count and truncation status, never raw stderr text.

## Result And Finding Model

| Capability | Behavior |
| --- | --- |
| Stable rule IDs | Findings carry stable identifiers for suppression, baselining, and automation |
| Rule-source labeling | Findings distinguish `spec`, `guideline`, and `heuristic` origins |
| Remediation guidance | Findings include remediation text suitable for reports and CI output |
| Structured evidence | Capability checks and interpretation layers attach explicit evidence instead of only free-form text |
| Canonical result object | `ValidationResult` preserves run, assessment, evidence, and compatibility documents plus trust assessment and policy outcome |
| Offline report replay | `report` can start from the canonical JSON result or from the Markdown report path that resolves to the sibling JSON snapshot |
| Transient probe calibration | Retryable protocol and tool probe responses are preserved as inconclusive operational evidence instead of being overstated as hard spec failures |

## Client Compatibility Profiles

`validate` can interpret neutral validation evidence against documented client expectations for:

- `claude-code`
- `vscode-copilot-agent`
- `github-copilot-cli`
- `github-copilot-cloud-agent`
- `visual-studio-copilot`
- `all`

These profiles do not mutate the underlying findings. They provide an additional host-specific interpretation layer on top of the same raw evidence set.

When profile evaluation is enabled, `validate` also emits `*-profile-summary.json` for dashboards and CI systems that only need the compatibility rollup.

## Known Product Limitation

The main transport gap in the public CLI is still `discover` for STDIO targets. The command returns a clear not-supported error instead of crashing, but process-backed capability discovery has not been implemented yet.

The local MCP wrapper accepts remote endpoints by default. Agent-triggered local STDIO commands require the operator to restart the wrapper with explicit local-execution authority in an isolated environment.

Application admission plus raw/SDK connect-time DNS and exact-origin enforcement are active, including rebinding checks, direct approved-address connections, disabled ambient proxies, and disabled automatic redirects. Network-enforced egress remains required before a hosted multi-tenant deployment is claimed.

Authentication assurance resolves typed environment `SecretRef` credentials transiently, validates protected-resource metadata and canonical resource indicators, discovers RFC 8414/OIDC authorization-server metadata, and checks exact issuer binding, HTTPS authorization/token endpoints, PKCE S256, invalid-token status, scope challenges, wrong-audience rejection, query-token placement, and token-passthrough indicators. Redirect/state and registration-flow certificates remain open.
