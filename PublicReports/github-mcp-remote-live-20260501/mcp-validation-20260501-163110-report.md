# MCP Server Compliance & Validation Report
**Generated:** 2026-05-01 16:31:10 UTC

## 1. Executive Summary

| Metric | Value |
| :--- | :--- |
| **Server Endpoint** | `https://api.githubcopilot.com/mcp/` |
| **Validation ID** | `d5e5297d-d079-4e5c-9a85-5243932227a6` |
| **Overall Status** | ❌ **Failed** |
| **Baseline Verdict** | **Reject** |
| **Protocol Verdict** | **Reject** |
| **Coverage Verdict** | **Trusted** |
| **Compliance Score** | **50.1%** |
| **Evidence Coverage** | **100.0%** |
| **Evidence Confidence** | **High (100.0%)** |
| **Compliance Profile** | `Authenticated (Inferred)` |
| **Duration** | 31.12s |
| **Transport** | HTTP |
| **Session Bootstrap** | ✅ Healthy |
| **Deferred Validation** | No — validation started from a clean bootstrap state. |
| **MCP Protocol Version (Effective)** | `2025-11-25` |
| **Benchmark Trust Level** | 🟠 **L2: Caution — Significant gaps in safety or compliance** |

## 2. Connectivity & Session Bootstrap

This section explains how the validator established initial connectivity and whether validation started from a clean initialize handshake or a deferred advisory state.

| Bootstrap Signal | Value |
| :--- | :--- |
| **Bootstrap State** | ✅ Healthy |
| **Validation Proceeded Under Deferment** | No — validation started from a clean bootstrap state. |
| **Initialize Handshake** | ✅ Initialize handshake succeeded. |
| **Handshake HTTP Status** | `HTTP 200` |
| **Handshake Duration** | 1361.6 ms |
| **Negotiated Protocol** | `2025-11-25` |
| **Observed Server Version** | `github-mcp-server/remote-3242d9e12bd9ffa96a76388614e42ce90d05f764` |
| **Server Profile Resolution** | `Authenticated (Inferred)` |

Validation began from a clean bootstrap with no deferred connectivity risk carried into later categories.

## 3. Priority Findings

These are the highest-signal outcomes from this validation run.
> **Authority legend:** Authority order: Spec blocking and warnings, then Guideline, Heuristic, and Operational signals.

- [Spec] 7 protocol violation(s), led by MCP-PROTO-JSONRPC: Request format does not comply with JSON-RPC 2.0 specification
- [Guideline] 6 guidance signal(s), led by MCP.GUIDELINE.AUTH.SECURE_COMPATIBLE_REJECTION: No Auth - initialize: secure authentication behavior observed without the preferred MCP/OAuth challenge pattern.
- [Heuristic] 11 deterministic AI-readiness advisory signal(s), led by AI.TOOL.SCHEMA.REQUIRED_ARRAY_SHAPE_MISSING: Tool 'push_files': 1/5 required array parameters lack item schemas or minItems guidance

## 4. Action Hints

Compact next-step guidance derived from the highest-signal evidence in this run.

- [Spec] Protocol: Ensure every JSON-RPC request includes "jsonrpc": "2.0", a string "method" field, and a string or integer "id". The "params" field....
- [Guideline] Authentication Security: No Auth - initialize: secure authentication behavior observed without the preferred MCP/OAuth challenge pattern.
- Spec: Ensure every JSON-RPC request includes "jsonrpc": "2.0", a string "method" field, and a string or integer "id". The "params" field, if presen....
- Spec: Validate the Origin header for HTTP transports and reject invalid origins with HTTP 403 Forbidden before processing the JSON-RPC message. See....

## 5. Recommended Remediation Order

Fix blocking dependencies in this order so later validation evidence becomes trustworthy instead of merely quieter.

### Priority 1: Bootstrap & Protocol Version

**Impact after fix:** Lifecycle, version, transport, and JSON-RPC fixes make downstream tool, resource, prompt, and auth probes trustworthy.

| Issue | Fix | Component | Evidence |
| :--- | :--- | :--- | :--- |
| MUST requirement failed: MUST: Use standard JSON-RPC error codes — Non-standard error codes | Fix the lifecycle or protocol contract first, then rerun validation before interpreting downstream surface results. | `errors` | [Spec] · `TierCheck` · Critical |
| Request format does not comply with JSON-RPC 2.0 specification | Ensure every JSON-RPC request includes "jsonrpc": "2.0", a string "method" field, and a string or integer "id". The "params" field, if present, must be an object or array. See https://spec.modelcontextprotocol.io/specification/2025-11-25/basic/json-rpc | `JSON-RPC Compliance` | [Spec] · `MCP-PROTO-JSONRPC` · High |
| Streamable HTTP transport violation (invalid-origin-403): HTTP transports must validate Origin and reject invalid origins with HTTP 403 Forbidden. Expected: HTTP 403 Forbidden. Observed: HTTP 200; Content-Type=text/event-stream; body length 84 | Validate the Origin header for HTTP transports and reject invalid origins with HTTP 403 Forbidden before processing the JSON-RPC message. See https://spec.modelcontextprotocol.io/specification/2025-11-25/basic/transports#origin-header-validation | `Transport` | [Spec] · `MCP-HTTP-005` · High |
| Error codes do not comply with JSON-RPC 2.0 standard error codes | Review all error responses to ensure they use standard JSON-RPC 2.0 error codes. Custom error codes must be outside the reserved range (-32000 to -32099 for server errors). See https://spec.modelcontextprotocol.io/specification/2025-11-25/basic/json-rpc#errors | `JSON-RPC Compliance` | [Spec] · `MCP-PROTO-ERR` · Low |
| JSON-RPC Error Code Violation: Invalid Params | This specific error code test failed. Ensure the server returns standard JSON-RPC error codes for known error conditions: -32700 (parse error), -32600 (invalid request), -32601 (method not found), -32602 (invalid params), -32603 (internal error). See https://spec.modelcontextprotocol.io/specification/2025-11-25/basic/json-rpc#errors | `JSON-RPC Compliance` | [Spec] · `MCP-PROTO-ERR` · Low |

### Priority 2: Authentication Boundary

**Impact after fix:** Auth fixes let protected-surface failures distinguish real product behavior from missing, rejected, or mis-scoped credentials.

| Issue | Fix | Component | Evidence |
| :--- | :--- | :--- | :--- |
| Invalid Token - prompts/get: token was rejected with HTTP 400 instead of HTTP 401. | Fix authentication and token-boundary behavior before judging protected tool, resource, and prompt behavior. | `prompts/get` | [Spec] · `MCP.AUTH.INVALID_TOKEN_STATUS` · Medium |

### Priority 3: Advertised Capabilities

**Impact after fix:** Capability-contract fixes make skipped and executed tool, resource, prompt, and task checks align with what the server declared.

| Issue | Fix | Component | Evidence |
| :--- | :--- | :--- | :--- |
| Name/URI suggests potential system impact capability (matched keyword: 'delete'). | Align advertised capabilities with implemented surfaces so downstream validation probes run only where applicable. | `delete_file` | [Heuristic] · `ContentSafety.SystemImpact` · High |
| Synthetic load probe executed against tools/list using 20 requests at concurrency 4. | Interpret this as a generic pressure probe, not a workload-specific SLA benchmark. | `tools/list` | [Guideline] · `MCP.GUIDELINE.PERFORMANCE.SYNTHETIC_PROBE` · Info |

### Priority 4: AI Safety, Security, And Performance

**Impact after fix:** After protocol, auth, and capability gates are stable, safety and performance evidence can be prioritized without masking core contract failures.

| Issue | Fix | Component | Evidence |
| :--- | :--- | :--- | :--- |
| 🔴 LLM-Friendliness: 10/100 (Anti-LLM) — Error will cause AI hallucination/loops | Address advisory safety, security, and performance evidence after the core validation contract is trustworthy. | `delete_file` | [Heuristic] · `AI.TOOL.ERROR.LLM_FRIENDLINESS` · High |

## 6. Deterministic Verdicts

These verdicts are the authoritative gate for pass/fail and policy decisions. Weighted benchmark scores remain descriptive only.
> **Authority legend:** Authority order: Spec blocking and warnings, then Guideline, Heuristic, and Operational signals.

| Lane | Verdict | Meaning |
| :--- | :--- | :--- |
| **Baseline** | **Reject** | Overall deterministic gate across blocking findings and coverage debt. |
| **Protocol** | **Reject** | Protocol correctness and contract integrity. |
| **Coverage** | **Trusted** | Whether enabled validation surfaces produced authoritative evidence. |

Baseline=Reject; Protocol=Reject; Coverage=Trusted; EvidenceConfidence=High (100%); BlockingDecisions=97.

### Blocking Decisions

- **Reject** [TierCheck] `errors`: MUST requirement failed: MUST: Use standard JSON-RPC error codes — Non-standard error codes
  - Evidence: IDs `tier-check:must:errors:must--use-standard-json-rpc-error-codes`; preview: Non-standard error codes
- **Reject** [JSON-RPC Compliance] `JSON-RPC Compliance`: Request format does not comply with JSON-RPC 2.0 specification
  - Evidence: IDs `protocol-violation:mcp-proto-jsonrpc:json-rpc-compliance:request-format-does-not-comply-with-json-rpc-2-0-specification`, `protocol-mcp-proto-err`, `protocol-mcp-proto-err-2`, `protocol-mcp-proto-err-3`, `protocol-mcp-proto-err-4`; preview: JSON-RPC Error Code Violation: Parse Error; spec: https://spec.modelcontextprotocol.io/specification/2025-11-25/basic/json-rpc; remediation: Ensure every JSON-RPC request includes "jsonrpc": "2.0", a string "method" field, and a string or integer "id". The "params" field, if present, must be an object or array. See https://spec.modelcontextprotocol.io/specification/2025-11-25/basic/json-rpc
- **Reject** [Protocol] `invalid-method`: Error-handling scenario 'Invalid Method Call' was rejected at the HTTP front door instead of returning JSON-RPC error code -32601.
  - Evidence: IDs `structured-finding:mcp-error-handling-non-standard-error-response:invalid-method:error-handling-scenario--invalid-method-call--was-rejected-at-the-http-front-door-instead-of-returning-json-rpc-error-code--32601`, `error-invalid-method-call`; preview: HTTP 400; HTTP 400 Bad Request. Body: JSON RPC not handled: "nonexistent_method_12345" unsupported; spec: https://spec.modelcontextprotocol.io/specification/2025-11-25/basic/json-rpc#errors; remediation: This specific error code test failed. Ensure the server returns standard JSON-RPC error codes for known error conditions: -32700 (parse error), -32600 (invalid request), -32601 (method not found), -32602 (invalid params), -32603 (internal error). See https://spec.modelcontextprotocol.io/specification/2025-11-25/basic/json-rpc#errors
- **Reject** [Protocol] `invalid-request`: Error-handling scenario 'Graceful Degradation On Invalid Request' was rejected at the HTTP front door instead of returning JSON-RPC error code -32600.
  - Evidence: IDs `structured-finding:mcp-error-handling-non-standard-error-response:invalid-request:error-handling-scenario--graceful-degradation-on-invalid-request--was-rejected-at-the-http-front-door-instead-of-returning-json-rpc-error-code--32600`, `error-graceful-degradation-on-invalid-request`; preview: HTTP 400; malformed payload: invalid message version tag ""; expected "2.0"; spec: https://spec.modelcontextprotocol.io/specification/2025-11-25/basic/json-rpc#errors; remediation: This specific error code test failed. Ensure the server returns standard JSON-RPC error codes for known error conditions: -32700 (parse error), -32600 (invalid request), -32601 (method not found), -32602 (invalid params), -32603 (internal error). See https://spec.modelcontextprotocol.io/specification/2025-11-25/basic/json-rpc#errors
