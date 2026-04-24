# MCP Server Compliance & Validation Report
**Generated:** 2026-04-24 20:01:07 UTC

## 1. Executive Summary

| Metric | Value |
| :--- | :--- |
| **Server Endpoint** | `https://learn.microsoft.com/api/mcp` |
| **Validation ID** | `0f983109-89c9-46fa-b5ad-6ef4d528d5c3` |
| **Overall Status** | ❌ **Failed** |
| **Baseline Verdict** | **Reject** |
| **Protocol Verdict** | **Reject** |
| **Coverage Verdict** | **Trusted** |
| **Compliance Score** | **91.4%** |
| **Compliance Profile** | `Public (Inferred)` |
| **Duration** | 19.39s |
| **Transport** | HTTP |
| **Session Bootstrap** | ✅ Healthy |
| **Deferred Validation** | No — validation started from a clean bootstrap state. |
| **MCP Protocol Version (Effective)** | `2025-06-18` |
| **Benchmark Trust Level** | 🔵 **L4: Trusted — Meets enterprise AI safety requirements** |

## 2. Connectivity & Session Bootstrap

This section explains how the validator established initial connectivity and whether validation started from a clean initialize handshake or a deferred advisory state.

| Bootstrap Signal | Value |
| :--- | :--- |
| **Bootstrap State** | ✅ Healthy |
| **Validation Proceeded Under Deferment** | No — validation started from a clean bootstrap state. |
| **Initialize Handshake** | ✅ Initialize handshake succeeded. |
| **Handshake HTTP Status** | `HTTP 200` |
| **Handshake Duration** | 744.1 ms |
| **Negotiated Protocol** | `2025-06-18` |
| **Observed Server Version** | `1.0.0` |
| **Server Profile Resolution** | `Public (Inferred)` |

Validation began from a clean bootstrap with no deferred connectivity risk carried into later categories.

## 3. Priority Findings

These are the highest-signal outcomes from this validation run.

- Policy balanced blocked the run: Balanced policy blocked the validation result with 17 unsuppressed signal(s).
- Tool 'microsoft_docs_fetch' accepts URI-like or outbound-target parameters that could be used for data exfiltration (evidence: 'url').
- JSON-RPC Error Code Violation: Parse Error
- MCP-PROTO-ERR: JSON-RPC Error Code Violation: Parse Error
- MCP-PROTO-ERR: JSON-RPC Error Code Violation: Invalid Request - Missing jsonrpc

## 4. Action Hints

Compact next-step guidance derived from the highest-signal evidence in this run.

