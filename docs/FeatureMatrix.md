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

## Validation Coverage

| Area | Coverage |
| --- | --- |
| Protocol | JSON-RPC structure, initialization, capability negotiation, error handling, and response-shape validation |
| Tools | `tools/list`, `tools/call`, pagination, annotations, destructive-action signaling, and AI-readiness schema checks |
| Prompts | `prompts/list` and `prompts/get` structure, metadata quality, argument guidance, and prompt-safety checks |
| Resources | `resources/list`, `resources/read`, `resources/templates/list`, URI clarity, MIME guidance, and template ergonomics |
| Security | Authentication behavior, access-aware enforcement, content safety analysis, and attack simulation coverage |
| Performance | Latency, throughput, concurrency handling, and execution stability |
| Reporting | Console summaries plus Markdown, HTML, JSON, SARIF, and client-profile summary JSON from `validate`; HTML, XML, SARIF, and JUnit from `report` |
| CI gating | Advisory, balanced, and strict policy modes with structured policy outcomes |

## Result And Finding Model

| Capability | Behavior |
| --- | --- |
| Stable rule IDs | Findings carry stable identifiers for suppression, baselining, and automation |
| Rule-source labeling | Findings distinguish `spec`, `guideline`, and `heuristic` origins |
| Remediation guidance | Findings include remediation text suitable for reports and CI output |
| Structured evidence | Capability checks and interpretation layers attach explicit evidence instead of only free-form text |
| Canonical result object | `ValidationResult` preserves raw category results, policy outcome, trust assessment, and client compatibility interpretation |
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