- **Reject** [Protocol] `malformed-json`: Error-handling scenario 'Malformed JSON Payload' was rejected at the HTTP front door instead of returning JSON-RPC error code -32700.
  - Evidence: IDs `structured-finding:mcp-error-handling-non-standard-error-response:malformed-json:error-handling-scenario--malformed-json-payload--was-rejected-at-the-http-front-door-instead-of-returning-json-rpc-error-code--32700`, `error-malformed-json-payload`; preview: HTTP 400; malformed payload: unmarshaling jsonrpc message: json: expected '"' at the beginning of a string value: invalid json; spec: https://spec.modelcontextprotocol.io/specification/2025-11-25/basic/json-rpc#errors; remediation: This specific error code test failed. Ensure the server returns standard JSON-RPC error codes for known error conditions: -32700 (parse error), -32600 (invalid request), -32601 (method not found), -32602 (invalid params), -32603 (internal error). See https://spec.modelcontextprotocol.io/specification/2025-11-25/basic/json-rpc#errors
- **Reject** [Transport] `Transport`: Streamable HTTP transport violation (invalid-origin-403): HTTP transports must validate Origin and reject invalid origins with HTTP 403 Forbidden. Expected: HTTP 403 Forbidden. Observed: HTTP 200; Content-Type=text/event-stream; body length 84
  - Evidence: IDs `protocol-violation:mcp-http-005:transport:streamable-http-transport-violation--invalid-origin-403---http-transports-must-validate-origin-and-reject-invalid-origins-with-http-403-forbidden--expected--http-403-forbidden--observed--http-200--content-type-text-event-stream--body-length-84`, `protocol-mcp-http-005`; preview: Streamable HTTP transport violation (invalid-origin-403): HTTP transports must validate Origin and reject invalid origins with HTTP 403 Forbidden. Expected: HTTP 403 Forbidden. Observed: HTTP 200; Content-Type=text/event-stream; body length 84; spec: https://spec.modelcontextprotocol.io/specification/2025-11-25/basic/transports#streamable-http; remediation: Validate the Origin header for HTTP transports and reject invalid origins with HTTP 403 Forbidden before processing the JSON-RPC message. See https://spec.modelcontextprotocol.io/specification/2025-11-25/basic/transports#origin-header-validation
- **Review Required** [Authentication Security] `prompts/get`: Invalid Token - prompts/get: token was rejected with HTTP 400 instead of HTTP 401.
  - Evidence: IDs `structured-finding:mcp-auth-invalid-token-status:prompts-get:invalid-token---prompts-get--token-was-rejected-with-http-400-instead-of-http-401`, `auth-no-auth-prompts-get`, `auth-malformed-token-prompts-get`, `auth-invalid-token-prompts-get`, `auth-token-expired-prompts-get`; preview: ⚠️ COMPATIBLE: Authentication appears to be enforced at the application layer instead of via an HTTP challenge.; spec: https://modelcontextprotocol.io/specification/2025-06-18/basic/authorization; remediation: Return HTTP 401 for invalid, expired, revoked, or wrong-audience access tokens.
- **Review Required** [Authentication Security] `prompts/get`: Token Expired - prompts/get: token was rejected with HTTP 400 instead of HTTP 401.
  - Evidence: IDs `structured-finding:mcp-auth-invalid-token-status:prompts-get:token-expired---prompts-get--token-was-rejected-with-http-400-instead-of-http-401`, `auth-no-auth-prompts-get`, `auth-malformed-token-prompts-get`, `auth-invalid-token-prompts-get`, `auth-token-expired-prompts-get`; preview: ⚠️ COMPATIBLE: Authentication appears to be enforced at the application layer instead of via an HTTP challenge.; spec: https://modelcontextprotocol.io/specification/2025-06-18/basic/authorization; remediation: Return HTTP 401 for invalid, expired, revoked, or wrong-audience access tokens.

## 7. Benchmark Trust Profile

Multi-dimensional benchmarking view of server trustworthiness for AI agent consumption.
This section is descriptive only. Release gating and pass/fail status are driven by the deterministic verdicts above, not by weighted trust averages.

| Dimension | Score | What It Measures |
| :--- | :---: | :--- |
| **Protocol Compliance** | 60% | MCP spec adherence, JSON-RPC compliance, response structures |
| **Security Posture** | 100% | Auth compliance, injection resistance, attack surface |
| **AI Safety** | 84% | Schema quality, destructive tool detection, exfiltration risk, prompt injection surface |
| **Operational Readiness** | 100% | Latency, throughput, error rate, stability |
| **LLM-Friendliness** | 10% | 🔴 Anti-LLM — Do error responses help AI agents self-correct? |

### AI Boundary Findings

These findings go **beyond MCP protocol** to assess how AI agents interact with this server.

| Category | Component | Severity | Finding |
| :--- | :--- | :---: | :--- |
| Destructive | `create_branch` | 🟡 Medium | Tool 'create_branch' appears to perform write/destructive operations. AI agents SHOULD require human confirmation. |
| Destructive | `create_or_update_file` | 🟡 Medium | Tool 'create_or_update_file' appears to perform write/destructive operations. AI agents SHOULD require human confirmation. |
| Destructive | `create_pull_request` | 🟡 Medium | Tool 'create_pull_request' appears to perform write/destructive operations. AI agents SHOULD require human confirmation. |
| Destructive | `create_repository` | 🟡 Medium | Tool 'create_repository' appears to perform write/destructive operations. AI agents SHOULD require human confirmation. |
| Destructive | `delete_file` | 🟠 High | Tool 'delete_file' declares destructiveHint=true. AI agents SHOULD require human confirmation before invocation. |
| Destructive | `issue_write` | 🟡 Medium | Tool 'issue_write' appears to perform write/destructive operations. AI agents SHOULD require human confirmation. |
| Destructive | `pull_request_review_write` | 🟡 Medium | Tool 'pull_request_review_write' appears to perform write/destructive operations. AI agents SHOULD require human confirmation. |
| Destructive | `sub_issue_write` | 🟡 Medium | Tool 'sub_issue_write' appears to perform write/destructive operations. AI agents SHOULD require human confirmation. |
| Destructive | `update_pull_request` | 🟡 Medium | Tool 'update_pull_request' appears to perform write/destructive operations. AI agents SHOULD require human confirmation. |
| Destructive | `update_pull_request_branch` | 🟡 Medium | Tool 'update_pull_request_branch' appears to perform write/destructive operations. AI agents SHOULD require human confirmation. |
| LLM-Hostile | `Error Responses` | 🟠 High | Average LLM-friendliness score is 10/100 (Anti-LLM). Error messages don't help AI agents self-correct, causing hallucination and retry loops. |

### Summary

- Destructive tools: **10/41 (24%)**
- Data exfiltration risk: **0**
- Prompt injection surface: **0**

### MCP Spec Compliance (RFC 2119 Tiers)

| Tier | Passed | Failed | Total | Impact |
| :--- | :---: | :---: | :---: | :--- |
| **MUST** | 10 | 1 | 11 | ❌ Non-compliant (trust capped at L2) |
| **SHOULD** | 5 | 0 | 5 | ✅ All expected behaviors present |
| **MAY** | 3 | 3 | 6 | ℹ️ Informational (no score impact) |

#### ❌ MUST Failures (Compliance Blockers)

- **MUST: Use standard JSON-RPC error codes** (errors) — Non-standard error codes

#### ℹ️ Optional Features (MAY)

- ➖ MAY: Server supports logging/setLevel
- ➖ MAY: Server supports sampling/createMessage
- ➖ MAY: Server supports roots/list
- ✅ MAY: Server supports completion/complete
- ✅ MAY: Server supports resources/templates/list
- ✅ MAY: Tools include 'annotations' (readOnlyHint, destructiveHint)

## 8. Client Profile Compatibility

Documented host-side compatibility assessments derived from the neutral validation evidence collected during this run.

| Client Profile | Status | Requirements | Documentation |
| :--- | :--- | :--- | :--- |
| **Claude Code** | ⚠️ Compatible with warnings | 3 passed / 2 warnings / 0 failed | <https://code.claude.com/docs/en/mcp> |
| **VS Code Copilot Agent** | ✅ Compatible | 3 passed / 0 warnings / 0 failed | <https://code.visualstudio.com/docs/copilot/chat/mcp-servers> |
| **GitHub Copilot CLI** | ✅ Compatible | 2 passed / 0 warnings / 0 failed | <https://docs.github.com/en/copilot/how-tos/copilot-cli/customize-copilot/add-mcp-servers> |
| **GitHub Copilot Cloud Agent** | ⚠️ Compatible with warnings | 3 passed / 1 warnings / 0 failed | <https://docs.github.com/en/copilot/how-tos/use-copilot-agents/cloud-agent/extend-cloud-agent-with-mcp> |
| **Visual Studio Copilot** | ⚠️ Compatible with warnings | 3 passed / 1 warnings / 0 failed | <https://learn.microsoft.com/en-us/visualstudio/ide/mcp-servers?view=vs-2022> |

### Claude Code

**Status:** ⚠️ Compatible with warnings

Required compatibility checks passed; 2 advisory requirements still need follow-up.

| Requirement | Level | Outcome | Rule IDs | Affected Components | Details |
| :--- | :--- | :--- | :--- | :--- | :--- |
| Initialize instructions help Claude Code find and use the server correctly | Recommended | ⚠️ Warning | - | - | Claude Code documentation recommends clear initialize instructions for tool search and server guidance, but initialize.instructions was missing. |
| Dynamic tool, prompt, and resource updates are declared for Claude Code | Recommended | ⚠️ Warning | - | `tools`, `prompts` | Claude Code documents dynamic list_changed updates, but this server did not advertise listChanged for the discovered tools, prompts surfaces. |

#### Remediation

- ⚠️ **Initialize instructions help Claude Code find and use the server correctly:** Populate initialize.instructions with concise guidance about when Claude should search and use this server.
- ⚠️ **Dynamic tool, prompt, and resource updates are declared for Claude Code:** Declare listChanged for tools, prompts when the server can notify clients about catalog changes without reconnecting.

### VS Code Copilot Agent

**Status:** ✅ Compatible

All applicable compatibility checks passed (3 satisfied).

- No client-specific compatibility gaps were detected.

### GitHub Copilot CLI

**Status:** ✅ Compatible

All applicable compatibility checks passed (2 satisfied).

- No client-specific compatibility gaps were detected.

### GitHub Copilot Cloud Agent

**Status:** ⚠️ Compatible with warnings

Required compatibility checks passed; 1 advisory requirement still needs follow-up.

| Requirement | Level | Outcome | Rule IDs | Affected Components | Details |
| :--- | :--- | :--- | :--- | :--- | :--- |
| Only the tool surface is currently consumed | Recommended | ⚠️ Warning | - | - | This profile currently consumes tools only; 2 prompt(s) will not contribute to compatibility. |

### Visual Studio Copilot

**Status:** ⚠️ Compatible with warnings

Required compatibility checks passed; 1 advisory requirement still needs follow-up.

| Requirement | Level | Outcome | Rule IDs | Affected Components | Details |
| :--- | :--- | :--- | :--- | :--- | :--- |
| Tool updates are declared through notifications/tools/list_changed | Recommended | ⚠️ Warning | - | `tools` | Visual Studio documents dynamic list_changed updates, but this server did not advertise listChanged for the discovered tools surfaces. |

#### Remediation

- ⚠️ **Tool updates are declared through notifications/tools/list_changed:** Declare listChanged for tools when the server can notify clients about catalog changes without reconnecting.

## 9. Validation Envelope

This section exposes the structured validation envelope used for layered reporting, coverage tracking, and pack provenance.

### Assessment Layers

