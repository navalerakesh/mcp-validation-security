# MCP Server Compliance & Validation Report
**Generated:** 2026-05-01 16:23:32 UTC

## 1. Executive Summary

| Metric | Value |
| :--- | :--- |
| **Server Endpoint** | `node mcpval-mcp/dist/index.js` |
| **Validation ID** | `3b708ba5-58c8-4ea0-a2b4-c7e63d3518e7` |
| **Overall Status** | ✅ **Passed** |
| **Baseline Verdict** | **Conditionally Acceptable** |
| **Protocol Verdict** | **Conditionally Acceptable** |
| **Coverage Verdict** | **Trusted** |
| **Compliance Score** | **100.0%** |
| **Evidence Coverage** | **100.0%** |
| **Evidence Confidence** | **High (100.0%)** |
| **Compliance Profile** | `Public (Inferred)` |
| **Duration** | 11.44s |
| **Transport** | STDIO |
| **Session Bootstrap** | ✅ Healthy |
| **Deferred Validation** | No — validation started from a clean bootstrap state. |
| **MCP Protocol Version (Effective)** | `2025-11-25` |
| **Benchmark Trust Level** | 🟢 **L5: Certified Secure — Production AI-agent ready** |

## 2. Connectivity & Session Bootstrap

This section explains how the validator established initial connectivity and whether validation started from a clean initialize handshake or a deferred advisory state.

| Bootstrap Signal | Value |
| :--- | :--- |
| **Bootstrap State** | ✅ Healthy |
| **Validation Proceeded Under Deferment** | No — validation started from a clean bootstrap state. |
| **Initialize Handshake** | ✅ Initialize handshake succeeded. |
| **Handshake Duration** | 35.0 ms |
| **Negotiated Protocol** | `2025-11-25` |
| **Observed Server Version** | `1.1.0` |
| **Server Profile Resolution** | `Public (Inferred)` |

Validation began from a clean bootstrap with no deferred connectivity risk carried into later categories.

## 3. Priority Findings

These are the highest-signal outcomes from this validation run.
> **Authority legend:** Authority order: Spec blocking and warnings, then Guideline, Heuristic, and Operational signals.

- [Spec] 1 spec signal(s), led by TierCheck: MAY requirement failed: MAY: Server supports sampling/createMessage

## 4. Action Hints

Compact next-step guidance derived from the highest-signal evidence in this run.

- No immediate remediation required; maintain current protocol and security posture.

## 5. Recommended Remediation Order

Fix blocking dependencies in this order so later validation evidence becomes trustworthy instead of merely quieter.

### Priority 3: Advertised Capabilities

**Impact after fix:** Capability-contract fixes make skipped and executed tool, resource, prompt, and task checks align with what the server declared.

| Issue | Fix | Component | Evidence |
| :--- | :--- | :--- | :--- |
| Synthetic load probe executed against tools/list using 20 requests at concurrency 4. | Interpret this as a generic pressure probe, not a workload-specific SLA benchmark. | `tools/list` | [Guideline] · `MCP.GUIDELINE.PERFORMANCE.SYNTHETIC_PROBE` · Info |

## 6. Deterministic Verdicts

These verdicts are the authoritative gate for pass/fail and policy decisions. Weighted benchmark scores remain descriptive only.
> **Authority legend:** Authority order: Spec blocking and warnings, then Guideline, Heuristic, and Operational signals.

| Lane | Verdict | Meaning |
| :--- | :--- | :--- |
| **Baseline** | **Conditionally Acceptable** | Overall deterministic gate across blocking findings and coverage debt. |
| **Protocol** | **Conditionally Acceptable** | Protocol correctness and contract integrity. |
| **Coverage** | **Trusted** | Whether enabled validation surfaces produced authoritative evidence. |

Baseline=ConditionallyAcceptable; Protocol=ConditionallyAcceptable; Coverage=Trusted; EvidenceConfidence=High (100%); BlockingDecisions=0.

## 7. Benchmark Trust Profile

Multi-dimensional benchmarking view of server trustworthiness for AI agent consumption.
This section is descriptive only. Release gating and pass/fail status are driven by the deterministic verdicts above, not by weighted trust averages.

