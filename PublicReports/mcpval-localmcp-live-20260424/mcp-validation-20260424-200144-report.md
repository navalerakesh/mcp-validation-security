# MCP Server Compliance & Validation Report
**Generated:** 2026-04-24 20:01:44 UTC

## 1. Executive Summary

| Metric | Value |
| :--- | :--- |
| **Server Endpoint** | `node /Users/navalerakesh/Work/engineering/mcp-validation-security/mcpval-mcp/dist/index.js` |
| **Validation ID** | `3b0ff0d9-b1f5-4c03-a6d4-73e9d49cf716` |
| **Overall Status** | ✅ **Passed** |
| **Baseline Verdict** | **Conditionally Acceptable** |
| **Protocol Verdict** | **Conditionally Acceptable** |
| **Coverage Verdict** | **Trusted** |
| **Compliance Score** | **100.0%** |
| **Compliance Profile** | `Public (Inferred)` |
| **Duration** | 30.98s |
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
| **Handshake Duration** | 43.7 ms |
| **Negotiated Protocol** | `2025-11-25` |
| **Observed Server Version** | `1.1.0` |
| **Server Profile Resolution** | `Public (Inferred)` |

Validation began from a clean bootstrap with no deferred connectivity risk carried into later categories.

## 3. Action Hints

Compact next-step guidance derived from the highest-signal evidence in this run.

- No immediate remediation required; maintain current protocol and security posture.

## 4. Deterministic Verdicts

These verdicts are the authoritative gate for pass/fail and policy decisions. Weighted benchmark scores remain descriptive only.

| Lane | Verdict | Meaning |
| :--- | :--- | :--- |
| **Baseline** | **Conditionally Acceptable** | Overall deterministic gate across blocking findings and coverage debt. |
| **Protocol** | **Conditionally Acceptable** | Protocol correctness and contract integrity. |
| **Coverage** | **Trusted** | Whether enabled validation surfaces produced authoritative evidence. |

Baseline=ConditionallyAcceptable; Protocol=ConditionallyAcceptable; Coverage=Trusted; BlockingDecisions=0.

## 5. Benchmark Trust Profile

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
| **MUST** | 9 | 0 | 9 | ✅ Fully compliant |
| **SHOULD** | 5 | 0 | 5 | ✅ All expected behaviors present |
| **MAY** | 1 | 5 | 6 | ℹ️ Informational (no score impact) |

#### ℹ️ Optional Features (MAY)

- ➖ MAY: Server supports logging/setLevel
- ➖ MAY: Server supports sampling/createMessage
- ➖ MAY: Server supports roots/list
- ➖ MAY: Server supports completion/complete
- ➖ MAY: Server supports resources/templates/list
- ✅ MAY: Tools include 'annotations' (readOnlyHint, destructiveHint)

## 6. Client Profile Compatibility

Documented host-side compatibility assessments derived from the neutral validation evidence collected during this run.

| Client Profile | Status | Requirements | Documentation |
| :--- | :--- | :--- | :--- |
| **Claude Code** | ✅ Compatible | 4 passed / 0 warnings / 0 failed | <https://docs.anthropic.com/en/docs/claude-code/mcp> |
| **VS Code Copilot Agent** | ✅ Compatible | 4 passed / 0 warnings / 0 failed | <https://code.visualstudio.com/docs/copilot/chat/mcp-servers> |
| **GitHub Copilot CLI** | ✅ Compatible | 4 passed / 0 warnings / 0 failed | <https://docs.github.com/en/copilot/how-tos/copilot-cli/customize-copilot/add-mcp-servers> |
| **GitHub Copilot Cloud Agent** | ✅ Compatible | 5 passed / 0 warnings / 0 failed | <https://docs.github.com/en/copilot/how-tos/use-copilot-agents/cloud-agent/extend-cloud-agent-with-mcp> |
| **Visual Studio Copilot** | ✅ Compatible | 4 passed / 0 warnings / 0 failed | <https://docs.github.com/en/copilot/how-tos/provide-context/use-mcp/extend-copilot-chat-with-mcp?tool=visualstudio> |

