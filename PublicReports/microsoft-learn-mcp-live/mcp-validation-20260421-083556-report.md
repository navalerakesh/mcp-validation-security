# MCP Server Compliance & Validation Report
**Generated:** 2026-04-21 08:35:56 UTC

## 1. Executive Summary

| Metric | Value |
| :--- | :--- |
| **Server Endpoint** | `https://learn.microsoft.com/api/mcp` |
| **Validation ID** | `8d2a92ab-32bd-4915-9388-835c6638b439` |
| **Overall Status** | ✅ **Passed** |
| **Compliance Score** | **85.6%** |
| **Compliance Profile** | `Public (Inferred)` |
| **Duration** | 21.04s |
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
| **Handshake Duration** | 623.0 ms |
| **Negotiated Protocol** | `2025-06-18` |
| **Observed Server Version** | `1.0.0` |
| **Server Profile Resolution** | `Public (Inferred)` |

Validation began from a clean bootstrap with no deferred connectivity risk carried into later categories.

## 3. Priority Findings

These are the highest-signal outcomes from this validation run.

- MCP-SEC-002: Potential enumeration: Different HTTP status codes for random vs common resources.
- MCP-PROTO-JSONRPC: Batch processing implementation is inconsistent or incomplete
- MCP-PROTO-ERR: JSON-RPC Error Code Violation: Parse Error

## 4. Action Hints

Compact next-step guidance derived from the highest-signal evidence in this run.

- Protocol: Batch processing implementation is inconsistent or incomplete.
- Protocol: JSON-RPC Error Code Violation: Parse Error.
- Security: Review MCP implementation against security best practices.
- Review and address failed test cases to improve server stability.

## 5. MCP Trust Assessment

Multi-dimensional evaluation of server trustworthiness for AI agent consumption.
Trust level is determined by a **weighted multi-dimensional score** and then capped by confirmed blockers such as critical security failures or MCP MUST failures.

| Dimension | Score | What It Measures |
| :--- | :---: | :--- |
| **Protocol Compliance** | 78% | MCP spec adherence, JSON-RPC compliance, response structures |
| **Security Posture** | 85% | Auth compliance, injection resistance, attack surface |
| **AI Safety** | 48% | Schema quality, destructive tool detection, exfiltration risk, prompt injection surface |
| **Operational Readiness** | 100% | Latency, throughput, error rate, stability |

### AI Boundary Findings

These findings go **beyond MCP protocol** to assess how AI agents interact with this server.

| Category | Component | Severity | Finding |
| :--- | :--- | :---: | :--- |
| Exfiltration | `microsoft_docs_fetch` | 🟠 High | Tool 'microsoft_docs_fetch' accepts URI-like or outbound-target parameters that could be used for data exfiltration (evidence: 'url'). |
| PromptInjection | `microsoft_code_sample_search` | 🔴 Critical | Tool 'microsoft_code_sample_search' metadata contains prompt-injection-like language: 'you are'. |
| Injection | `SecurityValidator` | 🟠 High | 1 injection attack(s) reflected back in server response. AI agents consuming this output may execute malicious content. |

### Summary

- Destructive tools: **0**
- Data exfiltration risk: **1/3 (33%)**
- Prompt injection surface: **1/3 (33%)**

### MCP Spec Compliance (RFC 2119 Tiers)

| Tier | Passed | Failed | Total | Impact |
| :--- | :---: | :---: | :---: | :--- |
| **MUST** | 6 | 0 | 6 | ✅ Fully compliant |
| **SHOULD** | 4 | 3 | 7 | ⚠️ 3 penalties applied |
| **MAY** | 3 | 2 | 5 | ℹ️ Informational (no score impact) |

#### ⚠️ SHOULD Gaps (Score Penalties)

- SHOULD: String parameters have enum/pattern/format constraints (tools/list)
- SHOULD: tools/call result includes 'isError' field (tools/call)
- SHOULD: Server sanitizes tool outputs (security)

