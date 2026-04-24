# MCP Server Compliance & Validation Report
**Generated:** 2026-04-24 22:27:42 UTC

## 1. Executive Summary

| Metric | Value |
| :--- | :--- |
| **Server Endpoint** | `https://learn.microsoft.com` |
| **Validation ID** | `27bea45f-1e3e-464b-bc77-f578e248ea97` |
| **Overall Status** | ❌ **Failed** |
| **Baseline Verdict** | **Reject** |
| **Protocol Verdict** | **Reject** |
| **Coverage Verdict** | **Review Required** |
| **Compliance Score** | **40.6%** |
| **Compliance Profile** | `Public (Inferred)` |
| **Duration** | 426.70s |
| **Transport** | HTTP |
| **Session Bootstrap** | ⚠️ Transient Failure |
| **Deferred Validation** | Yes — validation continued with a calibrated advisory bootstrap state. |
| **Benchmark Trust Level** | 🟠 **L2: Caution — Significant gaps in safety or compliance** |

## 2. Connectivity & Session Bootstrap

This section explains how the validator established initial connectivity and whether validation started from a clean initialize handshake or a deferred advisory state.

| Bootstrap Signal | Value |
| :--- | :--- |
| **Bootstrap State** | ⚠️ Transient Failure |
| **Validation Proceeded Under Deferment** | Yes — validation continued with a calibrated advisory bootstrap state. |
| **Initialize Handshake** | ⚠️ Initialize handshake encountered a transient capacity or transport constraint. |
| **Handshake HTTP Status** | `HTTP 429` |
| **Handshake Duration** | 127419.5 ms |
| **Server Profile Resolution** | `Public (Inferred)` |

> **Bootstrap Note:** HTTP 429 Too Many Requests. Body: <HTML><HEAD><TITLE>Too Many Requests</TITLE></HEAD><BODY>
<H1>Too Many Requests</H1>
<P>Reference 0.49d02e17.1777069363.2061c2c8</P>
<P>Your IP: 2601:600:8d00:cd30:7d25:f5d7:b9e4:39b5</P>
</BODY></HTML>


Validation continued because the preflight issue matched a retry-worthy transient constraint rather than a hard endpoint failure.

## 3. Priority Findings

These are the highest-signal outcomes from this validation run.

- Policy balanced blocked the run: Balanced policy blocked the validation result with 15 unsuppressed signal(s).
- MUST requirement failed: MUST: Use standard JSON-RPC error codes — Non-standard error codes
- JSON-RPC Error Code Violation: Parse Error
- Client profile Claude Code: Incompatible. 1 required compatibility check(s) failed; review the affected surfaces before relying on this client profile.
- Client profile VS Code Copilot Agent: Incompatible. 1 required compatibility check(s) failed; review the affected surfaces before relying on this client profile.

## 4. Action Hints

Compact next-step guidance derived from the highest-signal evidence in this run.