### Claude Code

**Status:** ✅ Compatible

All applicable compatibility checks passed (4 satisfied).

- No client-specific compatibility gaps were detected.

### VS Code Copilot Agent

**Status:** ✅ Compatible

All applicable compatibility checks passed (4 satisfied).

- No client-specific compatibility gaps were detected.

### GitHub Copilot CLI

**Status:** ✅ Compatible

All applicable compatibility checks passed (4 satisfied).

- No client-specific compatibility gaps were detected.

### GitHub Copilot Cloud Agent

**Status:** ✅ Compatible

All applicable compatibility checks passed (5 satisfied).

- No client-specific compatibility gaps were detected.

### Visual Studio Copilot

**Status:** ✅ Compatible

All applicable compatibility checks passed (4 satisfied).

- No client-specific compatibility gaps were detected.

## 7. Validation Envelope

This section exposes the structured validation envelope used for layered reporting, coverage tracking, and pack provenance.

### Assessment Layers

| Layer | Status | Findings | Summary |
| :--- | :--- | :---: | :--- |
| `protocol-core` | ✅ Passed | 1 | declared capabilities: tools \| roots/list: not supported \| logging/setLevel: not supported \| sampling/createMessage: not supported \| completion/complete: not supported |
| `tool-surface` | ✅ Passed | 0 | - |
| `resource-surface` | ✅ Passed | 0 | - |
| `prompt-surface` | ✅ Passed | 0 | - |
| `security-boundaries` | ✅ Passed | 0 | - |
| `performance` | ✅ Passed | 1 | - |
| `error-handling` | ✅ Passed | 2 | Validated 3 error scenario(s); 3 handled correctly. |
| `client-profiles` | ✅ Passed | 0 | Evaluated 5 profile(s); 0 warning profile(s); 0 incompatible profile(s). |

### Scenario Outcomes