#### ℹ️ Optional Features (MAY)

- ✅ MAY: Server supports logging/setLevel
- ➖ MAY: Server supports sampling/createMessage
- ➖ MAY: Server supports roots/list
- ✅ MAY: Server supports resources/templates/list
- ✅ MAY: Tools include 'annotations' (readOnlyHint, destructiveHint)

## 6. Capability Snapshot

| Probe | Discovered | HTTP Status | Duration | Result |
| :--- | :---: | :---: | :---: | :--- |
| Tools/list | 3 | HTTP 200 | 64.1 ms | ✅ Listed<br/>Call ✅ |
| Resources/list | 0 | HTTP 200 | 91.3 ms | ✅ Listed |
| Prompts/list | 0 | HTTP 200 | 104.5 ms | ✅ Listed |

- **First Tool Probed:** `microsoft_docs_search`

## 7. Scoring Methodology

These notes explain how the overall score and blocking decision were calibrated for this run.

- INFO: Authentication challenge observations are informational for the declared public profile.
- Score meets the preferred target with non-blocking improvement opportunities.

## 8. Compliance Matrix

| Category | Status | Score | Issues |
| :--- | :---: | :---: | :---: |
| Protocol Compliance | ✅ Passed | 78.1% | **4** |
| Security Assessment | ✅ Passed | 85.0% | **1** |
| Tool Validation | ✅ Passed | 100.0% | - |
| Resource Capabilities | ✅ Passed | 100.0% | - |
| Prompt Capabilities | ✅ Passed | 100.0% | - |
| Performance | ❌ Failed | 100.0% | - |

## 9. Performance Calibration

Performance is assessed using a synthetic load probe rather than a workload-specific SLA benchmark.
For public or remote SaaS endpoints, partial failures under synthetic pressure are reported as advisory when they look like capacity limits or edge protections rather than protocol instability.

- Synthetic load probe executed against tools/list using 500 requests at concurrency 100.

## 10. Security Assessment

**Security Score:** 85.0%