| Layer | Status | Findings | Summary |
| :--- | :--- | :---: | :--- |
| `protocol-core` | ✅ Passed | 2 | declared capabilities: completions, prompts, resources, tools \| roots/list: not advertised \| logging/setLevel: not advertised \| sampling/createMessage: not advertised \| completion/complete: supported |
| `tool-surface` | ✅ Passed | 151 | - |
| `resource-surface` | ✅ Passed | 0 | - |
| `prompt-surface` | ✅ Passed | 3 | - |
| `security-boundaries` | ✅ Passed | 58 | - |
| `performance` | ✅ Passed | 1 | - |
| `error-handling` | ❌ Failed | 3 | Validated 5 error scenario(s); 2 handled correctly. |
| `client-profiles` | ✅ Passed | 0 | Evaluated 5 profile(s); 3 warning profile(s); 0 incompatible profile(s). |

### Scenario Outcomes

| Scenario | Status | Findings | Summary |
| :--- | :--- | :---: | :--- |
| `bootstrap-initialize-handshake` | ✅ Passed | 0 | Bootstrap health checks completed successfully. |
| `protocol-compliance-review` | ✅ Passed | 2 | declared capabilities: completions, prompts, resources, tools \| roots/list: not advertised \| logging/setLevel: not advertised \| sampling/createMessage: not advertised \| completion/complete: supported |
| `tool-catalog-smoke` | ✅ Passed | 154 | Tool validation completed. |
| `resource-catalog-smoke` | ✅ Passed | 0 | No resources were discovered during validation. |
| `prompt-catalog-smoke` | ✅ Passed | 3 | Prompt validation completed. |
| `security-authentication-challenge` | ✅ Passed | 0 | Evaluated 58 authentication challenge scenario(s). |
| `security-attack-simulations` | ➖ Skipped | 0 | Evaluated 7 attack simulation(s). |
| `error-handling-matrix` | ❌ Failed | 3 | Validated 5 error scenario(s); 2 handled correctly. |

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
| `protocol-mcp-proto-err-3` | `protocol-core` | `JSON-RPC Compliance` | `protocol-violation` | JSON-RPC Error Code Violation: Method Not Found |
| `protocol-mcp-proto-err-4` | `protocol-core` | `JSON-RPC Compliance` | `protocol-violation` | JSON-RPC Error Code Violation: Invalid Params |
| `protocol-mcp-proto-jsonrpc` | `protocol-core` | `JSON-RPC Compliance` | `protocol-violation` | Request format does not comply with JSON-RPC 2.0 specification |
| `protocol-mcp-proto-err-5` | `protocol-core` | `JSON-RPC Compliance` | `protocol-violation` | Error codes do not comply with JSON-RPC 2.0 standard error codes |
| `protocol-mcp-http-005` | `protocol-core` | `Transport` | `protocol-violation` | Streamable HTTP transport violation (invalid-origin-403): HTTP transports must validate Origin and reject invalid origins with HTTP 403 Forbidden. Expected: HTTP 403 Forbidden. Observed: HTTP 200; Content-Type=text/event-stream; body length 84 |
| `tool-tools-list-schema-compliance` | `tool-surface` | `tools/list (Schema Compliance)` | `tool-result` | ⚠️ Schema validation warning: tools/list schema could not be fully processed |
| `tool-add-comment-to-pending-review` | `tool-surface` | `add_comment_to_pending_review` | `tool-result` | Tool 'add_comment_to_pending_review' does not declare annotations.readOnlyHint. |
| `tool-add-issue-comment` | `tool-surface` | `add_issue_comment` | `tool-result` | Tool 'add_issue_comment' does not declare annotations.readOnlyHint. |
| `tool-add-reply-to-pull-request-comment` | `tool-surface` | `add_reply_to_pull_request_comment` | `tool-result` | Tool 'add_reply_to_pull_request_comment' does not declare annotations.readOnlyHint. |
| `tool-create-branch` | `tool-surface` | `create_branch` | `tool-result` | Tool 'create_branch' does not declare annotations.readOnlyHint. |
| `tool-create-or-update-file` | `tool-surface` | `create_or_update_file` | `tool-result` | Tool 'create_or_update_file' does not declare annotations.readOnlyHint. |
| `tool-create-pull-request` | `tool-surface` | `create_pull_request` | `tool-result` | Tool 'create_pull_request' does not declare annotations.readOnlyHint. |
| `tool-create-repository` | `tool-surface` | `create_repository` | `tool-result` | Tool 'create_repository' does not declare annotations.readOnlyHint. |
| `tool-delete-file` | `tool-surface` | `delete_file` | `tool-result` | ℹ️ Server returned JSON-RPC error: failed to get branch reference: GET https://api.github.com/repos/test-check/test-check/git/ref/heads/test-check: 404 Not Found [] (code: 0) |
| `tool-fork-repository` | `tool-surface` | `fork_repository` | `tool-result` | Tool 'fork_repository' does not declare annotations.readOnlyHint. |
| `tool-get-commit` | `tool-surface` | `get_commit` | `tool-result` | Tool 'get_commit' does not declare annotations.destructiveHint. |
| `tool-get-file-contents` | `tool-surface` | `get_file_contents` | `tool-result` | Tool 'get_file_contents' does not declare annotations.destructiveHint. |
| `tool-get-label` | `tool-surface` | `get_label` | `tool-result` | Tool 'get_label' does not declare annotations.destructiveHint. |
| `tool-get-latest-release` | `tool-surface` | `get_latest_release` | `tool-result` | ℹ️ Server returned JSON-RPC error: failed to get latest release: GET https://api.github.com/repos/test-check/test-check/releases/latest: 404 Not Found [] (code: 0) |
| `tool-get-me` | `tool-surface` | `get_me` | `tool-result` | Tool 'get_me' does not declare annotations.destructiveHint. |
| `tool-get-release-by-tag` | `tool-surface` | `get_release_by_tag` | `tool-result` | Tool 'get_release_by_tag' does not declare annotations.destructiveHint. |
| `tool-get-tag` | `tool-surface` | `get_tag` | `tool-result` | Tool 'get_tag' does not declare annotations.destructiveHint. |
| `tool-get-team-members` | `tool-surface` | `get_team_members` | `tool-result` | Tool 'get_team_members' does not declare annotations.destructiveHint. |
| `tool-get-teams` | `tool-surface` | `get_teams` | `tool-result` | Tool 'get_teams' does not declare annotations.destructiveHint. |
| `tool-issue-read` | `tool-surface` | `issue_read` | `tool-result` | Tool 'issue_read' does not declare annotations.destructiveHint. |
| `tool-issue-write` | `tool-surface` | `issue_write` | `tool-result` | Tool 'issue_write' does not declare annotations.readOnlyHint. |
| `tool-list-branches` | `tool-surface` | `list_branches` | `tool-result` | Tool 'list_branches' does not declare annotations.destructiveHint. |
| `tool-list-commits` | `tool-surface` | `list_commits` | `tool-result` | Tool 'list_commits' does not declare annotations.destructiveHint. |
| `tool-list-issue-types` | `tool-surface` | `list_issue_types` | `tool-result` | Tool 'list_issue_types' does not declare annotations.destructiveHint. |
| `tool-list-issues` | `tool-surface` | `list_issues` | `tool-result` | Tool 'list_issues' does not declare annotations.destructiveHint. |
| `tool-list-pull-requests` | `tool-surface` | `list_pull_requests` | `tool-result` | Tool 'list_pull_requests' does not declare annotations.destructiveHint. |
| `tool-list-releases` | `tool-surface` | `list_releases` | `tool-result` | ℹ️ Server returned JSON-RPC error: failed to list releases: GET https://api.github.com/repos/test-check/test-check/releases?page=1&per_page=1: 404 Not Found [] (code: 0) |
| `tool-list-tags` | `tool-surface` | `list_tags` | `tool-result` | Tool 'list_tags' does not declare annotations.destructiveHint. |
| `tool-merge-pull-request` | `tool-surface` | `merge_pull_request` | `tool-result` | Tool 'merge_pull_request' does not declare annotations.readOnlyHint. |
| `tool-pull-request-read` | `tool-surface` | `pull_request_read` | `tool-result` | Tool 'pull_request_read' does not declare annotations.destructiveHint. |
| `tool-pull-request-review-write` | `tool-surface` | `pull_request_review_write` | `tool-result` | Tool 'pull_request_review_write' does not declare annotations.readOnlyHint. |
| `tool-push-files` | `tool-surface` | `push_files` | `tool-result` | Tool 'push_files' does not declare annotations.readOnlyHint. |
| `tool-request-copilot-review` | `tool-surface` | `request_copilot_review` | `tool-result` | Tool 'request_copilot_review' does not declare annotations.readOnlyHint. |
| `tool-run-secret-scanning` | `tool-surface` | `run_secret_scanning` | `tool-result` | Tool 'run_secret_scanning' does not declare annotations.destructiveHint. |
| `tool-search-code` | `tool-surface` | `search_code` | `tool-result` | Tool 'search_code' does not declare annotations.destructiveHint. |
| `tool-search-issues` | `tool-surface` | `search_issues` | `tool-result` | Tool 'search_issues' does not declare annotations.destructiveHint. |
| `tool-search-pull-requests` | `tool-surface` | `search_pull_requests` | `tool-result` | Tool 'search_pull_requests' does not declare annotations.destructiveHint. |
| `tool-search-repositories` | `tool-surface` | `search_repositories` | `tool-result` | Tool 'search_repositories' does not declare annotations.destructiveHint. |
| `tool-search-users` | `tool-surface` | `search_users` | `tool-result` | Tool 'search_users' does not declare annotations.destructiveHint. |
| `tool-sub-issue-write` | `tool-surface` | `sub_issue_write` | `tool-result` | Tool 'sub_issue_write' does not declare annotations.readOnlyHint. |
| `tool-update-pull-request` | `tool-surface` | `update_pull_request` | `tool-result` | Tool 'update_pull_request' does not declare annotations.readOnlyHint. |
| `tool-update-pull-request-branch` | `tool-surface` | `update_pull_request_branch` | `tool-result` | Tool 'update_pull_request_branch' does not declare annotations.readOnlyHint. |
| `prompt-assigncodingagent` | `prompt-surface` | `AssignCodingAgent` | `prompt-result` | ✅ prompts/get: messages[] array present (6 messages) |
| `prompt-issue-to-fix-workflow` | `prompt-surface` | `issue_to_fix_workflow` | `prompt-result` | ✅ prompts/get: messages[] array present (5 messages) |
| `auth-no-auth-initialize` | `security-boundaries` | `initialize` | `authentication-scenario` | ⚠️ COMPATIBLE: Discovery metadata was exposed without authentication. |
| `auth-no-auth-prompts-list` | `security-boundaries` | `prompts/list` | `authentication-scenario` | ⚠️ COMPATIBLE: Discovery metadata was exposed without authentication. |
| `auth-malformed-token-prompts-list` | `security-boundaries` | `prompts/list` | `authentication-scenario` | ✅ ALIGNED: Secure OAuth-style challenge returned with HTTP 400. |
| `auth-invalid-token-prompts-list` | `security-boundaries` | `prompts/list` | `authentication-scenario` | ⚠️ COMPATIBLE: Token was rejected, but invalid, expired, revoked, or wrong-audience tokens should receive HTTP 401. |
| `auth-token-expired-prompts-list` | `security-boundaries` | `prompts/list` | `authentication-scenario` | ⚠️ COMPATIBLE: Token was rejected, but invalid, expired, revoked, or wrong-audience tokens should receive HTTP 401. |
| `auth-invalid-scope-prompts-list` | `security-boundaries` | `prompts/list` | `authentication-scenario` | ✅ ALIGNED: Secure OAuth-style challenge returned with HTTP 400. |
| `auth-insufficient-permissions-prompts-list` | `security-boundaries` | `prompts/list` | `authentication-scenario` | ✅ ALIGNED: Secure OAuth-style challenge returned with HTTP 400. |
| `auth-revoked-token-prompts-list` | `security-boundaries` | `prompts/list` | `authentication-scenario` | ⚠️ COMPATIBLE: Token was rejected, but invalid, expired, revoked, or wrong-audience tokens should receive HTTP 401. |
| `auth-wrong-audience-rfc-8707-prompts-list` | `security-boundaries` | `prompts/list` | `authentication-scenario` | ⚠️ COMPATIBLE: Token was rejected, but invalid, expired, revoked, or wrong-audience tokens should receive HTTP 401. |
| `auth-valid-token-prompts-list` | `security-boundaries` | `prompts/list` | `authentication-scenario` | ✅ ALIGNED: Valid authentication was accepted and request processing proceeded. |
| `auth-no-auth-prompts-get` | `security-boundaries` | `prompts/get` | `authentication-scenario` | ⚠️ COMPATIBLE: Authentication appears to be enforced at the application layer instead of via an HTTP challenge. |
| `auth-malformed-token-prompts-get` | `security-boundaries` | `prompts/get` | `authentication-scenario` | ✅ ALIGNED: Secure OAuth-style challenge returned with HTTP 400. |
| `auth-invalid-token-prompts-get` | `security-boundaries` | `prompts/get` | `authentication-scenario` | ⚠️ COMPATIBLE: Token was rejected, but invalid, expired, revoked, or wrong-audience tokens should receive HTTP 401. |
| `auth-token-expired-prompts-get` | `security-boundaries` | `prompts/get` | `authentication-scenario` | ⚠️ COMPATIBLE: Token was rejected, but invalid, expired, revoked, or wrong-audience tokens should receive HTTP 401. |
| `auth-invalid-scope-prompts-get` | `security-boundaries` | `prompts/get` | `authentication-scenario` | ✅ ALIGNED: Secure OAuth-style challenge returned with HTTP 400. |
| `auth-insufficient-permissions-prompts-get` | `security-boundaries` | `prompts/get` | `authentication-scenario` | ✅ ALIGNED: Secure OAuth-style challenge returned with HTTP 400. |
| `auth-revoked-token-prompts-get` | `security-boundaries` | `prompts/get` | `authentication-scenario` | ⚠️ COMPATIBLE: Token was rejected, but invalid, expired, revoked, or wrong-audience tokens should receive HTTP 401. |
| `auth-wrong-audience-rfc-8707-prompts-get` | `security-boundaries` | `prompts/get` | `authentication-scenario` | ⚠️ COMPATIBLE: Token was rejected, but invalid, expired, revoked, or wrong-audience tokens should receive HTTP 401. |
| `auth-query-token-prompts-get` | `security-boundaries` | `prompts/get` | `authentication-scenario` | ⚠️ COMPATIBLE: Authentication appears to be enforced at the application layer instead of via an HTTP challenge. |
| `auth-valid-token-prompts-get` | `security-boundaries` | `prompts/get` | `authentication-scenario` | ✅ ALIGNED: Valid authentication was accepted and request processing proceeded. |
| `auth-no-auth-resources-list` | `security-boundaries` | `resources/list` | `authentication-scenario` | ⚠️ COMPATIBLE: Discovery metadata was exposed without authentication. |
| `auth-malformed-token-resources-list` | `security-boundaries` | `resources/list` | `authentication-scenario` | ✅ ALIGNED: Secure OAuth-style challenge returned with HTTP 400. |
| `auth-invalid-token-resources-list` | `security-boundaries` | `resources/list` | `authentication-scenario` | ⚠️ COMPATIBLE: Token was rejected, but invalid, expired, revoked, or wrong-audience tokens should receive HTTP 401. |
| `auth-token-expired-resources-list` | `security-boundaries` | `resources/list` | `authentication-scenario` | ⚠️ COMPATIBLE: Token was rejected, but invalid, expired, revoked, or wrong-audience tokens should receive HTTP 401. |
| `auth-invalid-scope-resources-list` | `security-boundaries` | `resources/list` | `authentication-scenario` | ✅ ALIGNED: Secure OAuth-style challenge returned with HTTP 400. |
| `auth-insufficient-permissions-resources-list` | `security-boundaries` | `resources/list` | `authentication-scenario` | ✅ ALIGNED: Secure OAuth-style challenge returned with HTTP 400. |
| `auth-revoked-token-resources-list` | `security-boundaries` | `resources/list` | `authentication-scenario` | ⚠️ COMPATIBLE: Token was rejected, but invalid, expired, revoked, or wrong-audience tokens should receive HTTP 401. |
| `auth-wrong-audience-rfc-8707-resources-list` | `security-boundaries` | `resources/list` | `authentication-scenario` | ⚠️ COMPATIBLE: Token was rejected, but invalid, expired, revoked, or wrong-audience tokens should receive HTTP 401. |
| `auth-valid-token-resources-list` | `security-boundaries` | `resources/list` | `authentication-scenario` | ✅ ALIGNED: Valid authentication was accepted and request processing proceeded. |
| `auth-no-auth-resources-read` | `security-boundaries` | `resources/read` | `authentication-scenario` | ⚠️ COMPATIBLE: Authentication appears to be enforced at the application layer instead of via an HTTP challenge. |
| `auth-malformed-token-resources-read` | `security-boundaries` | `resources/read` | `authentication-scenario` | ✅ ALIGNED: Secure OAuth-style challenge returned with HTTP 400. |
| `auth-invalid-token-resources-read` | `security-boundaries` | `resources/read` | `authentication-scenario` | ⚠️ COMPATIBLE: Token was rejected, but invalid, expired, revoked, or wrong-audience tokens should receive HTTP 401. |
| `auth-token-expired-resources-read` | `security-boundaries` | `resources/read` | `authentication-scenario` | ⚠️ COMPATIBLE: Token was rejected, but invalid, expired, revoked, or wrong-audience tokens should receive HTTP 401. |
| `auth-invalid-scope-resources-read` | `security-boundaries` | `resources/read` | `authentication-scenario` | ✅ ALIGNED: Secure OAuth-style challenge returned with HTTP 400. |
| `auth-insufficient-permissions-resources-read` | `security-boundaries` | `resources/read` | `authentication-scenario` | ✅ ALIGNED: Secure OAuth-style challenge returned with HTTP 400. |
| `auth-revoked-token-resources-read` | `security-boundaries` | `resources/read` | `authentication-scenario` | ⚠️ COMPATIBLE: Token was rejected, but invalid, expired, revoked, or wrong-audience tokens should receive HTTP 401. |
| `auth-wrong-audience-rfc-8707-resources-read` | `security-boundaries` | `resources/read` | `authentication-scenario` | ⚠️ COMPATIBLE: Token was rejected, but invalid, expired, revoked, or wrong-audience tokens should receive HTTP 401. |
| `auth-query-token-resources-read` | `security-boundaries` | `resources/read` | `authentication-scenario` | ⚠️ COMPATIBLE: Authentication appears to be enforced at the application layer instead of via an HTTP challenge. |
| `auth-valid-token-resources-read` | `security-boundaries` | `resources/read` | `authentication-scenario` | ✅ ALIGNED: Valid authentication was accepted and request processing proceeded. |
| `auth-no-auth-tools-list` | `security-boundaries` | `tools/list` | `authentication-scenario` | ⚠️ COMPATIBLE: Discovery metadata was exposed without authentication. |
| `auth-malformed-token-tools-list` | `security-boundaries` | `tools/list` | `authentication-scenario` | ✅ ALIGNED: Secure OAuth-style challenge returned with HTTP 400. |
| `auth-invalid-token-tools-list` | `security-boundaries` | `tools/list` | `authentication-scenario` | ⚠️ COMPATIBLE: Token was rejected, but invalid, expired, revoked, or wrong-audience tokens should receive HTTP 401. |
| `auth-token-expired-tools-list` | `security-boundaries` | `tools/list` | `authentication-scenario` | ⚠️ COMPATIBLE: Token was rejected, but invalid, expired, revoked, or wrong-audience tokens should receive HTTP 401. |
| `auth-invalid-scope-tools-list` | `security-boundaries` | `tools/list` | `authentication-scenario` | ✅ ALIGNED: Secure OAuth-style challenge returned with HTTP 400. |
| `auth-insufficient-permissions-tools-list` | `security-boundaries` | `tools/list` | `authentication-scenario` | ✅ ALIGNED: Secure OAuth-style challenge returned with HTTP 400. |
| `auth-revoked-token-tools-list` | `security-boundaries` | `tools/list` | `authentication-scenario` | ⚠️ COMPATIBLE: Token was rejected, but invalid, expired, revoked, or wrong-audience tokens should receive HTTP 401. |
| `auth-wrong-audience-rfc-8707-tools-list` | `security-boundaries` | `tools/list` | `authentication-scenario` | ⚠️ COMPATIBLE: Token was rejected, but invalid, expired, revoked, or wrong-audience tokens should receive HTTP 401. |
| `auth-valid-token-tools-list` | `security-boundaries` | `tools/list` | `authentication-scenario` | ✅ ALIGNED: Valid authentication was accepted and request processing proceeded. |
| `auth-no-auth-tools-call` | `security-boundaries` | `tools/call` | `authentication-scenario` | ⚠️ COMPATIBLE: Authentication appears to be enforced at the application layer instead of via an HTTP challenge. |
| `auth-malformed-token-tools-call` | `security-boundaries` | `tools/call` | `authentication-scenario` | ✅ ALIGNED: Secure OAuth-style challenge returned with HTTP 400. |
| `auth-invalid-token-tools-call` | `security-boundaries` | `tools/call` | `authentication-scenario` | ⚠️ COMPATIBLE: Token was rejected, but invalid, expired, revoked, or wrong-audience tokens should receive HTTP 401. |
| `auth-token-expired-tools-call` | `security-boundaries` | `tools/call` | `authentication-scenario` | ⚠️ COMPATIBLE: Token was rejected, but invalid, expired, revoked, or wrong-audience tokens should receive HTTP 401. |
| `auth-invalid-scope-tools-call` | `security-boundaries` | `tools/call` | `authentication-scenario` | ✅ ALIGNED: Secure OAuth-style challenge returned with HTTP 400. |
| `auth-insufficient-permissions-tools-call` | `security-boundaries` | `tools/call` | `authentication-scenario` | ✅ ALIGNED: Secure OAuth-style challenge returned with HTTP 400. |
| `auth-revoked-token-tools-call` | `security-boundaries` | `tools/call` | `authentication-scenario` | ⚠️ COMPATIBLE: Token was rejected, but invalid, expired, revoked, or wrong-audience tokens should receive HTTP 401. |
| `auth-wrong-audience-rfc-8707-tools-call` | `security-boundaries` | `tools/call` | `authentication-scenario` | ⚠️ COMPATIBLE: Token was rejected, but invalid, expired, revoked, or wrong-audience tokens should receive HTTP 401. |
| `auth-query-token-tools-call` | `security-boundaries` | `tools/call` | `authentication-scenario` | ⚠️ COMPATIBLE: Authentication appears to be enforced at the application layer instead of via an HTTP challenge. |
| `auth-valid-token-tools-call` | `security-boundaries` | `tools/call` | `authentication-scenario` | ✅ ALIGNED: Valid authentication was accepted and request processing proceeded. |
| `attack-inj-001-input-validation` | `security-boundaries` | `INJ-001 (Input Validation)` | `attack-simulation` | {"jsonrpc":"2.0","id":"3a9620f1-487c-45f8-8ec2-9e141f36f5aa","result":{"content":[{"type":"text","text":"failed to get latest review for current user: Could not resolve to a Repository with the name '/'."}],"isError":true}} |
| `attack-inj-002-input-validation` | `security-boundaries` | `INJ-002 (Input Validation)` | `attack-simulation` | {"jsonrpc":"2.0","id":"85666bdc-8c58-42e1-921c-5694b7cb2d07","result":{"content":[{"type":"text","text":"failed to get latest review for current user: Could not resolve to a Repository with the name '/'."}],"isError":true}} |
| `attack-inj-003-input-validation` | `security-boundaries` | `INJ-003 (Input Validation)` | `attack-simulation` | {"jsonrpc":"2.0","id":"c378c1df-5478-47f8-96d6-414de4650cd9","result":{"content":[{"type":"text","text":"failed to get latest review for current user: Could not resolve to a Repository with the name '/'."}],"isError":true}} |
| `attack-mcp-sec-001` | `security-boundaries` | `MCP-SEC-001` | `attack-simulation` | Server handled malformed requests gracefully with standard errors. |
| `attack-mcp-sec-002` | `security-boundaries` | `MCP-SEC-002` | `attack-simulation` | Skipped: Metadata enumeration requires an advertised concrete resource URI; no like-for-like resource surface was available. |
| `attack-mcp-sec-003` | `security-boundaries` | `MCP-SEC-003` | `attack-simulation` | Server correctly rejected invalid schema. |
| `attack-mcp-ai-001` | `security-boundaries` | `MCP-AI-001` | `attack-simulation` | Error clarity: 100/100 (Good). Server error messages help AI self-correct. |
| `error-invalid-method-call` | `error-handling` | `invalid-method` | `error-scenario` | HTTP 400; HTTP 400 Bad Request. Body: JSON RPC not handled: "nonexistent_method_12345" unsupported  |
| `error-malformed-json-payload` | `error-handling` | `malformed-json` | `error-scenario` | HTTP 400; malformed payload: unmarshaling jsonrpc message: json: expected '"' at the beginning of a string value: invalid json  |
| `error-graceful-degradation-on-invalid-request` | `error-handling` | `invalid-request` | `error-scenario` | HTTP 400; malformed payload: invalid message version tag ""; expected "2.0"  |
| `error-timeout-handling-recovery` | `error-handling` | `timeout-handling` | `error-scenario` | Validator cancelled the HTTP request after the induced timeout window elapsed. |
| `error-connection-interruption-recovery` | `error-handling` | `connection-interruption` | `error-scenario` | Validator aborted the HTTP request body mid-stream to simulate a connection interruption. |