| Scenario | Status | Findings | Summary |
| :--- | :--- | :---: | :--- |
| `bootstrap-initialize-handshake` | ✅ Passed | 0 | Bootstrap health checks completed successfully. |
| `protocol-compliance-review` | ✅ Passed | 1 | declared capabilities: tools \| roots/list: not supported \| logging/setLevel: not supported \| sampling/createMessage: not supported \| completion/complete: not supported |
| `tool-catalog-smoke` | ✅ Passed | 0 | Tool validation completed. |
| `resource-catalog-smoke` | ✅ Passed | 0 | No resources were discovered during validation. |
| `prompt-catalog-smoke` | ✅ Passed | 0 | No prompts were discovered during validation. |
| `security-authentication-challenge` | ✅ Passed | 0 | Evaluated 2 authentication challenge scenario(s). |
| `security-attack-simulations` | ➖ Skipped | 0 | Evaluated 7 attack simulation(s). |
| `error-handling-matrix` | ✅ Passed | 2 | Validated 3 error scenario(s); 3 handled correctly. |

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
| `tool-tools-list-schema-compliance` | `tool-surface` | `tools/list (Schema Compliance)` | `tool-result` | ⚠️ Schema validation warning: tools/list schema could not be fully processed |
| `auth-stdio-transport-protocol-check` | `security-boundaries` | `transport-validation` | `authentication-scenario` | ✅ Correctly using STDIO transport (no HTTP auth) |
| `auth-stdio-environment-credentials-check` | `security-boundaries` | `env-check` | `authentication-scenario` | ✅ Environment credential variables detected |
| `attack-inj-001-input-validation` | `security-boundaries` | `INJ-001 (Input Validation)` | `attack-simulation` | {"result":{"content":[{"type":"text","text":"MCP error -32602: Input validation error: Invalid arguments for tool validate: [\n  {\n    \"received\": \"'; DROP TABLE users; --\",\n    \"code\": \"invalid_enum_value\",\n    \"options\": [\n      \"public\",\n      \"authenticated\",\n      \"enterprise\",\n      \"unspecified\"\n    ],\n    \"path\": [\n      \"access\"\n    ],\n    \"message\": \"Invalid enum value. Expected 'public' \| 'authenticated' \| 'enterprise' \| 'unspecified', received ''; DROP TABLE users; --'\"\n  },\n  {\n    \"code\": \"invalid_union\",\n    \"unionErrors\": [\n      {\n        \"issues\": [\n          {\n            \"code\": \"invalid_type\",\n            \"expected\": \"string\",\n            \"received\": \"undefined\",\n            \"path\": [\n              \"server\"\n            ],\n            \"message\": \"Required\"\n          }\n        ],\n        \"name\": \"ZodError\"\n      },\n      {\n        \"issues\": [\n          {\n            \"code\": \"invalid_type\",\n            \"expected\": \"string\",\n            \"received\": \"undefined\",\n            \"path\": [\n              \"server\"\n            ],\n            \"message\": \"Required\"\n          }\n        ],\n        \"name\": \"ZodError\"\n      }\n    ],\n    \"path\": [\n      \"server\"\n    ],\n    \"message\": \"Invalid input\"\n  }\n]"}],"isError":true},"jsonrpc":"2.0","id":"5c8bb8b7-efc5-42fc-bde2-0c39e3c1767f"} |
| `attack-inj-002-input-validation` | `security-boundaries` | `INJ-002 (Input Validation)` | `attack-simulation` | {"result":{"content":[{"type":"text","text":"MCP error -32602: Input validation error: Invalid arguments for tool validate: [\n  {\n    \"received\": \"<script>alert('XSS')</script>\",\n    \"code\": \"invalid_enum_value\",\n    \"options\": [\n      \"public\",\n      \"authenticated\",\n      \"enterprise\",\n      \"unspecified\"\n    ],\n    \"path\": [\n      \"access\"\n    ],\n    \"message\": \"Invalid enum value. Expected 'public' \| 'authenticated' \| 'enterprise' \| 'unspecified', received '<script>alert('XSS')</script>'\"\n  },\n  {\n    \"code\": \"invalid_union\",\n    \"unionErrors\": [\n      {\n        \"issues\": [\n          {\n            \"code\": \"invalid_type\",\n            \"expected\": \"string\",\n            \"received\": \"undefined\",\n            \"path\": [\n              \"server\"\n            ],\n            \"message\": \"Required\"\n          }\n        ],\n        \"name\": \"ZodError\"\n      },\n      {\n        \"issues\": [\n          {\n            \"code\": \"invalid_type\",\n            \"expected\": \"string\",\n            \"received\": \"undefined\",\n            \"path\": [\n              \"server\"\n            ],\n            \"message\": \"Required\"\n          }\n        ],\n        \"name\": \"ZodError\"\n      }\n    ],\n    \"path\": [\n      \"server\"\n    ],\n    \"message\": \"Invalid input\"\n  }\n]"}],"isError":true},"jsonrpc":"2.0","id":"a6329be2-1377-4800-b592-e36ca9968b57"} |
| `attack-inj-003-input-validation` | `security-boundaries` | `INJ-003 (Input Validation)` | `attack-simulation` | {"result":{"content":[{"type":"text","text":"MCP error -32602: Input validation error: Invalid arguments for tool validate: [\n  {\n    \"received\": \"; rm -rf /\",\n    \"code\": \"invalid_enum_value\",\n    \"options\": [\n      \"public\",\n      \"authenticated\",\n      \"enterprise\",\n      \"unspecified\"\n    ],\n    \"path\": [\n      \"access\"\n    ],\n    \"message\": \"Invalid enum value. Expected 'public' \| 'authenticated' \| 'enterprise' \| 'unspecified', received '; rm -rf /'\"\n  },\n  {\n    \"code\": \"invalid_union\",\n    \"unionErrors\": [\n      {\n        \"issues\": [\n          {\n            \"code\": \"invalid_type\",\n            \"expected\": \"string\",\n            \"received\": \"undefined\",\n            \"path\": [\n              \"server\"\n            ],\n            \"message\": \"Required\"\n          }\n        ],\n        \"name\": \"ZodError\"\n      },\n      {\n        \"issues\": [\n          {\n            \"code\": \"invalid_type\",\n            \"expected\": \"string\",\n            \"received\": \"undefined\",\n            \"path\": [\n              \"server\"\n            ],\n            \"message\": \"Required\"\n          }\n        ],\n        \"name\": \"ZodError\"\n      }\n    ],\n    \"path\": [\n      \"server\"\n    ],\n    \"message\": \"Invalid input\"\n  }\n]"}],"isError":true},"jsonrpc":"2.0","id":"e475122f-46aa-4127-b433-956a40701052"} |
| `attack-mcp-sec-001` | `security-boundaries` | `MCP-SEC-001` | `attack-simulation` | Server handled malformed requests gracefully with standard errors. |
| `attack-mcp-sec-002` | `security-boundaries` | `MCP-SEC-002` | `attack-simulation` | Skipped: Metadata enumeration requires an advertised concrete resource URI; no like-for-like resource surface was available. |
| `attack-mcp-sec-003` | `security-boundaries` | `MCP-SEC-003` | `attack-simulation` | Server correctly rejected invalid schema. |
| `attack-mcp-ai-001` | `security-boundaries` | `MCP-AI-001` | `attack-simulation` | Error clarity: 100/100 (Good). Server error messages help AI self-correct. |
| `error-invalid-method-call` | `error-handling` | `invalid-method` | `error-scenario` | HTTP 400; JSON-RPC error -32601: Method not found |
| `error-timeout-handling-recovery` | `error-handling` | `timeout-handling` | `error-scenario` | Validator induced a zero-window STDIO read timeout and then drained the pending response. |
| `error-connection-interruption-recovery` | `error-handling` | `connection-interruption` | `error-scenario` | Validator terminated the STDIO child process and restarted it for recovery verification. |