| Dimension | Score | What It Measures |
| :--- | :---: | :--- |
| **Protocol Compliance** | 100% | MCP spec adherence, JSON-RPC compliance, response structures |
| **Security Posture** | 100% | Auth compliance, injection resistance, attack surface |
| **AI Safety** | 100% | Schema quality, destructive tool detection, exfiltration risk, prompt injection surface |
| **Operational Readiness** | 100% | Latency, throughput, error rate, stability |

### Summary

- Destructive tools: **0**
- Data exfiltration risk: **0**
- Prompt injection surface: **0**

### MCP Spec Compliance (RFC 2119 Tiers)

| Tier | Passed | Failed | Total | Impact |
| :--- | :---: | :---: | :---: | :--- |
| **MUST** | 14 | 0 | 14 | ✅ Fully compliant |
| **SHOULD** | 5 | 0 | 5 | ✅ All expected behaviors present |
| **MAY** | 3 | 3 | 6 | ℹ️ Informational (no score impact) |

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
| **Claude Code** | ✅ Compatible | 6 passed / 0 warnings / 0 failed | <https://code.claude.com/docs/en/mcp> |
| **VS Code Copilot Agent** | ✅ Compatible | 4 passed / 0 warnings / 0 failed | <https://code.visualstudio.com/docs/copilot/chat/mcp-servers> |
| **GitHub Copilot CLI** | ✅ Compatible | 2 passed / 0 warnings / 0 failed | <https://docs.github.com/en/copilot/how-tos/copilot-cli/customize-copilot/add-mcp-servers> |
| **GitHub Copilot Cloud Agent** | ⚠️ Compatible with warnings | 2 passed / 1 warnings / 0 failed | <https://docs.github.com/en/copilot/how-tos/use-copilot-agents/cloud-agent/extend-cloud-agent-with-mcp> |
| **Visual Studio Copilot** | ✅ Compatible | 5 passed / 0 warnings / 0 failed | <https://learn.microsoft.com/en-us/visualstudio/ide/mcp-servers?view=vs-2022> |

### Claude Code

**Status:** ✅ Compatible

All applicable compatibility checks passed (6 satisfied).

- No client-specific compatibility gaps were detected.

### VS Code Copilot Agent

**Status:** ✅ Compatible

All applicable compatibility checks passed (4 satisfied).

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
| Only the tool surface is currently consumed | Recommended | ⚠️ Warning | - | - | This profile currently consumes tools only; 1 prompt(s) and 1 resource(s) will not contribute to compatibility. |

### Visual Studio Copilot

**Status:** ✅ Compatible

All applicable compatibility checks passed (5 satisfied).

- No client-specific compatibility gaps were detected.

## 9. Validation Envelope

This section exposes the structured validation envelope used for layered reporting, coverage tracking, and pack provenance.

### Assessment Layers

| Layer | Status | Findings | Summary |
| :--- | :--- | :---: | :--- |
| `protocol-core` | ✅ Passed | 2 | declared capabilities: logging, prompts, prompts.listChanged, resources, resources.listChanged, tools, tools.listChanged \| roots/list: not advertised \| logging/setLevel: supported \| sampling/createMessage: not advertised \| completion/complete: not advertised |
| `tool-surface` | ✅ Passed | 0 | - |
| `resource-surface` | ✅ Passed | 0 | - |
| `prompt-surface` | ✅ Passed | 0 | - |
| `security-boundaries` | ✅ Passed | 0 | - |
| `performance` | ✅ Passed | 1 | - |
| `error-handling` | ✅ Passed | 0 | Validated 5 error scenario(s); 5 handled correctly. |
| `client-profiles` | ✅ Passed | 0 | Evaluated 5 profile(s); 1 warning profile(s); 0 incompatible profile(s). |

### Scenario Outcomes