- Exfiltration: Tool 'microsoft_docs_fetch' accepts URI-like or outbound-target parameters that could be used for data exfiltration (evidence: 'url').
- JSON-RPC Compliance: JSON-RPC Error Code Violation: Parse Error.
- Protocol: This specific error code test failed. Ensure the server returns standard JSON-RPC error codes for known error conditions: -32700 (parse e....
- Spec: This specific error code test failed. Ensure the server returns standard JSON-RPC error codes for known error conditions: -32700 (parse error....

## 5. Deterministic Verdicts

These verdicts are the authoritative gate for pass/fail and policy decisions. Weighted benchmark scores remain descriptive only.

| Lane | Verdict | Meaning |
| :--- | :--- | :--- |
| **Baseline** | **Reject** | Overall deterministic gate across blocking findings and coverage debt. |
| **Protocol** | **Reject** | Protocol correctness and contract integrity. |
| **Coverage** | **Trusted** | Whether enabled validation surfaces produced authoritative evidence. |

Baseline=Reject; Protocol=Reject; Coverage=Trusted; BlockingDecisions=17.

### Blocking Decisions

- **Reject** [Exfiltration] `microsoft_docs_fetch`: Tool 'microsoft_docs_fetch' accepts URI-like or outbound-target parameters that could be used for data exfiltration (evidence: 'url').
- **Reject** [JSON-RPC Compliance] `JSON-RPC Compliance`: JSON-RPC Error Code Violation: Parse Error
- **Reject** [JSON-RPC Compliance] `JSON-RPC Compliance`: JSON-RPC Error Code Violation: Invalid Request - Missing jsonrpc
- **Reject** [JSON-RPC Compliance] `JSON-RPC Compliance`: JSON-RPC Error Code Violation: Invalid Params
- **Reject** [Protocol] `invalid-request`: Error-handling scenario 'Graceful Degradation On Invalid Request' did not return the expected JSON-RPC error code -32600.
- **Reject** [Protocol] `malformed-json`: Error-handling scenario 'Malformed JSON Payload' did not return the expected JSON-RPC error code -32700.
- **Review Required** [TierCheck] `tools/call`: SHOULD requirement failed: SHOULD: tools/call result includes 'isError' field
- **Review Required** [TierCheck] `tools/list`: SHOULD requirement failed: SHOULD: String parameters have enum/pattern/format constraints

## 6. Benchmark Trust Profile

Multi-dimensional benchmarking view of server trustworthiness for AI agent consumption.
This section is descriptive only. Release gating and pass/fail status are driven by the deterministic verdicts above, not by weighted trust averages.

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

## 7. Client Profile Compatibility

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

## 8. Validation Envelope

This section exposes the structured validation envelope used for layered reporting, coverage tracking, and pack provenance.

### Assessment Layers

| Layer | Status | Findings | Summary |
| :--- | :--- | :---: | :--- |
| `protocol-core` | ✅ Passed | 1 | declared capabilities: logging, prompts, resources, resources.listChanged, tools \| roots/list: not supported \| logging/setLevel: supported \| sampling/createMessage: not supported \| completion/complete: not supported |
| `tool-surface` | ✅ Passed | 8 | - |
| `resource-surface` | ✅ Passed | 0 | - |
| `prompt-surface` | ✅ Passed | 0 | - |
| `security-boundaries` | ✅ Passed | 0 | - |
| `performance` | ✅ Passed | 1 | - |
| `error-handling` | ❌ Failed | 2 | Validated 5 error scenario(s); 3 handled correctly. |
| `client-profiles` | ✅ Passed | 0 | Evaluated 5 profile(s); 5 warning profile(s); 0 incompatible profile(s). |

### Scenario Outcomes

| Scenario | Status | Findings | Summary |
| :--- | :--- | :---: | :--- |
| `bootstrap-initialize-handshake` | ✅ Passed | 0 | Bootstrap health checks completed successfully. |
| `protocol-compliance-review` | ✅ Passed | 1 | declared capabilities: logging, prompts, resources, resources.listChanged, tools \| roots/list: not supported \| logging/setLevel: supported \| sampling/createMessage: not supported \| completion/complete: not supported |
| `tool-catalog-smoke` | ✅ Passed | 11 | Tool validation completed. |
| `resource-catalog-smoke` | ✅ Passed | 0 | No resources were discovered during validation. |
| `prompt-catalog-smoke` | ✅ Passed | 0 | No prompts were discovered during validation. |
| `security-authentication-challenge` | ➖ Skipped | 0 | Evaluated 57 authentication challenge scenario(s). |
| `security-attack-simulations` | ➖ Skipped | 0 | Evaluated 7 attack simulation(s). |
| `error-handling-matrix` | ❌ Failed | 2 | Validated 5 error scenario(s); 3 handled correctly. |

### Coverage Declarations

| Layer | Scope | Status | Reason |
| :--- | :--- | :--- | :--- |
| `protocol-core` | `json-rpc` | `Covered` | - |
| `tool-surface` | `tools/list` | `Covered` | - |
| `resource-surface` | `resources/list` | `Covered` | - |
| `prompt-surface` | `prompts/list` | `Covered` | - |
| `security-boundaries` | `security-assessment` | `Covered` | - |
| `performance` | `load-testing` | `Covered` | - |
| `error-handling` | `error-handling` | `Covered` | - |
| `bootstrap` | `bootstrap-initialize-handshake` | `Covered` | - |
| `protocol-core` | `protocol-compliance-review` | `Covered` | - |
| `tool-surface` | `tool-catalog-smoke` | `Covered` | - |
| `resource-surface` | `resource-catalog-smoke` | `Covered` | - |
| `prompt-surface` | `prompt-catalog-smoke` | `Covered` | - |
| `security-boundaries` | `security-authentication-challenge` | `Covered` | - |
| `security-boundaries` | `security-attack-simulations` | `Covered` | - |
| `error-handling` | `error-handling-matrix` | `Covered` | - |
| `client-profiles` | `claude-code,vscode-copilot-agent,github-copilot-cli,github-copilot-cloud-agent,visual-studio-copilot` | `Covered` | - |

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
| `protocol-mcp-proto-jsonrpc` | `protocol-core` | `JSON-RPC Compliance` | `protocol-violation` | Batch processing implementation is inconsistent or incomplete |
| `tool-tools-list-schema-compliance` | `tool-surface` | `tools/list (Schema Compliance)` | `tool-result` | ⚠️ Schema validation warning: tools/list schema could not be fully processed |
| `tool-microsoft-docs-search` | `tool-surface` | `microsoft_docs_search` | `tool-result` | ℹ️ MCP Note: result.isError field not present (SHOULD be included per spec) |
| `tool-microsoft-code-sample-search` | `tool-surface` | `microsoft_code_sample_search` | `tool-result` | ℹ️ MCP Note: result.isError field not present (SHOULD be included per spec) |
| `tool-microsoft-docs-fetch` | `tool-surface` | `microsoft_docs_fetch` | `tool-result` | ℹ️ MCP Note: result.isError field not present (SHOULD be included per spec) |
| `auth-no-auth-initialize` | `security-boundaries` | `initialize` | `authentication-scenario` | ℹ️ INFO (Public profile): ⚠️ COMPATIBLE: Discovery metadata was exposed without authentication. |
| `auth-no-auth-logging-setlevel` | `security-boundaries` | `logging/setLevel` | `authentication-scenario` | ℹ️ INFO (Public profile): ❌ INSECURE: Sensitive operation succeeded without valid authentication. |
| `auth-malformed-token-logging-setlevel` | `security-boundaries` | `logging/setLevel` | `authentication-scenario` | ℹ️ INFO (Public profile): ❌ INSECURE: Sensitive operation succeeded without valid authentication. |
| `auth-invalid-token-logging-setlevel` | `security-boundaries` | `logging/setLevel` | `authentication-scenario` | ℹ️ INFO (Public profile): ❌ INSECURE: Sensitive operation succeeded without valid authentication. |
| `auth-token-expired-logging-setlevel` | `security-boundaries` | `logging/setLevel` | `authentication-scenario` | ℹ️ INFO (Public profile): ❌ INSECURE: Sensitive operation succeeded without valid authentication. |
| `auth-invalid-scope-logging-setlevel` | `security-boundaries` | `logging/setLevel` | `authentication-scenario` | ℹ️ INFO (Public profile): ❌ INSECURE: Sensitive operation succeeded without valid authentication. |
| `auth-insufficient-permissions-logging-setlevel` | `security-boundaries` | `logging/setLevel` | `authentication-scenario` | ℹ️ INFO (Public profile): ❌ INSECURE: Sensitive operation succeeded without valid authentication. |
| `auth-revoked-token-logging-setlevel` | `security-boundaries` | `logging/setLevel` | `authentication-scenario` | ℹ️ INFO (Public profile): ❌ INSECURE: Sensitive operation succeeded without valid authentication. |
| `auth-wrong-audience-rfc-8707-logging-setlevel` | `security-boundaries` | `logging/setLevel` | `authentication-scenario` | ℹ️ INFO (Public profile): ❌ INSECURE: Sensitive operation succeeded without valid authentication. |
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
| `attack-inj-001-input-validation` | `security-boundaries` | `INJ-001 (Input Validation)` | `attack-simulation` | <HTML><HEAD> <TITLE>Access Denied</TITLE> </HEAD><BODY> <H1>Access Denied</H1>   You don't have permission to access "http&#58;&#47;&#47;learn&#46;microsoft&#46;com&#47;api&#47;mcp" on this server.<P> Reference&#32;&#35;18&#46;49d02e17&#46;1777060857&#46;1e02bf5c <P>https&#58;&#47;&#47;errors&#46;edgesuite&#46;net&#47;18&#46;49d02e17&#46;1777060857&#46;1e02bf5c</P> </BODY> </HTML>  |
| `attack-inj-002-input-validation` | `security-boundaries` | `INJ-002 (Input Validation)` | `attack-simulation` | <HTML><HEAD> <TITLE>Access Denied</TITLE> </HEAD><BODY> <H1>Access Denied</H1>   You don't have permission to access "http&#58;&#47;&#47;learn&#46;microsoft&#46;com&#47;api&#47;mcp" on this server.<P> Reference&#32;&#35;18&#46;49d02e17&#46;1777060857&#46;1e02c0d6 <P>https&#58;&#47;&#47;errors&#46;edgesuite&#46;net&#47;18&#46;49d02e17&#46;1777060857&#46;1e02c0d6</P> </BODY> </HTML>  |
| `attack-inj-003-input-validation` | `security-boundaries` | `INJ-003 (Input Validation)` | `attack-simulation` | <HTML><HEAD> <TITLE>Access Denied</TITLE> </HEAD><BODY> <H1>Access Denied</H1>   You don't have permission to access "http&#58;&#47;&#47;learn&#46;microsoft&#46;com&#47;api&#47;mcp" on this server.<P> Reference&#32;&#35;18&#46;49d02e17&#46;1777060857&#46;1e02c25c <P>https&#58;&#47;&#47;errors&#46;edgesuite&#46;net&#47;18&#46;49d02e17&#46;1777060857&#46;1e02c25c</P> </BODY> </HTML>  |
| `attack-mcp-sec-001` | `security-boundaries` | `MCP-SEC-001` | `attack-simulation` | Server handled malformed requests gracefully with standard errors. |
| `attack-mcp-sec-002` | `security-boundaries` | `MCP-SEC-002` | `attack-simulation` | Skipped: Metadata enumeration requires an advertised concrete resource URI; no like-for-like resource surface was available. |
| `attack-mcp-sec-003` | `security-boundaries` | `MCP-SEC-003` | `attack-simulation` | Server correctly rejected invalid schema. |
| `attack-mcp-ai-001` | `security-boundaries` | `MCP-AI-001` | `attack-simulation` | Error clarity: 55/100 (Fair). Error messages partially helpful for AI. |
| `error-invalid-method-call` | `error-handling` | `invalid-method` | `error-scenario` | HTTP 200; JSON-RPC error -32601: Method 'nonexistent_method_12345' is not available. |
| `error-malformed-json-payload` | `error-handling` | `malformed-json` | `error-scenario` | HTTP 500; 'i' is an invalid start of a property name. Expected a '"'. Path: $ \| LineNumber: 0 \| BytePositionInLine: 2. |
| `error-graceful-degradation-on-invalid-request` | `error-handling` | `invalid-request` | `error-scenario` | HTTP 500; Invalid or missing jsonrpc version |
| `error-timeout-handling-recovery` | `error-handling` | `timeout-handling` | `error-scenario` | Validator cancelled the HTTP request after the induced timeout window elapsed. |
| `error-connection-interruption-recovery` | `error-handling` | `connection-interruption` | `error-scenario` | Validator aborted the HTTP request body mid-stream to simulate a connection interruption. |

## 9. Capability Snapshot

| Probe | Discovered | HTTP Status | Duration | Result |
| :--- | :---: | :---: | :---: | :--- |
| Tools/list | 3 | HTTP 200 | 79.2 ms | ✅ Listed<br/>Call ✅ |
| Resources/list | 0 | HTTP 200 | 86.9 ms | ✅ Listed |
| Prompts/list | 0 | HTTP 200 | 91.5 ms | ✅ Listed |

- **First Tool Probed:** `microsoft_docs_search`

## 10. Scoring Methodology

These notes explain how the overall score and blocking decision were calibrated for this run.

- INFO: Authentication challenge observations are informational for the declared public profile.

## 11. Compliance Matrix

| Category | Status | Score | Issues |
| :--- | :---: | :---: | :---: |
| Protocol Compliance | ✅ Passed | 78.1% | **4** |
| Security Assessment | ✅ Passed | 100.0% | - |
| Tool Validation | ✅ Passed | 100.0% | - |
| Resource Capabilities | ✅ Passed | 100.0% | - |
| Prompt Capabilities | ✅ Passed | 100.0% | - |
| Performance | ✅ Passed | 100.0% | - |

## 12. Performance Calibration

Performance is assessed using a synthetic load probe rather than a workload-specific SLA benchmark.
For public or remote SaaS endpoints, partial failures under synthetic pressure are reported as advisory when they look like capacity limits or edge protections rather than protocol instability.

- Synthetic load probe executed against tools/list using 20 requests at concurrency 4.

## 13. Security Assessment

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
| INJ-001 (Input Validation) | Simulated INJ-001 (Input Validation) attack on microsoft_docs_search | 🛡️ BLOCKED | `<HTML><HEAD> <TITLE>Access Denied</TITLE> </HEAD><BODY> <H1>Access Denied</H1>   You don't have permission to access "http&#58;&#47;&#47;learn&#46;microsoft&#46;com&#47;api&#47;mcp" on this server.<P> Reference&#32;&#35;18&#46;49d02e17&#46;1777060857&#46;1e02bf5c <P>https&#58;&#47;&#47;errors&#46;edgesuite&#46;net&#47;18&#46;49d02e17&#46;1777060857&#46;1e02bf5c</P> </BODY> </HTML> ` |
| INJ-002 (Input Validation) | Simulated INJ-002 (Input Validation) attack on microsoft_docs_search | 🛡️ BLOCKED | `<HTML><HEAD> <TITLE>Access Denied</TITLE> </HEAD><BODY> <H1>Access Denied</H1>   You don't have permission to access "http&#58;&#47;&#47;learn&#46;microsoft&#46;com&#47;api&#47;mcp" on this server.<P> Reference&#32;&#35;18&#46;49d02e17&#46;1777060857&#46;1e02c0d6 <P>https&#58;&#47;&#47;errors&#46;edgesuite&#46;net&#47;18&#46;49d02e17&#46;1777060857&#46;1e02c0d6</P> </BODY> </HTML> ` |
| INJ-003 (Input Validation) | Simulated INJ-003 (Input Validation) attack on microsoft_docs_search | 🛡️ BLOCKED | `<HTML><HEAD> <TITLE>Access Denied</TITLE> </HEAD><BODY> <H1>Access Denied</H1>   You don't have permission to access "http&#58;&#47;&#47;learn&#46;microsoft&#46;com&#47;api&#47;mcp" on this server.<P> Reference&#32;&#35;18&#46;49d02e17&#46;1777060857&#46;1e02c25c <P>https&#58;&#47;&#47;errors&#46;edgesuite&#46;net&#47;18&#46;49d02e17&#46;1777060857&#46;1e02c25c</P> </BODY> </HTML> ` |
| MCP-SEC-001 | JSON-RPC Error Smuggling | 🛡️ BLOCKED | `Server handled malformed requests gracefully with standard errors.` |
| MCP-SEC-002 | Metadata Enumeration | ⏭️ SKIPPED | `Skipped: Metadata enumeration requires an advertised concrete resource URI; no like-for-like resource surface was available.` |
| MCP-SEC-003 | Schema Confusion | 🛡️ BLOCKED | `Server correctly rejected invalid schema.` |
| MCP-AI-001 | Hallucination Fuzzing | 🛡️ BLOCKED | `Error clarity: 55/100 (Fair). Error messages partially helpful for AI.` |

## 14. Protocol Compliance

**Status Detail:** declared capabilities: logging, prompts, resources, resources.listChanged, tools | roots/list: not supported | logging/setLevel: supported | sampling/createMessage: not supported | completion/complete: not supported

### Compliance Violations
| ID | Source | Description | Severity | Recommendation |
| :--- | :--- | :--- | :---: | :--- |
| MCP-PROTO-ERR | `spec` | JSON-RPC Error Code Violation: Parse Error | High | This specific error code test failed. Ensure the server returns standard JSON-RPC error codes for known error conditions: -32700 (parse error), -32600 (invalid request), -32601 (method not found), -32602 (invalid params), -32603 (internal error). See https://spec.modelcontextprotocol.io/specification/2025-11-25/basic/json-rpc#errors |
| MCP-PROTO-ERR | `spec` | JSON-RPC Error Code Violation: Invalid Request - Missing jsonrpc | High | This specific error code test failed. Ensure the server returns standard JSON-RPC error codes for known error conditions: -32700 (parse error), -32600 (invalid request), -32601 (method not found), -32602 (invalid params), -32603 (internal error). See https://spec.modelcontextprotocol.io/specification/2025-11-25/basic/json-rpc#errors |
| MCP-PROTO-ERR | `spec` | JSON-RPC Error Code Violation: Invalid Params | High | This specific error code test failed. Ensure the server returns standard JSON-RPC error codes for known error conditions: -32700 (parse error), -32600 (invalid request), -32601 (method not found), -32602 (invalid params), -32603 (internal error). See https://spec.modelcontextprotocol.io/specification/2025-11-25/basic/json-rpc#errors |
| MCP-PROTO-JSONRPC | `spec` | Batch processing implementation is inconsistent or incomplete | Medium | If the server accepts batch requests, it must process each request independently and return an array of responses in any order. Ensure batch handling does not drop or duplicate responses. See https://www.jsonrpc.org/specification#batch |

## 15. Tool Validation

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
**Execution Time:** 79.21ms
---

### Tool: `microsoft_docs_search`

**Status:** ✅ Passed
**Execution Time:** 1062.87ms

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
**Execution Time:** 1160.72ms

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
**Execution Time:** 233.98ms

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

## 16. Resource Capabilities

**Resources Discovered:** 0

## 17. AI Readiness Assessment

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

## 18. Optional MCP Capabilities

These probes check whether the server supports optional MCP features beyond the core primitives.

| Capability | Status |
| :--- | :--- |
| `declared capabilities` | ➖ logging, prompts, resources, resources.listChanged, tools |
| `roots/list` | ➖ not supported |
| `logging/setLevel` | ✅ supported |
| `sampling/createMessage` | ➖ not supported |
| `completion/complete` | ➖ not supported |

## 19. Performance Metrics

| Metric | Result | Verdict |
| :--- | :--- | :--- |
| **Avg Latency** | 78.90ms | 🚀 Excellent |
| **P95 Latency** | 96.91ms | - |
| **Throughput** | 12.64 req/sec | - |
| **Error Rate** | 0.00% | ✅ Clean |
| **Requests** | 20/20 successful | - |

## 20. Recommendations

- 💡 Spec: This specific error code test failed. Ensure the server returns standard JSON-RPC error codes for known error conditions: -32700 (parse error), -32600 (invalid request), -32601 (method not found), -32602 (invalid params), -32603 (internal error). See https://spec.modelcontextprotocol.io/specification/2025-11-25/basic/json-rpc#errors
- 💡 Spec: If the server accepts batch requests, it must process each request independently and return an array of responses in any order. Ensure batch handling does not drop or duplicate responses. See https://www.jsonrpc.org/specification#batch
- 💡 Heuristic: Constrain string parameters with enum, pattern, or format metadata when possible Gap affects 3/3 tools
- 💡 Guideline: Declare openWorldHint so agents can reason about whether execution can affect unknown external systems Gap affects 3/3 tools
- 💡 Spec: Include result.isError in tool responses so clients can distinguish failures from normal payloads Gap affects 3/3 tools
- 💡 Heuristic: Use enum, const, oneOf, or anyOf so agents can choose from explicit valid values instead of guessing Gap affects 1/3 tool

---
*Produced with [MCP Validator](https://github.com/navalerakesh/mcp-validation-security) · [McpVal on NuGet](https://www.nuget.org/packages/McpVal#versions-body-tab)*