## 10. Capability Snapshot

| Probe | Discovered | HTTP Status | Duration | Result |
| :--- | :---: | :---: | :---: | :--- |
| Tools/list | 41 | HTTP 200 | 351.9 ms | ✅ Listed<br/>Call ✅ |
| Resources/list | 0 | HTTP 200 | 109.7 ms | ✅ Listed |
| Prompts/list | 2 | HTTP 200 | 112.7 ms | ✅ Listed |

- **First Tool Probed:** `add_comment_to_pending_review`

## 11. Scoring Methodology

These notes explain how the overall score and blocking decision were calibrated for this run.

- GUIDANCE: 34 protected-endpoint authentication scenarios were secure but not fully aligned with the preferred MCP/OAuth challenge flow. Score reduced by 20%.
- SPEC: JSON-RPC 2.0 format deviations detected. Score reduced by 15%.
- Score is below the preferred target, but no blocking failure was observed in this run.

## 12. Compliance Matrix

| Category | Status | Score | Issues |
| :--- | :---: | :---: | :---: |
| Protocol Compliance | ✅ Passed | 60.4% | **7** |
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
| No Auth - initialize | `initialize` | 4xx (Secure Rejection) | Discovery metadata returned | 200 | ⚠️ COMPATIBLE: Discovery metadata was exposed without authentication. | ✅ |
| No Auth - prompts/list | `prompts/list` | 4xx (Secure Rejection) | Discovery metadata returned | 200 | ⚠️ COMPATIBLE: Discovery metadata was exposed without authentication. | ✅ |
| Malformed Token - prompts/list | `prompts/list` | 4xx (Secure Rejection) | Challenge returned | 400 | ✅ ALIGNED: Secure OAuth-style challenge returned with HTTP 400. | ✅ |
| Invalid Token - prompts/list | `prompts/list` | 401 + WWW-Authenticate challenge | Challenge returned with non-401 status | 400 | ⚠️ COMPATIBLE: Token was rejected, but invalid, expired, revoked, or wrong-audience tokens should receive HTTP 401. | ✅ |
| Token Expired - prompts/list | `prompts/list` | 401 + WWW-Authenticate challenge | Challenge returned with non-401 status | 400 | ⚠️ COMPATIBLE: Token was rejected, but invalid, expired, revoked, or wrong-audience tokens should receive HTTP 401. | ✅ |
| Invalid Scope - prompts/list | `prompts/list` | 4xx (Secure Rejection) | Challenge returned | 400 | ✅ ALIGNED: Secure OAuth-style challenge returned with HTTP 400. | ✅ |
| Insufficient Permissions - prompts/list | `prompts/list` | 4xx (Secure Rejection) | Challenge returned | 400 | ✅ ALIGNED: Secure OAuth-style challenge returned with HTTP 400. | ✅ |
| Revoked Token - prompts/list | `prompts/list` | 401 + WWW-Authenticate challenge | Challenge returned with non-401 status | 400 | ⚠️ COMPATIBLE: Token was rejected, but invalid, expired, revoked, or wrong-audience tokens should receive HTTP 401. | ✅ |
| Wrong Audience (RFC 8707) - prompts/list | `prompts/list` | 401 + audience-bound token rejection | Challenge returned with non-401 status | 400 | ⚠️ COMPATIBLE: Token was rejected, but invalid, expired, revoked, or wrong-audience tokens should receive HTTP 401. | ✅ |
| Valid Token - prompts/list | `prompts/list` | 200 + JSON-RPC Response | Authenticated request succeeded | 200 | ✅ ALIGNED: Valid authentication was accepted and request processing proceeded. | ✅ |
| No Auth - prompts/get | `prompts/get` | 4xx (Secure Rejection) | Request rejected in JSON-RPC layer | 200 | ⚠️ COMPATIBLE: Authentication appears to be enforced at the application layer instead of via an HTTP challenge. | ✅ |
| Malformed Token - prompts/get | `prompts/get` | 4xx (Secure Rejection) | Challenge returned | 400 | ✅ ALIGNED: Secure OAuth-style challenge returned with HTTP 400. | ✅ |
| Invalid Token - prompts/get | `prompts/get` | 401 + WWW-Authenticate challenge | Challenge returned with non-401 status | 400 | ⚠️ COMPATIBLE: Token was rejected, but invalid, expired, revoked, or wrong-audience tokens should receive HTTP 401. | ✅ |
| Token Expired - prompts/get | `prompts/get` | 401 + WWW-Authenticate challenge | Challenge returned with non-401 status | 400 | ⚠️ COMPATIBLE: Token was rejected, but invalid, expired, revoked, or wrong-audience tokens should receive HTTP 401. | ✅ |
| Invalid Scope - prompts/get | `prompts/get` | 4xx (Secure Rejection) | Challenge returned | 400 | ✅ ALIGNED: Secure OAuth-style challenge returned with HTTP 400. | ✅ |
| Insufficient Permissions - prompts/get | `prompts/get` | 4xx (Secure Rejection) | Challenge returned | 400 | ✅ ALIGNED: Secure OAuth-style challenge returned with HTTP 400. | ✅ |
| Revoked Token - prompts/get | `prompts/get` | 401 + WWW-Authenticate challenge | Challenge returned with non-401 status | 400 | ⚠️ COMPATIBLE: Token was rejected, but invalid, expired, revoked, or wrong-audience tokens should receive HTTP 401. | ✅ |
| Wrong Audience (RFC 8707) - prompts/get | `prompts/get` | 401 + audience-bound token rejection | Challenge returned with non-401 status | 400 | ⚠️ COMPATIBLE: Token was rejected, but invalid, expired, revoked, or wrong-audience tokens should receive HTTP 401. | ✅ |
| Query Token - prompts/get | `prompts/get` | 4xx or JSON-RPC rejection; query-string tokens must not grant access | Request rejected in JSON-RPC layer | 200 | ⚠️ COMPATIBLE: Authentication appears to be enforced at the application layer instead of via an HTTP challenge. | ✅ |
| Valid Token - prompts/get | `prompts/get` | 200 + JSON-RPC Response | Authenticated request returned JSON-RPC error | 200 | ✅ ALIGNED: Valid authentication was accepted and request processing proceeded. | ✅ |
| No Auth - resources/list | `resources/list` | 4xx (Secure Rejection) | Discovery metadata returned | 200 | ⚠️ COMPATIBLE: Discovery metadata was exposed without authentication. | ✅ |
| Malformed Token - resources/list | `resources/list` | 4xx (Secure Rejection) | Challenge returned | 400 | ✅ ALIGNED: Secure OAuth-style challenge returned with HTTP 400. | ✅ |
| Invalid Token - resources/list | `resources/list` | 401 + WWW-Authenticate challenge | Challenge returned with non-401 status | 400 | ⚠️ COMPATIBLE: Token was rejected, but invalid, expired, revoked, or wrong-audience tokens should receive HTTP 401. | ✅ |
| Token Expired - resources/list | `resources/list` | 401 + WWW-Authenticate challenge | Challenge returned with non-401 status | 400 | ⚠️ COMPATIBLE: Token was rejected, but invalid, expired, revoked, or wrong-audience tokens should receive HTTP 401. | ✅ |
| Invalid Scope - resources/list | `resources/list` | 4xx (Secure Rejection) | Challenge returned | 400 | ✅ ALIGNED: Secure OAuth-style challenge returned with HTTP 400. | ✅ |
| Insufficient Permissions - resources/list | `resources/list` | 4xx (Secure Rejection) | Challenge returned | 400 | ✅ ALIGNED: Secure OAuth-style challenge returned with HTTP 400. | ✅ |
| Revoked Token - resources/list | `resources/list` | 401 + WWW-Authenticate challenge | Challenge returned with non-401 status | 400 | ⚠️ COMPATIBLE: Token was rejected, but invalid, expired, revoked, or wrong-audience tokens should receive HTTP 401. | ✅ |
| Wrong Audience (RFC 8707) - resources/list | `resources/list` | 401 + audience-bound token rejection | Challenge returned with non-401 status | 400 | ⚠️ COMPATIBLE: Token was rejected, but invalid, expired, revoked, or wrong-audience tokens should receive HTTP 401. | ✅ |
| Valid Token - resources/list | `resources/list` | 200 + JSON-RPC Response | Authenticated request succeeded | 200 | ✅ ALIGNED: Valid authentication was accepted and request processing proceeded. | ✅ |
| No Auth - resources/read | `resources/read` | 4xx (Secure Rejection) | Request rejected in JSON-RPC layer | 200 | ⚠️ COMPATIBLE: Authentication appears to be enforced at the application layer instead of via an HTTP challenge. | ✅ |
| Malformed Token - resources/read | `resources/read` | 4xx (Secure Rejection) | Challenge returned | 400 | ✅ ALIGNED: Secure OAuth-style challenge returned with HTTP 400. | ✅ |
| Invalid Token - resources/read | `resources/read` | 401 + WWW-Authenticate challenge | Challenge returned with non-401 status | 400 | ⚠️ COMPATIBLE: Token was rejected, but invalid, expired, revoked, or wrong-audience tokens should receive HTTP 401. | ✅ |
| Token Expired - resources/read | `resources/read` | 401 + WWW-Authenticate challenge | Challenge returned with non-401 status | 400 | ⚠️ COMPATIBLE: Token was rejected, but invalid, expired, revoked, or wrong-audience tokens should receive HTTP 401. | ✅ |
| Invalid Scope - resources/read | `resources/read` | 4xx (Secure Rejection) | Challenge returned | 400 | ✅ ALIGNED: Secure OAuth-style challenge returned with HTTP 400. | ✅ |
| Insufficient Permissions - resources/read | `resources/read` | 4xx (Secure Rejection) | Challenge returned | 400 | ✅ ALIGNED: Secure OAuth-style challenge returned with HTTP 400. | ✅ |
| Revoked Token - resources/read | `resources/read` | 401 + WWW-Authenticate challenge | Challenge returned with non-401 status | 400 | ⚠️ COMPATIBLE: Token was rejected, but invalid, expired, revoked, or wrong-audience tokens should receive HTTP 401. | ✅ |
| Wrong Audience (RFC 8707) - resources/read | `resources/read` | 401 + audience-bound token rejection | Challenge returned with non-401 status | 400 | ⚠️ COMPATIBLE: Token was rejected, but invalid, expired, revoked, or wrong-audience tokens should receive HTTP 401. | ✅ |
| Query Token - resources/read | `resources/read` | 4xx or JSON-RPC rejection; query-string tokens must not grant access | Request rejected in JSON-RPC layer | 200 | ⚠️ COMPATIBLE: Authentication appears to be enforced at the application layer instead of via an HTTP challenge. | ✅ |
| Valid Token - resources/read | `resources/read` | 200 + JSON-RPC Response | Authenticated request returned JSON-RPC error | 200 | ✅ ALIGNED: Valid authentication was accepted and request processing proceeded. | ✅ |
| No Auth - tools/list | `tools/list` | 4xx (Secure Rejection) | Discovery metadata returned | 200 | ⚠️ COMPATIBLE: Discovery metadata was exposed without authentication. | ✅ |
| Malformed Token - tools/list | `tools/list` | 4xx (Secure Rejection) | Challenge returned | 400 | ✅ ALIGNED: Secure OAuth-style challenge returned with HTTP 400. | ✅ |
| Invalid Token - tools/list | `tools/list` | 401 + WWW-Authenticate challenge | Challenge returned with non-401 status | 400 | ⚠️ COMPATIBLE: Token was rejected, but invalid, expired, revoked, or wrong-audience tokens should receive HTTP 401. | ✅ |
| Token Expired - tools/list | `tools/list` | 401 + WWW-Authenticate challenge | Challenge returned with non-401 status | 400 | ⚠️ COMPATIBLE: Token was rejected, but invalid, expired, revoked, or wrong-audience tokens should receive HTTP 401. | ✅ |
| Invalid Scope - tools/list | `tools/list` | 4xx (Secure Rejection) | Challenge returned | 400 | ✅ ALIGNED: Secure OAuth-style challenge returned with HTTP 400. | ✅ |
| Insufficient Permissions - tools/list | `tools/list` | 4xx (Secure Rejection) | Challenge returned | 400 | ✅ ALIGNED: Secure OAuth-style challenge returned with HTTP 400. | ✅ |
| Revoked Token - tools/list | `tools/list` | 401 + WWW-Authenticate challenge | Challenge returned with non-401 status | 400 | ⚠️ COMPATIBLE: Token was rejected, but invalid, expired, revoked, or wrong-audience tokens should receive HTTP 401. | ✅ |
| Wrong Audience (RFC 8707) - tools/list | `tools/list` | 401 + audience-bound token rejection | Challenge returned with non-401 status | 400 | ⚠️ COMPATIBLE: Token was rejected, but invalid, expired, revoked, or wrong-audience tokens should receive HTTP 401. | ✅ |
| Valid Token - tools/list | `tools/list` | 200 + JSON-RPC Response | Authenticated request succeeded | 200 | ✅ ALIGNED: Valid authentication was accepted and request processing proceeded. | ✅ |
| No Auth - tools/call | `tools/call` | 4xx (Secure Rejection) | Request rejected in JSON-RPC layer | 200 | ⚠️ COMPATIBLE: Authentication appears to be enforced at the application layer instead of via an HTTP challenge. | ✅ |
| Malformed Token - tools/call | `tools/call` | 4xx (Secure Rejection) | Challenge returned | 400 | ✅ ALIGNED: Secure OAuth-style challenge returned with HTTP 400. | ✅ |
| Invalid Token - tools/call | `tools/call` | 401 + WWW-Authenticate challenge | Challenge returned with non-401 status | 400 | ⚠️ COMPATIBLE: Token was rejected, but invalid, expired, revoked, or wrong-audience tokens should receive HTTP 401. | ✅ |
| Token Expired - tools/call | `tools/call` | 401 + WWW-Authenticate challenge | Challenge returned with non-401 status | 400 | ⚠️ COMPATIBLE: Token was rejected, but invalid, expired, revoked, or wrong-audience tokens should receive HTTP 401. | ✅ |
| Invalid Scope - tools/call | `tools/call` | 4xx (Secure Rejection) | Challenge returned | 400 | ✅ ALIGNED: Secure OAuth-style challenge returned with HTTP 400. | ✅ |
| Insufficient Permissions - tools/call | `tools/call` | 4xx (Secure Rejection) | Challenge returned | 400 | ✅ ALIGNED: Secure OAuth-style challenge returned with HTTP 400. | ✅ |
| Revoked Token - tools/call | `tools/call` | 401 + WWW-Authenticate challenge | Challenge returned with non-401 status | 400 | ⚠️ COMPATIBLE: Token was rejected, but invalid, expired, revoked, or wrong-audience tokens should receive HTTP 401. | ✅ |
| Wrong Audience (RFC 8707) - tools/call | `tools/call` | 401 + audience-bound token rejection | Challenge returned with non-401 status | 400 | ⚠️ COMPATIBLE: Token was rejected, but invalid, expired, revoked, or wrong-audience tokens should receive HTTP 401. | ✅ |
| Query Token - tools/call | `tools/call` | 4xx or JSON-RPC rejection; query-string tokens must not grant access | Request rejected in JSON-RPC layer | 200 | ⚠️ COMPATIBLE: Authentication appears to be enforced at the application layer instead of via an HTTP challenge. | ✅ |
| Valid Token - tools/call | `tools/call` | 200 + JSON-RPC Response | Authenticated request returned JSON-RPC error | 200 | ✅ ALIGNED: Valid authentication was accepted and request processing proceeded. | ✅ |