## 8. Capability Snapshot

| Probe | Discovered | HTTP Status | Duration | Result |
| :--- | :---: | :---: | :---: | :--- |
| Tools/list | 3 | HTTP 200 | 106.6 ms | ✅ Listed<br/>Call ✅ |
| Resources/list | 0 | HTTP 400 | 0.9 ms | ❌ Failed |
| Prompts/list | 0 | HTTP 400 | 0.3 ms | ❌ Failed |

- **First Tool Probed:** `validate`

## 9. Scoring Methodology

These notes explain how the overall score and blocking decision were calibrated for this run.

- INFO: Authentication challenge observations are informational for the declared public profile.

## 10. Compliance Matrix

| Category | Status | Score | Issues |
| :--- | :---: | :---: | :---: |
| Protocol Compliance | ✅ Passed | 100.0% | - |
| Security Assessment | ✅ Passed | 100.0% | - |
| Tool Validation | ✅ Passed | 100.0% | - |
| Resource Capabilities | ✅ Passed | 100.0% | - |
| Prompt Capabilities | ✅ Passed | 100.0% | - |
| Performance | ✅ Passed | 100.0% | - |

## 11. Performance Calibration

Performance is assessed using a synthetic load probe rather than a workload-specific SLA benchmark.
For public or remote SaaS endpoints, partial failures under synthetic pressure are reported as advisory when they look like capacity limits or edge protections rather than protocol instability.

- Synthetic load probe executed against tools/list using 20 requests at concurrency 4.

## 12. Security Assessment

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