- TierCheck: MUST requirement failed: MUST: Use standard JSON-RPC error codes — Non-standard error codes.
- JSON-RPC Compliance: JSON-RPC Error Code Violation: Parse Error.
- Protocol: This specific error code test failed. Ensure the server returns standard JSON-RPC error codes for known error conditions: -32700 (parse e....
- Client compatibility: Claude Code - 1 required compatibility check(s) failed; review the affected surfaces before relying on this client profile.

## 5. Deterministic Verdicts

These verdicts are the authoritative gate for pass/fail and policy decisions. Weighted benchmark scores remain descriptive only.

| Lane | Verdict | Meaning |
| :--- | :--- | :--- |
| **Baseline** | **Reject** | Overall deterministic gate across blocking findings and coverage debt. |
| **Protocol** | **Reject** | Protocol correctness and contract integrity. |
| **Coverage** | **Review Required** | Whether enabled validation surfaces produced authoritative evidence. |

Baseline=Reject; Protocol=Reject; Coverage=ReviewRequired; BlockingDecisions=15.

### Blocking Decisions

- **Reject** [TierCheck] `errors`: MUST requirement failed: MUST: Use standard JSON-RPC error codes — Non-standard error codes
- **Reject** [JSON-RPC Compliance] `JSON-RPC Compliance`: JSON-RPC Error Code Violation: Parse Error
- **Reject** [JSON-RPC Compliance] `JSON-RPC Compliance`: JSON-RPC Error Code Violation: Invalid Request - Missing jsonrpc
- **Reject** [JSON-RPC Compliance] `JSON-RPC Compliance`: JSON-RPC Error Code Violation: Method Not Found
- **Reject** [JSON-RPC Compliance] `JSON-RPC Compliance`: JSON-RPC Error Code Violation: Invalid Params
- **Reject** [JSON-RPC Compliance] `JSON-RPC Compliance`: Request format does not comply with JSON-RPC 2.0 specification
- **Reject** [JSON-RPC Compliance] `JSON-RPC Compliance`: Response format does not comply with JSON-RPC 2.0 specification
- **Reject** [JSON-RPC Compliance] `JSON-RPC Compliance`: Error codes do not comply with JSON-RPC 2.0 standard error codes

## 6. Benchmark Trust Profile

Multi-dimensional benchmarking view of server trustworthiness for AI agent consumption.
This section is descriptive only. Release gating and pass/fail status are driven by the deterministic verdicts above, not by weighted trust averages.

| Dimension | Score | What It Measures |
| :--- | :---: | :--- |
| **Protocol Compliance** | 29% | MCP spec adherence, JSON-RPC compliance, response structures |
| **Security Posture** | 100% | Auth compliance, injection resistance, attack surface |
| **AI Safety** | 100% | Schema quality, destructive tool detection, exfiltration risk, prompt injection surface |
| **Operational Readiness** | 70% | Latency, throughput, error rate, stability |

### Summary

- Destructive tools: **0**
- Data exfiltration risk: **0**
- Prompt injection surface: **0**

### MCP Spec Compliance (RFC 2119 Tiers)

| Tier | Passed | Failed | Total | Impact |
| :--- | :---: | :---: | :---: | :--- |
| **MUST** | 6 | 1 | 7 | ❌ Non-compliant (trust capped at L2) |
| **SHOULD** | 4 | 0 | 4 | ✅ All expected behaviors present |
| **MAY** | 4 | 2 | 6 | ℹ️ Informational (no score impact) |

#### ❌ MUST Failures (Compliance Blockers)

- **MUST: Use standard JSON-RPC error codes** (errors) — Non-standard error codes

#### ℹ️ Optional Features (MAY)

- ✅ MAY: Server supports logging/setLevel
- ✅ MAY: Server supports sampling/createMessage
- ✅ MAY: Server supports roots/list
- ✅ MAY: Server supports completion/complete
- ➖ MAY: Server supports resources/templates/list
- ➖ MAY: Tools include 'annotations' (readOnlyHint, destructiveHint)

## 7. Client Profile Compatibility

Documented host-side compatibility assessments derived from the neutral validation evidence collected during this run.

| Client Profile | Status | Requirements | Documentation |
| :--- | :--- | :--- | :--- |
| **Claude Code** | ❌ Incompatible | 0 passed / 0 warnings / 1 failed | <https://code.claude.com/docs/en/mcp> |
| **VS Code Copilot Agent** | ❌ Incompatible | 0 passed / 0 warnings / 1 failed | <https://code.visualstudio.com/docs/copilot/chat/mcp-servers> |
| **GitHub Copilot CLI** | ❌ Incompatible | 0 passed / 0 warnings / 1 failed | <https://docs.github.com/en/copilot/how-tos/copilot-cli/customize-copilot/add-mcp-servers> |
| **GitHub Copilot Cloud Agent** | ❌ Incompatible | 2 passed / 0 warnings / 1 failed | <https://docs.github.com/en/copilot/how-tos/use-copilot-agents/cloud-agent/extend-cloud-agent-with-mcp> |
| **Visual Studio Copilot** | ❌ Incompatible | 1 passed / 0 warnings / 1 failed | <https://learn.microsoft.com/en-us/visualstudio/ide/mcp-servers?view=vs-2022> |

### Claude Code

**Status:** ❌ Incompatible

1 required compatibility check(s) failed; review the affected surfaces before relying on this client profile.

| Requirement | Level | Outcome | Rule IDs | Affected Components | Details |
| :--- | :--- | :--- | :--- | :--- | :--- |
| At least one interactive MCP surface is available | Required | ❌ Failed | - | - | No tools, prompts, or resources were discovered for this profile. |

### VS Code Copilot Agent

**Status:** ❌ Incompatible

1 required compatibility check(s) failed; review the affected surfaces before relying on this client profile.

| Requirement | Level | Outcome | Rule IDs | Affected Components | Details |
| :--- | :--- | :--- | :--- | :--- | :--- |
| At least one interactive MCP surface is available | Required | ❌ Failed | - | - | No tools, prompts, or resources were discovered for this profile. |

### GitHub Copilot CLI

**Status:** ❌ Incompatible

1 required compatibility check(s) failed; review the affected surfaces before relying on this client profile.

| Requirement | Level | Outcome | Rule IDs | Affected Components | Details |
| :--- | :--- | :--- | :--- | :--- | :--- |
| Tool surface is available | Required | ❌ Failed | - | - | No tools were discovered, but this client profile currently depends on the tool surface. |

### GitHub Copilot Cloud Agent

**Status:** ❌ Incompatible

1 required compatibility check(s) failed; review the affected surfaces before relying on this client profile.

| Requirement | Level | Outcome | Rule IDs | Affected Components | Details |
| :--- | :--- | :--- | :--- | :--- | :--- |
| Tool surface is available | Required | ❌ Failed | - | - | No tools were discovered, but this client profile currently depends on the tool surface. |

### Visual Studio Copilot

**Status:** ❌ Incompatible

1 required compatibility check(s) failed; review the affected surfaces before relying on this client profile.

| Requirement | Level | Outcome | Rule IDs | Affected Components | Details |
| :--- | :--- | :--- | :--- | :--- | :--- |
| At least one interactive MCP surface is available | Required | ❌ Failed | - | - | No tools, prompts, or resources were discovered for this profile. |

## 8. Validation Envelope

This section exposes the structured validation envelope used for layered reporting, coverage tracking, and pack provenance.

### Assessment Layers

| Layer | Status | Findings | Summary |
| :--- | :--- | :---: | :--- |
| `protocol-core` | ✅ Passed | 7 | roots/list: supported \| logging/setLevel: supported \| sampling/createMessage: supported \| completion/complete: supported |
| `tool-surface` | ➖ Skipped | 0 | tools/list probe inconclusive: HTTP 429 Too Many Requests. Body: <HTML><HEAD><TITLE>Too Many Requests</TITLE></HEAD><BODY> <H1>Too Many Requests</H1> <P>Reference 0.49d02e17.1777069371.20623cd6</P> <P>Your IP: 2601:600:8d00:cd30:7d25:f5d7:b9e4:39b5</P> </BODY></HTML>  |
| `resource-surface` | ❌ Failed | 0 | - |
| `prompt-surface` | ❌ Failed | 0 | - |
| `security-boundaries` | ✅ Passed | 0 | - |
| `performance` | ➖ Skipped | 1 | Public remote synthetic load probe did not capture measurements before timeout/cancellation; results are reported as advisory and excluded from pass/fail decisions. |
| `error-handling` | ❌ Failed | 3 | Validated 5 error scenario(s); 2 handled correctly. |
| `client-profiles` | ❌ Failed | 0 | Evaluated 5 profile(s); 0 warning profile(s); 5 incompatible profile(s). |

### Scenario Outcomes

| Scenario | Status | Findings | Summary |
| :--- | :--- | :---: | :--- |
| `bootstrap-initialize-handshake` | ➖ Skipped | 0 | HTTP 429 Too Many Requests. Body: <HTML><HEAD><TITLE>Too Many Requests</TITLE></HEAD><BODY> <H1>Too Many Requests</H1> <P>Reference 0.49d02e17.1777069363.2061c2c8</P> <P>Your IP: 2601:600:8d00:cd30:7d25:f5d7:b9e4:39b5</P> </BODY></HTML>  |
| `protocol-compliance-review` | ✅ Passed | 7 | roots/list: supported \| logging/setLevel: supported \| sampling/createMessage: supported \| completion/complete: supported |
| `tool-catalog-smoke` | ➖ Skipped | 0 | tools/list probe inconclusive: HTTP 429 Too Many Requests. Body: <HTML><HEAD><TITLE>Too Many Requests</TITLE></HEAD><BODY> <H1>Too Many Requests</H1> <P>Reference 0.49d02e17.1777069371.20623cd6</P> <P>Your IP: 2601:600:8d00:cd30:7d25:f5d7:b9e4:39b5</P> </BODY></HTML>  |
| `resource-catalog-smoke` | ❌ Failed | 0 | No resources were discovered during validation. |
| `prompt-catalog-smoke` | ❌ Failed | 0 | No prompts were discovered during validation. |
| `security-authentication-challenge` | ➖ Skipped | 0 | Evaluated 57 authentication challenge scenario(s). |
| `security-attack-simulations` | ➖ Skipped | 0 | Evaluated 7 attack simulation(s). |
| `error-handling-matrix` | ❌ Failed | 3 | Validated 5 error scenario(s); 2 handled correctly. |

### Coverage Declarations

| Layer | Scope | Status | Reason |
| :--- | :--- | :--- | :--- |
| `protocol-core` | `json-rpc` | `Covered` | - |
| `tool-surface` | `tools/list` | `Skipped` | tools/list probe inconclusive: HTTP 429 Too Many Requests. Body: <HTML><HEAD><TITLE>Too Many Requests</TITLE></HEAD><BODY> <H1>Too Many Requests</H1> <P>Reference 0.49d02e17.1777069371.20623cd6</P> <P>Your IP: 2601:600:8d00:cd30:7d25:f5d7:b9e4:39b5</P> </BODY></HTML>  |
| `resource-surface` | `resources/list` | `Covered` | - |
| `prompt-surface` | `prompts/list` | `Covered` | - |
| `security-boundaries` | `security-assessment` | `Covered` | - |
| `performance` | `load-testing` | `Skipped` | Public remote synthetic load probe did not capture measurements before timeout/cancellation; results are reported as advisory and excluded from pass/fail decisions. |
| `error-handling` | `error-handling` | `Covered` | - |
| `bootstrap` | `bootstrap-initialize-handshake` | `Covered` | - |
| `protocol-core` | `protocol-compliance-review` | `Covered` | - |
| `tool-surface` | `tool-catalog-smoke` | `Skipped` | tools/list probe inconclusive: HTTP 429 Too Many Requests. Body: <HTML><HEAD><TITLE>Too Many Requests</TITLE></HEAD><BODY> <H1>Too Many Requests</H1> <P>Reference 0.49d02e17.1777069371.20623cd6</P> <P>Your IP: 2601:600:8d00:cd30:7d25:f5d7:b9e4:39b5</P> </BODY></HTML>  |
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
| `bootstrap-initialize` | `bootstrap` | `initialize` | `bootstrap-health` | HTTP 429 Too Many Requests. Body: <HTML><HEAD><TITLE>Too Many Requests</TITLE></HEAD><BODY> <H1>Too Many Requests</H1> <P>Reference 0.49d02e17.1777069363.2061c2c8</P> <P>Your IP: 2601:600:8d00:cd30:7d25:f5d7:b9e4:39b5</P> </BODY></HTML>  |
| `protocol-mcp-proto-err` | `protocol-core` | `JSON-RPC Compliance` | `protocol-violation` | JSON-RPC Error Code Violation: Parse Error |
| `protocol-mcp-proto-err-2` | `protocol-core` | `JSON-RPC Compliance` | `protocol-violation` | JSON-RPC Error Code Violation: Invalid Request - Missing jsonrpc |
| `protocol-mcp-proto-err-3` | `protocol-core` | `JSON-RPC Compliance` | `protocol-violation` | JSON-RPC Error Code Violation: Method Not Found |
| `protocol-mcp-proto-err-4` | `protocol-core` | `JSON-RPC Compliance` | `protocol-violation` | JSON-RPC Error Code Violation: Invalid Params |
| `protocol-mcp-proto-jsonrpc` | `protocol-core` | `JSON-RPC Compliance` | `protocol-violation` | Request format does not comply with JSON-RPC 2.0 specification |
| `protocol-mcp-proto-jsonrpc-2` | `protocol-core` | `JSON-RPC Compliance` | `protocol-violation` | Response format does not comply with JSON-RPC 2.0 specification |
| `protocol-mcp-proto-jsonrpc-3` | `protocol-core` | `JSON-RPC Compliance` | `protocol-violation` | Batch processing implementation is inconsistent or incomplete |
| `protocol-mcp-proto-err-5` | `protocol-core` | `JSON-RPC Compliance` | `protocol-violation` | Error codes do not comply with JSON-RPC 2.0 standard error codes |
| `auth-no-auth-initialize` | `security-boundaries` | `initialize` | `authentication-scenario` | ℹ️ INFO (Public profile): ℹ️ INFO: Rate limiting observed; auth semantics inconclusive. |
| `auth-no-auth-tools-list` | `security-boundaries` | `tools/list` | `authentication-scenario` | ℹ️ INFO (Public profile): ℹ️ INFO: Rate limiting observed; auth semantics inconclusive. |
| `auth-malformed-token-tools-list` | `security-boundaries` | `tools/list` | `authentication-scenario` | ℹ️ INFO (Public profile): ℹ️ INFO: Rate limiting observed; auth semantics inconclusive. |
| `auth-invalid-token-tools-list` | `security-boundaries` | `tools/list` | `authentication-scenario` | ℹ️ INFO (Public profile): ℹ️ INFO: Rate limiting observed; auth semantics inconclusive. |
| `auth-token-expired-tools-list` | `security-boundaries` | `tools/list` | `authentication-scenario` | ℹ️ INFO (Public profile): ℹ️ INFO: Rate limiting observed; auth semantics inconclusive. |
| `auth-invalid-scope-tools-list` | `security-boundaries` | `tools/list` | `authentication-scenario` | ℹ️ INFO (Public profile): ℹ️ INFO: Rate limiting observed; auth semantics inconclusive. |
| `auth-insufficient-permissions-tools-list` | `security-boundaries` | `tools/list` | `authentication-scenario` | ℹ️ INFO (Public profile): ℹ️ INFO: Rate limiting observed; auth semantics inconclusive. |
| `auth-revoked-token-tools-list` | `security-boundaries` | `tools/list` | `authentication-scenario` | ℹ️ INFO (Public profile): ℹ️ INFO: Authentication test inconclusive due to validator/network timeout or cancellation. |
| `auth-wrong-audience-rfc-8707-tools-list` | `security-boundaries` | `tools/list` | `authentication-scenario` | ℹ️ INFO (Public profile): ℹ️ INFO: Authentication test inconclusive due to validator/network timeout or cancellation. |
| `auth-no-auth-tools-call` | `security-boundaries` | `tools/call` | `authentication-scenario` | ℹ️ INFO (Public profile): ℹ️ INFO: Authentication test inconclusive due to validator/network timeout or cancellation. |
| `auth-malformed-token-tools-call` | `security-boundaries` | `tools/call` | `authentication-scenario` | ℹ️ INFO (Public profile): ℹ️ INFO: Authentication test inconclusive due to validator/network timeout or cancellation. |
| `auth-invalid-token-tools-call` | `security-boundaries` | `tools/call` | `authentication-scenario` | ℹ️ INFO (Public profile): ℹ️ INFO: Authentication test inconclusive due to validator/network timeout or cancellation. |
| `auth-token-expired-tools-call` | `security-boundaries` | `tools/call` | `authentication-scenario` | ℹ️ INFO (Public profile): ℹ️ INFO: Authentication test inconclusive due to validator/network timeout or cancellation. |
| `auth-invalid-scope-tools-call` | `security-boundaries` | `tools/call` | `authentication-scenario` | ℹ️ INFO (Public profile): ℹ️ INFO: Authentication test inconclusive due to validator/network timeout or cancellation. |
| `auth-insufficient-permissions-tools-call` | `security-boundaries` | `tools/call` | `authentication-scenario` | ℹ️ INFO (Public profile): ℹ️ INFO: Authentication test inconclusive due to validator/network timeout or cancellation. |
| `auth-revoked-token-tools-call` | `security-boundaries` | `tools/call` | `authentication-scenario` | ℹ️ INFO (Public profile): ℹ️ INFO: Authentication test inconclusive due to validator/network timeout or cancellation. |
| `auth-wrong-audience-rfc-8707-tools-call` | `security-boundaries` | `tools/call` | `authentication-scenario` | ℹ️ INFO (Public profile): ℹ️ INFO: Authentication test inconclusive due to validator/network timeout or cancellation. |
| `auth-no-auth-resources-list` | `security-boundaries` | `resources/list` | `authentication-scenario` | ℹ️ INFO (Public profile): ℹ️ INFO: Authentication test inconclusive due to validator/network timeout or cancellation. |
| `auth-malformed-token-resources-list` | `security-boundaries` | `resources/list` | `authentication-scenario` | ℹ️ INFO (Public profile): ℹ️ INFO: Authentication test inconclusive due to validator/network timeout or cancellation. |
| `auth-invalid-token-resources-list` | `security-boundaries` | `resources/list` | `authentication-scenario` | ℹ️ INFO (Public profile): ℹ️ INFO: Authentication test inconclusive due to validator/network timeout or cancellation. |
| `auth-token-expired-resources-list` | `security-boundaries` | `resources/list` | `authentication-scenario` | ℹ️ INFO (Public profile): ℹ️ INFO: Authentication test inconclusive due to validator/network timeout or cancellation. |
| `auth-invalid-scope-resources-list` | `security-boundaries` | `resources/list` | `authentication-scenario` | ℹ️ INFO (Public profile): ℹ️ INFO: Authentication test inconclusive due to validator/network timeout or cancellation. |
| `auth-insufficient-permissions-resources-list` | `security-boundaries` | `resources/list` | `authentication-scenario` | ℹ️ INFO (Public profile): ℹ️ INFO: Authentication test inconclusive due to validator/network timeout or cancellation. |
| `auth-revoked-token-resources-list` | `security-boundaries` | `resources/list` | `authentication-scenario` | ℹ️ INFO (Public profile): ℹ️ INFO: Authentication test inconclusive due to validator/network timeout or cancellation. |
| `auth-wrong-audience-rfc-8707-resources-list` | `security-boundaries` | `resources/list` | `authentication-scenario` | ℹ️ INFO (Public profile): ℹ️ INFO: Authentication test inconclusive due to validator/network timeout or cancellation. |
| `auth-no-auth-resources-read` | `security-boundaries` | `resources/read` | `authentication-scenario` | ℹ️ INFO (Public profile): ℹ️ INFO: Authentication test inconclusive due to validator/network timeout or cancellation. |
| `auth-malformed-token-resources-read` | `security-boundaries` | `resources/read` | `authentication-scenario` | ℹ️ INFO (Public profile): ℹ️ INFO: Authentication test inconclusive due to validator/network timeout or cancellation. |
| `auth-invalid-token-resources-read` | `security-boundaries` | `resources/read` | `authentication-scenario` | ℹ️ INFO (Public profile): ℹ️ INFO: Authentication test inconclusive due to validator/network timeout or cancellation. |
| `auth-token-expired-resources-read` | `security-boundaries` | `resources/read` | `authentication-scenario` | ℹ️ INFO (Public profile): ℹ️ INFO: Authentication test inconclusive due to validator/network timeout or cancellation. |
| `auth-invalid-scope-resources-read` | `security-boundaries` | `resources/read` | `authentication-scenario` | ℹ️ INFO (Public profile): ℹ️ INFO: Authentication test inconclusive due to validator/network timeout or cancellation. |
| `auth-insufficient-permissions-resources-read` | `security-boundaries` | `resources/read` | `authentication-scenario` | ℹ️ INFO (Public profile): ℹ️ INFO: Authentication test inconclusive due to validator/network timeout or cancellation. |
| `auth-revoked-token-resources-read` | `security-boundaries` | `resources/read` | `authentication-scenario` | ℹ️ INFO (Public profile): ℹ️ INFO: Authentication test inconclusive due to validator/network timeout or cancellation. |
| `auth-wrong-audience-rfc-8707-resources-read` | `security-boundaries` | `resources/read` | `authentication-scenario` | ℹ️ INFO (Public profile): ℹ️ INFO: Authentication test inconclusive due to validator/network timeout or cancellation. |
| `auth-no-auth-prompts-list` | `security-boundaries` | `prompts/list` | `authentication-scenario` | ℹ️ INFO (Public profile): ℹ️ INFO: Authentication test inconclusive due to validator/network timeout or cancellation. |
| `auth-malformed-token-prompts-list` | `security-boundaries` | `prompts/list` | `authentication-scenario` | ℹ️ INFO (Public profile): ℹ️ INFO: Authentication test inconclusive due to validator/network timeout or cancellation. |
| `auth-invalid-token-prompts-list` | `security-boundaries` | `prompts/list` | `authentication-scenario` | ℹ️ INFO (Public profile): ℹ️ INFO: Authentication test inconclusive due to validator/network timeout or cancellation. |
| `auth-token-expired-prompts-list` | `security-boundaries` | `prompts/list` | `authentication-scenario` | ℹ️ INFO (Public profile): ℹ️ INFO: Authentication test inconclusive due to validator/network timeout or cancellation. |
| `auth-invalid-scope-prompts-list` | `security-boundaries` | `prompts/list` | `authentication-scenario` | ℹ️ INFO (Public profile): ℹ️ INFO: Authentication test inconclusive due to validator/network timeout or cancellation. |
| `auth-insufficient-permissions-prompts-list` | `security-boundaries` | `prompts/list` | `authentication-scenario` | ℹ️ INFO (Public profile): ℹ️ INFO: Authentication test inconclusive due to validator/network timeout or cancellation. |
| `auth-revoked-token-prompts-list` | `security-boundaries` | `prompts/list` | `authentication-scenario` | ℹ️ INFO (Public profile): ℹ️ INFO: Authentication test inconclusive due to validator/network timeout or cancellation. |
| `auth-wrong-audience-rfc-8707-prompts-list` | `security-boundaries` | `prompts/list` | `authentication-scenario` | ℹ️ INFO (Public profile): ℹ️ INFO: Authentication test inconclusive due to validator/network timeout or cancellation. |
| `auth-no-auth-prompts-get` | `security-boundaries` | `prompts/get` | `authentication-scenario` | ℹ️ INFO (Public profile): ℹ️ INFO: Authentication test inconclusive due to validator/network timeout or cancellation. |
| `auth-malformed-token-prompts-get` | `security-boundaries` | `prompts/get` | `authentication-scenario` | ℹ️ INFO (Public profile): ℹ️ INFO: Authentication test inconclusive due to validator/network timeout or cancellation. |
| `auth-invalid-token-prompts-get` | `security-boundaries` | `prompts/get` | `authentication-scenario` | ℹ️ INFO (Public profile): ℹ️ INFO: Authentication test inconclusive due to validator/network timeout or cancellation. |
| `auth-token-expired-prompts-get` | `security-boundaries` | `prompts/get` | `authentication-scenario` | ℹ️ INFO (Public profile): ℹ️ INFO: Authentication test inconclusive due to validator/network timeout or cancellation. |
| `auth-invalid-scope-prompts-get` | `security-boundaries` | `prompts/get` | `authentication-scenario` | ℹ️ INFO (Public profile): ℹ️ INFO: Authentication test inconclusive due to validator/network timeout or cancellation. |
| `auth-insufficient-permissions-prompts-get` | `security-boundaries` | `prompts/get` | `authentication-scenario` | ℹ️ INFO (Public profile): ℹ️ INFO: Authentication test inconclusive due to validator/network timeout or cancellation. |
| `auth-revoked-token-prompts-get` | `security-boundaries` | `prompts/get` | `authentication-scenario` | ℹ️ INFO (Public profile): ℹ️ INFO: Authentication test inconclusive due to validator/network timeout or cancellation. |
| `auth-wrong-audience-rfc-8707-prompts-get` | `security-boundaries` | `prompts/get` | `authentication-scenario` | ℹ️ INFO (Public profile): ℹ️ INFO: Authentication test inconclusive due to validator/network timeout or cancellation. |
| `auth-no-auth-logging-setlevel` | `security-boundaries` | `logging/setLevel` | `authentication-scenario` | ℹ️ INFO (Public profile): ℹ️ INFO: Authentication test inconclusive due to validator/network timeout or cancellation. |
| `auth-malformed-token-logging-setlevel` | `security-boundaries` | `logging/setLevel` | `authentication-scenario` | ℹ️ INFO (Public profile): ℹ️ INFO: Authentication test inconclusive due to validator/network timeout or cancellation. |
| `auth-invalid-token-logging-setlevel` | `security-boundaries` | `logging/setLevel` | `authentication-scenario` | ℹ️ INFO (Public profile): ℹ️ INFO: Authentication test inconclusive due to validator/network timeout or cancellation. |
| `auth-token-expired-logging-setlevel` | `security-boundaries` | `logging/setLevel` | `authentication-scenario` | ℹ️ INFO (Public profile): ℹ️ INFO: Authentication test inconclusive due to validator/network timeout or cancellation. |
| `auth-invalid-scope-logging-setlevel` | `security-boundaries` | `logging/setLevel` | `authentication-scenario` | ℹ️ INFO (Public profile): ℹ️ INFO: Authentication test inconclusive due to validator/network timeout or cancellation. |
| `auth-insufficient-permissions-logging-setlevel` | `security-boundaries` | `logging/setLevel` | `authentication-scenario` | ℹ️ INFO (Public profile): ℹ️ INFO: Authentication test inconclusive due to validator/network timeout or cancellation. |
| `auth-revoked-token-logging-setlevel` | `security-boundaries` | `logging/setLevel` | `authentication-scenario` | ℹ️ INFO (Public profile): ℹ️ INFO: Authentication test inconclusive due to validator/network timeout or cancellation. |
| `auth-wrong-audience-rfc-8707-logging-setlevel` | `security-boundaries` | `logging/setLevel` | `authentication-scenario` | ℹ️ INFO (Public profile): ℹ️ INFO: Authentication test inconclusive due to validator/network timeout or cancellation. |
| `attack-inj-001-input-validation` | `security-boundaries` | `INJ-001 (Input Validation)` | `attack-simulation` | A task was canceled. |
| `attack-inj-002-input-validation` | `security-boundaries` | `INJ-002 (Input Validation)` | `attack-simulation` | A task was canceled. |
| `attack-inj-003-input-validation` | `security-boundaries` | `INJ-003 (Input Validation)` | `attack-simulation` | A task was canceled. |
| `attack-mcp-sec-001` | `security-boundaries` | `MCP-SEC-001` | `attack-simulation` | Server handled malformed requests gracefully with standard errors. |
| `attack-mcp-sec-002` | `security-boundaries` | `MCP-SEC-002` | `attack-simulation` | Skipped: Metadata enumeration requires an advertised concrete resource URI; no like-for-like resource surface was available. |
| `attack-mcp-sec-003` | `security-boundaries` | `MCP-SEC-003` | `attack-simulation` | Skipped: Could not list tools to test. |
| `attack-mcp-ai-001` | `security-boundaries` | `MCP-AI-001` | `attack-simulation` | Skipped: Could not list tools for fuzzing. |
| `error-invalid-method-call` | `error-handling` | `invalid-method` | `error-scenario` | HTTP 429; HTTP 429 Too Many Requests. Body: <HTML><HEAD><TITLE>Too Many Requests</TITLE></HEAD><BODY> <H1>Too Many Requests</H1> <P>Reference 0.49d02e17.1777069520.206b9bbb</P> <P>Your IP: 2601:600:8d00:cd30:7d25:f5d7:b9e4:39b5</P> </BODY></HTML>  |
| `error-malformed-json-payload` | `error-handling` | `malformed-json` | `error-scenario` | HTTP 429; <HTML><HEAD><TITLE>Too Many Requests</TITLE></HEAD><BODY> <H1>Too Many Requests</H1> <P>Reference 0.49d02e17.1777069513.206b2202</P> <P>Your IP: 2601:600:8d00:cd30:7d25:f5d7:b9e4:39b5</P> </BODY></HTM |
| `error-graceful-degradation-on-invalid-request` | `error-handling` | `invalid-request` | `error-scenario` | HTTP 429; <HTML><HEAD><TITLE>Too Many Requests</TITLE></HEAD><BODY> <H1>Too Many Requests</H1> <P>Reference 0.49d02e17.1777069513.206b2376</P> <P>Your IP: 2601:600:8d00:cd30:7d25:f5d7:b9e4:39b5</P> </BODY></HTM |
| `error-timeout-handling-recovery` | `error-handling` | `timeout-handling` | `error-scenario` | Validator cancelled the HTTP request after the induced timeout window elapsed. |
| `error-connection-interruption-recovery` | `error-handling` | `connection-interruption` | `error-scenario` | Validator aborted the HTTP request body mid-stream to simulate a connection interruption. |

## 9. Capability Snapshot

| Probe | Discovered | HTTP Status | Duration | Result |
| :--- | :---: | :---: | :---: | :--- |
| Tools/list | 0 | HTTP 429 | 7319.5 ms | ❌ Failed |
| Resources/list | 0 | HTTP 429 | 7331.4 ms | ❌ Failed |
| Prompts/list | 0 | HTTP 429 | 7314.8 ms | ❌ Failed |


## 10. Scoring Methodology

These notes explain how the overall score and blocking decision were calibrated for this run.

- Tools validation skipped (Auth required). Score coverage reduced accordingly.
- Performance testing skipped (Auth required). Score coverage reduced accordingly.
- Coverage multiplier applied (90%). Missing or skipped categories: Tools, Performance.
- INFO: Authentication challenge observations are informational for the declared public profile.
- SPEC: JSON-RPC 2.0 format deviations detected. Score reduced by 15%.
- Score is below the preferred target, but no blocking failure was observed in this run.

## 11. Compliance Matrix

| Category | Status | Score | Issues |
| :--- | :---: | :---: | :---: |
| Protocol Compliance | ✅ Passed | 28.6% | **8** |
| Security Assessment | ✅ Passed | 100.0% | - |
| Tool Validation | ➖ Skipped | - | - |
| Resource Capabilities | ❌ Failed | 0.0% | - |
| Prompt Capabilities | ❌ Failed | 0.0% | - |
| Performance | ➖ Skipped | - | - |

## 12. Performance Calibration

Performance is assessed using a synthetic load probe rather than a workload-specific SLA benchmark.
For public or remote SaaS endpoints, partial failures under synthetic pressure are reported as advisory when they look like capacity limits or edge protections rather than protocol instability.

- Public remote synthetic load probe did not capture any measurements before timing out or being cancelled, so the performance result is treated as advisory rather than a readiness failure.

## 13. Security Assessment

**Security Score:** 100.0%

### Authentication Analysis
| Scenario | Method | Expected | Actual | HTTP | Analysis | Status |
| :--- | :--- | :--- | :--- | :---: | :--- | :---: |
| No Auth - initialize | `initialize` | Informational (public profile) | Rate limited | 429 | ℹ️ INFO (Public profile): ℹ️ INFO: Rate limiting observed; auth semantics inconclusive. | ✅ |
| No Auth - tools/list | `tools/list` | Informational (public profile) | Rate limited | 429 | ℹ️ INFO (Public profile): ℹ️ INFO: Rate limiting observed; auth semantics inconclusive. | ✅ |
| Malformed Token - tools/list | `tools/list` | Informational (public profile) | Rate limited | 429 | ℹ️ INFO (Public profile): ℹ️ INFO: Rate limiting observed; auth semantics inconclusive. | ✅ |
| Invalid Token - tools/list | `tools/list` | Informational (public profile) | Rate limited | 429 | ℹ️ INFO (Public profile): ℹ️ INFO: Rate limiting observed; auth semantics inconclusive. | ✅ |
| Token Expired - tools/list | `tools/list` | Informational (public profile) | Rate limited | 429 | ℹ️ INFO (Public profile): ℹ️ INFO: Rate limiting observed; auth semantics inconclusive. | ✅ |
| Invalid Scope - tools/list | `tools/list` | Informational (public profile) | Rate limited | 429 | ℹ️ INFO (Public profile): ℹ️ INFO: Rate limiting observed; auth semantics inconclusive. | ✅ |
| Insufficient Permissions - tools/list | `tools/list` | Informational (public profile) | Rate limited | 429 | ℹ️ INFO (Public profile): ℹ️ INFO: Rate limiting observed; auth semantics inconclusive. | ✅ |
| Revoked Token - tools/list | `tools/list` | Informational (public profile) | ⏱️ Request canceled or timed out (validator/network) | -1 | ℹ️ INFO (Public profile): ℹ️ INFO: Authentication test inconclusive due to validator/network timeout or cancellation. | ✅ |
| Wrong Audience (RFC 8707) - tools/list | `tools/list` | Informational (public profile) | ⏱️ Request canceled or timed out (validator/network) | -1 | ℹ️ INFO (Public profile): ℹ️ INFO: Authentication test inconclusive due to validator/network timeout or cancellation. | ✅ |
| No Auth - tools/call | `tools/call` | Informational (public profile) | ⏱️ Request canceled or timed out (validator/network) | -1 | ℹ️ INFO (Public profile): ℹ️ INFO: Authentication test inconclusive due to validator/network timeout or cancellation. | ✅ |
| Malformed Token - tools/call | `tools/call` | Informational (public profile) | ⏱️ Request canceled or timed out (validator/network) | -1 | ℹ️ INFO (Public profile): ℹ️ INFO: Authentication test inconclusive due to validator/network timeout or cancellation. | ✅ |
| Invalid Token - tools/call | `tools/call` | Informational (public profile) | ⏱️ Request canceled or timed out (validator/network) | -1 | ℹ️ INFO (Public profile): ℹ️ INFO: Authentication test inconclusive due to validator/network timeout or cancellation. | ✅ |
| Token Expired - tools/call | `tools/call` | Informational (public profile) | ⏱️ Request canceled or timed out (validator/network) | -1 | ℹ️ INFO (Public profile): ℹ️ INFO: Authentication test inconclusive due to validator/network timeout or cancellation. | ✅ |
| Invalid Scope - tools/call | `tools/call` | Informational (public profile) | ⏱️ Request canceled or timed out (validator/network) | -1 | ℹ️ INFO (Public profile): ℹ️ INFO: Authentication test inconclusive due to validator/network timeout or cancellation. | ✅ |
| Insufficient Permissions - tools/call | `tools/call` | Informational (public profile) | ⏱️ Request canceled or timed out (validator/network) | -1 | ℹ️ INFO (Public profile): ℹ️ INFO: Authentication test inconclusive due to validator/network timeout or cancellation. | ✅ |
| Revoked Token - tools/call | `tools/call` | Informational (public profile) | ⏱️ Request canceled or timed out (validator/network) | -1 | ℹ️ INFO (Public profile): ℹ️ INFO: Authentication test inconclusive due to validator/network timeout or cancellation. | ✅ |
| Wrong Audience (RFC 8707) - tools/call | `tools/call` | Informational (public profile) | ⏱️ Request canceled or timed out (validator/network) | -1 | ℹ️ INFO (Public profile): ℹ️ INFO: Authentication test inconclusive due to validator/network timeout or cancellation. | ✅ |
| No Auth - resources/list | `resources/list` | Informational (public profile) | ⏱️ Request canceled or timed out (validator/network) | -1 | ℹ️ INFO (Public profile): ℹ️ INFO: Authentication test inconclusive due to validator/network timeout or cancellation. | ✅ |
| Malformed Token - resources/list | `resources/list` | Informational (public profile) | ⏱️ Request canceled or timed out (validator/network) | -1 | ℹ️ INFO (Public profile): ℹ️ INFO: Authentication test inconclusive due to validator/network timeout or cancellation. | ✅ |
| Invalid Token - resources/list | `resources/list` | Informational (public profile) | ⏱️ Request canceled or timed out (validator/network) | -1 | ℹ️ INFO (Public profile): ℹ️ INFO: Authentication test inconclusive due to validator/network timeout or cancellation. | ✅ |
| Token Expired - resources/list | `resources/list` | Informational (public profile) | ⏱️ Request canceled or timed out (validator/network) | -1 | ℹ️ INFO (Public profile): ℹ️ INFO: Authentication test inconclusive due to validator/network timeout or cancellation. | ✅ |
| Invalid Scope - resources/list | `resources/list` | Informational (public profile) | ⏱️ Request canceled or timed out (validator/network) | -1 | ℹ️ INFO (Public profile): ℹ️ INFO: Authentication test inconclusive due to validator/network timeout or cancellation. | ✅ |
| Insufficient Permissions - resources/list | `resources/list` | Informational (public profile) | ⏱️ Request canceled or timed out (validator/network) | -1 | ℹ️ INFO (Public profile): ℹ️ INFO: Authentication test inconclusive due to validator/network timeout or cancellation. | ✅ |
| Revoked Token - resources/list | `resources/list` | Informational (public profile) | ⏱️ Request canceled or timed out (validator/network) | -1 | ℹ️ INFO (Public profile): ℹ️ INFO: Authentication test inconclusive due to validator/network timeout or cancellation. | ✅ |
| Wrong Audience (RFC 8707) - resources/list | `resources/list` | Informational (public profile) | ⏱️ Request canceled or timed out (validator/network) | -1 | ℹ️ INFO (Public profile): ℹ️ INFO: Authentication test inconclusive due to validator/network timeout or cancellation. | ✅ |
| No Auth - resources/read | `resources/read` | Informational (public profile) | ⏱️ Request canceled or timed out (validator/network) | -1 | ℹ️ INFO (Public profile): ℹ️ INFO: Authentication test inconclusive due to validator/network timeout or cancellation. | ✅ |
| Malformed Token - resources/read | `resources/read` | Informational (public profile) | ⏱️ Request canceled or timed out (validator/network) | -1 | ℹ️ INFO (Public profile): ℹ️ INFO: Authentication test inconclusive due to validator/network timeout or cancellation. | ✅ |
| Invalid Token - resources/read | `resources/read` | Informational (public profile) | ⏱️ Request canceled or timed out (validator/network) | -1 | ℹ️ INFO (Public profile): ℹ️ INFO: Authentication test inconclusive due to validator/network timeout or cancellation. | ✅ |
| Token Expired - resources/read | `resources/read` | Informational (public profile) | ⏱️ Request canceled or timed out (validator/network) | -1 | ℹ️ INFO (Public profile): ℹ️ INFO: Authentication test inconclusive due to validator/network timeout or cancellation. | ✅ |
| Invalid Scope - resources/read | `resources/read` | Informational (public profile) | ⏱️ Request canceled or timed out (validator/network) | -1 | ℹ️ INFO (Public profile): ℹ️ INFO: Authentication test inconclusive due to validator/network timeout or cancellation. | ✅ |
| Insufficient Permissions - resources/read | `resources/read` | Informational (public profile) | ⏱️ Request canceled or timed out (validator/network) | -1 | ℹ️ INFO (Public profile): ℹ️ INFO: Authentication test inconclusive due to validator/network timeout or cancellation. | ✅ |
| Revoked Token - resources/read | `resources/read` | Informational (public profile) | ⏱️ Request canceled or timed out (validator/network) | -1 | ℹ️ INFO (Public profile): ℹ️ INFO: Authentication test inconclusive due to validator/network timeout or cancellation. | ✅ |
| Wrong Audience (RFC 8707) - resources/read | `resources/read` | Informational (public profile) | ⏱️ Request canceled or timed out (validator/network) | -1 | ℹ️ INFO (Public profile): ℹ️ INFO: Authentication test inconclusive due to validator/network timeout or cancellation. | ✅ |
| No Auth - prompts/list | `prompts/list` | Informational (public profile) | ⏱️ Request canceled or timed out (validator/network) | -1 | ℹ️ INFO (Public profile): ℹ️ INFO: Authentication test inconclusive due to validator/network timeout or cancellation. | ✅ |
| Malformed Token - prompts/list | `prompts/list` | Informational (public profile) | ⏱️ Request canceled or timed out (validator/network) | -1 | ℹ️ INFO (Public profile): ℹ️ INFO: Authentication test inconclusive due to validator/network timeout or cancellation. | ✅ |
| Invalid Token - prompts/list | `prompts/list` | Informational (public profile) | ⏱️ Request canceled or timed out (validator/network) | -1 | ℹ️ INFO (Public profile): ℹ️ INFO: Authentication test inconclusive due to validator/network timeout or cancellation. | ✅ |
| Token Expired - prompts/list | `prompts/list` | Informational (public profile) | ⏱️ Request canceled or timed out (validator/network) | -1 | ℹ️ INFO (Public profile): ℹ️ INFO: Authentication test inconclusive due to validator/network timeout or cancellation. | ✅ |
| Invalid Scope - prompts/list | `prompts/list` | Informational (public profile) | ⏱️ Request canceled or timed out (validator/network) | -1 | ℹ️ INFO (Public profile): ℹ️ INFO: Authentication test inconclusive due to validator/network timeout or cancellation. | ✅ |
| Insufficient Permissions - prompts/list | `prompts/list` | Informational (public profile) | ⏱️ Request canceled or timed out (validator/network) | -1 | ℹ️ INFO (Public profile): ℹ️ INFO: Authentication test inconclusive due to validator/network timeout or cancellation. | ✅ |
| Revoked Token - prompts/list | `prompts/list` | Informational (public profile) | ⏱️ Request canceled or timed out (validator/network) | -1 | ℹ️ INFO (Public profile): ℹ️ INFO: Authentication test inconclusive due to validator/network timeout or cancellation. | ✅ |
| Wrong Audience (RFC 8707) - prompts/list | `prompts/list` | Informational (public profile) | ⏱️ Request canceled or timed out (validator/network) | -1 | ℹ️ INFO (Public profile): ℹ️ INFO: Authentication test inconclusive due to validator/network timeout or cancellation. | ✅ |
| No Auth - prompts/get | `prompts/get` | Informational (public profile) | ⏱️ Request canceled or timed out (validator/network) | -1 | ℹ️ INFO (Public profile): ℹ️ INFO: Authentication test inconclusive due to validator/network timeout or cancellation. | ✅ |
| Malformed Token - prompts/get | `prompts/get` | Informational (public profile) | ⏱️ Request canceled or timed out (validator/network) | -1 | ℹ️ INFO (Public profile): ℹ️ INFO: Authentication test inconclusive due to validator/network timeout or cancellation. | ✅ |
| Invalid Token - prompts/get | `prompts/get` | Informational (public profile) | ⏱️ Request canceled or timed out (validator/network) | -1 | ℹ️ INFO (Public profile): ℹ️ INFO: Authentication test inconclusive due to validator/network timeout or cancellation. | ✅ |
| Token Expired - prompts/get | `prompts/get` | Informational (public profile) | ⏱️ Request canceled or timed out (validator/network) | -1 | ℹ️ INFO (Public profile): ℹ️ INFO: Authentication test inconclusive due to validator/network timeout or cancellation. | ✅ |
| Invalid Scope - prompts/get | `prompts/get` | Informational (public profile) | ⏱️ Request canceled or timed out (validator/network) | -1 | ℹ️ INFO (Public profile): ℹ️ INFO: Authentication test inconclusive due to validator/network timeout or cancellation. | ✅ |
| Insufficient Permissions - prompts/get | `prompts/get` | Informational (public profile) | ⏱️ Request canceled or timed out (validator/network) | -1 | ℹ️ INFO (Public profile): ℹ️ INFO: Authentication test inconclusive due to validator/network timeout or cancellation. | ✅ |
| Revoked Token - prompts/get | `prompts/get` | Informational (public profile) | ⏱️ Request canceled or timed out (validator/network) | -1 | ℹ️ INFO (Public profile): ℹ️ INFO: Authentication test inconclusive due to validator/network timeout or cancellation. | ✅ |
| Wrong Audience (RFC 8707) - prompts/get | `prompts/get` | Informational (public profile) | ⏱️ Request canceled or timed out (validator/network) | -1 | ℹ️ INFO (Public profile): ℹ️ INFO: Authentication test inconclusive due to validator/network timeout or cancellation. | ✅ |
| No Auth - logging/setLevel | `logging/setLevel` | Informational (public profile) | ⏱️ Request canceled or timed out (validator/network) | -1 | ℹ️ INFO (Public profile): ℹ️ INFO: Authentication test inconclusive due to validator/network timeout or cancellation. | ✅ |
| Malformed Token - logging/setLevel | `logging/setLevel` | Informational (public profile) | ⏱️ Request canceled or timed out (validator/network) | -1 | ℹ️ INFO (Public profile): ℹ️ INFO: Authentication test inconclusive due to validator/network timeout or cancellation. | ✅ |
| Invalid Token - logging/setLevel | `logging/setLevel` | Informational (public profile) | ⏱️ Request canceled or timed out (validator/network) | -1 | ℹ️ INFO (Public profile): ℹ️ INFO: Authentication test inconclusive due to validator/network timeout or cancellation. | ✅ |
| Token Expired - logging/setLevel | `logging/setLevel` | Informational (public profile) | ⏱️ Request canceled or timed out (validator/network) | -1 | ℹ️ INFO (Public profile): ℹ️ INFO: Authentication test inconclusive due to validator/network timeout or cancellation. | ✅ |
| Invalid Scope - logging/setLevel | `logging/setLevel` | Informational (public profile) | ⏱️ Request canceled or timed out (validator/network) | -1 | ℹ️ INFO (Public profile): ℹ️ INFO: Authentication test inconclusive due to validator/network timeout or cancellation. | ✅ |
| Insufficient Permissions - logging/setLevel | `logging/setLevel` | Informational (public profile) | ⏱️ Request canceled or timed out (validator/network) | -1 | ℹ️ INFO (Public profile): ℹ️ INFO: Authentication test inconclusive due to validator/network timeout or cancellation. | ✅ |
| Revoked Token - logging/setLevel | `logging/setLevel` | Informational (public profile) | ⏱️ Request canceled or timed out (validator/network) | -1 | ℹ️ INFO (Public profile): ℹ️ INFO: Authentication test inconclusive due to validator/network timeout or cancellation. | ✅ |
| Wrong Audience (RFC 8707) - logging/setLevel | `logging/setLevel` | Informational (public profile) | ⏱️ Request canceled or timed out (validator/network) | -1 | ℹ️ INFO (Public profile): ℹ️ INFO: Authentication test inconclusive due to validator/network timeout or cancellation. | ✅ |

### Adversarial Input Handling

> **Legend:**
> * **REFLECTED / UNSAFE ECHO**: The server reflected the payload back without sanitization. No execution confirmed.
> * **BLOCKED**: The server correctly rejected or sanitized the input.
> * **SKIPPED**: Test could not be executed (e.g., no tools discovered for injection target).

| Attack Vector | Description | Result | Analysis |
| :--- | :--- | :---: | :--- |
| INJ-001 (Input Validation) | Simulated INJ-001 (Input Validation) attack on endpoint | 🛡️ BLOCKED | `A task was canceled.` |
| INJ-002 (Input Validation) | Simulated INJ-002 (Input Validation) attack on endpoint | 🛡️ BLOCKED | `A task was canceled.` |
| INJ-003 (Input Validation) | Simulated INJ-003 (Input Validation) attack on endpoint | 🛡️ BLOCKED | `A task was canceled.` |
| MCP-SEC-001 | JSON-RPC Error Smuggling | 🛡️ BLOCKED | `Server handled malformed requests gracefully with standard errors.` |
| MCP-SEC-002 | Metadata Enumeration | ⏭️ SKIPPED | `Skipped: Metadata enumeration requires an advertised concrete resource URI; no like-for-like resource surface was available.` |
| MCP-SEC-003 | Schema Confusion | ⏭️ SKIPPED | `Skipped: Could not list tools to test.` |
| MCP-AI-001 | Hallucination Fuzzing | ⏭️ SKIPPED | `Skipped: Could not list tools for fuzzing.` |

## 14. Protocol Compliance

**Status Detail:** roots/list: supported | logging/setLevel: supported | sampling/createMessage: supported | completion/complete: supported

### Compliance Violations
| ID | Source | Description | Severity | Recommendation |
| :--- | :--- | :--- | :---: | :--- |
| MCP-PROTO-ERR | `spec` | JSON-RPC Error Code Violation: Parse Error | High | This specific error code test failed. Ensure the server returns standard JSON-RPC error codes for known error conditions: -32700 (parse error), -32600 (invalid request), -32601 (method not found), -32602 (invalid params), -32603 (internal error). See https://spec.modelcontextprotocol.io/specification/2025-11-25/basic/json-rpc#errors |
| MCP-PROTO-ERR | `spec` | JSON-RPC Error Code Violation: Invalid Request - Missing jsonrpc | High | This specific error code test failed. Ensure the server returns standard JSON-RPC error codes for known error conditions: -32700 (parse error), -32600 (invalid request), -32601 (method not found), -32602 (invalid params), -32603 (internal error). See https://spec.modelcontextprotocol.io/specification/2025-11-25/basic/json-rpc#errors |
| MCP-PROTO-ERR | `spec` | JSON-RPC Error Code Violation: Method Not Found | High | This specific error code test failed. Ensure the server returns standard JSON-RPC error codes for known error conditions: -32700 (parse error), -32600 (invalid request), -32601 (method not found), -32602 (invalid params), -32603 (internal error). See https://spec.modelcontextprotocol.io/specification/2025-11-25/basic/json-rpc#errors |
| MCP-PROTO-ERR | `spec` | JSON-RPC Error Code Violation: Invalid Params | High | This specific error code test failed. Ensure the server returns standard JSON-RPC error codes for known error conditions: -32700 (parse error), -32600 (invalid request), -32601 (method not found), -32602 (invalid params), -32603 (internal error). See https://spec.modelcontextprotocol.io/specification/2025-11-25/basic/json-rpc#errors |
| MCP-PROTO-JSONRPC | `spec` | Request format does not comply with JSON-RPC 2.0 specification | High | Ensure every JSON-RPC request includes "jsonrpc": "2.0", a string "method" field, and an integer "id". The "params" field, if present, must be an object or array. See https://spec.modelcontextprotocol.io/specification/2025-11-25/basic/json-rpc |
| MCP-PROTO-JSONRPC | `spec` | Response format does not comply with JSON-RPC 2.0 specification | High | Every JSON-RPC response must include "jsonrpc": "2.0" and the same "id" as the request. Successful responses use a "result" field; failures use an "error" object with code and message. See https://spec.modelcontextprotocol.io/specification/2025-11-25/basic/json-rpc |
| MCP-PROTO-JSONRPC | `spec` | Batch processing implementation is inconsistent or incomplete | Medium | If the server accepts batch requests, it must process each request independently and return an array of responses in any order. Ensure batch handling does not drop or duplicate responses. See https://www.jsonrpc.org/specification#batch |
| MCP-PROTO-ERR | `spec` | Error codes do not comply with JSON-RPC 2.0 standard error codes | High | Review all error responses to ensure they use standard JSON-RPC 2.0 error codes. Custom error codes must be outside the reserved range (-32000 to -32099 for server errors). See https://spec.modelcontextprotocol.io/specification/2025-11-25/basic/json-rpc#errors |

## 15. Tool Validation

**Tools Discovered:** 0

## 16. Resource Capabilities

**Resources Discovered:** 0

## 17. Optional MCP Capabilities

These probes check whether the server supports optional MCP features beyond the core primitives.

| Capability | Status |
| :--- | :--- |
| `roots/list` | ✅ supported |
| `logging/setLevel` | ✅ supported |
| `sampling/createMessage` | ✅ supported |
| `completion/complete` | ✅ supported |

## 18. Performance Metrics

**Status:** ➖ Skipped
**Measurements:** unavailable
**Reason:** Public remote synthetic load probe did not capture measurements before timeout/cancellation; results are reported as advisory and excluded from pass/fail decisions.

**Critical Errors:**
- Operation timed out or was cancelled

## 19. Recommendations

- 💡 Spec: This specific error code test failed. Ensure the server returns standard JSON-RPC error codes for known error conditions: -32700 (parse error), -32600 (invalid request), -32601 (method not found), -32602 (invalid params), -32603 (internal error). See https://spec.modelcontextprotocol.io/specification/2025-11-25/basic/json-rpc#errors
- 💡 Spec: Ensure every JSON-RPC request includes "jsonrpc": "2.0", a string "method" field, and an integer "id". The "params" field, if present, must be an object or array. See https://spec.modelcontextprotocol.io/specification/2025-11-25/basic/json-rpc
- 💡 Spec: Every JSON-RPC response must include "jsonrpc": "2.0" and the same "id" as the request. Successful responses use a "result" field; failures use an "error" object with code and message. See https://spec.modelcontextprotocol.io/specification/2025-11-25/basic/json-rpc
- 💡 Spec: Review all error responses to ensure they use standard JSON-RPC 2.0 error codes. Custom error codes must be outside the reserved range (-32000 to -32099 for server errors). See https://spec.modelcontextprotocol.io/specification/2025-11-25/basic/json-rpc#errors
- 💡 Spec: If the server accepts batch requests, it must process each request independently and return an array of responses in any order. Ensure batch handling does not drop or duplicate responses. See https://www.jsonrpc.org/specification#batch
- 💡 Operational: Investigate why the performance probe ended without captured measurements (Public remote synthetic load probe did not capture measurements before timeout/cancellation; results are reported as advisory and excluded from pass/fail decisions.) before treating runtime behavior as representative

---
*Produced with [MCP Validator](https://github.com/navalerakesh/mcp-validation-security) · [McpVal on NuGet](https://www.nuget.org/packages/McpVal#versions-body-tab)*