| Scenario | Status | Findings | Summary |
| :--- | :--- | :---: | :--- |
| `bootstrap-initialize-handshake` | ✅ Passed | 0 | Bootstrap health checks completed successfully. |
| `protocol-compliance-review` | ✅ Passed | 2 | declared capabilities: logging, prompts, prompts.listChanged, resources, resources.listChanged, tools, tools.listChanged \| roots/list: not advertised \| logging/setLevel: supported \| sampling/createMessage: not advertised \| completion/complete: not advertised |
| `tool-catalog-smoke` | ✅ Passed | 0 | Tool validation completed. |
| `resource-catalog-smoke` | ✅ Passed | 0 | Resource validation completed. |
| `prompt-catalog-smoke` | ✅ Passed | 0 | Prompt validation completed. |
| `security-authentication-challenge` | ✅ Passed | 0 | Evaluated 2 authentication challenge scenario(s). |
| `security-attack-simulations` | ✅ Passed | 0 | Evaluated 7 attack simulation(s). |
| `error-handling-matrix` | ✅ Passed | 0 | Validated 5 error scenario(s); 5 handled correctly. |

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
| `tool-tools-list-schema-compliance` | `tool-surface` | `tools/list (Schema Compliance)` | `tool-result` | ⚠️ Schema validation warning: tools/list schema could not be fully processed |
| `resource-mcpval-server-metadata` | `resource-surface` | `mcpval://server/metadata` | `resource-result` | ✅ Resource read: contents[] array present |
| `prompt-validate-mcp-server` | `prompt-surface` | `validate-mcp-server` | `prompt-result` | ✅ prompts/get: messages[] array present (1 messages) |
| `auth-stdio-transport-protocol-check` | `security-boundaries` | `transport-validation` | `authentication-scenario` | ✅ Correctly using STDIO transport (no HTTP auth) |
| `auth-stdio-environment-credentials-check` | `security-boundaries` | `env-check` | `authentication-scenario` | ✅ Environment credential variables detected |
| `attack-inj-001-input-validation` | `security-boundaries` | `INJ-001 (Input Validation)` | `attack-simulation` | {"result":{"content":[{"type":"text","text":"MCP error -32602: Input validation error: Invalid arguments for tool validate: [\n  {\n    \"code\": \"invalid_union\",\n    \"errors\": [\n      [\n        {\n          \"expected\": \"string\",\n          \"code\": \"invalid_type\",\n          \"path\": [],\n          \"message\": \"Invalid input: expected string, received undefined\"\n        }\n      ],\n      [\n        {\n          \"expected\": \"string\",\n          \"code\": \"invalid_type\",\n          \"path\": [],\n          \"message\": \"Invalid input: expected string, received undefined\"\n        }\n      ]\n    ],\n    \"path\": [\n      \"server\"\n    ],\n    \"message\": \"Invalid input\"\n  },\n  {\n    \"code\": \"invalid_value\",\n    \"values\": [\n      \"public\",\n      \"authenticated\",\n      \"enterprise\",\n      \"unspecified\"\n    ],\n    \"path\": [\n      \"access\"\n    ],\n    \"message\": \"Invalid option: expected one of \\\"public\\\"\|\\\"authenticated\\\"\|\\\"enterprise\\\"\|\\\"unspecified\\\"\"\n  }\n]"}],"isError":true},"jsonrpc":"2.0","id":"d33cb9b3-bba3-4849-b000-5343a1e652ab"} |
| `attack-inj-002-input-validation` | `security-boundaries` | `INJ-002 (Input Validation)` | `attack-simulation` | {"result":{"content":[{"type":"text","text":"MCP error -32602: Input validation error: Invalid arguments for tool validate: [\n  {\n    \"code\": \"invalid_union\",\n    \"errors\": [\n      [\n        {\n          \"expected\": \"string\",\n          \"code\": \"invalid_type\",\n          \"path\": [],\n          \"message\": \"Invalid input: expected string, received undefined\"\n        }\n      ],\n      [\n        {\n          \"expected\": \"string\",\n          \"code\": \"invalid_type\",\n          \"path\": [],\n          \"message\": \"Invalid input: expected string, received undefined\"\n        }\n      ]\n    ],\n    \"path\": [\n      \"server\"\n    ],\n    \"message\": \"Invalid input\"\n  },\n  {\n    \"code\": \"invalid_value\",\n    \"values\": [\n      \"public\",\n      \"authenticated\",\n      \"enterprise\",\n      \"unspecified\"\n    ],\n    \"path\": [\n      \"access\"\n    ],\n    \"message\": \"Invalid option: expected one of \\\"public\\\"\|\\\"authenticated\\\"\|\\\"enterprise\\\"\|\\\"unspecified\\\"\"\n  }\n]"}],"isError":true},"jsonrpc":"2.0","id":"04fec1c3-bbee-4eff-81b5-b58d75c8f6df"} |
| `attack-inj-003-input-validation` | `security-boundaries` | `INJ-003 (Input Validation)` | `attack-simulation` | {"result":{"content":[{"type":"text","text":"MCP error -32602: Input validation error: Invalid arguments for tool validate: [\n  {\n    \"code\": \"invalid_union\",\n    \"errors\": [\n      [\n        {\n          \"expected\": \"string\",\n          \"code\": \"invalid_type\",\n          \"path\": [],\n          \"message\": \"Invalid input: expected string, received undefined\"\n        }\n      ],\n      [\n        {\n          \"expected\": \"string\",\n          \"code\": \"invalid_type\",\n          \"path\": [],\n          \"message\": \"Invalid input: expected string, received undefined\"\n        }\n      ]\n    ],\n    \"path\": [\n      \"server\"\n    ],\n    \"message\": \"Invalid input\"\n  },\n  {\n    \"code\": \"invalid_value\",\n    \"values\": [\n      \"public\",\n      \"authenticated\",\n      \"enterprise\",\n      \"unspecified\"\n    ],\n    \"path\": [\n      \"access\"\n    ],\n    \"message\": \"Invalid option: expected one of \\\"public\\\"\|\\\"authenticated\\\"\|\\\"enterprise\\\"\|\\\"unspecified\\\"\"\n  }\n]"}],"isError":true},"jsonrpc":"2.0","id":"c2c78952-cacd-427c-94d2-93ed588b936e"} |
| `attack-mcp-sec-001` | `security-boundaries` | `MCP-SEC-001` | `attack-simulation` | Server handled malformed requests gracefully with standard errors. |
| `attack-mcp-sec-002` | `security-boundaries` | `MCP-SEC-002` | `attack-simulation` | Consistent same-family error handling observed for unadvertised resource probes. |
| `attack-mcp-sec-003` | `security-boundaries` | `MCP-SEC-003` | `attack-simulation` | Server correctly rejected invalid schema. |
| `attack-mcp-ai-001` | `security-boundaries` | `MCP-AI-001` | `attack-simulation` | Error clarity: 100/100 (Good). Server error messages help AI self-correct. |
| `error-invalid-method-call` | `error-handling` | `invalid-method` | `error-scenario` | HTTP 400; JSON-RPC error -32601: Method not found |
| `error-malformed-json-payload` | `error-handling` | `malformed-json` | `error-scenario` | HTTP 200; JSON-RPC error -32700: Parse error |
| `error-graceful-degradation-on-invalid-request` | `error-handling` | `invalid-request` | `error-scenario` | HTTP 200; JSON-RPC error -32600: Invalid Request |
| `error-timeout-handling-recovery` | `error-handling` | `timeout-handling` | `error-scenario` | Validator induced a zero-window STDIO read timeout and then drained the pending response. |
| `error-connection-interruption-recovery` | `error-handling` | `connection-interruption` | `error-scenario` | Validator terminated the STDIO child process and restarted it for recovery verification. |