### Adversarial Input Handling

> **Legend:**
> * **REFLECTED / UNSAFE ECHO**: The server reflected the payload back without sanitization. No execution confirmed.
> * **BLOCKED**: The server correctly rejected or sanitized the input.
> * **SKIPPED**: Test could not be executed (e.g., no tools discovered for injection target).

| Attack Vector | Description | Result | Probe | Analysis |
| :--- | :--- | :---: | :--- | :--- |
| INJ-001 (Input Validation) | Simulated INJ-001 (Input Validation) attack on add_comment_to_pending_review | 🛡️ BLOCKED | `tools/list`/http: Success HTTP 200; auth Applied (+1) | `{"jsonrpc":"2.0","id":"3a9620f1-487c-45f8-8ec2-9e141f36f5aa","result":{"content":[{"type":"text","text":"failed to get latest review for current user: Could not resolve to a Repository with the name '/'."}],"isError":true}}` |
| INJ-002 (Input Validation) | Simulated INJ-002 (Input Validation) attack on add_comment_to_pending_review | 🛡️ BLOCKED | `tools/list`/http: Success HTTP 200; auth Applied (+1) | `{"jsonrpc":"2.0","id":"85666bdc-8c58-42e1-921c-5694b7cb2d07","result":{"content":[{"type":"text","text":"failed to get latest review for current user: Could not resolve to a Repository with the name '/'."}],"isError":true}}` |
| INJ-003 (Input Validation) | Simulated INJ-003 (Input Validation) attack on add_comment_to_pending_review | 🛡️ BLOCKED | `tools/list`/http: Success HTTP 200; auth Applied (+1) | `{"jsonrpc":"2.0","id":"c378c1df-5478-47f8-96d6-414de4650cd9","result":{"content":[{"type":"text","text":"failed to get latest review for current user: Could not resolve to a Repository with the name '/'."}],"isError":true}}` |
| MCP-SEC-001 | JSON-RPC Error Smuggling | 🛡️ BLOCKED | `rpc.system.invalid`/http: ProtocolError HTTP 400; auth Applied | `Server handled malformed requests gracefully with standard errors.` |
| MCP-SEC-002 | Metadata Enumeration | ⏭️ SKIPPED | `resources/list`/http: Success HTTP 200; auth Applied | `Skipped: Metadata enumeration requires an advertised concrete resource URI; no like-for-like resource surface was available.` |
| MCP-SEC-003 | Schema Confusion | 🛡️ BLOCKED | `tools/list`/http: Success HTTP 200; auth Applied (+1) | `Server correctly rejected invalid schema.` |
| MCP-AI-001 | Hallucination Fuzzing | 🛡️ BLOCKED | `tools/list`/http: Success HTTP 200; auth Applied (+1) | `Error clarity: 100/100 (Good). Server error messages help AI self-correct.` |

