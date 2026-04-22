# MCP Server Compliance & Validation Report
**Generated:** 2026-04-22 13:44:01 UTC

## 1. Executive Summary

| Metric | Value |
| :--- | :--- |
| **Server Endpoint** | `https://learn.microsoft.com/api/mcp` |
| **Validation ID** | `218f4501-4d25-4385-9bf1-7cdacba726f6` |
| **Overall Status** | ✅ **Passed** |
| **Compliance Score** | **92.3%** |
| **Compliance Profile** | `Public (Inferred)` |
| **Duration** | 62.14s |
| **Transport** | HTTP |
| **Session Bootstrap** | ✅ Healthy |
| **Deferred Validation** | No — validation started from a clean bootstrap state. |
| **MCP Protocol Version (Effective)** | `2025-06-18` |
| **MCP Trust Level** | 🔵 **L4: Trusted — Meets enterprise AI safety requirements** |

## 2. Connectivity & Session Bootstrap

This section explains how the validator established initial connectivity and whether validation started from a clean initialize handshake or a deferred advisory state.

| Bootstrap Signal | Value |
| :--- | :--- |
| **Bootstrap State** | ✅ Healthy |
| **Validation Proceeded Under Deferment** | No — validation started from a clean bootstrap state. |
| **Initialize Handshake** | ✅ Initialize handshake succeeded. |
| **Handshake HTTP Status** | `HTTP 200` |
| **Handshake Duration** | 761.1 ms |
| **Negotiated Protocol** | `2025-06-18` |
| **Observed Server Version** | `1.0.0` |
| **Server Profile Resolution** | `Public (Inferred)` |

Validation began from a clean bootstrap with no deferred connectivity risk carried into later categories.

## 3. Priority Findings

These are the highest-signal outcomes from this validation run.

- MCP-PROTO-JSONRPC: Batch processing implementation is inconsistent or incomplete
- MCP-PROTO-ERR: JSON-RPC Error Code Violation: Parse Error

## 4. Action Hints

Compact next-step guidance derived from the highest-signal evidence in this run.