## 10. Capability Snapshot

| Probe | Discovered | HTTP Status | Duration | Result |
| :--- | :---: | :---: | :---: | :--- |
| Tools/list | 3 | HTTP 200 | 105.4 ms | ✅ Listed<br/>Call ✅ |
| Resources/list | 1 | HTTP 200 | 0.4 ms | ✅ Listed |
| Prompts/list | 1 | HTTP 200 | 0.6 ms | ✅ Listed |

- **First Tool Probed:** `validate`

## 11. Scoring Methodology

These notes explain how the overall score and blocking decision were calibrated for this run.

- INFO: Authentication challenge observations are informational for the declared public profile.

## 12. Compliance Matrix

| Category | Status | Score | Issues |
| :--- | :---: | :---: | :---: |
| Protocol Compliance | ✅ Passed | 100.0% | - |
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
| STDIO Transport Protocol Check | `transport-validation` | STDIO transport should NOT use HTTP authentication headers | ✅ Correctly using STDIO transport (no HTTP auth) |  |  | ✅ |
| STDIO Environment Credentials Check | `env-check` | Environment variables may carry credentials for STDIO servers | ✅ Environment credential variables detected |  |  | ✅ |

### Adversarial Input Handling

> **Legend:**
> * **REFLECTED / UNSAFE ECHO**: The server reflected the payload back without sanitization. No execution confirmed.
> * **BLOCKED**: The server correctly rejected or sanitized the input.
> * **SKIPPED**: Test could not be executed (e.g., no tools discovered for injection target).