## 15. Protocol Compliance

**Status Detail:** declared capabilities: completions, prompts, resources, tools | roots/list: not advertised | logging/setLevel: not advertised | sampling/createMessage: not advertised | completion/complete: supported

### Compliance Violations
| ID | Source | Description | Severity | Recommendation |
| :--- | :--- | :--- | :---: | :--- |
| MCP-PROTO-ERR | `spec` | JSON-RPC Error Code Violation: Parse Error | Low | This specific error code test failed. Ensure the server returns standard JSON-RPC error codes for known error conditions: -32700 (parse error), -32600 (invalid request), -32601 (method not found), -32602 (invalid params), -32603 (internal error). See https://spec.modelcontextprotocol.io/specification/2025-11-25/basic/json-rpc#errors |
| MCP-PROTO-ERR | `spec` | JSON-RPC Error Code Violation: Invalid Request - Missing jsonrpc | Low | This specific error code test failed. Ensure the server returns standard JSON-RPC error codes for known error conditions: -32700 (parse error), -32600 (invalid request), -32601 (method not found), -32602 (invalid params), -32603 (internal error). See https://spec.modelcontextprotocol.io/specification/2025-11-25/basic/json-rpc#errors |
| MCP-PROTO-ERR | `spec` | JSON-RPC Error Code Violation: Method Not Found | Low | This specific error code test failed. Ensure the server returns standard JSON-RPC error codes for known error conditions: -32700 (parse error), -32600 (invalid request), -32601 (method not found), -32602 (invalid params), -32603 (internal error). See https://spec.modelcontextprotocol.io/specification/2025-11-25/basic/json-rpc#errors |
| MCP-PROTO-ERR | `spec` | JSON-RPC Error Code Violation: Invalid Params | Low | This specific error code test failed. Ensure the server returns standard JSON-RPC error codes for known error conditions: -32700 (parse error), -32600 (invalid request), -32601 (method not found), -32602 (invalid params), -32603 (internal error). See https://spec.modelcontextprotocol.io/specification/2025-11-25/basic/json-rpc#errors |
| MCP-PROTO-JSONRPC | `spec` | Request format does not comply with JSON-RPC 2.0 specification | High | Ensure every JSON-RPC request includes "jsonrpc": "2.0", a string "method" field, and a string or integer "id". The "params" field, if present, must be an object or array. See https://spec.modelcontextprotocol.io/specification/2025-11-25/basic/json-rpc |
| MCP-PROTO-ERR | `spec` | Error codes do not comply with JSON-RPC 2.0 standard error codes | Low | Review all error responses to ensure they use standard JSON-RPC 2.0 error codes. Custom error codes must be outside the reserved range (-32000 to -32099 for server errors). See https://spec.modelcontextprotocol.io/specification/2025-11-25/basic/json-rpc#errors |
| MCP-HTTP-005 | `spec` | Streamable HTTP transport violation (invalid-origin-403): HTTP transports must validate Origin and reject invalid origins with HTTP 403 Forbidden. Expected: HTTP 403 Forbidden. Observed: HTTP 200; Content-Type=text/event-stream; body length 84 | High | Validate the Origin header for HTTP transports and reject invalid origins with HTTP 403 Forbidden before processing the JSON-RPC message. See https://spec.modelcontextprotocol.io/specification/2025-11-25/basic/transports#origin-header-validation |