| Attack Vector | Description | Result | Analysis |
| :--- | :--- | :---: | :--- |
| INJ-001 (Input Validation) | Simulated INJ-001 (Input Validation) attack on validate | 🛡️ BLOCKED | `{"result":{"content":[{"type":"text","text":"MCP error -32602: Input validation error: Invalid arguments for tool validate: [\n  {\n    \"received\": \"'; DROP TABLE users; --\",\n    \"code\": \"invalid_enum_value\",\n    \"options\": [\n      \"public\",\n      \"authenticated\",\n      \"enterprise\",\n      \"unspecified\"\n    ],\n    \"path\": [\n      \"access\"\n    ],\n    \"message\": \"Invalid enum value. Expected 'public' \| 'authenticated' \| 'enterprise' \| 'unspecified', received ''; DROP TABLE users; --'\"\n  },\n  {\n    \"code\": \"invalid_union\",\n    \"unionErrors\": [\n      {\n        \"issues\": [\n          {\n            \"code\": \"invalid_type\",\n            \"expected\": \"string\",\n            \"received\": \"undefined\",\n            \"path\": [\n              \"server\"\n            ],\n            \"message\": \"Required\"\n          }\n        ],\n        \"name\": \"ZodError\"\n      },\n      {\n        \"issues\": [\n          {\n            \"code\": \"invalid_type\",\n            \"expected\": \"string\",\n            \"received\": \"undefined\",\n            \"path\": [\n              \"server\"\n            ],\n            \"message\": \"Required\"\n          }\n        ],\n        \"name\": \"ZodError\"\n      }\n    ],\n    \"path\": [\n      \"server\"\n    ],\n    \"message\": \"Invalid input\"\n  }\n]"}],"isError":true},"jsonrpc":"2.0","id":"5c8bb8b7-efc5-42fc-bde2-0c39e3c1767f"}` |
| INJ-002 (Input Validation) | Simulated INJ-002 (Input Validation) attack on validate | 🛡️ BLOCKED | `{"result":{"content":[{"type":"text","text":"MCP error -32602: Input validation error: Invalid arguments for tool validate: [\n  {\n    \"received\": \"<script>alert('XSS')</script>\",\n    \"code\": \"invalid_enum_value\",\n    \"options\": [\n      \"public\",\n      \"authenticated\",\n      \"enterprise\",\n      \"unspecified\"\n    ],\n    \"path\": [\n      \"access\"\n    ],\n    \"message\": \"Invalid enum value. Expected 'public' \| 'authenticated' \| 'enterprise' \| 'unspecified', received '<script>alert('XSS')</script>'\"\n  },\n  {\n    \"code\": \"invalid_union\",\n    \"unionErrors\": [\n      {\n        \"issues\": [\n          {\n            \"code\": \"invalid_type\",\n            \"expected\": \"string\",\n            \"received\": \"undefined\",\n            \"path\": [\n              \"server\"\n            ],\n            \"message\": \"Required\"\n          }\n        ],\n        \"name\": \"ZodError\"\n      },\n      {\n        \"issues\": [\n          {\n            \"code\": \"invalid_type\",\n            \"expected\": \"string\",\n            \"received\": \"undefined\",\n            \"path\": [\n              \"server\"\n            ],\n            \"message\": \"Required\"\n          }\n        ],\n        \"name\": \"ZodError\"\n      }\n    ],\n    \"path\": [\n      \"server\"\n    ],\n    \"message\": \"Invalid input\"\n  }\n]"}],"isError":true},"jsonrpc":"2.0","id":"a6329be2-1377-4800-b592-e36ca9968b57"}` |
| INJ-003 (Input Validation) | Simulated INJ-003 (Input Validation) attack on validate | 🛡️ BLOCKED | `{"result":{"content":[{"type":"text","text":"MCP error -32602: Input validation error: Invalid arguments for tool validate: [\n  {\n    \"received\": \"; rm -rf /\",\n    \"code\": \"invalid_enum_value\",\n    \"options\": [\n      \"public\",\n      \"authenticated\",\n      \"enterprise\",\n      \"unspecified\"\n    ],\n    \"path\": [\n      \"access\"\n    ],\n    \"message\": \"Invalid enum value. Expected 'public' \| 'authenticated' \| 'enterprise' \| 'unspecified', received '; rm -rf /'\"\n  },\n  {\n    \"code\": \"invalid_union\",\n    \"unionErrors\": [\n      {\n        \"issues\": [\n          {\n            \"code\": \"invalid_type\",\n            \"expected\": \"string\",\n            \"received\": \"undefined\",\n            \"path\": [\n              \"server\"\n            ],\n            \"message\": \"Required\"\n          }\n        ],\n        \"name\": \"ZodError\"\n      },\n      {\n        \"issues\": [\n          {\n            \"code\": \"invalid_type\",\n            \"expected\": \"string\",\n            \"received\": \"undefined\",\n            \"path\": [\n              \"server\"\n            ],\n            \"message\": \"Required\"\n          }\n        ],\n        \"name\": \"ZodError\"\n      }\n    ],\n    \"path\": [\n      \"server\"\n    ],\n    \"message\": \"Invalid input\"\n  }\n]"}],"isError":true},"jsonrpc":"2.0","id":"e475122f-46aa-4127-b433-956a40701052"}` |
| MCP-SEC-001 | JSON-RPC Error Smuggling | 🛡️ BLOCKED | `Server handled malformed requests gracefully with standard errors.` |
| MCP-SEC-002 | Metadata Enumeration | ⏭️ SKIPPED | `Skipped: Metadata enumeration requires an advertised concrete resource URI; no like-for-like resource surface was available.` |
| MCP-SEC-003 | Schema Confusion | 🛡️ BLOCKED | `Server correctly rejected invalid schema.` |
| MCP-AI-001 | Hallucination Fuzzing | 🛡️ BLOCKED | `Error clarity: 100/100 (Good). Server error messages help AI self-correct.` |