- Protocol: If the server accepts batch requests, it must process each request independently and return an array of responses in any order. Ensure ba....
- Protocol: This specific error code test failed. Ensure the server returns standard JSON-RPC error codes for known error conditions: -32700 (parse e....
- Spec: If the server accepts batch requests, it must process each request independently and return an array of responses in any order. Ensure batch....
- Spec: This specific error code test failed. Ensure the server returns standard JSON-RPC error codes for known error conditions: -32700 (parse error....

## 5. MCP Trust Assessment

Multi-dimensional evaluation of server trustworthiness for AI agent consumption.
Trust level is determined by a **weighted multi-dimensional score** and then capped by confirmed blockers such as critical security failures or MCP MUST failures.

| Dimension | Score | What It Measures |
| :--- | :---: | :--- |
| **Protocol Compliance** | 78% | MCP spec adherence, JSON-RPC compliance, response structures |
| **Security Posture** | 100% | Auth compliance, injection resistance, attack surface |
| **AI Safety** | 67% | Schema quality, destructive tool detection, exfiltration risk, prompt injection surface |
| **Operational Readiness** | 100% | Latency, throughput, error rate, stability |

### AI Boundary Findings

These findings go **beyond MCP protocol** to assess how AI agents interact with this server.

| Category | Component | Severity | Finding |
| :--- | :--- | :---: | :--- |
| Exfiltration | `microsoft_docs_fetch` | 🟠 High | Tool 'microsoft_docs_fetch' accepts URI-like or outbound-target parameters that could be used for data exfiltration (evidence: 'url'). |

### Summary

- Destructive tools: **0**
- Data exfiltration risk: **1/3 (33%)**
- Prompt injection surface: **0**

### MCP Spec Compliance (RFC 2119 Tiers)

| Tier | Passed | Failed | Total | Impact |
| :--- | :---: | :---: | :---: | :--- |
| **MUST** | 9 | 0 | 9 | ✅ Fully compliant |
| **SHOULD** | 3 | 2 | 5 | ⚠️ 2 penalties applied |
| **MAY** | 3 | 3 | 6 | ℹ️ Informational (no score impact) |

#### ⚠️ SHOULD Gaps (Score Penalties)

- SHOULD: String parameters have enum/pattern/format constraints (tools/list)
- SHOULD: tools/call result includes 'isError' field (tools/call)

#### ℹ️ Optional Features (MAY)

- ✅ MAY: Server supports logging/setLevel
- ➖ MAY: Server supports sampling/createMessage
- ➖ MAY: Server supports roots/list
- ➖ MAY: Server supports completion/complete
- ✅ MAY: Server supports resources/templates/list
- ✅ MAY: Tools include 'annotations' (readOnlyHint, destructiveHint)

## 6. Client Profile Compatibility

Documented host-side compatibility assessments derived from the neutral validation evidence collected during this run.

| Client Profile | Status | Requirements | Documentation |
| :--- | :--- | :--- | :--- |
| **Claude Code** | ⚠️ Compatible with warnings | 2 passed / 2 warnings / 0 failed | <https://docs.anthropic.com/en/docs/claude-code/mcp> |
| **VS Code Copilot Agent** | ⚠️ Compatible with warnings | 2 passed / 2 warnings / 0 failed | <https://code.visualstudio.com/docs/copilot/chat/mcp-servers> |
| **GitHub Copilot CLI** | ⚠️ Compatible with warnings | 2 passed / 2 warnings / 0 failed | <https://docs.github.com/en/copilot/how-tos/copilot-cli/customize-copilot/add-mcp-servers> |
| **GitHub Copilot Cloud Agent** | ⚠️ Compatible with warnings | 3 passed / 2 warnings / 0 failed | <https://docs.github.com/en/copilot/how-tos/use-copilot-agents/cloud-agent/extend-cloud-agent-with-mcp> |
| **Visual Studio Copilot** | ⚠️ Compatible with warnings | 2 passed / 2 warnings / 0 failed | <https://docs.github.com/en/copilot/how-tos/provide-context/use-mcp/extend-copilot-chat-with-mcp?tool=visualstudio> |

### Claude Code

**Status:** ⚠️ Compatible with warnings

Required compatibility checks passed; 2 advisory requirements still need follow-up.

| Requirement | Level | Outcome | Rule IDs | Affected Components | Details |
| :--- | :--- | :--- | :--- | :--- | :--- |
| Tool presentation and approval metadata is complete | Recommended | ⚠️ Warning | `MCP.GUIDELINE.TOOL.OPENWORLD_HINT_MISSING` | `microsoft_docs_search`, `microsoft_code_sample_search`, `microsoft_docs_fetch` | Advisory tool guidance gaps affect 3/3 tool(s). |
| Tool schemas are clear enough for agent planning | Recommended | ⚠️ Warning | `AI.TOOL.SCHEMA.STRING_CONSTRAINT_MISSING`, `AI.TOOL.SCHEMA.ENUM_COVERAGE_MISSING`, `AI.TOOL.SCHEMA.FORMAT_HINT_MISSING` | `microsoft_docs_search`, `microsoft_code_sample_search`, `microsoft_docs_fetch` | Advisory tool guidance gaps affect 3/3 tool(s). |

#### Remediation

- ⚠️ **Tool presentation and approval metadata is complete:** Declare openWorldHint so agents can reason about whether execution can affect unknown external systems.
- ⚠️ **Tool schemas are clear enough for agent planning:** Constrain string parameters with enum, pattern, or format metadata when possible.

### VS Code Copilot Agent

**Status:** ⚠️ Compatible with warnings

Required compatibility checks passed; 2 advisory requirements still need follow-up.

| Requirement | Level | Outcome | Rule IDs | Affected Components | Details |
| :--- | :--- | :--- | :--- | :--- | :--- |
| Tool presentation and approval metadata is complete | Recommended | ⚠️ Warning | `MCP.GUIDELINE.TOOL.OPENWORLD_HINT_MISSING` | `microsoft_docs_search`, `microsoft_code_sample_search`, `microsoft_docs_fetch` | Advisory tool guidance gaps affect 3/3 tool(s). |
| Tool schemas are clear enough for agent planning | Recommended | ⚠️ Warning | `AI.TOOL.SCHEMA.STRING_CONSTRAINT_MISSING`, `AI.TOOL.SCHEMA.ENUM_COVERAGE_MISSING`, `AI.TOOL.SCHEMA.FORMAT_HINT_MISSING` | `microsoft_docs_search`, `microsoft_code_sample_search`, `microsoft_docs_fetch` | Advisory tool guidance gaps affect 3/3 tool(s). |

#### Remediation

- ⚠️ **Tool presentation and approval metadata is complete:** Declare openWorldHint so agents can reason about whether execution can affect unknown external systems.
- ⚠️ **Tool schemas are clear enough for agent planning:** Constrain string parameters with enum, pattern, or format metadata when possible.

### GitHub Copilot CLI

**Status:** ⚠️ Compatible with warnings

Required compatibility checks passed; 2 advisory requirements still need follow-up.

| Requirement | Level | Outcome | Rule IDs | Affected Components | Details |
| :--- | :--- | :--- | :--- | :--- | :--- |
| Tool presentation and approval metadata is complete | Recommended | ⚠️ Warning | `MCP.GUIDELINE.TOOL.OPENWORLD_HINT_MISSING` | `microsoft_docs_search`, `microsoft_code_sample_search`, `microsoft_docs_fetch` | Advisory tool guidance gaps affect 3/3 tool(s). |
| Tool schemas are clear enough for agent planning | Recommended | ⚠️ Warning | `AI.TOOL.SCHEMA.STRING_CONSTRAINT_MISSING`, `AI.TOOL.SCHEMA.ENUM_COVERAGE_MISSING`, `AI.TOOL.SCHEMA.FORMAT_HINT_MISSING` | `microsoft_docs_search`, `microsoft_code_sample_search`, `microsoft_docs_fetch` | Advisory tool guidance gaps affect 3/3 tool(s). |

#### Remediation

- ⚠️ **Tool presentation and approval metadata is complete:** Declare openWorldHint so agents can reason about whether execution can affect unknown external systems.
- ⚠️ **Tool schemas are clear enough for agent planning:** Constrain string parameters with enum, pattern, or format metadata when possible.

### GitHub Copilot Cloud Agent

**Status:** ⚠️ Compatible with warnings

Required compatibility checks passed; 2 advisory requirements still need follow-up.

| Requirement | Level | Outcome | Rule IDs | Affected Components | Details |
| :--- | :--- | :--- | :--- | :--- | :--- |
| Tool presentation and approval metadata is complete | Recommended | ⚠️ Warning | `MCP.GUIDELINE.TOOL.OPENWORLD_HINT_MISSING` | `microsoft_docs_search`, `microsoft_code_sample_search`, `microsoft_docs_fetch` | Advisory tool guidance gaps affect 3/3 tool(s). |
| Tool schemas are clear enough for agent planning | Recommended | ⚠️ Warning | `AI.TOOL.SCHEMA.STRING_CONSTRAINT_MISSING`, `AI.TOOL.SCHEMA.ENUM_COVERAGE_MISSING`, `AI.TOOL.SCHEMA.FORMAT_HINT_MISSING` | `microsoft_docs_search`, `microsoft_code_sample_search`, `microsoft_docs_fetch` | Advisory tool guidance gaps affect 3/3 tool(s). |

#### Remediation

- ⚠️ **Tool presentation and approval metadata is complete:** Declare openWorldHint so agents can reason about whether execution can affect unknown external systems.
- ⚠️ **Tool schemas are clear enough for agent planning:** Constrain string parameters with enum, pattern, or format metadata when possible.

### Visual Studio Copilot

**Status:** ⚠️ Compatible with warnings

Required compatibility checks passed; 2 advisory requirements still need follow-up.

| Requirement | Level | Outcome | Rule IDs | Affected Components | Details |
| :--- | :--- | :--- | :--- | :--- | :--- |
| Tool presentation and approval metadata is complete | Recommended | ⚠️ Warning | `MCP.GUIDELINE.TOOL.OPENWORLD_HINT_MISSING` | `microsoft_docs_search`, `microsoft_code_sample_search`, `microsoft_docs_fetch` | Advisory tool guidance gaps affect 3/3 tool(s). |
| Tool schemas are clear enough for agent planning | Recommended | ⚠️ Warning | `AI.TOOL.SCHEMA.STRING_CONSTRAINT_MISSING`, `AI.TOOL.SCHEMA.ENUM_COVERAGE_MISSING`, `AI.TOOL.SCHEMA.FORMAT_HINT_MISSING` | `microsoft_docs_search`, `microsoft_code_sample_search`, `microsoft_docs_fetch` | Advisory tool guidance gaps affect 3/3 tool(s). |

#### Remediation

- ⚠️ **Tool presentation and approval metadata is complete:** Declare openWorldHint so agents can reason about whether execution can affect unknown external systems.
- ⚠️ **Tool schemas are clear enough for agent planning:** Constrain string parameters with enum, pattern, or format metadata when possible.

## 7. Capability Snapshot

| Probe | Discovered | HTTP Status | Duration | Result |
| :--- | :---: | :---: | :---: | :--- |
| Tools/list | 3 | HTTP 200 | 68.8 ms | ✅ Listed<br/>Call ✅ |
| Resources/list | 0 | HTTP 200 | 63.9 ms | ✅ Listed |
| Prompts/list | 0 | HTTP 200 | 63.3 ms | ✅ Listed |

- **First Tool Probed:** `microsoft_docs_search`

## 8. Scoring Methodology

These notes explain how the overall score and blocking decision were calibrated for this run.

- INFO: Authentication challenge observations are informational for the declared public profile.

## 9. Compliance Matrix

| Category | Status | Score | Issues |
| :--- | :---: | :---: | :---: |
| Protocol Compliance | ✅ Passed | 78.1% | **4** |
| Security Assessment | ✅ Passed | 100.0% | - |
| Tool Validation | ✅ Passed | 100.0% | - |
| Resource Capabilities | ✅ Passed | 100.0% | - |
| Prompt Capabilities | ✅ Passed | 100.0% | - |
| Performance | ✅ Passed | 100.0% | - |

## 10. Performance Calibration

Performance is assessed using a synthetic load probe rather than a workload-specific SLA benchmark.
For public or remote SaaS endpoints, partial failures under synthetic pressure are reported as advisory when they look like capacity limits or edge protections rather than protocol instability.

- Public remote synthetic load probe stabilized at concurrency 20 before ramping to the configured target concurrency 100.
- Synthetic load probe executed against tools/list using 500 requests at concurrency 100.
- Synthetic load probe executed 2 round(s) and observed 0 rate-limited request(s) plus 0 retryable transient failure(s) across calibration.

## 11. Security Assessment

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

### Adversarial Input Handling

> **Legend:**
> * **REFLECTED / UNSAFE ECHO**: The server reflected the payload back without sanitization. No execution confirmed.
> * **BLOCKED**: The server correctly rejected or sanitized the input.
> * **SKIPPED**: Test could not be executed (e.g., no tools discovered for injection target).

| Attack Vector | Description | Result | Analysis |
| :--- | :--- | :---: | :--- |
| INJ-001 (Input Validation) | Simulated INJ-001 (Input Validation) attack on microsoft_docs_search | 🛡️ BLOCKED | `<HTML><HEAD> <TITLE>Access Denied</TITLE> </HEAD><BODY> <H1>Access Denied</H1>   You don't have permission to access "http&#58;&#47;&#47;learn&#46;microsoft&#46;com&#47;api&#47;mcp" on this server.<P> Reference&#32;&#35;18&#46;43d02e17&#46;1776865388&#46;faadbeeb <P>https&#58;&#47;&#47;errors&#46;edgesuite&#46;net&#47;18&#46;43d02e17&#46;1776865388&#46;faadbeeb</P> </BODY> </HTML> ` |
| INJ-002 (Input Validation) | Simulated INJ-002 (Input Validation) attack on microsoft_docs_search | 🛡️ BLOCKED | `<HTML><HEAD> <TITLE>Access Denied</TITLE> </HEAD><BODY> <H1>Access Denied</H1>   You don't have permission to access "http&#58;&#47;&#47;learn&#46;microsoft&#46;com&#47;api&#47;mcp" on this server.<P> Reference&#32;&#35;18&#46;43d02e17&#46;1776865388&#46;faadc149 <P>https&#58;&#47;&#47;errors&#46;edgesuite&#46;net&#47;18&#46;43d02e17&#46;1776865388&#46;faadc149</P> </BODY> </HTML> ` |
| INJ-003 (Input Validation) | Simulated INJ-003 (Input Validation) attack on microsoft_docs_search | 🛡️ BLOCKED | `<HTML><HEAD> <TITLE>Access Denied</TITLE> </HEAD><BODY> <H1>Access Denied</H1>   You don't have permission to access "http&#58;&#47;&#47;learn&#46;microsoft&#46;com&#47;api&#47;mcp" on this server.<P> Reference&#32;&#35;18&#46;43d02e17&#46;1776865388&#46;faadc3b1 <P>https&#58;&#47;&#47;errors&#46;edgesuite&#46;net&#47;18&#46;43d02e17&#46;1776865388&#46;faadc3b1</P> </BODY> </HTML> ` |
| MCP-SEC-001 | JSON-RPC Error Smuggling | 🛡️ BLOCKED | `Server handled malformed requests gracefully with standard errors.` |
| MCP-SEC-002 | Metadata Enumeration | ⏭️ SKIPPED | `Skipped: Metadata enumeration requires an advertised concrete resource URI; no like-for-like resource surface was available.` |
| MCP-SEC-003 | Schema Confusion | 🛡️ BLOCKED | `Server correctly rejected invalid schema.` |
| MCP-AI-001 | Hallucination Fuzzing | 🛡️ BLOCKED | `Error clarity: 55/100 (Fair). Error messages partially helpful for AI.` |

## 12. Protocol Compliance

**Status Detail:** declared capabilities: logging, prompts, resources, resources.listChanged, tools | roots/list: not supported | logging/setLevel: supported | sampling/createMessage: not supported | completion/complete: not supported

### Compliance Violations
| ID | Source | Description | Severity | Recommendation |
| :--- | :--- | :--- | :---: | :--- |
| MCP-PROTO-ERR | `spec` | JSON-RPC Error Code Violation: Parse Error | Low | This specific error code test failed. Ensure the server returns standard JSON-RPC error codes for known error conditions: -32700 (parse error), -32600 (invalid request), -32601 (method not found), -32602 (invalid params), -32603 (internal error). See https://spec.modelcontextprotocol.io/specification/2025-11-25/basic/json-rpc#errors |
| MCP-PROTO-ERR | `spec` | JSON-RPC Error Code Violation: Invalid Request - Missing jsonrpc | Low | This specific error code test failed. Ensure the server returns standard JSON-RPC error codes for known error conditions: -32700 (parse error), -32600 (invalid request), -32601 (method not found), -32602 (invalid params), -32603 (internal error). See https://spec.modelcontextprotocol.io/specification/2025-11-25/basic/json-rpc#errors |
| MCP-PROTO-ERR | `spec` | JSON-RPC Error Code Violation: Invalid Params | Low | This specific error code test failed. Ensure the server returns standard JSON-RPC error codes for known error conditions: -32700 (parse error), -32600 (invalid request), -32601 (method not found), -32602 (invalid params), -32603 (internal error). See https://spec.modelcontextprotocol.io/specification/2025-11-25/basic/json-rpc#errors |
| MCP-PROTO-JSONRPC | `spec` | Batch processing implementation is inconsistent or incomplete | Medium | If the server accepts batch requests, it must process each request independently and return an array of responses in any order. Ensure batch handling does not drop or duplicate responses. See https://www.jsonrpc.org/specification#batch |

## 13. Tool Validation

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

### Tool: `tools/list (Schema Compliance)`

**Status:** ✅ Passed
**Execution Time:** 68.80ms
---

### Tool: `microsoft_docs_search`

**Status:** ✅ Passed
**Execution Time:** 1016.32ms

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
**Execution Time:** 1076.18ms

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
**Execution Time:** 224.42ms

#### Tool Metadata
| Property | Value |
| :--- | :--- |
| Display Title | Microsoft Docs Fetch |
| readOnlyHint | True |
| destructiveHint | False |
| idempotentHint | True |

---

### Tool Catalog Advisory Breakdown

Remaining tool-catalog debt is grouped by authority so MCP specification failures are not conflated with guidance or AI-oriented heuristics.

| Authority | Active Rules | Coverage | Highest Severity | Representative Gaps |
| :--- | :---: | :--- | :---: | :--- |
| Spec | 1 | 3/3 (100%) | 🔵 Low | tools/call result.isError field not present |
| Guideline | 1 | 3/3 (100%) | 🔵 Low | Tool 'microsoft_code_sample_search' does not declare annotations.openWorldHint. |
| Heuristic | 3 | 3/3 (100%) | 🟡 Medium | Tool 'microsoft_code_sample_search': 2/2 string parameters have no enum/pattern/format constraint<br />Tool 'microsoft_code_sample_search': 1/2 string parameters look like fixed-choice fields but do not declare enum/const choices |

### MCP Guideline Findings

These findings capture MCP guidance signals that improve agent safety and UX but are not strict protocol MUST failures.
Coverage shows how prevalent each issue is across the discovered tool catalog so larger servers are judged by rate, not raw count alone.

| Rule ID | Source | Coverage | Severity | Example Components | Finding |
| :--- | :--- | :--- | :---: | :--- | :--- |
| `MCP.GUIDELINE.TOOL.OPENWORLD_HINT_MISSING` | `guideline` | 3/3 (100%) | 🔵 Low | `microsoft_code_sample_search`, `microsoft_docs_fetch`, `microsoft_docs_search` | Tool 'microsoft_code_sample_search' does not declare annotations.openWorldHint. |

## 14. Resource Capabilities

**Resources Discovered:** 0

## 15. AI Readiness Assessment

**AI Readiness Score:** ⚠️ **74/100** (Fair)

This score measures how well the server's tool schemas are optimized for consumption by AI agents (LLMs).

| Criterion | What It Measures |
| :--- | :--- |
| **Description Completeness** | Do all tool parameters have descriptions? Missing descriptions increase hallucination risk. |
| **Type Specificity** | Are `string` parameters constrained with `enum`, `pattern`, or `format`? Vague types cause incorrect arguments. |
| **Token Efficiency** | Is the `tools/list` payload within context window limits? Estimated: ~1,241 tokens. |

### Findings

| Rule ID | Source | Coverage | Severity | Finding |
| :--- | :--- | :--- | :---: | :--- |
| `AI.TOOL.SCHEMA.STRING_CONSTRAINT_MISSING` | `heuristic` | 3/3 (100%) | 🟡 Medium | Tool 'microsoft_code_sample_search': 2/2 string parameters have no enum/pattern/format constraint |
| `AI.TOOL.SCHEMA.ENUM_COVERAGE_MISSING` | `heuristic` | 1/3 (33%) | 🔵 Low | Tool 'microsoft_code_sample_search': 1/2 string parameters look like fixed-choice fields but do not declare enum/const choices |
| `AI.TOOL.SCHEMA.FORMAT_HINT_MISSING` | `heuristic` | 1/3 (33%) | 🔵 Low | Tool 'microsoft_docs_fetch': 1/1 string parameters look like structured values but do not declare format/pattern hints |

## 16. Optional MCP Capabilities

These probes check whether the server supports optional MCP features beyond the core primitives.

| Capability | Status |
| :--- | :--- |
| `declared capabilities` | ➖ logging, prompts, resources, resources.listChanged, tools |
| `roots/list` | ➖ not supported |
| `logging/setLevel` | ✅ supported |
| `sampling/createMessage` | ➖ not supported |
| `completion/complete` | ➖ not supported |

## 17. Performance Metrics

| Metric | Result | Verdict |
| :--- | :--- | :--- |
| **Avg Latency** | 75.58ms | 🚀 Excellent |
| **P95 Latency** | 94.00ms | - |
| **Throughput** | 13.20 req/sec | - |
| **Error Rate** | 0.00% | ✅ Clean |
| **Requests** | 500/500 successful | - |
| **Probe Rounds** | 2 | ℹ️ Calibrated |

## 18. Recommendations

- 💡 Spec: If the server accepts batch requests, it must process each request independently and return an array of responses in any order. Ensure batch handling does not drop or duplicate responses. See https://www.jsonrpc.org/specification#batch
- 💡 Spec: This specific error code test failed. Ensure the server returns standard JSON-RPC error codes for known error conditions: -32700 (parse error), -32600 (invalid request), -32601 (method not found), -32602 (invalid params), -32603 (internal error). See https://spec.modelcontextprotocol.io/specification/2025-11-25/basic/json-rpc#errors
- 💡 Heuristic: Constrain string parameters with enum, pattern, or format metadata when possible Gap affects 3/3 tools
- 💡 Guideline: Declare openWorldHint so agents can reason about whether execution can affect unknown external systems Gap affects 3/3 tools
- 💡 Spec: Include result.isError in tool responses so clients can distinguish failures from normal payloads Gap affects 3/3 tools
- 💡 Heuristic: Use enum, const, oneOf, or anyOf so agents can choose from explicit valid values instead of guessing Gap affects 1/3 tool

---
*Produced with [MCP Validator](https://github.com/navalerakesh/mcp-validation-security) · [McpVal on NuGet](https://www.nuget.org/packages/McpVal#versions-body-tab)*