| Attack Vector | Description | Result | Probe | Analysis |
| :--- | :--- | :---: | :--- | :--- |
| INJ-001 (Input Validation) | Simulated INJ-001 (Input Validation) attack on validate | 🛡️ BLOCKED | `tools/list`/stdio: Success HTTP 200; auth NotApplied (+1) | `{"result":{"content":[{"type":"text","text":"MCP error -32602: Input validation error: Invalid arguments for tool validate: [\n  {\n    \"code\": \"invalid_union\",\n    \"errors\": [\n      [\n        {\n          \"expected\": \"string\",\n          \"code\": \"invalid_type\",\n          \"path\": [],\n          \"message\": \"Invalid input: expected string, received undefined\"\n        }\n      ],\n      [\n        {\n          \"expected\": \"string\",\n          \"code\": \"invalid_type\",\n          \"path\": [],\n          \"message\": \"Invalid input: expected string, received undefined\"\n        }\n      ]\n    ],\n    \"path\": [\n      \"server\"\n    ],\n    \"message\": \"Invalid input\"\n  },\n  {\n    \"code\": \"invalid_value\",\n    \"values\": [\n      \"public\",\n      \"authenticated\",\n      \"enterprise\",\n      \"unspecified\"\n    ],\n    \"path\": [\n      \"access\"\n    ],\n    \"message\": \"Invalid option: expected one of \\\"public\\\"\|\\\"authenticated\\\"\|\\\"enterprise\\\"\|\\\"unspecified\\\"\"\n  }\n]"}],"isError":true},"jsonrpc":"2.0","id":"d33cb9b3-bba3-4849-b000-5343a1e652ab"}` |
| INJ-002 (Input Validation) | Simulated INJ-002 (Input Validation) attack on validate | 🛡️ BLOCKED | `tools/list`/stdio: Success HTTP 200; auth NotApplied (+1) | `{"result":{"content":[{"type":"text","text":"MCP error -32602: Input validation error: Invalid arguments for tool validate: [\n  {\n    \"code\": \"invalid_union\",\n    \"errors\": [\n      [\n        {\n          \"expected\": \"string\",\n          \"code\": \"invalid_type\",\n          \"path\": [],\n          \"message\": \"Invalid input: expected string, received undefined\"\n        }\n      ],\n      [\n        {\n          \"expected\": \"string\",\n          \"code\": \"invalid_type\",\n          \"path\": [],\n          \"message\": \"Invalid input: expected string, received undefined\"\n        }\n      ]\n    ],\n    \"path\": [\n      \"server\"\n    ],\n    \"message\": \"Invalid input\"\n  },\n  {\n    \"code\": \"invalid_value\",\n    \"values\": [\n      \"public\",\n      \"authenticated\",\n      \"enterprise\",\n      \"unspecified\"\n    ],\n    \"path\": [\n      \"access\"\n    ],\n    \"message\": \"Invalid option: expected one of \\\"public\\\"\|\\\"authenticated\\\"\|\\\"enterprise\\\"\|\\\"unspecified\\\"\"\n  }\n]"}],"isError":true},"jsonrpc":"2.0","id":"04fec1c3-bbee-4eff-81b5-b58d75c8f6df"}` |
| INJ-003 (Input Validation) | Simulated INJ-003 (Input Validation) attack on validate | 🛡️ BLOCKED | `tools/list`/stdio: Success HTTP 200; auth NotApplied (+1) | `{"result":{"content":[{"type":"text","text":"MCP error -32602: Input validation error: Invalid arguments for tool validate: [\n  {\n    \"code\": \"invalid_union\",\n    \"errors\": [\n      [\n        {\n          \"expected\": \"string\",\n          \"code\": \"invalid_type\",\n          \"path\": [],\n          \"message\": \"Invalid input: expected string, received undefined\"\n        }\n      ],\n      [\n        {\n          \"expected\": \"string\",\n          \"code\": \"invalid_type\",\n          \"path\": [],\n          \"message\": \"Invalid input: expected string, received undefined\"\n        }\n      ]\n    ],\n    \"path\": [\n      \"server\"\n    ],\n    \"message\": \"Invalid input\"\n  },\n  {\n    \"code\": \"invalid_value\",\n    \"values\": [\n      \"public\",\n      \"authenticated\",\n      \"enterprise\",\n      \"unspecified\"\n    ],\n    \"path\": [\n      \"access\"\n    ],\n    \"message\": \"Invalid option: expected one of \\\"public\\\"\|\\\"authenticated\\\"\|\\\"enterprise\\\"\|\\\"unspecified\\\"\"\n  }\n]"}],"isError":true},"jsonrpc":"2.0","id":"c2c78952-cacd-427c-94d2-93ed588b936e"}` |
| MCP-SEC-001 | JSON-RPC Error Smuggling | 🛡️ BLOCKED | `rpc.system.invalid`/stdio: ProtocolError HTTP 400; auth NotApplied | `Server handled malformed requests gracefully with standard errors.` |
| MCP-SEC-002 | Metadata Enumeration | 🛡️ BLOCKED | `resources/list`/stdio: Success HTTP 200; auth NotApplied (+2) | `Consistent same-family error handling observed for unadvertised resource probes.` |
| MCP-SEC-003 | Schema Confusion | 🛡️ BLOCKED | `tools/list`/stdio: Success HTTP 200; auth NotApplied (+1) | `Server correctly rejected invalid schema.` |
| MCP-AI-001 | Hallucination Fuzzing | 🛡️ BLOCKED | `tools/list`/stdio: Success HTTP 200; auth NotApplied (+1) | `Error clarity: 100/100 (Good). Server error messages help AI self-correct.` |