## 13. Protocol Compliance

**Status Detail:** declared capabilities: tools | roots/list: not supported | logging/setLevel: not supported | sampling/createMessage: not supported | completion/complete: not supported

✅ No protocol violations detected.

## 14. Tool Validation

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

### Tool: `tools/list (Schema Compliance)`

**Status:** ✅ Passed
**Execution Time:** 106.62ms
---

### Tool: `validate`

**Status:** ✅ Passed
**Execution Time:** 9950.07ms

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
**Execution Time:** 10001.85ms

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
**Execution Time:** 2.64ms

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

Remaining tool-catalog debt is grouped by authority so MCP specification failures are not conflated with guidance or AI-oriented heuristics.

| Authority | Active Rules | Coverage | Highest Severity | Representative Gaps |
| :--- | :---: | :--- | :---: | :--- |
| Spec | 0 | 0/3 (0%) | - | No current catalog-wide tool advisories. |
| Guideline | 0 | 0/3 (0%) | - | No current catalog-wide tool advisories. |
| Heuristic | 0 | 0/3 (0%) | - | No current catalog-wide tool advisories. |

## 15. Resource Capabilities

**Resources Discovered:** 0

## 16. AI Readiness Assessment

**AI Readiness Score:** ✅ **100/100** (Good)

This score measures how well the server's tool schemas are optimized for consumption by AI agents (LLMs).

| Criterion | What It Measures |
| :--- | :--- |
| **Description Completeness** | Do all tool parameters have descriptions? Missing descriptions increase hallucination risk. |
| **Type Specificity** | Are `string` parameters constrained with `enum`, `pattern`, or `format`? Vague types cause incorrect arguments. |
| **Token Efficiency** | Is the `tools/list` payload within context window limits? Estimated: ~1,378 tokens. |

## 17. Optional MCP Capabilities

These probes check whether the server supports optional MCP features beyond the core primitives.

| Capability | Status |
| :--- | :--- |
| `declared capabilities` | ➖ tools |
| `roots/list` | ➖ not supported |
| `logging/setLevel` | ➖ not supported |
| `sampling/createMessage` | ➖ not supported |
| `completion/complete` | ➖ not supported |

## 18. Performance Metrics

| Metric | Result | Verdict |
| :--- | :--- | :--- |
| **Avg Latency** | 0.44ms | 🚀 Excellent |
| **P95 Latency** | 0.72ms | - |
| **Throughput** | 1835.70 req/sec | - |
| **Error Rate** | 0.00% | ✅ Clean |
| **Requests** | 20/20 successful | - |

---
*Produced with [MCP Validator](https://github.com/navalerakesh/mcp-validation-security) · [McpVal on NuGet](https://www.nuget.org/packages/McpVal#versions-body-tab)*