### Authentication Analysis
| Scenario | Method | Expected | Actual | Analysis | Status |
| :--- | :--- | :--- | :--- | :--- | :---: |
| No Auth - initialize | `initialize` | Informational (public profile) | 200 + JSON-RPC success | ℹ️ INFO (Public profile): ⚠️ COMPATIBLE: Discovery metadata was exposed without authentication. | ✅ |
| No Auth - logging/setLevel | `logging/setLevel` | Informational (public profile) | 200 + JSON-RPC success | ℹ️ INFO (Public profile): ❌ INSECURE: Sensitive operation succeeded without valid authentication. | ✅ |
| Malformed Token - logging/setLevel | `logging/setLevel` | Informational (public profile) | 200 + JSON-RPC success | ℹ️ INFO (Public profile): ❌ INSECURE: Sensitive operation succeeded without valid authentication. | ✅ |
| Invalid Token - logging/setLevel | `logging/setLevel` | Informational (public profile) | 200 + JSON-RPC success | ℹ️ INFO (Public profile): ❌ INSECURE: Sensitive operation succeeded without valid authentication. | ✅ |
| Token Expired - logging/setLevel | `logging/setLevel` | Informational (public profile) | 200 + JSON-RPC success | ℹ️ INFO (Public profile): ❌ INSECURE: Sensitive operation succeeded without valid authentication. | ✅ |
| Invalid Scope - logging/setLevel | `logging/setLevel` | Informational (public profile) | 200 + JSON-RPC success | ℹ️ INFO (Public profile): ❌ INSECURE: Sensitive operation succeeded without valid authentication. | ✅ |
| Insufficient Permissions - logging/setLevel | `logging/setLevel` | Informational (public profile) | 200 + JSON-RPC success | ℹ️ INFO (Public profile): ❌ INSECURE: Sensitive operation succeeded without valid authentication. | ✅ |
| Revoked Token - logging/setLevel | `logging/setLevel` | Informational (public profile) | 200 + JSON-RPC success | ℹ️ INFO (Public profile): ❌ INSECURE: Sensitive operation succeeded without valid authentication. | ✅ |
| Wrong Audience (RFC 8707) - logging/setLevel | `logging/setLevel` | Informational (public profile) | 200 + JSON-RPC success | ℹ️ INFO (Public profile): ❌ INSECURE: Sensitive operation succeeded without valid authentication. | ✅ |
| No Auth - prompts/list | `prompts/list` | Informational (public profile) | 200 + JSON-RPC success | ℹ️ INFO (Public profile): ⚠️ COMPATIBLE: Discovery metadata was exposed without authentication. | ✅ |
| Malformed Token - prompts/list | `prompts/list` | Informational (public profile) | 200 + JSON-RPC success | ℹ️ INFO (Public profile): ⚠️ COMPATIBLE: Discovery metadata was exposed without authentication. | ✅ |
| Invalid Token - prompts/list | `prompts/list` | Informational (public profile) | 200 + JSON-RPC success | ℹ️ INFO (Public profile): ⚠️ COMPATIBLE: Discovery metadata was exposed without authentication. | ✅ |
| Token Expired - prompts/list | `prompts/list` | Informational (public profile) | 200 + JSON-RPC success | ℹ️ INFO (Public profile): ⚠️ COMPATIBLE: Discovery metadata was exposed without authentication. | ✅ |
| Invalid Scope - prompts/list | `prompts/list` | Informational (public profile) | 200 + JSON-RPC success | ℹ️ INFO (Public profile): ⚠️ COMPATIBLE: Discovery metadata was exposed without authentication. | ✅ |
| Insufficient Permissions - prompts/list | `prompts/list` | Informational (public profile) | 200 + JSON-RPC success | ℹ️ INFO (Public profile): ⚠️ COMPATIBLE: Discovery metadata was exposed without authentication. | ✅ |
| Revoked Token - prompts/list | `prompts/list` | Informational (public profile) | 200 + JSON-RPC success | ℹ️ INFO (Public profile): ⚠️ COMPATIBLE: Discovery metadata was exposed without authentication. | ✅ |
| Wrong Audience (RFC 8707) - prompts/list | `prompts/list` | Informational (public profile) | 200 + JSON-RPC success | ℹ️ INFO (Public profile): ⚠️ COMPATIBLE: Discovery metadata was exposed without authentication. | ✅ |
| No Auth - prompts/get | `prompts/get` | Informational (public profile) | 200 + JSON-RPC error | ℹ️ INFO (Public profile): ⚠️ COMPATIBLE: Authentication appears to be enforced at the application layer instead of via an HTTP challenge. | ✅ |
| Malformed Token - prompts/get | `prompts/get` | Informational (public profile) | 200 + JSON-RPC error | ℹ️ INFO (Public profile): ⚠️ COMPATIBLE: Authentication appears to be enforced at the application layer instead of via an HTTP challenge. | ✅ |
| Invalid Token - prompts/get | `prompts/get` | Informational (public profile) | 200 + JSON-RPC error | ℹ️ INFO (Public profile): ⚠️ COMPATIBLE: Authentication appears to be enforced at the application layer instead of via an HTTP challenge. | ✅ |
| Token Expired - prompts/get | `prompts/get` | Informational (public profile) | 200 + JSON-RPC error | ℹ️ INFO (Public profile): ⚠️ COMPATIBLE: Authentication appears to be enforced at the application layer instead of via an HTTP challenge. | ✅ |
| Invalid Scope - prompts/get | `prompts/get` | Informational (public profile) | 200 + JSON-RPC error | ℹ️ INFO (Public profile): ⚠️ COMPATIBLE: Authentication appears to be enforced at the application layer instead of via an HTTP challenge. | ✅ |
| Insufficient Permissions - prompts/get | `prompts/get` | Informational (public profile) | 200 + JSON-RPC error | ℹ️ INFO (Public profile): ⚠️ COMPATIBLE: Authentication appears to be enforced at the application layer instead of via an HTTP challenge. | ✅ |
| Revoked Token - prompts/get | `prompts/get` | Informational (public profile) | 200 + JSON-RPC error | ℹ️ INFO (Public profile): ⚠️ COMPATIBLE: Authentication appears to be enforced at the application layer instead of via an HTTP challenge. | ✅ |
| Wrong Audience (RFC 8707) - prompts/get | `prompts/get` | Informational (public profile) | 200 + JSON-RPC error | ℹ️ INFO (Public profile): ⚠️ COMPATIBLE: Authentication appears to be enforced at the application layer instead of via an HTTP challenge. | ✅ |
| No Auth - resources/list | `resources/list` | Informational (public profile) | 200 + JSON-RPC success | ℹ️ INFO (Public profile): ⚠️ COMPATIBLE: Discovery metadata was exposed without authentication. | ✅ |
| Malformed Token - resources/list | `resources/list` | Informational (public profile) | 200 + JSON-RPC success | ℹ️ INFO (Public profile): ⚠️ COMPATIBLE: Discovery metadata was exposed without authentication. | ✅ |
| Invalid Token - resources/list | `resources/list` | Informational (public profile) | 200 + JSON-RPC success | ℹ️ INFO (Public profile): ⚠️ COMPATIBLE: Discovery metadata was exposed without authentication. | ✅ |
| Token Expired - resources/list | `resources/list` | Informational (public profile) | 200 + JSON-RPC success | ℹ️ INFO (Public profile): ⚠️ COMPATIBLE: Discovery metadata was exposed without authentication. | ✅ |
| Invalid Scope - resources/list | `resources/list` | Informational (public profile) | 200 + JSON-RPC success | ℹ️ INFO (Public profile): ⚠️ COMPATIBLE: Discovery metadata was exposed without authentication. | ✅ |
| Insufficient Permissions - resources/list | `resources/list` | Informational (public profile) | 200 + JSON-RPC success | ℹ️ INFO (Public profile): ⚠️ COMPATIBLE: Discovery metadata was exposed without authentication. | ✅ |
| Revoked Token - resources/list | `resources/list` | Informational (public profile) | 200 + JSON-RPC success | ℹ️ INFO (Public profile): ⚠️ COMPATIBLE: Discovery metadata was exposed without authentication. | ✅ |
| Wrong Audience (RFC 8707) - resources/list | `resources/list` | Informational (public profile) | 200 + JSON-RPC success | ℹ️ INFO (Public profile): ⚠️ COMPATIBLE: Discovery metadata was exposed without authentication. | ✅ |
| No Auth - resources/read | `resources/read` | Informational (public profile) | 200 + JSON-RPC error | ℹ️ INFO (Public profile): ⚠️ COMPATIBLE: Authentication appears to be enforced at the application layer instead of via an HTTP challenge. | ✅ |
| Malformed Token - resources/read | `resources/read` | Informational (public profile) | 200 + JSON-RPC error | ℹ️ INFO (Public profile): ⚠️ COMPATIBLE: Authentication appears to be enforced at the application layer instead of via an HTTP challenge. | ✅ |
| Invalid Token - resources/read | `resources/read` | Informational (public profile) | 200 + JSON-RPC error | ℹ️ INFO (Public profile): ⚠️ COMPATIBLE: Authentication appears to be enforced at the application layer instead of via an HTTP challenge. | ✅ |
| Token Expired - resources/read | `resources/read` | Informational (public profile) | 200 + JSON-RPC error | ℹ️ INFO (Public profile): ⚠️ COMPATIBLE: Authentication appears to be enforced at the application layer instead of via an HTTP challenge. | ✅ |
| Invalid Scope - resources/read | `resources/read` | Informational (public profile) | 200 + JSON-RPC error | ℹ️ INFO (Public profile): ⚠️ COMPATIBLE: Authentication appears to be enforced at the application layer instead of via an HTTP challenge. | ✅ |
| Insufficient Permissions - resources/read | `resources/read` | Informational (public profile) | 200 + JSON-RPC error | ℹ️ INFO (Public profile): ⚠️ COMPATIBLE: Authentication appears to be enforced at the application layer instead of via an HTTP challenge. | ✅ |
| Revoked Token - resources/read | `resources/read` | Informational (public profile) | 200 + JSON-RPC error | ℹ️ INFO (Public profile): ⚠️ COMPATIBLE: Authentication appears to be enforced at the application layer instead of via an HTTP challenge. | ✅ |
| Wrong Audience (RFC 8707) - resources/read | `resources/read` | Informational (public profile) | 200 + JSON-RPC error | ℹ️ INFO (Public profile): ⚠️ COMPATIBLE: Authentication appears to be enforced at the application layer instead of via an HTTP challenge. | ✅ |
| No Auth - tools/list | `tools/list` | Informational (public profile) | 200 + JSON-RPC success | ℹ️ INFO (Public profile): ⚠️ COMPATIBLE: Discovery metadata was exposed without authentication. | ✅ |
| Malformed Token - tools/list | `tools/list` | Informational (public profile) | 200 + JSON-RPC success | ℹ️ INFO (Public profile): ⚠️ COMPATIBLE: Discovery metadata was exposed without authentication. | ✅ |
| Invalid Token - tools/list | `tools/list` | Informational (public profile) | 200 + JSON-RPC success | ℹ️ INFO (Public profile): ⚠️ COMPATIBLE: Discovery metadata was exposed without authentication. | ✅ |
| Token Expired - tools/list | `tools/list` | Informational (public profile) | 200 + JSON-RPC success | ℹ️ INFO (Public profile): ⚠️ COMPATIBLE: Discovery metadata was exposed without authentication. | ✅ |
| Invalid Scope - tools/list | `tools/list` | Informational (public profile) | 200 + JSON-RPC success | ℹ️ INFO (Public profile): ⚠️ COMPATIBLE: Discovery metadata was exposed without authentication. | ✅ |
| Insufficient Permissions - tools/list | `tools/list` | Informational (public profile) | 200 + JSON-RPC success | ℹ️ INFO (Public profile): ⚠️ COMPATIBLE: Discovery metadata was exposed without authentication. | ✅ |
| Revoked Token - tools/list | `tools/list` | Informational (public profile) | 200 + JSON-RPC success | ℹ️ INFO (Public profile): ⚠️ COMPATIBLE: Discovery metadata was exposed without authentication. | ✅ |
| Wrong Audience (RFC 8707) - tools/list | `tools/list` | Informational (public profile) | 200 + JSON-RPC success | ℹ️ INFO (Public profile): ⚠️ COMPATIBLE: Discovery metadata was exposed without authentication. | ✅ |
| No Auth - tools/call | `tools/call` | Informational (public profile) | 200 + JSON-RPC error | ℹ️ INFO (Public profile): ⚠️ COMPATIBLE: Authentication appears to be enforced at the application layer instead of via an HTTP challenge. | ✅ |
| Malformed Token - tools/call | `tools/call` | Informational (public profile) | 200 + JSON-RPC error | ℹ️ INFO (Public profile): ⚠️ COMPATIBLE: Authentication appears to be enforced at the application layer instead of via an HTTP challenge. | ✅ |
| Invalid Token - tools/call | `tools/call` | Informational (public profile) | 200 + JSON-RPC error | ℹ️ INFO (Public profile): ⚠️ COMPATIBLE: Authentication appears to be enforced at the application layer instead of via an HTTP challenge. | ✅ |
| Token Expired - tools/call | `tools/call` | Informational (public profile) | 200 + JSON-RPC error | ℹ️ INFO (Public profile): ⚠️ COMPATIBLE: Authentication appears to be enforced at the application layer instead of via an HTTP challenge. | ✅ |
| Invalid Scope - tools/call | `tools/call` | Informational (public profile) | 200 + JSON-RPC error | ℹ️ INFO (Public profile): ⚠️ COMPATIBLE: Authentication appears to be enforced at the application layer instead of via an HTTP challenge. | ✅ |
| Insufficient Permissions - tools/call | `tools/call` | Informational (public profile) | 200 + JSON-RPC error | ℹ️ INFO (Public profile): ⚠️ COMPATIBLE: Authentication appears to be enforced at the application layer instead of via an HTTP challenge. | ✅ |
| Revoked Token - tools/call | `tools/call` | Informational (public profile) | 200 + JSON-RPC error | ℹ️ INFO (Public profile): ⚠️ COMPATIBLE: Authentication appears to be enforced at the application layer instead of via an HTTP challenge. | ✅ |
| Wrong Audience (RFC 8707) - tools/call | `tools/call` | Informational (public profile) | 200 + JSON-RPC error | ℹ️ INFO (Public profile): ⚠️ COMPATIBLE: Authentication appears to be enforced at the application layer instead of via an HTTP challenge. | ✅ |