## 15. Protocol Compliance

**Status Detail:** declared capabilities: logging, prompts, prompts.listChanged, resources, resources.listChanged, tools, tools.listChanged | roots/list: not advertised | logging/setLevel: supported | sampling/createMessage: not advertised | completion/complete: not advertised

✅ No protocol violations detected.

## 16. Tool Validation

**Tools Discovered:** 3

### Tool Metadata Completeness

Annotation coverage across the discovered tool catalog. Missing annotations reduce AI agent safety and UX quality.

| Tool | title | description | readOnlyHint | destructiveHint | openWorldHint | idempotentHint |
| :--- | :---: | :---: | :---: | :---: | :---: | :---: |
| `tools/list (Schema Compliance)` | ❌ | ❌ | ❌ | ❌ | ❌ | ❌ |
| `validate` | ✅ | ✅ | ✅ | ✅ | ✅ | ✅ |
| `health_check` | ✅ | ✅ | ✅ | ✅ | ✅ | ✅ |
| `discover` | ✅ | ✅ | ✅ | ✅ | ✅ | ✅ |
| **Coverage** | **3/4** | **3/4** | **3/4** | **3/4** | **3/4** | **3/4** |

### AI Safety Control Evidence

This table separates server-declared tool metadata from host-side controls that the validator cannot directly observe.

