# MCP Server Compliance & Validation Report
**Generated:** 2026-05-01 16:31:51 UTC

## 1. Executive Summary

| Metric | Value |
| :--- | :--- |
| **Server Endpoint** | `https://learn.microsoft.com/api/mcp` |
| **Validation ID** | `73172173-6e1c-44f3-ba96-fb7bcfc4603f` |
| **Overall Status** | ❌ **Failed** |
| **Baseline Verdict** | **Reject** |
| **Protocol Verdict** | **Reject** |
| **Coverage Verdict** | **Trusted** |
| **Compliance Score** | **67.7%** |
| **Evidence Coverage** | **100.0%** |
| **Evidence Confidence** | **High (100.0%)** |
| **Compliance Profile** | `Public (Inferred)` |
| **Duration** | 19.57s |
| **Transport** | HTTP |
| **Session Bootstrap** | ✅ Healthy |
| **Deferred Validation** | No — validation started from a clean bootstrap state. |
| **MCP Protocol Version (Effective)** | `2025-06-18` |
| **Benchmark Trust Level** | 🟠 **L2: Caution — Significant gaps in safety or compliance** |

## 2. Connectivity & Session Bootstrap

This section explains how the validator established initial connectivity and whether validation started from a clean initialize handshake or a deferred advisory state.

| Bootstrap Signal | Value |
| :--- | :--- |
| **Bootstrap State** | ✅ Healthy |
| **Validation Proceeded Under Deferment** | No — validation started from a clean bootstrap state. |
| **Initialize Handshake** | ✅ Initialize handshake succeeded. |
| **Handshake HTTP Status** | `HTTP 200` |
| **Handshake Duration** | 673.8 ms |
| **Negotiated Protocol** | `2025-06-18` |
| **Observed Server Version** | `1.0.0` |
| **Server Profile Resolution** | `Public (Inferred)` |

Validation began from a clean bootstrap with no deferred connectivity risk carried into later categories.

## 3. Priority Findings

These are the highest-signal outcomes from this validation run.
> **Authority legend:** Authority order: Spec blocking and warnings, then Guideline, Heuristic, and Operational signals.

- [Spec] 8 protocol violation(s), led by MCP-PROTO-ERR: JSON-RPC Error Code Violation: Parse Error
- [Guideline] 1 guidance signal(s), led by MCP.GUIDELINE.TOOL.OPENWORLD_HINT_MISSING: Tool 'microsoft_code_sample_search' does not declare annotations.openWorldHint.
- [Heuristic] 2 deterministic AI-readiness advisory signal(s), led by AI.TOOL.SCHEMA.ENUM_COVERAGE_MISSING: Tool 'microsoft_code_sample_search': 1/2 string parameters look like fixed-choice fields but do not declare enum/const choices

## 4. Action Hints

Compact next-step guidance derived from the highest-signal evidence in this run.