### Adversarial Input Handling

> **Legend:**
> * **REFLECTED / UNSAFE ECHO**: The server reflected the payload back without sanitization. No execution confirmed.
> * **BLOCKED**: The server correctly rejected or sanitized the input.
> * **SKIPPED**: Test could not be executed (e.g., no tools discovered for injection target).

| Attack Vector | Description | Result | Analysis |
| :--- | :--- | :---: | :--- |
| INJ-001 (Input Validation) | Simulated INJ-001 (Input Validation) attack on microsoft_docs_search | 🛡️ BLOCKED | `<HTML><HEAD> <TITLE>Access Denied</TITLE> </HEAD><BODY> <H1>Access Denied</H1>   You don't have permission to access "http&#58;&#47;&#47;learn&#46;microsoft&#46;com&#47;api&#47;mcp" on this server.<P> Reference&#32;&#35;18&#46;49d02e17&#46;1776760541&#46;ac037a1f <P>https&#58;&#47;&#47;errors&#46;edgesuite&#46;net&#47;18&#46;49d02e17&#46;1776760541&#46;ac037a1f</P> </BODY> </HTML> ` |
| INJ-002 (Input Validation) | Simulated INJ-002 (Input Validation) attack on microsoft_docs_search | 🛡️ BLOCKED | `<HTML><HEAD> <TITLE>Access Denied</TITLE> </HEAD><BODY> <H1>Access Denied</H1>   You don't have permission to access "http&#58;&#47;&#47;learn&#46;microsoft&#46;com&#47;api&#47;mcp" on this server.<P> Reference&#32;&#35;18&#46;49d02e17&#46;1776760541&#46;ac036eb5 <P>https&#58;&#47;&#47;errors&#46;edgesuite&#46;net&#47;18&#46;49d02e17&#46;1776760541&#46;ac036eb5</P> </BODY> </HTML> ` |
| INJ-003 (Input Validation) | Simulated INJ-003 (Input Validation) attack on microsoft_docs_search | 🛡️ BLOCKED | `<HTML><HEAD> <TITLE>Access Denied</TITLE> </HEAD><BODY> <H1>Access Denied</H1>   You don't have permission to access "http&#58;&#47;&#47;learn&#46;microsoft&#46;com&#47;api&#47;mcp" on this server.<P> Reference&#32;&#35;18&#46;49d02e17&#46;1776760541&#46;ac037a81 <P>https&#58;&#47;&#47;errors&#46;edgesuite&#46;net&#47;18&#46;49d02e17&#46;1776760541&#46;ac037a81</P> </BODY> </HTML> ` |
| MCP-SEC-001 | JSON-RPC Error Smuggling | 🛡️ BLOCKED | `Server handled malformed requests gracefully with standard errors.` |
| MCP-SEC-002 | Metadata Enumeration | ⚠️ REFLECTED / UNSAFE ECHO | `Potential enumeration: Different HTTP status codes for random vs common resources.` |
| MCP-SEC-003 | Schema Confusion | 🛡️ BLOCKED | `Server correctly rejected invalid schema.` |
| MCP-AI-001 | Hallucination Fuzzing | 🛡️ BLOCKED | `Error clarity: 55/100 (Fair). Error messages partially helpful for AI.` |