#### Violation Context

| ID | Context |
| :--- | :--- |
| `MCP-HTTP-005` | **Probe ID:** `invalid-origin-403`; **HTTP status:** `200`; **Content-Type:** `text/event-stream`; **Expected:** `HTTP 403 Forbidden.`; **Actual:** `HTTP 200; Content-Type=text/event-stream; body length 84` |

## 16. Tool Validation

**Tools Discovered:** 41

### Tool Metadata Completeness

Annotation coverage across the discovered tool catalog. Missing annotations reduce AI agent safety and UX quality.

| Tool | title | description | readOnlyHint | destructiveHint | openWorldHint | idempotentHint |
| :--- | :---: | :---: | :---: | :---: | :---: | :---: |
| `tools/list (Schema Compliance)` | ❌ | ❌ | ❌ | ❌ | ❌ | ❌ |
| `add_comment_to_pending_review` | ✅ | ✅ | ❌ | ❌ | ❌ | ❌ |
| `add_issue_comment` | ✅ | ✅ | ❌ | ❌ | ❌ | ❌ |
| `add_reply_to_pull_request_comment` | ✅ | ✅ | ❌ | ❌ | ❌ | ❌ |
| `create_branch` | ✅ | ✅ | ❌ | ❌ | ❌ | ❌ |
| `create_or_update_file` | ✅ | ✅ | ❌ | ❌ | ❌ | ❌ |
| `create_pull_request` | ✅ | ✅ | ❌ | ❌ | ❌ | ❌ |
| `create_repository` | ✅ | ✅ | ❌ | ❌ | ❌ | ❌ |
| `delete_file` | ✅ | ✅ | ❌ | ✅ | ❌ | ❌ |
| `fork_repository` | ✅ | ✅ | ❌ | ❌ | ❌ | ❌ |
| `get_commit` | ✅ | ✅ | ✅ | ❌ | ❌ | ❌ |
| `get_file_contents` | ✅ | ✅ | ✅ | ❌ | ❌ | ❌ |
| `get_label` | ✅ | ✅ | ✅ | ❌ | ❌ | ❌ |
| `get_latest_release` | ✅ | ✅ | ✅ | ❌ | ❌ | ❌ |
| `get_me` | ✅ | ✅ | ✅ | ❌ | ❌ | ❌ |
| `get_release_by_tag` | ✅ | ✅ | ✅ | ❌ | ❌ | ❌ |
| `get_tag` | ✅ | ✅ | ✅ | ❌ | ❌ | ❌ |
| `get_team_members` | ✅ | ✅ | ✅ | ❌ | ❌ | ❌ |
| `get_teams` | ✅ | ✅ | ✅ | ❌ | ❌ | ❌ |
| `issue_read` | ✅ | ✅ | ✅ | ❌ | ❌ | ❌ |
| `issue_write` | ✅ | ✅ | ❌ | ❌ | ❌ | ❌ |
| `list_branches` | ✅ | ✅ | ✅ | ❌ | ❌ | ❌ |
| `list_commits` | ✅ | ✅ | ✅ | ❌ | ❌ | ❌ |
| `list_issue_types` | ✅ | ✅ | ✅ | ❌ | ❌ | ❌ |
| `list_issues` | ✅ | ✅ | ✅ | ❌ | ❌ | ❌ |
| `list_pull_requests` | ✅ | ✅ | ✅ | ❌ | ❌ | ❌ |
| `list_releases` | ✅ | ✅ | ✅ | ❌ | ❌ | ❌ |
| `list_tags` | ✅ | ✅ | ✅ | ❌ | ❌ | ❌ |
| `merge_pull_request` | ✅ | ✅ | ❌ | ❌ | ❌ | ❌ |
| `pull_request_read` | ✅ | ✅ | ✅ | ❌ | ❌ | ❌ |
| `pull_request_review_write` | ✅ | ✅ | ❌ | ❌ | ❌ | ❌ |
| `push_files` | ✅ | ✅ | ❌ | ❌ | ❌ | ❌ |
| `request_copilot_review` | ✅ | ✅ | ❌ | ❌ | ❌ | ❌ |
| `run_secret_scanning` | ✅ | ✅ | ✅ | ❌ | ✅ | ❌ |
| `search_code` | ✅ | ✅ | ✅ | ❌ | ❌ | ❌ |
| `search_issues` | ✅ | ✅ | ✅ | ❌ | ❌ | ❌ |
| `search_pull_requests` | ✅ | ✅ | ✅ | ❌ | ❌ | ❌ |
| `search_repositories` | ✅ | ✅ | ✅ | ❌ | ❌ | ❌ |
| `search_users` | ✅ | ✅ | ✅ | ❌ | ❌ | ❌ |
| `sub_issue_write` | ✅ | ✅ | ❌ | ❌ | ❌ | ❌ |
| `update_pull_request` | ✅ | ✅ | ❌ | ❌ | ❌ | ❌ |
| `update_pull_request_branch` | ✅ | ✅ | ❌ | ❌ | ❌ | ❌ |
| **Coverage** | **41/42** | **41/42** | **24/42** | **1/42** | **1/42** | **0/42** |

### AI Safety Control Evidence

This table separates server-declared tool metadata from host-side controls that the validator cannot directly observe.

| Control | Declared | Missing | Not Observable | Not Applicable | Representative Note |
| :--- | ---: | ---: | ---: | ---: | :--- |
| User confirmation | 1 | 7 | 0 | 33 | Tool metadata does not declare human confirmation or consent guidance for a potentially destructive action. |
| Audit trail | 0 | 0 | 41 | 0 | Audit trail behavior is not observable from tool metadata. |
| Data-sharing disclosure | 1 | 40 | 0 | 0 | Tool metadata does not declare open-world/data-sharing behavior. |
| Destructive-action confirmation | 1 | 7 | 0 | 33 | Destructive action confirmation guidance is missing from tool metadata. |
| Host/server responsibility split | 0 | 0 | 41 | 0 | Host-side consent, denial, and disclosure UX cannot be proven from server tool metadata alone. |

### Tool: `tools/list (Schema Compliance)`

**Status:** ✅ Passed
**Execution Time:** 351.91ms
---

### Tool: `add_comment_to_pending_review`

**Status:** ✅ Passed
**Execution Time:** 888.40ms

#### Tool Metadata
| Property | Value |
| :--- | :--- |
| Display Title | Add review comment to the requester's latest pending pull request review |

---

### Tool: `add_issue_comment`

**Status:** ✅ Passed
**Execution Time:** 734.75ms

#### Tool Metadata
| Property | Value |
| :--- | :--- |
| Display Title | Add comment to issue |

---

### Tool: `add_reply_to_pull_request_comment`

**Status:** ✅ Passed
**Execution Time:** 530.93ms

#### Tool Metadata
| Property | Value |
| :--- | :--- |
| Display Title | Add reply to pull request comment |

---

### Tool: `create_branch`

**Status:** ✅ Passed
**Execution Time:** 553.31ms

#### Tool Metadata
| Property | Value |
| :--- | :--- |
| Display Title | Create branch |

---

### Tool: `create_or_update_file`

**Status:** ✅ Passed
**Execution Time:** 582.00ms

#### Tool Metadata
| Property | Value |
| :--- | :--- |
| Display Title | Create or update file |

---

### Tool: `create_pull_request`

**Status:** ✅ Passed
**Execution Time:** 431.33ms

#### Tool Metadata
| Property | Value |
| :--- | :--- |
| Display Title | Open new pull request |

---

### Tool: `create_repository`

**Status:** ✅ Passed
**Execution Time:** 318.81ms

#### Tool Metadata
| Property | Value |
| :--- | :--- |
| Display Title | Create repository |

---

### Tool: `delete_file`

**Status:** ✅ Passed
**Execution Time:** 529.82ms

#### Tool Metadata
| Property | Value |
| :--- | :--- |
| Display Title | Delete file |
| destructiveHint | True |

---

### Tool: `fork_repository`

**Status:** ✅ Passed
**Execution Time:** 500.37ms

#### Tool Metadata
| Property | Value |
| :--- | :--- |
| Display Title | Fork repository |

---

### Tool: `get_commit`

**Status:** ✅ Passed
**Execution Time:** 457.06ms

#### Tool Metadata
| Property | Value |
| :--- | :--- |
| Display Title | Get commit details |
| readOnlyHint | True |

---

### Tool: `get_file_contents`

**Status:** ✅ Passed
**Execution Time:** 494.13ms

#### Tool Metadata
| Property | Value |
| :--- | :--- |
| Display Title | Get file or directory contents |
| readOnlyHint | True |

---

### Tool: `get_label`

**Status:** ✅ Passed
**Execution Time:** 436.24ms

#### Tool Metadata
| Property | Value |
| :--- | :--- |
| Display Title | Get a specific label from a repository. |
| readOnlyHint | True |

---

### Tool: `get_latest_release`

**Status:** ✅ Passed
**Execution Time:** 413.00ms

#### Tool Metadata
| Property | Value |
| :--- | :--- |
| Display Title | Get latest release |
| readOnlyHint | True |

---

### Tool: `get_me`

**Status:** ✅ Passed
**Execution Time:** 355.73ms

#### Tool Metadata
| Property | Value |
| :--- | :--- |
| Display Title | Get my user profile |
| readOnlyHint | True |

---

### Tool: `get_release_by_tag`

**Status:** ✅ Passed
**Execution Time:** 449.50ms

#### Tool Metadata
| Property | Value |
| :--- | :--- |
| Display Title | Get a release by tag name |
| readOnlyHint | True |

---

### Tool: `get_tag`

**Status:** ✅ Passed
**Execution Time:** 377.12ms

#### Tool Metadata
| Property | Value |
| :--- | :--- |
| Display Title | Get tag details |
| readOnlyHint | True |

---

### Tool: `get_team_members`

**Status:** ✅ Passed
**Execution Time:** 320.30ms

#### Tool Metadata
| Property | Value |
| :--- | :--- |
| Display Title | Get team members |
| readOnlyHint | True |

---

### Tool: `get_teams`

**Status:** ✅ Passed
**Execution Time:** 377.40ms

#### Tool Metadata
| Property | Value |
| :--- | :--- |
| Display Title | Get teams |
| readOnlyHint | True |

---

### Tool: `issue_read`

**Status:** ✅ Passed
**Execution Time:** 310.68ms

#### Tool Metadata
| Property | Value |
| :--- | :--- |
| Display Title | Get issue details |
| readOnlyHint | True |

---

### Tool: `issue_write`

**Status:** ✅ Passed
**Execution Time:** 377.67ms

#### Tool Metadata
| Property | Value |
| :--- | :--- |
| Display Title | Create or update issue. |

---

### Tool: `list_branches`

**Status:** ✅ Passed
**Execution Time:** 466.30ms

#### Tool Metadata
| Property | Value |
| :--- | :--- |
| Display Title | List branches |
| readOnlyHint | True |

---

### Tool: `list_commits`

**Status:** ✅ Passed
**Execution Time:** 360.30ms

#### Tool Metadata
| Property | Value |
| :--- | :--- |
| Display Title | List commits |
| readOnlyHint | True |

---

### Tool: `list_issue_types`

**Status:** ✅ Passed
**Execution Time:** 282.27ms

#### Tool Metadata
| Property | Value |
| :--- | :--- |
| Display Title | List available issue types |
| readOnlyHint | True |

---

### Tool: `list_issues`

**Status:** ✅ Passed
**Execution Time:** 284.81ms

#### Tool Metadata
| Property | Value |
| :--- | :--- |
| Display Title | List issues |
| readOnlyHint | True |

---

### Tool: `list_pull_requests`

**Status:** ✅ Passed
**Execution Time:** 430.90ms