- [Spec] Protocol: This specific error code test failed. Ensure the server returns standard JSON-RPC error codes for known error conditions: -32700 (....
- [Guideline] McpGuideline: Tool 'microsoft_code_sample_search' does not declare annotations.openWorldHint.
- Spec: This specific error code test failed. Ensure the server returns standard JSON-RPC error codes for known error conditions: -32700 (parse error....
- Spec: Ensure every JSON-RPC request includes "jsonrpc": "2.0", a string "method" field, and a string or integer "id". The "params" field, if presen....

## 5. Recommended Remediation Order

Fix blocking dependencies in this order so later validation evidence becomes trustworthy instead of merely quieter.

### Priority 1: Bootstrap & Protocol Version

**Impact after fix:** Lifecycle, version, transport, and JSON-RPC fixes make downstream tool, resource, prompt, and auth probes trustworthy.

| Issue | Fix | Component | Evidence |
| :--- | :--- | :--- | :--- |
| MUST requirement failed: MUST: Use standard JSON-RPC error codes — Non-standard error codes | Fix the lifecycle or protocol contract first, then rerun validation before interpreting downstream surface results. | `errors` | [Spec] · `TierCheck` · Critical |
| Content-Type requirements not enforced (Server should reject non-JSON) | Set the Content-Type header to 'application/json' for all JSON-RPC messages and reject requests with incorrect content types. See https://spec.modelcontextprotocol.io/specification/2025-11-25/basic/transports#http | `Transport` | [Spec] · `MCP-HTTP-001` · High |
| Error codes do not comply with JSON-RPC 2.0 standard error codes | Review all error responses to ensure they use standard JSON-RPC 2.0 error codes. Custom error codes must be outside the reserved range (-32000 to -32099 for server errors). See https://spec.modelcontextprotocol.io/specification/2025-11-25/basic/json-rpc#errors | `JSON-RPC Compliance` | [Spec] · `MCP-PROTO-ERR` · High |
| JSON-RPC Error Code Violation: Invalid Params | This specific error code test failed. Ensure the server returns standard JSON-RPC error codes for known error conditions: -32700 (parse error), -32600 (invalid request), -32601 (method not found), -32602 (invalid params), -32603 (internal error). See https://spec.modelcontextprotocol.io/specification/2025-11-25/basic/json-rpc#errors | `JSON-RPC Compliance` | [Spec] · `MCP-PROTO-ERR` · High |
| Request format does not comply with JSON-RPC 2.0 specification | Ensure every JSON-RPC request includes "jsonrpc": "2.0", a string "method" field, and a string or integer "id". The "params" field, if present, must be an object or array. See https://spec.modelcontextprotocol.io/specification/2025-11-25/basic/json-rpc | `JSON-RPC Compliance` | [Spec] · `MCP-PROTO-JSONRPC` · High |

### Priority 3: Advertised Capabilities

**Impact after fix:** Capability-contract fixes make skipped and executed tool, resource, prompt, and task checks align with what the server declared.

| Issue | Fix | Component | Evidence |
| :--- | :--- | :--- | :--- |
| Tool 'microsoft_code_sample_search': 1/2 string parameters look like fixed-choice fields but do not declare enum/const choices | Align advertised capabilities with implemented surfaces so downstream validation probes run only where applicable. | `microsoft_code_sample_search` | [Heuristic] · `AI.TOOL.SCHEMA.ENUM_COVERAGE_MISSING` · Low |
| Synthetic load probe executed against tools/list using 20 requests at concurrency 4. | Interpret this as a generic pressure probe, not a workload-specific SLA benchmark. | `tools/list` | [Guideline] · `MCP.GUIDELINE.PERFORMANCE.SYNTHETIC_PROBE` · Info |

## 6. Deterministic Verdicts

These verdicts are the authoritative gate for pass/fail and policy decisions. Weighted benchmark scores remain descriptive only.
> **Authority legend:** Authority order: Spec blocking and warnings, then Guideline, Heuristic, and Operational signals.

| Lane | Verdict | Meaning |
| :--- | :--- | :--- |
| **Baseline** | **Reject** | Overall deterministic gate across blocking findings and coverage debt. |
| **Protocol** | **Reject** | Protocol correctness and contract integrity. |
| **Coverage** | **Trusted** | Whether enabled validation surfaces produced authoritative evidence. |

Baseline=Reject; Protocol=Reject; Coverage=Trusted; EvidenceConfidence=High (100%); BlockingDecisions=13.

### Blocking Decisions

- **Reject** [TierCheck] `errors`: MUST requirement failed: MUST: Use standard JSON-RPC error codes — Non-standard error codes
  - Evidence: IDs `tier-check:must:errors:must--use-standard-json-rpc-error-codes`; preview: Non-standard error codes
- **Reject** [JSON-RPC Compliance] `JSON-RPC Compliance`: JSON-RPC Error Code Violation: Parse Error
  - Evidence: IDs `protocol-violation:mcp-proto-err:json-rpc-compliance:json-rpc-error-code-violation--parse-error`, `protocol-mcp-proto-err`, `protocol-mcp-proto-err-2`, `protocol-mcp-proto-err-3`, `protocol-mcp-proto-jsonrpc`; preview: JSON-RPC Error Code Violation: Parse Error; spec: https://spec.modelcontextprotocol.io/specification/2025-11-25/basic/json-rpc#errors; remediation: This specific error code test failed. Ensure the server returns standard JSON-RPC error codes for known error conditions: -32700 (parse error), -32600 (invalid request), -32601 (method not found), -32602 (invalid params), -32603 (internal error). See https://spec.modelcontextprotocol.io/specification/2025-11-25/basic/json-rpc#errors
- **Reject** [JSON-RPC Compliance] `JSON-RPC Compliance`: JSON-RPC Error Code Violation: Invalid Request - Missing jsonrpc
  - Evidence: IDs `protocol-violation:mcp-proto-err:json-rpc-compliance:json-rpc-error-code-violation--invalid-request---missing-jsonrpc`, `protocol-mcp-proto-err`, `protocol-mcp-proto-err-2`, `protocol-mcp-proto-err-3`, `protocol-mcp-proto-jsonrpc`; preview: JSON-RPC Error Code Violation: Parse Error; spec: https://spec.modelcontextprotocol.io/specification/2025-11-25/basic/json-rpc#errors; remediation: This specific error code test failed. Ensure the server returns standard JSON-RPC error codes for known error conditions: -32700 (parse error), -32600 (invalid request), -32601 (method not found), -32602 (invalid params), -32603 (internal error). See https://spec.modelcontextprotocol.io/specification/2025-11-25/basic/json-rpc#errors
- **Reject** [JSON-RPC Compliance] `JSON-RPC Compliance`: JSON-RPC Error Code Violation: Invalid Params
  - Evidence: IDs `protocol-violation:mcp-proto-err:json-rpc-compliance:json-rpc-error-code-violation--invalid-params`, `protocol-mcp-proto-err`, `protocol-mcp-proto-err-2`, `protocol-mcp-proto-err-3`, `protocol-mcp-proto-jsonrpc`; preview: JSON-RPC Error Code Violation: Parse Error; spec: https://spec.modelcontextprotocol.io/specification/2025-11-25/basic/json-rpc#errors; remediation: This specific error code test failed. Ensure the server returns standard JSON-RPC error codes for known error conditions: -32700 (parse error), -32600 (invalid request), -32601 (method not found), -32602 (invalid params), -32603 (internal error). See https://spec.modelcontextprotocol.io/specification/2025-11-25/basic/json-rpc#errors
- **Reject** [JSON-RPC Compliance] `JSON-RPC Compliance`: Request format does not comply with JSON-RPC 2.0 specification
  - Evidence: IDs `protocol-violation:mcp-proto-jsonrpc:json-rpc-compliance:request-format-does-not-comply-with-json-rpc-2-0-specification`, `protocol-mcp-proto-err`, `protocol-mcp-proto-err-2`, `protocol-mcp-proto-err-3`, `protocol-mcp-proto-jsonrpc`; preview: JSON-RPC Error Code Violation: Parse Error; spec: https://spec.modelcontextprotocol.io/specification/2025-11-25/basic/json-rpc; remediation: Ensure every JSON-RPC request includes "jsonrpc": "2.0", a string "method" field, and a string or integer "id". The "params" field, if present, must be an object or array. See https://spec.modelcontextprotocol.io/specification/2025-11-25/basic/json-rpc
- **Reject** [JSON-RPC Compliance] `JSON-RPC Compliance`: Error codes do not comply with JSON-RPC 2.0 standard error codes
  - Evidence: IDs `protocol-violation:mcp-proto-err:json-rpc-compliance:error-codes-do-not-comply-with-json-rpc-2-0-standard-error-codes`, `protocol-mcp-proto-err`, `protocol-mcp-proto-err-2`, `protocol-mcp-proto-err-3`, `protocol-mcp-proto-jsonrpc`; preview: JSON-RPC Error Code Violation: Parse Error; spec: https://spec.modelcontextprotocol.io/specification/2025-11-25/basic/json-rpc#errors; remediation: Review all error responses to ensure they use standard JSON-RPC 2.0 error codes. Custom error codes must be outside the reserved range (-32000 to -32099 for server errors). See https://spec.modelcontextprotocol.io/specification/2025-11-25/basic/json-rpc#errors
- **Reject** [Protocol] `invalid-request`: Error-handling scenario 'Graceful Degradation On Invalid Request' was rejected at the HTTP front door instead of returning JSON-RPC error code -32600.
  - Evidence: IDs `structured-finding:mcp-error-handling-non-standard-error-response:invalid-request:error-handling-scenario--graceful-degradation-on-invalid-request--was-rejected-at-the-http-front-door-instead-of-returning-json-rpc-error-code--32600`, `error-graceful-degradation-on-invalid-request`; preview: HTTP 500; Invalid or missing jsonrpc version; spec: https://spec.modelcontextprotocol.io/specification/2025-11-25/basic/json-rpc#errors; remediation: This specific error code test failed. Ensure the server returns standard JSON-RPC error codes for known error conditions: -32700 (parse error), -32600 (invalid request), -32601 (method not found), -32602 (invalid params), -32603 (internal error). See https://spec.modelcontextprotocol.io/specification/2025-11-25/basic/json-rpc#errors
- **Reject** [Protocol] `malformed-json`: Error-handling scenario 'Malformed JSON Payload' was rejected at the HTTP front door instead of returning JSON-RPC error code -32700.
  - Evidence: IDs `structured-finding:mcp-error-handling-non-standard-error-response:malformed-json:error-handling-scenario--malformed-json-payload--was-rejected-at-the-http-front-door-instead-of-returning-json-rpc-error-code--32700`, `error-malformed-json-payload`; preview: HTTP 500; 'i' is an invalid start of a property name. Expected a '"'. Path: $ | LineNumber: 0 | BytePositionInLine: 2.; spec: https://spec.modelcontextprotocol.io/specification/2025-11-25/basic/json-rpc#errors; remediation: This specific error code test failed. Ensure the server returns standard JSON-RPC error codes for known error conditions: -32700 (parse error), -32600 (invalid request), -32601 (method not found), -32602 (invalid params), -32603 (internal error). See https://spec.modelcontextprotocol.io/specification/2025-11-25/basic/json-rpc#errors

## 7. Benchmark Trust Profile

Multi-dimensional benchmarking view of server trustworthiness for AI agent consumption.
This section is descriptive only. Release gating and pass/fail status are driven by the deterministic verdicts above, not by weighted trust averages.

| Dimension | Score | What It Measures |
| :--- | :---: | :--- |
| **Protocol Compliance** | 49% | MCP spec adherence, JSON-RPC compliance, response structures |
| **Security Posture** | 100% | Auth compliance, injection resistance, attack surface |
| **AI Safety** | 94% | Schema quality, destructive tool detection, exfiltration risk, prompt injection surface |
| **Operational Readiness** | 100% | Latency, throughput, error rate, stability |

### Summary

- Destructive tools: **0**
- Data exfiltration risk: **0**
- Prompt injection surface: **0**

### MCP Spec Compliance (RFC 2119 Tiers)

| Tier | Passed | Failed | Total | Impact |
| :--- | :---: | :---: | :---: | :--- |
| **MUST** | 8 | 1 | 9 | ❌ Non-compliant (trust capped at L2) |
| **SHOULD** | 5 | 0 | 5 | ✅ All expected behaviors present |
| **MAY** | 3 | 3 | 6 | ℹ️ Informational (no score impact) |

#### ❌ MUST Failures (Compliance Blockers)

- **MUST: Use standard JSON-RPC error codes** (errors) — Non-standard error codes

#### ℹ️ Optional Features (MAY)

- ✅ MAY: Server supports logging/setLevel
- ➖ MAY: Server supports sampling/createMessage
- ➖ MAY: Server supports roots/list
- ➖ MAY: Server supports completion/complete
- ✅ MAY: Server supports resources/templates/list
- ✅ MAY: Tools include 'annotations' (readOnlyHint, destructiveHint)

## 8. Client Profile Compatibility

Documented host-side compatibility assessments derived from the neutral validation evidence collected during this run.

| Client Profile | Status | Requirements | Documentation |
| :--- | :--- | :--- | :--- |
| **Claude Code** | ✅ Compatible | 4 passed / 0 warnings / 0 failed | <https://code.claude.com/docs/en/mcp> |
| **VS Code Copilot Agent** | ✅ Compatible | 2 passed / 0 warnings / 0 failed | <https://code.visualstudio.com/docs/copilot/chat/mcp-servers> |
| **GitHub Copilot CLI** | ✅ Compatible | 2 passed / 0 warnings / 0 failed | <https://docs.github.com/en/copilot/how-tos/copilot-cli/customize-copilot/add-mcp-servers> |
| **GitHub Copilot Cloud Agent** | ✅ Compatible | 4 passed / 0 warnings / 0 failed | <https://docs.github.com/en/copilot/how-tos/use-copilot-agents/cloud-agent/extend-cloud-agent-with-mcp> |
| **Visual Studio Copilot** | ✅ Compatible | 3 passed / 0 warnings / 0 failed | <https://learn.microsoft.com/en-us/visualstudio/ide/mcp-servers?view=vs-2022> |

### Claude Code

**Status:** ✅ Compatible

All applicable compatibility checks passed (4 satisfied).

- No client-specific compatibility gaps were detected.

### VS Code Copilot Agent

**Status:** ✅ Compatible

All applicable compatibility checks passed (2 satisfied).

- No client-specific compatibility gaps were detected.

### GitHub Copilot CLI

**Status:** ✅ Compatible

All applicable compatibility checks passed (2 satisfied).

- No client-specific compatibility gaps were detected.

### GitHub Copilot Cloud Agent

**Status:** ✅ Compatible

All applicable compatibility checks passed (4 satisfied).

- No client-specific compatibility gaps were detected.

### Visual Studio Copilot

**Status:** ✅ Compatible

All applicable compatibility checks passed (3 satisfied).

- No client-specific compatibility gaps were detected.

## 9. Validation Envelope

This section exposes the structured validation envelope used for layered reporting, coverage tracking, and pack provenance.

### Assessment Layers

| Layer | Status | Findings | Summary |
| :--- | :--- | :---: | :--- |
| `protocol-core` | ✅ Passed | 2 | declared capabilities: logging, prompts, prompts.listChanged, resources, resources.listChanged, tools, tools.listChanged \| roots/list: not advertised \| logging/setLevel: supported \| sampling/createMessage: not advertised \| completion/complete: not advertised |
| `tool-surface` | ✅ Passed | 5 | - |
| `resource-surface` | ✅ Passed | 0 | - |
| `prompt-surface` | ✅ Passed | 0 | - |
| `security-boundaries` | ✅ Passed | 0 | - |
| `performance` | ✅ Passed | 1 | - |
| `error-handling` | ❌ Failed | 2 | Validated 5 error scenario(s); 3 handled correctly. |
| `client-profiles` | ✅ Passed | 0 | Evaluated 5 profile(s); 0 warning profile(s); 0 incompatible profile(s). |

### Scenario Outcomes

| Scenario | Status | Findings | Summary |
| :--- | :--- | :---: | :--- |
| `bootstrap-initialize-handshake` | ✅ Passed | 0 | Bootstrap health checks completed successfully. |
| `protocol-compliance-review` | ✅ Passed | 2 | declared capabilities: logging, prompts, prompts.listChanged, resources, resources.listChanged, tools, tools.listChanged \| roots/list: not advertised \| logging/setLevel: supported \| sampling/createMessage: not advertised \| completion/complete: not advertised |
| `tool-catalog-smoke` | ✅ Passed | 5 | Tool validation completed. |
| `resource-catalog-smoke` | ✅ Passed | 0 | No resources were discovered during validation. |
| `prompt-catalog-smoke` | ✅ Passed | 0 | No prompts were discovered during validation. |
| `security-authentication-challenge` | ➖ Skipped | 0 | Evaluated 61 authentication challenge scenario(s). |
| `security-attack-simulations` | ➖ Skipped | 0 | Evaluated 7 attack simulation(s). |
| `error-handling-matrix` | ❌ Failed | 2 | Validated 5 error scenario(s); 3 handled correctly. |

### Evidence Confidence

Score reflects observed behavior. Evidence coverage and confidence describe how much of the enabled surface was actually assessed.

| Metric | Value |
| :--- | :---: |
| Evidence Coverage | 100.0% |
| Evidence Confidence | 100.0% |
| Confidence Level | High |
| Auth Required | 0 |
| Inconclusive | 0 |
| Blocked/Unavailable | 0 |

### Evidence Confidence By Layer

| Layer | Coverage | Confidence | Covered | Auth Required | Inconclusive | Skipped | Blocked/Unavailable |
| :--- | :---: | :---: | ---: | ---: | ---: | ---: | ---: |
| `bootstrap` | 100.0% | High (100.0%) | 1 | 0 | 0 | 0 | 0 |
| `error-handling` | 100.0% | High (100.0%) | 2 | 0 | 0 | 0 | 0 |
| `performance` | 100.0% | High (100.0%) | 1 | 0 | 0 | 0 | 0 |
| `prompt-surface` | 100.0% | High (100.0%) | 2 | 0 | 0 | 0 | 0 |
| `protocol-core` | 100.0% | High (100.0%) | 2 | 0 | 0 | 0 | 0 |
| `resource-surface` | 100.0% | High (100.0%) | 2 | 0 | 0 | 0 | 0 |
| `security-boundaries` | 100.0% | High (100.0%) | 3 | 0 | 0 | 0 | 0 |
| `tool-surface` | 100.0% | High (100.0%) | 2 | 0 | 0 | 0 | 0 |

### Coverage Declarations

| Layer | Scope | Status | Blocker | Confidence | Reason |
| :--- | :--- | :--- | :--- | :---: | :--- |
| `protocol-core` | `json-rpc` | `Covered` | `None` | High | - |
| `tool-surface` | `tools/list` | `Covered` | `None` | High | - |
| `resource-surface` | `resources/list` | `Covered` | `None` | High | - |
| `prompt-surface` | `prompts/list` | `Covered` | `None` | High | - |
| `security-boundaries` | `security-assessment` | `Covered` | `None` | High | - |
| `performance` | `load-testing` | `Covered` | `None` | High | - |
| `error-handling` | `error-handling` | `Covered` | `None` | High | - |
| `bootstrap` | `bootstrap-initialize-handshake` | `Covered` | `None` | None | - |
| `protocol-core` | `protocol-compliance-review` | `Covered` | `None` | None | - |
| `tool-surface` | `tool-catalog-smoke` | `Covered` | `None` | None | - |
| `resource-surface` | `resource-catalog-smoke` | `Covered` | `None` | None | - |
| `prompt-surface` | `prompt-catalog-smoke` | `Covered` | `None` | None | - |
| `security-boundaries` | `security-authentication-challenge` | `Covered` | `None` | None | - |
| `security-boundaries` | `security-attack-simulations` | `Covered` | `None` | None | - |
| `error-handling` | `error-handling-matrix` | `Covered` | `None` | None | - |
| `client-profiles` | `claude-code,vscode-copilot-agent,github-copilot-cli,github-copilot-cloud-agent,visual-studio-copilot` | `Covered` | `None` | None | - |

### Applied Validation Packs

- **Embedded MCP Protocol Features** — `protocol-features/mcp-embedded` · revision `2026-04` · ProtocolFeatures · Stable
- **Built-in Observed Surface Scenario Pack** — `scenario-pack/observed-surface` · revision `2026-04` · ScenarioPack · Stable
- **Built-in Protocol Rule Pack** — `rule-pack/protocol-core` · revision `2026-04` · RulePack · Stable
- **Built-in Client Profile Pack** — `client-profile-pack/built-in` · revision `2026-04` · ClientProfilePack · Stable

### Recorded Observations

| Observation | Layer | Component | Kind | Preview |
| :--- | :--- | :--- | :--- | :--- |
| `bootstrap-initialize` | `bootstrap` | `initialize` | `bootstrap-health` | Healthy |
| `protocol-mcp-proto-err` | `protocol-core` | `JSON-RPC Compliance` | `protocol-violation` | JSON-RPC Error Code Violation: Parse Error |
| `protocol-mcp-proto-err-2` | `protocol-core` | `JSON-RPC Compliance` | `protocol-violation` | JSON-RPC Error Code Violation: Invalid Request - Missing jsonrpc |
| `protocol-mcp-proto-err-3` | `protocol-core` | `JSON-RPC Compliance` | `protocol-violation` | JSON-RPC Error Code Violation: Invalid Params |
| `protocol-mcp-proto-jsonrpc` | `protocol-core` | `JSON-RPC Compliance` | `protocol-violation` | Request format does not comply with JSON-RPC 2.0 specification |
| `protocol-mcp-proto-err-4` | `protocol-core` | `JSON-RPC Compliance` | `protocol-violation` | Error codes do not comply with JSON-RPC 2.0 standard error codes |
| `protocol-mcp-http-001` | `protocol-core` | `Transport` | `protocol-violation` | Content-Type requirements not enforced (Server should reject non-JSON) |
| `protocol-mcp-http-004` | `protocol-core` | `Transport` | `protocol-violation` | Streamable HTTP transport violation (invalid-protocol-version-400): Requests with an unsupported MCP-Protocol-Version header must be rejected before normal JSON-RPC handling. Expected: HTTP 400 Bad Request. Observed: HTTP 200; Content-Type=text/event-stream; body length 85 |
| `protocol-mcp-http-005` | `protocol-core` | `Transport` | `protocol-violation` | Streamable HTTP transport violation (invalid-origin-403): HTTP transports must validate Origin and reject invalid origins with HTTP 403 Forbidden. Expected: HTTP 403 Forbidden. Observed: HTTP 200; Content-Type=text/event-stream; body length 84 |
| `tool-tools-list-schema-compliance` | `tool-surface` | `tools/list (Schema Compliance)` | `tool-result` | ⚠️ Schema validation warning: tools/list schema could not be fully processed |
| `tool-microsoft-docs-search` | `tool-surface` | `microsoft_docs_search` | `tool-result` | Tool 'microsoft_docs_search' does not declare annotations.openWorldHint. |
| `tool-microsoft-code-sample-search` | `tool-surface` | `microsoft_code_sample_search` | `tool-result` | Tool 'microsoft_code_sample_search' does not declare annotations.openWorldHint. |
| `tool-microsoft-docs-fetch` | `tool-surface` | `microsoft_docs_fetch` | `tool-result` | Tool 'microsoft_docs_fetch' does not declare annotations.openWorldHint. |
| `auth-no-auth-initialize` | `security-boundaries` | `initialize` | `authentication-scenario` | ℹ️ INFO (Public profile): ⚠️ COMPATIBLE: Discovery metadata was exposed without authentication. |
| `auth-no-auth-logging-setlevel` | `security-boundaries` | `logging/setLevel` | `authentication-scenario` | ℹ️ INFO (Public profile): ❌ INSECURE: Sensitive operation succeeded without valid authentication. |
| `auth-malformed-token-logging-setlevel` | `security-boundaries` | `logging/setLevel` | `authentication-scenario` | ℹ️ INFO (Public profile): ❌ INSECURE: Sensitive operation succeeded without valid authentication. |
| `auth-invalid-token-logging-setlevel` | `security-boundaries` | `logging/setLevel` | `authentication-scenario` | ℹ️ INFO (Public profile): ❌ INSECURE: Sensitive operation succeeded without valid authentication. |
| `auth-token-expired-logging-setlevel` | `security-boundaries` | `logging/setLevel` | `authentication-scenario` | ℹ️ INFO (Public profile): ❌ INSECURE: Sensitive operation succeeded without valid authentication. |
| `auth-invalid-scope-logging-setlevel` | `security-boundaries` | `logging/setLevel` | `authentication-scenario` | ℹ️ INFO (Public profile): ❌ INSECURE: Sensitive operation succeeded without valid authentication. |
| `auth-insufficient-permissions-logging-setlevel` | `security-boundaries` | `logging/setLevel` | `authentication-scenario` | ℹ️ INFO (Public profile): ❌ INSECURE: Sensitive operation succeeded without valid authentication. |
| `auth-revoked-token-logging-setlevel` | `security-boundaries` | `logging/setLevel` | `authentication-scenario` | ℹ️ INFO (Public profile): ❌ INSECURE: Sensitive operation succeeded without valid authentication. |
| `auth-wrong-audience-rfc-8707-logging-setlevel` | `security-boundaries` | `logging/setLevel` | `authentication-scenario` | ℹ️ INFO (Public profile): ❌ INSECURE: Sensitive operation succeeded without valid authentication. |
| `auth-query-token-logging-setlevel` | `security-boundaries` | `logging/setLevel` | `authentication-scenario` | ℹ️ INFO (Public profile): ❌ INSECURE: Sensitive operation succeeded without valid authentication. |
| `auth-no-auth-prompts-list` | `security-boundaries` | `prompts/list` | `authentication-scenario` | ℹ️ INFO (Public profile): ⚠️ COMPATIBLE: Discovery metadata was exposed without authentication. |
| `auth-malformed-token-prompts-list` | `security-boundaries` | `prompts/list` | `authentication-scenario` | ℹ️ INFO (Public profile): ⚠️ COMPATIBLE: Discovery metadata was exposed without authentication. |
| `auth-invalid-token-prompts-list` | `security-boundaries` | `prompts/list` | `authentication-scenario` | ℹ️ INFO (Public profile): ⚠️ COMPATIBLE: Discovery metadata was exposed without authentication. |
| `auth-token-expired-prompts-list` | `security-boundaries` | `prompts/list` | `authentication-scenario` | ℹ️ INFO (Public profile): ⚠️ COMPATIBLE: Discovery metadata was exposed without authentication. |
| `auth-invalid-scope-prompts-list` | `security-boundaries` | `prompts/list` | `authentication-scenario` | ℹ️ INFO (Public profile): ⚠️ COMPATIBLE: Discovery metadata was exposed without authentication. |
| `auth-insufficient-permissions-prompts-list` | `security-boundaries` | `prompts/list` | `authentication-scenario` | ℹ️ INFO (Public profile): ⚠️ COMPATIBLE: Discovery metadata was exposed without authentication. |
| `auth-revoked-token-prompts-list` | `security-boundaries` | `prompts/list` | `authentication-scenario` | ℹ️ INFO (Public profile): ⚠️ COMPATIBLE: Discovery metadata was exposed without authentication. |
| `auth-wrong-audience-rfc-8707-prompts-list` | `security-boundaries` | `prompts/list` | `authentication-scenario` | ℹ️ INFO (Public profile): ⚠️ COMPATIBLE: Discovery metadata was exposed without authentication. |
| `auth-no-auth-prompts-get` | `security-boundaries` | `prompts/get` | `authentication-scenario` | ℹ️ INFO (Public profile): ⚠️ COMPATIBLE: Authentication appears to be enforced at the application layer instead of via an HTTP challenge. |
| `auth-malformed-token-prompts-get` | `security-boundaries` | `prompts/get` | `authentication-scenario` | ℹ️ INFO (Public profile): ⚠️ COMPATIBLE: Authentication appears to be enforced at the application layer instead of via an HTTP challenge. |
| `auth-invalid-token-prompts-get` | `security-boundaries` | `prompts/get` | `authentication-scenario` | ℹ️ INFO (Public profile): ⚠️ COMPATIBLE: Authentication appears to be enforced at the application layer instead of via an HTTP challenge. |
| `auth-token-expired-prompts-get` | `security-boundaries` | `prompts/get` | `authentication-scenario` | ℹ️ INFO (Public profile): ⚠️ COMPATIBLE: Authentication appears to be enforced at the application layer instead of via an HTTP challenge. |
| `auth-invalid-scope-prompts-get` | `security-boundaries` | `prompts/get` | `authentication-scenario` | ℹ️ INFO (Public profile): ⚠️ COMPATIBLE: Authentication appears to be enforced at the application layer instead of via an HTTP challenge. |
| `auth-insufficient-permissions-prompts-get` | `security-boundaries` | `prompts/get` | `authentication-scenario` | ℹ️ INFO (Public profile): ⚠️ COMPATIBLE: Authentication appears to be enforced at the application layer instead of via an HTTP challenge. |
| `auth-revoked-token-prompts-get` | `security-boundaries` | `prompts/get` | `authentication-scenario` | ℹ️ INFO (Public profile): ⚠️ COMPATIBLE: Authentication appears to be enforced at the application layer instead of via an HTTP challenge. |
| `auth-wrong-audience-rfc-8707-prompts-get` | `security-boundaries` | `prompts/get` | `authentication-scenario` | ℹ️ INFO (Public profile): ⚠️ COMPATIBLE: Authentication appears to be enforced at the application layer instead of via an HTTP challenge. |
| `auth-query-token-prompts-get` | `security-boundaries` | `prompts/get` | `authentication-scenario` | ℹ️ INFO (Public profile): ⚠️ COMPATIBLE: Authentication appears to be enforced at the application layer instead of via an HTTP challenge. |
| `auth-no-auth-resources-list` | `security-boundaries` | `resources/list` | `authentication-scenario` | ℹ️ INFO (Public profile): ⚠️ COMPATIBLE: Discovery metadata was exposed without authentication. |
| `auth-malformed-token-resources-list` | `security-boundaries` | `resources/list` | `authentication-scenario` | ℹ️ INFO (Public profile): ⚠️ COMPATIBLE: Discovery metadata was exposed without authentication. |
| `auth-invalid-token-resources-list` | `security-boundaries` | `resources/list` | `authentication-scenario` | ℹ️ INFO (Public profile): ⚠️ COMPATIBLE: Discovery metadata was exposed without authentication. |
| `auth-token-expired-resources-list` | `security-boundaries` | `resources/list` | `authentication-scenario` | ℹ️ INFO (Public profile): ⚠️ COMPATIBLE: Discovery metadata was exposed without authentication. |
| `auth-invalid-scope-resources-list` | `security-boundaries` | `resources/list` | `authentication-scenario` | ℹ️ INFO (Public profile): ⚠️ COMPATIBLE: Discovery metadata was exposed without authentication. |
| `auth-insufficient-permissions-resources-list` | `security-boundaries` | `resources/list` | `authentication-scenario` | ℹ️ INFO (Public profile): ⚠️ COMPATIBLE: Discovery metadata was exposed without authentication. |
| `auth-revoked-token-resources-list` | `security-boundaries` | `resources/list` | `authentication-scenario` | ℹ️ INFO (Public profile): ⚠️ COMPATIBLE: Discovery metadata was exposed without authentication. |
| `auth-wrong-audience-rfc-8707-resources-list` | `security-boundaries` | `resources/list` | `authentication-scenario` | ℹ️ INFO (Public profile): ⚠️ COMPATIBLE: Discovery metadata was exposed without authentication. |
| `auth-no-auth-resources-read` | `security-boundaries` | `resources/read` | `authentication-scenario` | ℹ️ INFO (Public profile): ⚠️ COMPATIBLE: Authentication appears to be enforced at the application layer instead of via an HTTP challenge. |
| `auth-malformed-token-resources-read` | `security-boundaries` | `resources/read` | `authentication-scenario` | ℹ️ INFO (Public profile): ⚠️ COMPATIBLE: Authentication appears to be enforced at the application layer instead of via an HTTP challenge. |
| `auth-invalid-token-resources-read` | `security-boundaries` | `resources/read` | `authentication-scenario` | ℹ️ INFO (Public profile): ⚠️ COMPATIBLE: Authentication appears to be enforced at the application layer instead of via an HTTP challenge. |
| `auth-token-expired-resources-read` | `security-boundaries` | `resources/read` | `authentication-scenario` | ℹ️ INFO (Public profile): ⚠️ COMPATIBLE: Authentication appears to be enforced at the application layer instead of via an HTTP challenge. |
| `auth-invalid-scope-resources-read` | `security-boundaries` | `resources/read` | `authentication-scenario` | ℹ️ INFO (Public profile): ⚠️ COMPATIBLE: Authentication appears to be enforced at the application layer instead of via an HTTP challenge. |
| `auth-insufficient-permissions-resources-read` | `security-boundaries` | `resources/read` | `authentication-scenario` | ℹ️ INFO (Public profile): ⚠️ COMPATIBLE: Authentication appears to be enforced at the application layer instead of via an HTTP challenge. |
| `auth-revoked-token-resources-read` | `security-boundaries` | `resources/read` | `authentication-scenario` | ℹ️ INFO (Public profile): ⚠️ COMPATIBLE: Authentication appears to be enforced at the application layer instead of via an HTTP challenge. |
| `auth-wrong-audience-rfc-8707-resources-read` | `security-boundaries` | `resources/read` | `authentication-scenario` | ℹ️ INFO (Public profile): ⚠️ COMPATIBLE: Authentication appears to be enforced at the application layer instead of via an HTTP challenge. |
| `auth-query-token-resources-read` | `security-boundaries` | `resources/read` | `authentication-scenario` | ℹ️ INFO (Public profile): ⚠️ COMPATIBLE: Authentication appears to be enforced at the application layer instead of via an HTTP challenge. |
| `auth-no-auth-tools-list` | `security-boundaries` | `tools/list` | `authentication-scenario` | ℹ️ INFO (Public profile): ⚠️ COMPATIBLE: Discovery metadata was exposed without authentication. |
| `auth-malformed-token-tools-list` | `security-boundaries` | `tools/list` | `authentication-scenario` | ℹ️ INFO (Public profile): ⚠️ COMPATIBLE: Discovery metadata was exposed without authentication. |
| `auth-invalid-token-tools-list` | `security-boundaries` | `tools/list` | `authentication-scenario` | ℹ️ INFO (Public profile): ⚠️ COMPATIBLE: Discovery metadata was exposed without authentication. |
| `auth-token-expired-tools-list` | `security-boundaries` | `tools/list` | `authentication-scenario` | ℹ️ INFO (Public profile): ⚠️ COMPATIBLE: Discovery metadata was exposed without authentication. |
| `auth-invalid-scope-tools-list` | `security-boundaries` | `tools/list` | `authentication-scenario` | ℹ️ INFO (Public profile): ⚠️ COMPATIBLE: Discovery metadata was exposed without authentication. |
| `auth-insufficient-permissions-tools-list` | `security-boundaries` | `tools/list` | `authentication-scenario` | ℹ️ INFO (Public profile): ⚠️ COMPATIBLE: Discovery metadata was exposed without authentication. |
| `auth-revoked-token-tools-list` | `security-boundaries` | `tools/list` | `authentication-scenario` | ℹ️ INFO (Public profile): ⚠️ COMPATIBLE: Discovery metadata was exposed without authentication. |
| `auth-wrong-audience-rfc-8707-tools-list` | `security-boundaries` | `tools/list` | `authentication-scenario` | ℹ️ INFO (Public profile): ⚠️ COMPATIBLE: Discovery metadata was exposed without authentication. |
| `auth-no-auth-tools-call` | `security-boundaries` | `tools/call` | `authentication-scenario` | ℹ️ INFO (Public profile): ⚠️ COMPATIBLE: Authentication appears to be enforced at the application layer instead of via an HTTP challenge. |
| `auth-malformed-token-tools-call` | `security-boundaries` | `tools/call` | `authentication-scenario` | ℹ️ INFO (Public profile): ⚠️ COMPATIBLE: Authentication appears to be enforced at the application layer instead of via an HTTP challenge. |
| `auth-invalid-token-tools-call` | `security-boundaries` | `tools/call` | `authentication-scenario` | ℹ️ INFO (Public profile): ⚠️ COMPATIBLE: Authentication appears to be enforced at the application layer instead of via an HTTP challenge. |
| `auth-token-expired-tools-call` | `security-boundaries` | `tools/call` | `authentication-scenario` | ℹ️ INFO (Public profile): ⚠️ COMPATIBLE: Authentication appears to be enforced at the application layer instead of via an HTTP challenge. |
| `auth-invalid-scope-tools-call` | `security-boundaries` | `tools/call` | `authentication-scenario` | ℹ️ INFO (Public profile): ⚠️ COMPATIBLE: Authentication appears to be enforced at the application layer instead of via an HTTP challenge. |
| `auth-insufficient-permissions-tools-call` | `security-boundaries` | `tools/call` | `authentication-scenario` | ℹ️ INFO (Public profile): ⚠️ COMPATIBLE: Authentication appears to be enforced at the application layer instead of via an HTTP challenge. |
| `auth-revoked-token-tools-call` | `security-boundaries` | `tools/call` | `authentication-scenario` | ℹ️ INFO (Public profile): ⚠️ COMPATIBLE: Authentication appears to be enforced at the application layer instead of via an HTTP challenge. |
| `auth-wrong-audience-rfc-8707-tools-call` | `security-boundaries` | `tools/call` | `authentication-scenario` | ℹ️ INFO (Public profile): ⚠️ COMPATIBLE: Authentication appears to be enforced at the application layer instead of via an HTTP challenge. |
| `auth-query-token-tools-call` | `security-boundaries` | `tools/call` | `authentication-scenario` | ℹ️ INFO (Public profile): ⚠️ COMPATIBLE: Authentication appears to be enforced at the application layer instead of via an HTTP challenge. |
| `attack-inj-001-input-validation` | `security-boundaries` | `INJ-001 (Input Validation)` | `attack-simulation` | <HTML><HEAD> <TITLE>Access Denied</TITLE> </HEAD><BODY> <H1>Access Denied</H1>   You don't have permission to access "http&#58;&#47;&#47;learn&#46;microsoft&#46;com&#47;api&#47;mcp" on this server.<P> Reference&#32;&#35;18&#46;49d02e17&#46;1777653101&#46;1c9116d2 <P>https&#58;&#47;&#47;errors&#46;edgesuite&#46;net&#47;18&#46;49d02e17&#46;1777653101&#46;1c9116d2</P> </BODY> </HTML>  |
| `attack-inj-002-input-validation` | `security-boundaries` | `INJ-002 (Input Validation)` | `attack-simulation` | <HTML><HEAD> <TITLE>Access Denied</TITLE> </HEAD><BODY> <H1>Access Denied</H1>   You don't have permission to access "http&#58;&#47;&#47;learn&#46;microsoft&#46;com&#47;api&#47;mcp" on this server.<P> Reference&#32;&#35;18&#46;49d02e17&#46;1777653101&#46;1c90c1ea <P>https&#58;&#47;&#47;errors&#46;edgesuite&#46;net&#47;18&#46;49d02e17&#46;1777653101&#46;1c90c1ea</P> </BODY> </HTML>  |
| `attack-inj-003-input-validation` | `security-boundaries` | `INJ-003 (Input Validation)` | `attack-simulation` | <HTML><HEAD> <TITLE>Access Denied</TITLE> </HEAD><BODY> <H1>Access Denied</H1>   You don't have permission to access "http&#58;&#47;&#47;learn&#46;microsoft&#46;com&#47;api&#47;mcp" on this server.<P> Reference&#32;&#35;18&#46;49d02e17&#46;1777653101&#46;1c911899 <P>https&#58;&#47;&#47;errors&#46;edgesuite&#46;net&#47;18&#46;49d02e17&#46;1777653101&#46;1c911899</P> </BODY> </HTML>  |
| `attack-mcp-sec-001` | `security-boundaries` | `MCP-SEC-001` | `attack-simulation` | Server handled malformed requests gracefully with standard errors. |
| `attack-mcp-sec-002` | `security-boundaries` | `MCP-SEC-002` | `attack-simulation` | Skipped: Metadata enumeration requires an advertised concrete resource URI; no like-for-like resource surface was available. |
| `attack-mcp-sec-003` | `security-boundaries` | `MCP-SEC-003` | `attack-simulation` | Server correctly rejected invalid schema. |
| `attack-mcp-ai-001` | `security-boundaries` | `MCP-AI-001` | `attack-simulation` | Error clarity: 55/100 (Fair). Error messages partially helpful for AI. |
| `error-invalid-method-call` | `error-handling` | `invalid-method` | `error-scenario` | HTTP 200; JSON-RPC error -32601: Method 'nonexistent_method_12345' is not available. |
| `error-malformed-json-payload` | `error-handling` | `malformed-json` | `error-scenario` | HTTP 500; 'i' is an invalid start of a property name. Expected a '"'. Path: $ \| LineNumber: 0 \| BytePositionInLine: 2. |
| `error-graceful-degradation-on-invalid-request` | `error-handling` | `invalid-request` | `error-scenario` | HTTP 500; Invalid or missing jsonrpc version |
| `error-timeout-handling-recovery` | `error-handling` | `timeout-handling` | `error-scenario` | Validator cancelled the HTTP request after the induced timeout window elapsed. |
| `error-connection-interruption-recovery` | `error-handling` | `connection-interruption` | `error-scenario` | Validator aborted the HTTP request body mid-stream to simulate a connection interruption. |

## 10. Capability Snapshot

| Probe | Discovered | HTTP Status | Duration | Result |
| :--- | :---: | :---: | :---: | :--- |
| Tools/list | 3 | HTTP 200 | 85.1 ms | ✅ Listed<br/>Call ✅ |
| Resources/list | 0 | HTTP 200 | 77.6 ms | ✅ Listed |
| Prompts/list | 0 | HTTP 200 | 80.0 ms | ✅ Listed |

- **First Tool Probed:** `microsoft_docs_search`

## 11. Scoring Methodology

These notes explain how the overall score and blocking decision were calibrated for this run.

- INFO: Authentication challenge observations are informational for the declared public profile.
- SPEC: JSON-RPC 2.0 format deviations detected. Score reduced by 15%.
- Score is below the preferred target, but no blocking failure was observed in this run.

## 12. Compliance Matrix

| Category | Status | Score | Issues |
| :--- | :---: | :---: | :---: |
| Protocol Compliance | ✅ Passed | 49.0% | **8** |
| Security Assessment | ✅ Passed | 100.0% | - |
| Tool Validation | ✅ Passed | 100.0% | - |
| Resource Capabilities | ✅ Passed | 100.0% | - |
| Prompt Capabilities | ✅ Passed | 100.0% | - |
| Performance | ✅ Passed | 100.0% | - |

## 13. Performance Calibration

Performance is assessed using a synthetic load probe rather than a workload-specific SLA benchmark.
For public or remote SaaS endpoints, partial failures under synthetic pressure are reported as advisory when they look like capacity limits or edge protections rather than protocol instability.

- Synthetic load probe executed against tools/list using 20 requests at concurrency 4.

## 14. Security Assessment

**Security Score:** 100.0%

### Authentication Analysis
| Scenario | Method | Expected | Actual | HTTP | Analysis | Status |
| :--- | :--- | :--- | :--- | :---: | :--- | :---: |
| No Auth - initialize | `initialize` | Informational (public profile) | Discovery metadata returned | 200 | ℹ️ INFO (Public profile): ⚠️ COMPATIBLE: Discovery metadata was exposed without authentication. | ✅ |
| No Auth - logging/setLevel | `logging/setLevel` | Informational (public profile) | Sensitive operation succeeded | 200 | ℹ️ INFO (Public profile): ❌ INSECURE: Sensitive operation succeeded without valid authentication. | ✅ |
| Malformed Token - logging/setLevel | `logging/setLevel` | Informational (public profile) | Sensitive operation succeeded | 200 | ℹ️ INFO (Public profile): ❌ INSECURE: Sensitive operation succeeded without valid authentication. | ✅ |
| Invalid Token - logging/setLevel | `logging/setLevel` | Informational (public profile) | Sensitive operation succeeded | 200 | ℹ️ INFO (Public profile): ❌ INSECURE: Sensitive operation succeeded without valid authentication. | ✅ |
| Token Expired - logging/setLevel | `logging/setLevel` | Informational (public profile) | Sensitive operation succeeded | 200 | ℹ️ INFO (Public profile): ❌ INSECURE: Sensitive operation succeeded without valid authentication. | ✅ |
| Invalid Scope - logging/setLevel | `logging/setLevel` | Informational (public profile) | Sensitive operation succeeded | 200 | ℹ️ INFO (Public profile): ❌ INSECURE: Sensitive operation succeeded without valid authentication. | ✅ |
| Insufficient Permissions - logging/setLevel | `logging/setLevel` | Informational (public profile) | Sensitive operation succeeded | 200 | ℹ️ INFO (Public profile): ❌ INSECURE: Sensitive operation succeeded without valid authentication. | ✅ |
| Revoked Token - logging/setLevel | `logging/setLevel` | Informational (public profile) | Sensitive operation succeeded | 200 | ℹ️ INFO (Public profile): ❌ INSECURE: Sensitive operation succeeded without valid authentication. | ✅ |
| Wrong Audience (RFC 8707) - logging/setLevel | `logging/setLevel` | Informational (public profile) | Sensitive operation succeeded | 200 | ℹ️ INFO (Public profile): ❌ INSECURE: Sensitive operation succeeded without valid authentication. | ✅ |
| Query Token - logging/setLevel | `logging/setLevel` | Informational (public profile) | Sensitive operation succeeded | 200 | ℹ️ INFO (Public profile): ❌ INSECURE: Sensitive operation succeeded without valid authentication. | ✅ |
| No Auth - prompts/list | `prompts/list` | Informational (public profile) | Discovery metadata returned | 200 | ℹ️ INFO (Public profile): ⚠️ COMPATIBLE: Discovery metadata was exposed without authentication. | ✅ |
| Malformed Token - prompts/list | `prompts/list` | Informational (public profile) | Discovery metadata returned | 200 | ℹ️ INFO (Public profile): ⚠️ COMPATIBLE: Discovery metadata was exposed without authentication. | ✅ |
| Invalid Token - prompts/list | `prompts/list` | Informational (public profile) | Discovery metadata returned | 200 | ℹ️ INFO (Public profile): ⚠️ COMPATIBLE: Discovery metadata was exposed without authentication. | ✅ |
| Token Expired - prompts/list | `prompts/list` | Informational (public profile) | Discovery metadata returned | 200 | ℹ️ INFO (Public profile): ⚠️ COMPATIBLE: Discovery metadata was exposed without authentication. | ✅ |
| Invalid Scope - prompts/list | `prompts/list` | Informational (public profile) | Discovery metadata returned | 200 | ℹ️ INFO (Public profile): ⚠️ COMPATIBLE: Discovery metadata was exposed without authentication. | ✅ |
| Insufficient Permissions - prompts/list | `prompts/list` | Informational (public profile) | Discovery metadata returned | 200 | ℹ️ INFO (Public profile): ⚠️ COMPATIBLE: Discovery metadata was exposed without authentication. | ✅ |
| Revoked Token - prompts/list | `prompts/list` | Informational (public profile) | Discovery metadata returned | 200 | ℹ️ INFO (Public profile): ⚠️ COMPATIBLE: Discovery metadata was exposed without authentication. | ✅ |
| Wrong Audience (RFC 8707) - prompts/list | `prompts/list` | Informational (public profile) | Discovery metadata returned | 200 | ℹ️ INFO (Public profile): ⚠️ COMPATIBLE: Discovery metadata was exposed without authentication. | ✅ |
| No Auth - prompts/get | `prompts/get` | Informational (public profile) | Request rejected in JSON-RPC layer | 200 | ℹ️ INFO (Public profile): ⚠️ COMPATIBLE: Authentication appears to be enforced at the application layer instead of via an HTTP challenge. | ✅ |
| Malformed Token - prompts/get | `prompts/get` | Informational (public profile) | Request rejected in JSON-RPC layer | 200 | ℹ️ INFO (Public profile): ⚠️ COMPATIBLE: Authentication appears to be enforced at the application layer instead of via an HTTP challenge. | ✅ |
| Invalid Token - prompts/get | `prompts/get` | Informational (public profile) | Request rejected in JSON-RPC layer | 200 | ℹ️ INFO (Public profile): ⚠️ COMPATIBLE: Authentication appears to be enforced at the application layer instead of via an HTTP challenge. | ✅ |
| Token Expired - prompts/get | `prompts/get` | Informational (public profile) | Request rejected in JSON-RPC layer | 200 | ℹ️ INFO (Public profile): ⚠️ COMPATIBLE: Authentication appears to be enforced at the application layer instead of via an HTTP challenge. | ✅ |
| Invalid Scope - prompts/get | `prompts/get` | Informational (public profile) | Request rejected in JSON-RPC layer | 200 | ℹ️ INFO (Public profile): ⚠️ COMPATIBLE: Authentication appears to be enforced at the application layer instead of via an HTTP challenge. | ✅ |
| Insufficient Permissions - prompts/get | `prompts/get` | Informational (public profile) | Request rejected in JSON-RPC layer | 200 | ℹ️ INFO (Public profile): ⚠️ COMPATIBLE: Authentication appears to be enforced at the application layer instead of via an HTTP challenge. | ✅ |
| Revoked Token - prompts/get | `prompts/get` | Informational (public profile) | Request rejected in JSON-RPC layer | 200 | ℹ️ INFO (Public profile): ⚠️ COMPATIBLE: Authentication appears to be enforced at the application layer instead of via an HTTP challenge. | ✅ |
| Wrong Audience (RFC 8707) - prompts/get | `prompts/get` | Informational (public profile) | Request rejected in JSON-RPC layer | 200 | ℹ️ INFO (Public profile): ⚠️ COMPATIBLE: Authentication appears to be enforced at the application layer instead of via an HTTP challenge. | ✅ |
| Query Token - prompts/get | `prompts/get` | Informational (public profile) | Request rejected in JSON-RPC layer | 200 | ℹ️ INFO (Public profile): ⚠️ COMPATIBLE: Authentication appears to be enforced at the application layer instead of via an HTTP challenge. | ✅ |
| No Auth - resources/list | `resources/list` | Informational (public profile) | Discovery metadata returned | 200 | ℹ️ INFO (Public profile): ⚠️ COMPATIBLE: Discovery metadata was exposed without authentication. | ✅ |
| Malformed Token - resources/list | `resources/list` | Informational (public profile) | Discovery metadata returned | 200 | ℹ️ INFO (Public profile): ⚠️ COMPATIBLE: Discovery metadata was exposed without authentication. | ✅ |
| Invalid Token - resources/list | `resources/list` | Informational (public profile) | Discovery metadata returned | 200 | ℹ️ INFO (Public profile): ⚠️ COMPATIBLE: Discovery metadata was exposed without authentication. | ✅ |
| Token Expired - resources/list | `resources/list` | Informational (public profile) | Discovery metadata returned | 200 | ℹ️ INFO (Public profile): ⚠️ COMPATIBLE: Discovery metadata was exposed without authentication. | ✅ |
| Invalid Scope - resources/list | `resources/list` | Informational (public profile) | Discovery metadata returned | 200 | ℹ️ INFO (Public profile): ⚠️ COMPATIBLE: Discovery metadata was exposed without authentication. | ✅ |
| Insufficient Permissions - resources/list | `resources/list` | Informational (public profile) | Discovery metadata returned | 200 | ℹ️ INFO (Public profile): ⚠️ COMPATIBLE: Discovery metadata was exposed without authentication. | ✅ |
| Revoked Token - resources/list | `resources/list` | Informational (public profile) | Discovery metadata returned | 200 | ℹ️ INFO (Public profile): ⚠️ COMPATIBLE: Discovery metadata was exposed without authentication. | ✅ |
| Wrong Audience (RFC 8707) - resources/list | `resources/list` | Informational (public profile) | Discovery metadata returned | 200 | ℹ️ INFO (Public profile): ⚠️ COMPATIBLE: Discovery metadata was exposed without authentication. | ✅ |
| No Auth - resources/read | `resources/read` | Informational (public profile) | Request rejected in JSON-RPC layer | 200 | ℹ️ INFO (Public profile): ⚠️ COMPATIBLE: Authentication appears to be enforced at the application layer instead of via an HTTP challenge. | ✅ |
| Malformed Token - resources/read | `resources/read` | Informational (public profile) | Request rejected in JSON-RPC layer | 200 | ℹ️ INFO (Public profile): ⚠️ COMPATIBLE: Authentication appears to be enforced at the application layer instead of via an HTTP challenge. | ✅ |
| Invalid Token - resources/read | `resources/read` | Informational (public profile) | Request rejected in JSON-RPC layer | 200 | ℹ️ INFO (Public profile): ⚠️ COMPATIBLE: Authentication appears to be enforced at the application layer instead of via an HTTP challenge. | ✅ |
| Token Expired - resources/read | `resources/read` | Informational (public profile) | Request rejected in JSON-RPC layer | 200 | ℹ️ INFO (Public profile): ⚠️ COMPATIBLE: Authentication appears to be enforced at the application layer instead of via an HTTP challenge. | ✅ |
| Invalid Scope - resources/read | `resources/read` | Informational (public profile) | Request rejected in JSON-RPC layer | 200 | ℹ️ INFO (Public profile): ⚠️ COMPATIBLE: Authentication appears to be enforced at the application layer instead of via an HTTP challenge. | ✅ |
| Insufficient Permissions - resources/read | `resources/read` | Informational (public profile) | Request rejected in JSON-RPC layer | 200 | ℹ️ INFO (Public profile): ⚠️ COMPATIBLE: Authentication appears to be enforced at the application layer instead of via an HTTP challenge. | ✅ |
| Revoked Token - resources/read | `resources/read` | Informational (public profile) | Request rejected in JSON-RPC layer | 200 | ℹ️ INFO (Public profile): ⚠️ COMPATIBLE: Authentication appears to be enforced at the application layer instead of via an HTTP challenge. | ✅ |
| Wrong Audience (RFC 8707) - resources/read | `resources/read` | Informational (public profile) | Request rejected in JSON-RPC layer | 200 | ℹ️ INFO (Public profile): ⚠️ COMPATIBLE: Authentication appears to be enforced at the application layer instead of via an HTTP challenge. | ✅ |
| Query Token - resources/read | `resources/read` | Informational (public profile) | Request rejected in JSON-RPC layer | 200 | ℹ️ INFO (Public profile): ⚠️ COMPATIBLE: Authentication appears to be enforced at the application layer instead of via an HTTP challenge. | ✅ |
| No Auth - tools/list | `tools/list` | Informational (public profile) | Discovery metadata returned | 200 | ℹ️ INFO (Public profile): ⚠️ COMPATIBLE: Discovery metadata was exposed without authentication. | ✅ |
| Malformed Token - tools/list | `tools/list` | Informational (public profile) | Discovery metadata returned | 200 | ℹ️ INFO (Public profile): ⚠️ COMPATIBLE: Discovery metadata was exposed without authentication. | ✅ |
| Invalid Token - tools/list | `tools/list` | Informational (public profile) | Discovery metadata returned | 200 | ℹ️ INFO (Public profile): ⚠️ COMPATIBLE: Discovery metadata was exposed without authentication. | ✅ |
| Token Expired - tools/list | `tools/list` | Informational (public profile) | Discovery metadata returned | 200 | ℹ️ INFO (Public profile): ⚠️ COMPATIBLE: Discovery metadata was exposed without authentication. | ✅ |
| Invalid Scope - tools/list | `tools/list` | Informational (public profile) | Discovery metadata returned | 200 | ℹ️ INFO (Public profile): ⚠️ COMPATIBLE: Discovery metadata was exposed without authentication. | ✅ |
| Insufficient Permissions - tools/list | `tools/list` | Informational (public profile) | Discovery metadata returned | 200 | ℹ️ INFO (Public profile): ⚠️ COMPATIBLE: Discovery metadata was exposed without authentication. | ✅ |
| Revoked Token - tools/list | `tools/list` | Informational (public profile) | Discovery metadata returned | 200 | ℹ️ INFO (Public profile): ⚠️ COMPATIBLE: Discovery metadata was exposed without authentication. | ✅ |
| Wrong Audience (RFC 8707) - tools/list | `tools/list` | Informational (public profile) | Discovery metadata returned | 200 | ℹ️ INFO (Public profile): ⚠️ COMPATIBLE: Discovery metadata was exposed without authentication. | ✅ |
| No Auth - tools/call | `tools/call` | Informational (public profile) | Request rejected in JSON-RPC layer | 200 | ℹ️ INFO (Public profile): ⚠️ COMPATIBLE: Authentication appears to be enforced at the application layer instead of via an HTTP challenge. | ✅ |
| Malformed Token - tools/call | `tools/call` | Informational (public profile) | Request rejected in JSON-RPC layer | 200 | ℹ️ INFO (Public profile): ⚠️ COMPATIBLE: Authentication appears to be enforced at the application layer instead of via an HTTP challenge. | ✅ |
| Invalid Token - tools/call | `tools/call` | Informational (public profile) | Request rejected in JSON-RPC layer | 200 | ℹ️ INFO (Public profile): ⚠️ COMPATIBLE: Authentication appears to be enforced at the application layer instead of via an HTTP challenge. | ✅ |
| Token Expired - tools/call | `tools/call` | Informational (public profile) | Request rejected in JSON-RPC layer | 200 | ℹ️ INFO (Public profile): ⚠️ COMPATIBLE: Authentication appears to be enforced at the application layer instead of via an HTTP challenge. | ✅ |
| Invalid Scope - tools/call | `tools/call` | Informational (public profile) | Request rejected in JSON-RPC layer | 200 | ℹ️ INFO (Public profile): ⚠️ COMPATIBLE: Authentication appears to be enforced at the application layer instead of via an HTTP challenge. | ✅ |
| Insufficient Permissions - tools/call | `tools/call` | Informational (public profile) | Request rejected in JSON-RPC layer | 200 | ℹ️ INFO (Public profile): ⚠️ COMPATIBLE: Authentication appears to be enforced at the application layer instead of via an HTTP challenge. | ✅ |
| Revoked Token - tools/call | `tools/call` | Informational (public profile) | Request rejected in JSON-RPC layer | 200 | ℹ️ INFO (Public profile): ⚠️ COMPATIBLE: Authentication appears to be enforced at the application layer instead of via an HTTP challenge. | ✅ |
| Wrong Audience (RFC 8707) - tools/call | `tools/call` | Informational (public profile) | Request rejected in JSON-RPC layer | 200 | ℹ️ INFO (Public profile): ⚠️ COMPATIBLE: Authentication appears to be enforced at the application layer instead of via an HTTP challenge. | ✅ |
| Query Token - tools/call | `tools/call` | Informational (public profile) | Request rejected in JSON-RPC layer | 200 | ℹ️ INFO (Public profile): ⚠️ COMPATIBLE: Authentication appears to be enforced at the application layer instead of via an HTTP challenge. | ✅ |

### Adversarial Input Handling

> **Legend:**
> * **REFLECTED / UNSAFE ECHO**: The server reflected the payload back without sanitization. No execution confirmed.
> * **BLOCKED**: The server correctly rejected or sanitized the input.
> * **SKIPPED**: Test could not be executed (e.g., no tools discovered for injection target).

| Attack Vector | Description | Result | Probe | Analysis |
| :--- | :--- | :---: | :--- | :--- |
| INJ-001 (Input Validation) | Simulated INJ-001 (Input Validation) attack on microsoft_docs_search | 🛡️ BLOCKED | `tools/list`/http: Success HTTP 200; auth NotRequired (+1) | `<HTML><HEAD> <TITLE>Access Denied</TITLE> </HEAD><BODY> <H1>Access Denied</H1>   You don't have permission to access "http&#58;&#47;&#47;learn&#46;microsoft&#46;com&#47;api&#47;mcp" on this server.<P> Reference&#32;&#35;18&#46;49d02e17&#46;1777653101&#46;1c9116d2 <P>https&#58;&#47;&#47;errors&#46;edgesuite&#46;net&#47;18&#46;49d02e17&#46;1777653101&#46;1c9116d2</P> </BODY> </HTML> ` |
| INJ-002 (Input Validation) | Simulated INJ-002 (Input Validation) attack on microsoft_docs_search | 🛡️ BLOCKED | `tools/list`/http: Success HTTP 200; auth NotRequired (+1) | `<HTML><HEAD> <TITLE>Access Denied</TITLE> </HEAD><BODY> <H1>Access Denied</H1>   You don't have permission to access "http&#58;&#47;&#47;learn&#46;microsoft&#46;com&#47;api&#47;mcp" on this server.<P> Reference&#32;&#35;18&#46;49d02e17&#46;1777653101&#46;1c90c1ea <P>https&#58;&#47;&#47;errors&#46;edgesuite&#46;net&#47;18&#46;49d02e17&#46;1777653101&#46;1c90c1ea</P> </BODY> </HTML> ` |
| INJ-003 (Input Validation) | Simulated INJ-003 (Input Validation) attack on microsoft_docs_search | 🛡️ BLOCKED | `tools/list`/http: Success HTTP 200; auth NotRequired (+1) | `<HTML><HEAD> <TITLE>Access Denied</TITLE> </HEAD><BODY> <H1>Access Denied</H1>   You don't have permission to access "http&#58;&#47;&#47;learn&#46;microsoft&#46;com&#47;api&#47;mcp" on this server.<P> Reference&#32;&#35;18&#46;49d02e17&#46;1777653101&#46;1c911899 <P>https&#58;&#47;&#47;errors&#46;edgesuite&#46;net&#47;18&#46;49d02e17&#46;1777653101&#46;1c911899</P> </BODY> </HTML> ` |
| MCP-SEC-001 | JSON-RPC Error Smuggling | 🛡️ BLOCKED | `rpc.system.invalid`/http: Success HTTP 200; auth NotRequired | `Server handled malformed requests gracefully with standard errors.` |
| MCP-SEC-002 | Metadata Enumeration | ⏭️ SKIPPED | `resources/list`/http: Success HTTP 200; auth NotRequired | `Skipped: Metadata enumeration requires an advertised concrete resource URI; no like-for-like resource surface was available.` |
| MCP-SEC-003 | Schema Confusion | 🛡️ BLOCKED | `tools/list`/http: Success HTTP 200; auth NotRequired (+1) | `Server correctly rejected invalid schema.` |
| MCP-AI-001 | Hallucination Fuzzing | 🛡️ BLOCKED | `tools/list`/http: Success HTTP 200; auth NotRequired (+1) | `Error clarity: 55/100 (Fair). Error messages partially helpful for AI.` |

## 15. Protocol Compliance

**Status Detail:** declared capabilities: logging, prompts, prompts.listChanged, resources, resources.listChanged, tools, tools.listChanged | roots/list: not advertised | logging/setLevel: supported | sampling/createMessage: not advertised | completion/complete: not advertised

### Compliance Violations
| ID | Source | Description | Severity | Recommendation |
| :--- | :--- | :--- | :---: | :--- |
| MCP-PROTO-ERR | `spec` | JSON-RPC Error Code Violation: Parse Error | High | This specific error code test failed. Ensure the server returns standard JSON-RPC error codes for known error conditions: -32700 (parse error), -32600 (invalid request), -32601 (method not found), -32602 (invalid params), -32603 (internal error). See https://spec.modelcontextprotocol.io/specification/2025-11-25/basic/json-rpc#errors |
| MCP-PROTO-ERR | `spec` | JSON-RPC Error Code Violation: Invalid Request - Missing jsonrpc | High | This specific error code test failed. Ensure the server returns standard JSON-RPC error codes for known error conditions: -32700 (parse error), -32600 (invalid request), -32601 (method not found), -32602 (invalid params), -32603 (internal error). See https://spec.modelcontextprotocol.io/specification/2025-11-25/basic/json-rpc#errors |
| MCP-PROTO-ERR | `spec` | JSON-RPC Error Code Violation: Invalid Params | High | This specific error code test failed. Ensure the server returns standard JSON-RPC error codes for known error conditions: -32700 (parse error), -32600 (invalid request), -32601 (method not found), -32602 (invalid params), -32603 (internal error). See https://spec.modelcontextprotocol.io/specification/2025-11-25/basic/json-rpc#errors |
| MCP-PROTO-JSONRPC | `spec` | Request format does not comply with JSON-RPC 2.0 specification | High | Ensure every JSON-RPC request includes "jsonrpc": "2.0", a string "method" field, and a string or integer "id". The "params" field, if present, must be an object or array. See https://spec.modelcontextprotocol.io/specification/2025-11-25/basic/json-rpc |
| MCP-PROTO-ERR | `spec` | Error codes do not comply with JSON-RPC 2.0 standard error codes | High | Review all error responses to ensure they use standard JSON-RPC 2.0 error codes. Custom error codes must be outside the reserved range (-32000 to -32099 for server errors). See https://spec.modelcontextprotocol.io/specification/2025-11-25/basic/json-rpc#errors |
| MCP-HTTP-001 | `spec` | Content-Type requirements not enforced (Server should reject non-JSON) | High | Set the Content-Type header to 'application/json' for all JSON-RPC messages and reject requests with incorrect content types. See https://spec.modelcontextprotocol.io/specification/2025-11-25/basic/transports#http |
| MCP-HTTP-004 | `spec` | Streamable HTTP transport violation (invalid-protocol-version-400): Requests with an unsupported MCP-Protocol-Version header must be rejected before normal JSON-RPC handling. Expected: HTTP 400 Bad Request. Observed: HTTP 200; Content-Type=text/event-stream; body length 85 | High | Reject requests that advertise an unsupported MCP-Protocol-Version header with HTTP 400 Bad Request. See https://spec.modelcontextprotocol.io/specification/2025-11-25/basic/transports#protocol-version-header |
| MCP-HTTP-005 | `spec` | Streamable HTTP transport violation (invalid-origin-403): HTTP transports must validate Origin and reject invalid origins with HTTP 403 Forbidden. Expected: HTTP 403 Forbidden. Observed: HTTP 200; Content-Type=text/event-stream; body length 84 | High | Validate the Origin header for HTTP transports and reject invalid origins with HTTP 403 Forbidden before processing the JSON-RPC message. See https://spec.modelcontextprotocol.io/specification/2025-11-25/basic/transports#origin-header-validation |

#### Violation Context

| ID | Context |
| :--- | :--- |
| `MCP-HTTP-004` | **Probe ID:** `invalid-protocol-version-400`; **HTTP status:** `200`; **Content-Type:** `text/event-stream`; **Expected:** `HTTP 400 Bad Request.`; **Actual:** `HTTP 200; Content-Type=text/event-stream; body length 85` |
| `MCP-HTTP-005` | **Probe ID:** `invalid-origin-403`; **HTTP status:** `200`; **Content-Type:** `text/event-stream`; **Expected:** `HTTP 403 Forbidden.`; **Actual:** `HTTP 200; Content-Type=text/event-stream; body length 84` |

## 16. Tool Validation

**Tools Discovered:** 3

### Tool Metadata Completeness

Annotation coverage across the discovered tool catalog. Missing annotations reduce AI agent safety and UX quality.

| Tool | title | description | readOnlyHint | destructiveHint | openWorldHint | idempotentHint |
| :--- | :---: | :---: | :---: | :---: | :---: | :---: |
| `tools/list (Schema Compliance)` | ❌ | ❌ | ❌ | ❌ | ❌ | ❌ |
| `microsoft_docs_search` | ✅ | ✅ | ✅ | ✅ | ❌ | ✅ |
| `microsoft_code_sample_search` | ✅ | ✅ | ✅ | ✅ | ❌ | ✅ |
| `microsoft_docs_fetch` | ✅ | ✅ | ✅ | ✅ | ❌ | ✅ |
| **Coverage** | **3/4** | **3/4** | **3/4** | **3/4** | **0/4** | **3/4** |

### AI Safety Control Evidence

This table separates server-declared tool metadata from host-side controls that the validator cannot directly observe.

| Control | Declared | Missing | Not Observable | Not Applicable | Representative Note |
| :--- | ---: | ---: | ---: | ---: | :--- |
| User confirmation | 0 | 0 | 0 | 3 | No destructive tool behavior was declared or inferred from tool metadata. |
| Audit trail | 0 | 0 | 3 | 0 | Audit trail behavior is not observable from tool metadata. |
| Data-sharing disclosure | 0 | 3 | 0 | 0 | Tool metadata does not declare open-world/data-sharing behavior. |
| Destructive-action confirmation | 0 | 0 | 0 | 3 | Tool is not declared or inferred as destructive. |
| Host/server responsibility split | 0 | 0 | 3 | 0 | Host-side consent, denial, and disclosure UX cannot be proven from server tool metadata alone. |

### Tool: `tools/list (Schema Compliance)`

**Status:** ✅ Passed
**Execution Time:** 85.09ms
---

### Tool: `microsoft_docs_search`

**Status:** ✅ Passed
**Execution Time:** 837.38ms

#### Tool Metadata
| Property | Value |
| :--- | :--- |
| Display Title | Microsoft Docs Search |
| readOnlyHint | True |
| destructiveHint | False |
| idempotentHint | True |

---

### Tool: `microsoft_code_sample_search`

**Status:** ✅ Passed
**Execution Time:** 880.38ms

#### Tool Metadata
| Property | Value |
| :--- | :--- |
| Display Title | Microsoft Code Sample Search |
| readOnlyHint | True |
| destructiveHint | False |
| idempotentHint | True |

---

### Tool: `microsoft_docs_fetch`

**Status:** ✅ Passed
**Execution Time:** 232.09ms

#### Tool Metadata
| Property | Value |
| :--- | :--- |
| Display Title | Microsoft Docs Fetch |
| readOnlyHint | True |
| destructiveHint | False |
| idempotentHint | True |

---

### Tool Catalog Advisory Breakdown

Remaining tool-catalog debt is grouped by authority so MCP specification failures are not conflated with guidance or deterministic AI-oriented heuristics.

| Authority | Active Rules | Coverage | Highest Severity | Representative Gaps |
| :--- | :---: | :--- | :---: | :--- |
| Spec | 0 | 0/3 (0%) | - | No current catalog-wide tool advisories. |
| Guideline | 1 | 3/3 (100%) | 🔵 Low | Tool 'microsoft_code_sample_search' does not declare annotations.openWorldHint. |
| Heuristic | 2 | 2/3 (67%) | 🔵 Low | Tool 'microsoft_code_sample_search': 1/2 string parameters look like fixed-choice fields but do not declare enum/const choices<br />Tool 'microsoft_docs_fetch': 1/1 string parameters look like structured values but do not declare format/pattern hints |
| Operational | 0 | 0/3 (0%) | - | No current catalog-wide tool advisories. |

### MCP Guideline Findings

These findings capture MCP guidance signals that improve agent safety and UX but are not strict protocol MUST failures.
Coverage shows how prevalent each issue is across the discovered tool catalog so larger servers are judged by rate, not raw count alone.

| Rule ID | Source | Coverage | Severity | Example Components | Finding |
| :--- | :--- | :--- | :---: | :--- | :--- |
| `MCP.GUIDELINE.TOOL.OPENWORLD_HINT_MISSING` | `guideline` | 3/3 (100%) | 🔵 Low | `microsoft_code_sample_search`, `microsoft_docs_fetch`, `microsoft_docs_search` | Tool 'microsoft_code_sample_search' does not declare annotations.openWorldHint. |

## 17. Resource Capabilities

**Resources Discovered:** 0

**Catalog Applicability:** Resources capability was not advertised during initialize; resources/list and resources/read probes were skipped; no resource reads were required.

**Surface Notes:**
- resources.subscribe was not advertised during initialize; resource subscription probes were skipped.
- ✅ Resource templates: 0 templates discovered
- ✅ COMPLIANT: 0 resources discovered and validated

## 18. Prompt Capabilities

**Prompts Discovered:** 0

**Catalog Applicability:** Prompts capability was advertised, but prompts/list returned an empty catalog; no prompt executions were required.

**Surface Notes:**
- ✅ COMPLIANT: No prompts were advertised; no prompt executions were required

## 19. AI Readiness Assessment

**AI Readiness Score:** ✅ **94/100** (Good)

**Evidence basis:** Deterministic schema and payload heuristics. Measured model-evaluation results, when enabled, are written as a separate companion artifact and are not blended into this score.

| Criterion | What It Measures |
| :--- | :--- |
| **Description Completeness** | Do all tool parameters have descriptions? Missing descriptions increase hallucination risk. |
| **Type Specificity** | Are `string` parameters constrained with `enum`, `pattern`, or `format`? Vague types cause incorrect arguments. |
| **Token Efficiency** | Is the `tools/list` payload within context window limits? Estimated: ~1,241 tokens. |

### Findings

| Rule ID | Evidence Basis | Source | Coverage | Severity | Finding |
| :--- | :--- | :--- | :--- | :---: | :--- |
| `AI.TOOL.SCHEMA.ENUM_COVERAGE_MISSING` | Deterministic schema heuristic | `heuristic` | 1/3 (33%) | 🔵 Low | Tool 'microsoft_code_sample_search': 1/2 string parameters look like fixed-choice fields but do not declare enum/const choices |
| `AI.TOOL.SCHEMA.FORMAT_HINT_MISSING` | Deterministic schema heuristic | `heuristic` | 1/3 (33%) | 🔵 Low | Tool 'microsoft_docs_fetch': 1/1 string parameters look like structured values but do not declare format/pattern hints |

## 20. Optional MCP Capabilities

These probes check whether the server supports optional MCP features beyond the core primitives.

| Capability | Status |
| :--- | :--- |
| `declared capabilities` | ➖ logging, prompts, prompts.listChanged, resources, resources.listChanged, tools, tools.listChanged |
| `roots/list` | ➖ not advertised |
| `logging/setLevel` | ✅ supported |
| `sampling/createMessage` | ➖ not advertised |
| `completion/complete` | ➖ not advertised |

## 21. Performance Metrics

| Metric | Result | Verdict |
| :--- | :--- | :--- |
| **Avg Latency** | 88.31ms | 🚀 Excellent |
| **P95 Latency** | 100.77ms | - |
| **Throughput** | 11.31 req/sec | - |
| **Error Rate** | 0.00% | ✅ Clean |
| **Requests** | 20/20 successful | - |

## 22. Recommendations

- 💡 Spec: This specific error code test failed. Ensure the server returns standard JSON-RPC error codes for known error conditions: -32700 (parse error), -32600 (invalid request), -32601 (method not found), -32602 (invalid params), -32603 (internal error). See https://spec.modelcontextprotocol.io/specification/2025-11-25/basic/json-rpc#errors
- 💡 Spec: Ensure every JSON-RPC request includes "jsonrpc": "2.0", a string "method" field, and a string or integer "id". The "params" field, if present, must be an object or array. See https://spec.modelcontextprotocol.io/specification/2025-11-25/basic/json-rpc
- 💡 Spec: Review all error responses to ensure they use standard JSON-RPC 2.0 error codes. Custom error codes must be outside the reserved range (-32000 to -32099 for server errors). See https://spec.modelcontextprotocol.io/specification/2025-11-25/basic/json-rpc#errors
- 💡 Spec: Set the Content-Type header to 'application/json' for all JSON-RPC messages and reject requests with incorrect content types. See https://spec.modelcontextprotocol.io/specification/2025-11-25/basic/transports#http
- 💡 Spec: Reject requests that advertise an unsupported MCP-Protocol-Version header with HTTP 400 Bad Request. See https://spec.modelcontextprotocol.io/specification/2025-11-25/basic/transports#protocol-version-header
- 💡 Spec: Validate the Origin header for HTTP transports and reject invalid origins with HTTP 403 Forbidden before processing the JSON-RPC message. See https://spec.modelcontextprotocol.io/specification/2025-11-25/basic/transports#origin-header-validation

---
*Produced with [MCP Validator](https://github.com/navalerakesh/mcp-validation-security) · [McpVal on NuGet](https://www.nuget.org/packages/McpVal#versions-body-tab)*