## 11. Protocol Compliance

**Status Detail:** declared capabilities: logging, prompts, resources, resources.listChanged, tools | roots/list: not supported | logging/setLevel: supported | sampling/createMessage: not supported | completion/complete: not supported

### Compliance Violations
| ID | Source | Description | Severity |
| :--- | :--- | :--- | :---: |
| MCP-PROTO-ERR | `spec` | JSON-RPC Error Code Violation: Parse Error | Low |
| MCP-PROTO-ERR | `spec` | JSON-RPC Error Code Violation: Invalid Request - Missing jsonrpc | Low |
| MCP-PROTO-ERR | `spec` | JSON-RPC Error Code Violation: Invalid Params | Low |
| MCP-PROTO-JSONRPC | `spec` | Batch processing implementation is inconsistent or incomplete | Medium |

## 12. Tool Validation

**Tools Discovered:** 3

### Tool: `tools/list (Schema Compliance)`

**Status:** ✅ Passed
**Execution Time:** 64.14ms
---

### Tool: `microsoft_docs_search`

**Status:** ✅ Passed
**Execution Time:** 464.63ms

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
**Execution Time:** 742.91ms

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
**Execution Time:** 72.68ms

#### Tool Metadata
| Property | Value |
| :--- | :--- |
| Display Title | Microsoft Docs Fetch |
| readOnlyHint | True |
| destructiveHint | False |
| idempotentHint | True |