#### Tool Metadata
| Property | Value |
| :--- | :--- |
| Display Title | List pull requests |
| readOnlyHint | True |

---

### Tool: `list_releases`

**Status:** ✅ Passed
**Execution Time:** 360.36ms

#### Tool Metadata
| Property | Value |
| :--- | :--- |
| Display Title | List releases |
| readOnlyHint | True |

---

### Tool: `list_tags`

**Status:** ✅ Passed
**Execution Time:** 409.81ms

#### Tool Metadata
| Property | Value |
| :--- | :--- |
| Display Title | List tags |
| readOnlyHint | True |

---

### Tool: `merge_pull_request`

**Status:** ✅ Passed
**Execution Time:** 351.90ms

#### Tool Metadata
| Property | Value |
| :--- | :--- |
| Display Title | Merge pull request |

---

### Tool: `pull_request_read`

**Status:** ✅ Passed
**Execution Time:** 433.65ms

#### Tool Metadata
| Property | Value |
| :--- | :--- |
| Display Title | Get details for a single pull request |
| readOnlyHint | True |

---

### Tool: `pull_request_review_write`

**Status:** ✅ Passed
**Execution Time:** 388.47ms

#### Tool Metadata
| Property | Value |
| :--- | :--- |
| Display Title | Write operations (create, submit, delete) on pull request reviews. |

---

### Tool: `push_files`

**Status:** ✅ Passed
**Execution Time:** 540.43ms

#### Tool Metadata
| Property | Value |
| :--- | :--- |
| Display Title | Push files to repository |

---

### Tool: `request_copilot_review`

**Status:** ✅ Passed
**Execution Time:** 472.51ms

#### Tool Metadata
| Property | Value |
| :--- | :--- |
| Display Title | Request Copilot review |

---

### Tool: `run_secret_scanning`

**Status:** ✅ Passed
**Execution Time:** 217.57ms

#### Tool Metadata
| Property | Value |
| :--- | :--- |
| Display Title | Run Secret Scanning |
| readOnlyHint | True |
| openWorldHint | False |

---

### Tool: `search_code`

**Status:** ✅ Passed
**Execution Time:** 525.46ms

#### Tool Metadata
| Property | Value |
| :--- | :--- |
| Display Title | Search code |
| readOnlyHint | True |

---

### Tool: `search_issues`

**Status:** ✅ Passed
**Execution Time:** 482.79ms

#### Tool Metadata
| Property | Value |
| :--- | :--- |
| Display Title | Search issues |
| readOnlyHint | True |

---

### Tool: `search_pull_requests`

**Status:** ✅ Passed
**Execution Time:** 494.34ms

#### Tool Metadata
| Property | Value |
| :--- | :--- |
| Display Title | Search pull requests |
| readOnlyHint | True |

---

### Tool: `search_repositories`

**Status:** ✅ Passed
**Execution Time:** 571.26ms

#### Tool Metadata
| Property | Value |
| :--- | :--- |
| Display Title | Search repositories |
| readOnlyHint | True |

---

### Tool: `search_users`

**Status:** ✅ Passed
**Execution Time:** 555.99ms

#### Tool Metadata
| Property | Value |
| :--- | :--- |
| Display Title | Search users |
| readOnlyHint | True |

---

### Tool: `sub_issue_write`

**Status:** ✅ Passed
**Execution Time:** 337.41ms

#### Tool Metadata
| Property | Value |
| :--- | :--- |
| Display Title | Change sub-issue |

---

### Tool: `update_pull_request`

**Status:** ✅ Passed
**Execution Time:** 446.36ms

#### Tool Metadata
| Property | Value |
| :--- | :--- |
| Display Title | Edit pull request |

---

### Tool: `update_pull_request_branch`

**Status:** ✅ Passed
**Execution Time:** 434.17ms

#### Tool Metadata
| Property | Value |
| :--- | :--- |
| Display Title | Update pull request branch |

---

### Tool Catalog Advisory Breakdown

Remaining tool-catalog debt is grouped by authority so MCP specification failures are not conflated with guidance or deterministic AI-oriented heuristics.

| Authority | Active Rules | Coverage | Highest Severity | Representative Gaps |
| :--- | :---: | :--- | :---: | :--- |
| Spec | 0 | 0/41 (0%) | - | No current catalog-wide tool advisories. |
| Guideline | 5 | 42/41 (100%) | 🔵 Low | Tool 'add_comment_to_pending_review' does not declare annotations.idempotentHint.<br />Tool 'add_comment_to_pending_review' does not declare annotations.destructiveHint. |
| Heuristic | 6 | 13/41 (32%) | 🟠 High | 🔴 LLM-Friendliness: 10/100 (Anti-LLM) — Error will cause AI hallucination/loops<br />Tool 'delete_file' is marked destructive but its description does not mention confirmation, approval, or warning guidance. |
| Operational | 0 | 0/41 (0%) | - | No current catalog-wide tool advisories. |

### MCP Guideline Findings

These findings capture MCP guidance signals that improve agent safety and UX but are not strict protocol MUST failures.
Coverage shows how prevalent each issue is across the discovered tool catalog so larger servers are judged by rate, not raw count alone.

| Rule ID | Source | Coverage | Severity | Example Components | Finding |
| :--- | :--- | :--- | :---: | :--- | :--- |
| `AI.TOOL.SAFETY.CONFIRMATION_GUIDANCE_MISSING` | `heuristic` | 1/41 (2%) | 🟡 Medium | `delete_file` | Tool 'delete_file' is marked destructive but its description does not mention confirmation, approval, or warning guidance. |
| `MCP.GUIDELINE.TOOL.IDEMPOTENT_HINT_MISSING` | `guideline` | 41/41 (100%) | 🔵 Low | `add_comment_to_pending_review`, `add_issue_comment`, `add_reply_to_pull_request_comment` | Tool 'add_comment_to_pending_review' does not declare annotations.idempotentHint. |
| `MCP.GUIDELINE.TOOL.DESTRUCTIVE_HINT_MISSING` | `guideline` | 40/41 (98%) | 🔵 Low | `add_comment_to_pending_review`, `add_issue_comment`, `add_reply_to_pull_request_comment` | Tool 'add_comment_to_pending_review' does not declare annotations.destructiveHint. |
| `MCP.GUIDELINE.TOOL.OPENWORLD_HINT_MISSING` | `guideline` | 40/41 (98%) | 🔵 Low | `add_comment_to_pending_review`, `add_issue_comment`, `add_reply_to_pull_request_comment` | Tool 'add_comment_to_pending_review' does not declare annotations.openWorldHint. |
| `MCP.GUIDELINE.TOOL.READONLY_HINT_MISSING` | `guideline` | 17/41 (41%) | 🔵 Low | `add_comment_to_pending_review`, `add_issue_comment`, `add_reply_to_pull_request_comment` | Tool 'add_comment_to_pending_review' does not declare annotations.readOnlyHint. |

## 17. Resource Capabilities

**Resources Discovered:** 0

**Catalog Applicability:** Resources capability was not advertised during initialize; resources/list and resources/read probes were skipped; no resource reads were required.

**Surface Notes:**
- resources.subscribe was not advertised during initialize; resource subscription probes were skipped.
- ✅ Resource templates: 5 templates discovered
- ✅ COMPLIANT: 0 resources discovered and validated

## 18. Prompt Capabilities

**Prompts Discovered:** 2

| Prompt Name | Status | Issues |
| :--- | :---: | :--- |
| AssignCodingAgent | ✅ Passed | ✅ prompts/get: messages[] array present (6 messages) |
| issue_to_fix_workflow | ✅ Passed | ✅ prompts/get: messages[] array present (5 messages) |

## 19. AI Readiness Assessment

**AI Readiness Score:** ✅ **99/100** (Good)

**Evidence basis:** Deterministic schema and payload heuristics. Measured model-evaluation results, when enabled, are written as a separate companion artifact and are not blended into this score.

| Criterion | What It Measures |
| :--- | :--- |
| **Description Completeness** | Do all tool parameters have descriptions? Missing descriptions increase hallucination risk. |
| **Type Specificity** | Are `string` parameters constrained with `enum`, `pattern`, or `format`? Vague types cause incorrect arguments. |
| **Token Efficiency** | Is the `tools/list` payload within context window limits? Estimated: ~25,152 tokens. |

### Findings

| Rule ID | Evidence Basis | Source | Coverage | Severity | Finding |
| :--- | :--- | :--- | :--- | :---: | :--- |
| `AI.TOOL.SCHEMA.REQUIRED_ARRAY_SHAPE_MISSING` | Deterministic schema heuristic | `heuristic` | 1/41 (2%) | 🟡 Medium | Tool 'push_files': 1/5 required array parameters lack item schemas or minItems guidance |
| `AI.TOOL.SCHEMA.FORMAT_HINT_MISSING` | Deterministic schema heuristic | `heuristic` | 5/41 (12%) | 🔵 Low | Tool 'create_or_update_file': 2/7 string parameters look like structured values but do not declare format/pattern hints |
| `AI.TOOL.SCHEMA.ENUM_COVERAGE_MISSING` | Deterministic schema heuristic | `heuristic` | 4/41 (10%) | 🔵 Low | Tool 'issue_write': 1/13 string parameters look like fixed-choice fields but do not declare enum/const choices |
| `AI.TOOL.SCHEMA.TOKEN_BUDGET_WARNING` | Deterministic payload heuristic | `heuristic` | 1/41 (2%) | 🔵 Low | ℹ️ tools/list response is ~25,152 tokens — consider reducing descriptions for token efficiency. |

## 20. Optional MCP Capabilities

These probes check whether the server supports optional MCP features beyond the core primitives.

| Capability | Status |
| :--- | :--- |
| `declared capabilities` | ➖ completions, prompts, resources, tools |
| `roots/list` | ➖ not advertised |
| `logging/setLevel` | ➖ not advertised |
| `sampling/createMessage` | ➖ not advertised |
| `completion/complete` | ✅ supported |

## 21. Performance Metrics

| Metric | Result | Verdict |
| :--- | :--- | :--- |
| **Avg Latency** | 135.98ms | ✅ Good |
| **P95 Latency** | 159.01ms | - |
| **Throughput** | 7.33 req/sec | - |
| **Error Rate** | 0.00% | ✅ Clean |
| **Requests** | 20/20 successful | - |

## 22. Recommendations

- 💡 Spec: Ensure every JSON-RPC request includes "jsonrpc": "2.0", a string "method" field, and a string or integer "id". The "params" field, if present, must be an object or array. See https://spec.modelcontextprotocol.io/specification/2025-11-25/basic/json-rpc
- 💡 Spec: Validate the Origin header for HTTP transports and reject invalid origins with HTTP 403 Forbidden before processing the JSON-RPC message. See https://spec.modelcontextprotocol.io/specification/2025-11-25/basic/transports#origin-header-validation
- 💡 Spec: This specific error code test failed. Ensure the server returns standard JSON-RPC error codes for known error conditions: -32700 (parse error), -32600 (invalid request), -32601 (method not found), -32602 (invalid params), -32603 (internal error). See https://spec.modelcontextprotocol.io/specification/2025-11-25/basic/json-rpc#errors
- 💡 Spec: Review all error responses to ensure they use standard JSON-RPC 2.0 error codes. Custom error codes must be outside the reserved range (-32000 to -32099 for server errors). See https://spec.modelcontextprotocol.io/specification/2025-11-25/basic/json-rpc#errors
- 💡 Heuristic: Return specific, structured errors that identify the invalid argument and expected shape Gap affects 3/41 tools
- 💡 Heuristic: Add explicit confirmation, approval, or warning language so agents know human review is expected before destructive execution Gap affects 1/41 tool

---
*Produced with [MCP Validator](https://github.com/navalerakesh/mcp-validation-security) · [McpVal on NuGet](https://www.nuget.org/packages/McpVal#versions-body-tab)*