| Control | Declared | Missing | Not Observable | Not Applicable | Representative Note |
| :--- | ---: | ---: | ---: | ---: | :--- |
| User confirmation | 0 | 0 | 0 | 3 | No destructive tool behavior was declared or inferred from tool metadata. |
| Audit trail | 0 | 0 | 3 | 0 | Audit trail behavior is not observable from tool metadata. |
| Data-sharing disclosure | 3 | 0 | 0 | 0 | Tool declares that it may interact with external systems. |
| Destructive-action confirmation | 0 | 0 | 0 | 3 | Tool is not declared or inferred as destructive. |
| Host/server responsibility split | 0 | 0 | 3 | 0 | Host-side consent, denial, and disclosure UX cannot be proven from server tool metadata alone. |

### Tool: `tools/list (Schema Compliance)`

**Status:** ✅ Passed
**Execution Time:** 105.39ms
---

### Tool: `validate`

**Status:** ✅ Passed
**Execution Time:** 9959.51ms

#### Tool Metadata
| Property | Value |
| :--- | :--- |
| Display Title | Validate MCP Server |
| readOnlyHint | False |
| destructiveHint | False |
| openWorldHint | True |
| idempotentHint | False |

---

### Tool: `health_check`

**Status:** ✅ Passed
**Execution Time:** 0.39ms

#### Tool Metadata
| Property | Value |
| :--- | :--- |
| Display Title | Health Check MCP Server |
| readOnlyHint | True |
| destructiveHint | False |
| openWorldHint | True |
| idempotentHint | True |

---

### Tool: `discover`

**Status:** ✅ Passed
**Execution Time:** 0.24ms

#### Tool Metadata
| Property | Value |
| :--- | :--- |
| Display Title | Discover MCP Server Capabilities |
| readOnlyHint | True |
| destructiveHint | False |
| openWorldHint | True |
| idempotentHint | True |

---

### Tool Catalog Advisory Breakdown

Remaining tool-catalog debt is grouped by authority so MCP specification failures are not conflated with guidance or deterministic AI-oriented heuristics.

| Authority | Active Rules | Coverage | Highest Severity | Representative Gaps |
| :--- | :---: | :--- | :---: | :--- |
| Spec | 0 | 0/3 (0%) | - | No current catalog-wide tool advisories. |
| Guideline | 0 | 0/3 (0%) | - | No current catalog-wide tool advisories. |
| Heuristic | 0 | 0/3 (0%) | - | No current catalog-wide tool advisories. |
| Operational | 0 | 0/3 (0%) | - | No current catalog-wide tool advisories. |

## 17. Resource Capabilities

**Resources Discovered:** 1

| Resource Name | URI | MIME Type | Size | Status |
| :--- | :--- | :--- | :--- | :---: |
| mcpval-server-metadata | `mcpval://server/metadata` | application/json | 320 | ✅ |

## 18. Prompt Capabilities

**Prompts Discovered:** 1

| Prompt Name | Status | Issues |
| :--- | :---: | :--- |
| validate-mcp-server | ✅ Passed | ✅ prompts/get: messages[] array present (1 messages) |

## 19. AI Readiness Assessment

**AI Readiness Score:** ✅ **100/100** (Good)

**Evidence basis:** Deterministic schema and payload heuristics. Measured model-evaluation results, when enabled, are written as a separate companion artifact and are not blended into this score.

| Criterion | What It Measures |
| :--- | :--- |
| **Description Completeness** | Do all tool parameters have descriptions? Missing descriptions increase hallucination risk. |
| **Type Specificity** | Are `string` parameters constrained with `enum`, `pattern`, or `format`? Vague types cause incorrect arguments. |
| **Token Efficiency** | Is the `tools/list` payload within context window limits? Estimated: ~1,356 tokens. |

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
| **Avg Latency** | 0.37ms | 🚀 Excellent |
| **P95 Latency** | 0.51ms | - |
| **Throughput** | 2331.27 req/sec | - |
| **Error Rate** | 0.00% | ✅ Clean |
| **Requests** | 20/20 successful | - |

---
*Produced with [MCP Validator](https://github.com/navalerakesh/mcp-validation-security) · [McpVal on NuGet](https://www.nuget.org/packages/McpVal#versions-body-tab)*