---

### MCP Guideline Findings

These findings capture MCP guidance signals that improve agent safety and UX but are not strict protocol MUST failures.
Coverage shows how prevalent each issue is across the discovered tool catalog so larger servers are judged by rate, not raw count alone.

| Rule ID | Source | Coverage | Severity | Example Components | Finding |
| :--- | :--- | :--- | :---: | :--- | :--- |
| `MCP.GUIDELINE.TOOL.OPENWORLD_HINT_MISSING` | `guideline` | 3/3 (100%) | 🔵 Low | `microsoft_code_sample_search`, `microsoft_docs_fetch`, `microsoft_docs_search` | Tool 'microsoft_code_sample_search' does not declare annotations.openWorldHint. |

## 13. Resource Capabilities

**Resources Discovered:** 0

## 14. AI Readiness Assessment

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

## 15. Optional MCP Capabilities

These probes check whether the server supports optional MCP features beyond the core primitives.

| Capability | Status |
| :--- | :--- |
| `declared capabilities` | ➖ logging, prompts, resources, resources.listChanged, tools |
| `roots/list` | ➖ not supported |
| `logging/setLevel` | ✅ supported |
| `sampling/createMessage` | ➖ not supported |
| `completion/complete` | ➖ not supported |

## 16. Performance Metrics

| Metric | Result | Verdict |
| :--- | :--- | :--- |
| **Avg Latency** | 83.61ms | 🚀 Excellent |
| **P95 Latency** | 123.00ms | - |
| **Throughput** | 68.17 req/sec | - |
| **Error Rate** | 0.00% | ✅ Clean |

## 17. Recommendations

- 💡 Review and address failed test cases to improve server stability
- 💡 Ensure full compliance with MCP protocol specification

---
*Report generated by mcpval — MCP Validator*
