# MCP Server Compliance & Validation Report
**Generated:** 2026-04-21 09:11:57 UTC

## 1. Executive Summary

| Metric | Value |
| :--- | :--- |
| **Server Endpoint** | `https://api.githubcopilot.com/mcp/` |
| **Validation ID** | `aa6de268-e40d-49d8-a654-203789259723` |
| **Overall Status** | ❌ **Failed** |
| **Compliance Score** | **40.0%** |
| **Compliance Profile** | `Authenticated (Inferred)` |
| **Duration** | 140.12s |
| **Transport** | HTTP |
| **Session Bootstrap** | ✅ Healthy |
| **Deferred Validation** | No — validation started from a clean bootstrap state. |
| **MCP Protocol Version (Effective)** | `2025-11-25` |
| **MCP Trust Level** | 🟡 **L3: Acceptable — Compliant with known limitations** |

## 2. Connectivity & Session Bootstrap

This section explains how the validator established initial connectivity and whether validation started from a clean initialize handshake or a deferred advisory state.

| Bootstrap Signal | Value |
| :--- | :--- |
| **Bootstrap State** | ✅ Healthy |
| **Validation Proceeded Under Deferment** | No — validation started from a clean bootstrap state. |
| **Initialize Handshake** | ✅ Initialize handshake succeeded. |
| **Handshake HTTP Status** | `HTTP 200` |
| **Handshake Duration** | 1427.2 ms |
| **Negotiated Protocol** | `2025-11-25` |
| **Observed Server Version** | `github-mcp-server/remote-de5a4d08b4062af957fdf51b49d93dafef534655` |
| **Server Profile Resolution** | `Authenticated (Inferred)` |

Validation began from a clean bootstrap with no deferred connectivity risk carried into later categories.

## 3. Priority Findings

These are the highest-signal outcomes from this validation run.

- Policy balanced blocked the run: Balanced policy blocked the validation result with 1 unsuppressed signal(s).
- Notification handling violates JSON-RPC 2.0: Server MUST NOT respond to notifications
- MCP-PROTO-JSONRPC: Notification handling violates JSON-RPC 2.0: Server MUST NOT respond to notifications
- MCP-PROTO-JSONRPC: Batch processing implementation is inconsistent or incomplete

## 4. Action Hints

Compact next-step guidance derived from the highest-signal evidence in this run.

- Balanced policy blocked the validation result with 1 unsuppressed signal(s).
- Protocol: Notification handling violates JSON-RPC 2.0: Server MUST NOT respond to notifications.
- Protocol: Batch processing implementation is inconsistent or incomplete.
- Consider improving overall compliance score to meet industry standards.

## 5. MCP Trust Assessment

Multi-dimensional evaluation of server trustworthiness for AI agent consumption.
Trust level is determined by a **weighted multi-dimensional score** and then capped by confirmed blockers such as critical security failures or MCP MUST failures.

| Dimension | Score | What It Measures |
| :--- | :---: | :--- |
| **Protocol Compliance** | 69% | MCP spec adherence, JSON-RPC compliance, response structures |
| **Security Posture** | 100% | Auth compliance, injection resistance, attack surface |
| **AI Safety** | 71% | Schema quality, destructive tool detection, exfiltration risk, prompt injection surface |
| **Operational Readiness** | 0% | Latency, throughput, error rate, stability |
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
| **MUST** | 8 | 0 | 8 | ✅ Fully compliant |
| **SHOULD** | 5 | 2 | 7 | ⚠️ 2 penalties applied |
| **MAY** | 5 | 0 | 5 | ℹ️ Informational (no score impact) |

#### ⚠️ SHOULD Gaps (Score Penalties)

- SHOULD: String parameters have enum/pattern/format constraints (tools/list)
- SHOULD: tools/call result includes 'isError' field (tools/call)

#### ℹ️ Optional Features (MAY)

- ✅ MAY: Server supports logging/setLevel
- ✅ MAY: Server supports sampling/createMessage
- ✅ MAY: Server supports roots/list
- ✅ MAY: Server supports resources/templates/list
- ✅ MAY: Tools include 'annotations' (readOnlyHint, destructiveHint)

## 6. Capability Snapshot

| Probe | Discovered | HTTP Status | Duration | Result |
| :--- | :---: | :---: | :---: | :--- |
| Tools/list | 0 | HTTP 200 | 363.5 ms | ✅ Listed<br/>Call ✅ |
| Resources/list | 0 | HTTP 200 | 116.2 ms | ✅ Listed |
| Prompts/list | 2 | HTTP 200 | 170.9 ms | ✅ Listed |

- **First Tool Probed:** `add_comment_to_pending_review`

## 7. Scoring Methodology

These notes explain how the overall score and blocking decision were calibrated for this run.

- GUIDANCE: 7 protected-endpoint authentication scenarios were secure but not fully aligned with the preferred MCP/OAuth challenge flow. Score reduced by 20%.
- BLOCKER: Critical MCP/JSON-RPC requirement violation detected.

## 8. Compliance Matrix

| Category | Status | Score | Issues |
| :--- | :---: | :---: | :---: |
| Protocol Compliance | ❌ Failed | 68.8% | **4** |
| Security Assessment | ✅ Passed | 100.0% | - |
| Tool Validation | ✅ Passed | 100.0% | - |
| Resource Capabilities | ✅ Passed | 100.0% | - |
| Prompt Capabilities | ❌ Failed | 100.0% | - |
| Performance | ❌ Failed | 0.0% | - |

## 9. Performance Calibration

Performance is assessed using a synthetic load probe rather than a workload-specific SLA benchmark.
For public or remote SaaS endpoints, partial failures under synthetic pressure are reported as advisory when they look like capacity limits or edge protections rather than protocol instability.

## 10. Security Assessment

**Security Score:** 100.0%

### Authentication Analysis
| Scenario | Method | Expected | Actual | Analysis | Status |
| :--- | :--- | :--- | :--- | :--- | :---: |
| No Auth - initialize | `initialize` | 4xx (Secure Rejection) | 200 + JSON-RPC success | ⚠️ COMPATIBLE: Discovery metadata was exposed without authentication. | ✅ |
| No Auth - prompts/list | `prompts/list` | 4xx (Secure Rejection) | 200 + JSON-RPC success | ⚠️ COMPATIBLE: Discovery metadata was exposed without authentication. | ✅ |
| Malformed Token - prompts/list | `prompts/list` | 4xx (Secure Rejection) | 400 + WWW-Auth | ✅ ALIGNED: Secure OAuth-style challenge returned with HTTP 400. | ✅ |
| Invalid Token - prompts/list | `prompts/list` | 4xx (Secure Rejection) | 400 + WWW-Auth | ✅ ALIGNED: Secure OAuth-style challenge returned with HTTP 400. | ✅ |
| Token Expired - prompts/list | `prompts/list` | 4xx (Secure Rejection) | 400 + WWW-Auth | ✅ ALIGNED: Secure OAuth-style challenge returned with HTTP 400. | ✅ |
| Invalid Scope - prompts/list | `prompts/list` | 4xx (Secure Rejection) | 400 + WWW-Auth | ✅ ALIGNED: Secure OAuth-style challenge returned with HTTP 400. | ✅ |
| Insufficient Permissions - prompts/list | `prompts/list` | 4xx (Secure Rejection) | 400 + WWW-Auth | ✅ ALIGNED: Secure OAuth-style challenge returned with HTTP 400. | ✅ |
| Revoked Token - prompts/list | `prompts/list` | 4xx (Secure Rejection) | 400 + WWW-Auth | ✅ ALIGNED: Secure OAuth-style challenge returned with HTTP 400. | ✅ |
| Wrong Audience (RFC 8707) - prompts/list | `prompts/list` | 4xx (Secure Rejection) | 400 + WWW-Auth | ✅ ALIGNED: Secure OAuth-style challenge returned with HTTP 400. | ✅ |
| Valid Token - prompts/list | `prompts/list` | 200 + JSON-RPC Response | 200 + JSON-RPC success | ✅ ALIGNED: Valid authentication was accepted and request processing proceeded. | ✅ |
| No Auth - prompts/get | `prompts/get` | 4xx (Secure Rejection) | 200 + JSON-RPC error | ⚠️ COMPATIBLE: Authentication appears to be enforced at the application layer instead of via an HTTP challenge. | ✅ |
| Malformed Token - prompts/get | `prompts/get` | 4xx (Secure Rejection) | 400 + WWW-Auth | ✅ ALIGNED: Secure OAuth-style challenge returned with HTTP 400. | ✅ |
| Invalid Token - prompts/get | `prompts/get` | 4xx (Secure Rejection) | 400 + WWW-Auth | ✅ ALIGNED: Secure OAuth-style challenge returned with HTTP 400. | ✅ |
| Token Expired - prompts/get | `prompts/get` | 4xx (Secure Rejection) | 400 + WWW-Auth | ✅ ALIGNED: Secure OAuth-style challenge returned with HTTP 400. | ✅ |
| Invalid Scope - prompts/get | `prompts/get` | 4xx (Secure Rejection) | 400 + WWW-Auth | ✅ ALIGNED: Secure OAuth-style challenge returned with HTTP 400. | ✅ |
| Insufficient Permissions - prompts/get | `prompts/get` | 4xx (Secure Rejection) | 400 + WWW-Auth | ✅ ALIGNED: Secure OAuth-style challenge returned with HTTP 400. | ✅ |
| Revoked Token - prompts/get | `prompts/get` | 4xx (Secure Rejection) | 400 + WWW-Auth | ✅ ALIGNED: Secure OAuth-style challenge returned with HTTP 400. | ✅ |
| Wrong Audience (RFC 8707) - prompts/get | `prompts/get` | 4xx (Secure Rejection) | 400 + WWW-Auth | ✅ ALIGNED: Secure OAuth-style challenge returned with HTTP 400. | ✅ |
| Valid Token - prompts/get | `prompts/get` | 200 + JSON-RPC Response | 200 + JSON-RPC error | ✅ ALIGNED: Valid authentication was accepted and request processing proceeded. | ✅ |
| No Auth - resources/list | `resources/list` | 4xx (Secure Rejection) | 200 + JSON-RPC success | ⚠️ COMPATIBLE: Discovery metadata was exposed without authentication. | ✅ |
| Malformed Token - resources/list | `resources/list` | 4xx (Secure Rejection) | 400 + WWW-Auth | ✅ ALIGNED: Secure OAuth-style challenge returned with HTTP 400. | ✅ |
| Invalid Token - resources/list | `resources/list` | 4xx (Secure Rejection) | 400 + WWW-Auth | ✅ ALIGNED: Secure OAuth-style challenge returned with HTTP 400. | ✅ |
| Token Expired - resources/list | `resources/list` | 4xx (Secure Rejection) | 400 + WWW-Auth | ✅ ALIGNED: Secure OAuth-style challenge returned with HTTP 400. | ✅ |
| Invalid Scope - resources/list | `resources/list` | 4xx (Secure Rejection) | 400 + WWW-Auth | ✅ ALIGNED: Secure OAuth-style challenge returned with HTTP 400. | ✅ |
| Insufficient Permissions - resources/list | `resources/list` | 4xx (Secure Rejection) | 400 + WWW-Auth | ✅ ALIGNED: Secure OAuth-style challenge returned with HTTP 400. | ✅ |
| Revoked Token - resources/list | `resources/list` | 4xx (Secure Rejection) | 400 + WWW-Auth | ✅ ALIGNED: Secure OAuth-style challenge returned with HTTP 400. | ✅ |
| Wrong Audience (RFC 8707) - resources/list | `resources/list` | 4xx (Secure Rejection) | 400 + WWW-Auth | ✅ ALIGNED: Secure OAuth-style challenge returned with HTTP 400. | ✅ |
| Valid Token - resources/list | `resources/list` | 200 + JSON-RPC Response | 200 + JSON-RPC success | ✅ ALIGNED: Valid authentication was accepted and request processing proceeded. | ✅ |
| No Auth - resources/read | `resources/read` | 4xx (Secure Rejection) | 200 + JSON-RPC error | ⚠️ COMPATIBLE: Authentication appears to be enforced at the application layer instead of via an HTTP challenge. | ✅ |
| Malformed Token - resources/read | `resources/read` | 4xx (Secure Rejection) | 400 + WWW-Auth | ✅ ALIGNED: Secure OAuth-style challenge returned with HTTP 400. | ✅ |
| Invalid Token - resources/read | `resources/read` | 4xx (Secure Rejection) | 400 + WWW-Auth | ✅ ALIGNED: Secure OAuth-style challenge returned with HTTP 400. | ✅ |
| Token Expired - resources/read | `resources/read` | 4xx (Secure Rejection) | 400 + WWW-Auth | ✅ ALIGNED: Secure OAuth-style challenge returned with HTTP 400. | ✅ |
| Invalid Scope - resources/read | `resources/read` | 4xx (Secure Rejection) | 400 + WWW-Auth | ✅ ALIGNED: Secure OAuth-style challenge returned with HTTP 400. | ✅ |
| Insufficient Permissions - resources/read | `resources/read` | 4xx (Secure Rejection) | 400 + WWW-Auth | ✅ ALIGNED: Secure OAuth-style challenge returned with HTTP 400. | ✅ |
| Revoked Token - resources/read | `resources/read` | 4xx (Secure Rejection) | 400 + WWW-Auth | ✅ ALIGNED: Secure OAuth-style challenge returned with HTTP 400. | ✅ |
| Wrong Audience (RFC 8707) - resources/read | `resources/read` | 4xx (Secure Rejection) | 400 + WWW-Auth | ✅ ALIGNED: Secure OAuth-style challenge returned with HTTP 400. | ✅ |
| Valid Token - resources/read | `resources/read` | 200 + JSON-RPC Response | 200 + JSON-RPC error | ✅ ALIGNED: Valid authentication was accepted and request processing proceeded. | ✅ |
| No Auth - tools/list | `tools/list` | 4xx (Secure Rejection) | 200 + JSON-RPC success | ⚠️ COMPATIBLE: Discovery metadata was exposed without authentication. | ✅ |
| Malformed Token - tools/list | `tools/list` | 4xx (Secure Rejection) | 400 + WWW-Auth | ✅ ALIGNED: Secure OAuth-style challenge returned with HTTP 400. | ✅ |
| Invalid Token - tools/list | `tools/list` | 4xx (Secure Rejection) | 400 + WWW-Auth | ✅ ALIGNED: Secure OAuth-style challenge returned with HTTP 400. | ✅ |
| Token Expired - tools/list | `tools/list` | 4xx (Secure Rejection) | 400 + WWW-Auth | ✅ ALIGNED: Secure OAuth-style challenge returned with HTTP 400. | ✅ |
| Invalid Scope - tools/list | `tools/list` | 4xx (Secure Rejection) | 400 + WWW-Auth | ✅ ALIGNED: Secure OAuth-style challenge returned with HTTP 400. | ✅ |
| Insufficient Permissions - tools/list | `tools/list` | 4xx (Secure Rejection) | 400 + WWW-Auth | ✅ ALIGNED: Secure OAuth-style challenge returned with HTTP 400. | ✅ |
| Revoked Token - tools/list | `tools/list` | 4xx (Secure Rejection) | 400 + WWW-Auth | ✅ ALIGNED: Secure OAuth-style challenge returned with HTTP 400. | ✅ |
| Wrong Audience (RFC 8707) - tools/list | `tools/list` | 4xx (Secure Rejection) | 400 + WWW-Auth | ✅ ALIGNED: Secure OAuth-style challenge returned with HTTP 400. | ✅ |
| Valid Token - tools/list | `tools/list` | 200 + JSON-RPC Response | 200 + JSON-RPC success | ✅ ALIGNED: Valid authentication was accepted and request processing proceeded. | ✅ |
| No Auth - tools/call | `tools/call` | 4xx (Secure Rejection) | 200 + JSON-RPC error | ⚠️ COMPATIBLE: Authentication appears to be enforced at the application layer instead of via an HTTP challenge. | ✅ |
| Malformed Token - tools/call | `tools/call` | 4xx (Secure Rejection) | 400 + WWW-Auth | ✅ ALIGNED: Secure OAuth-style challenge returned with HTTP 400. | ✅ |
| Invalid Token - tools/call | `tools/call` | 4xx (Secure Rejection) | 400 + WWW-Auth | ✅ ALIGNED: Secure OAuth-style challenge returned with HTTP 400. | ✅ |
| Token Expired - tools/call | `tools/call` | 4xx (Secure Rejection) | 400 + WWW-Auth | ✅ ALIGNED: Secure OAuth-style challenge returned with HTTP 400. | ✅ |
| Invalid Scope - tools/call | `tools/call` | 4xx (Secure Rejection) | 400 + WWW-Auth | ✅ ALIGNED: Secure OAuth-style challenge returned with HTTP 400. | ✅ |
| Insufficient Permissions - tools/call | `tools/call` | 4xx (Secure Rejection) | 400 + WWW-Auth | ✅ ALIGNED: Secure OAuth-style challenge returned with HTTP 400. | ✅ |
| Revoked Token - tools/call | `tools/call` | 4xx (Secure Rejection) | 400 + WWW-Auth | ✅ ALIGNED: Secure OAuth-style challenge returned with HTTP 400. | ✅ |
| Wrong Audience (RFC 8707) - tools/call | `tools/call` | 4xx (Secure Rejection) | 400 + WWW-Auth | ✅ ALIGNED: Secure OAuth-style challenge returned with HTTP 400. | ✅ |
| Valid Token - tools/call | `tools/call` | 200 + JSON-RPC Response | 200 + JSON-RPC error | ✅ ALIGNED: Valid authentication was accepted and request processing proceeded. | ✅ |

### Adversarial Input Handling

> **Legend:**
> * **REFLECTED / UNSAFE ECHO**: The server reflected the payload back without sanitization. No execution confirmed.
> * **BLOCKED**: The server correctly rejected or sanitized the input.
> * **SKIPPED**: Test could not be executed (e.g., no tools discovered for injection target).

| Attack Vector | Description | Result | Analysis |
| :--- | :--- | :---: | :--- |
| INJ-001 (Input Validation) | Simulated INJ-001 (Input Validation) attack on add_comment_to_pending_review | 🛡️ BLOCKED | `{"jsonrpc":"2.0","id":"38f1ef35-a394-44cd-bc81-140c9375af12","result":{"content":[{"type":"text","text":"failed to get latest review for current user: Could not resolve to a Repository with the name '/'."}],"isError":true}}` |
| INJ-002 (Input Validation) | Simulated INJ-002 (Input Validation) attack on add_comment_to_pending_review | 🛡️ BLOCKED | `{"jsonrpc":"2.0","id":"ba245333-860d-4ed2-bd97-694f098d1209","result":{"content":[{"type":"text","text":"failed to get latest review for current user: Could not resolve to a Repository with the name '/'."}],"isError":true}}` |
| INJ-003 (Input Validation) | Simulated INJ-003 (Input Validation) attack on add_comment_to_pending_review | 🛡️ BLOCKED | `{"jsonrpc":"2.0","id":"e7600564-fe2d-48c6-be39-a1a96159e0d5","result":{"content":[{"type":"text","text":"failed to get latest review for current user: Could not resolve to a Repository with the name '/'."}],"isError":true}}` |
| MCP-SEC-001 | JSON-RPC Error Smuggling | 🛡️ BLOCKED | `Server handled malformed requests gracefully with standard errors.` |
| MCP-SEC-002 | Metadata Enumeration | 🛡️ BLOCKED | `Consistent error responses observed.` |
| MCP-SEC-003 | Schema Confusion | 🛡️ BLOCKED | `Server correctly rejected invalid schema.` |
| MCP-AI-001 | Hallucination Fuzzing | 🛡️ BLOCKED | `Error clarity: 100/100 (Good). Server error messages help AI self-correct.` |

## 11. Protocol Compliance

**Status Detail:** declared capabilities: completions, prompts, resources, tools | roots/list: supported | logging/setLevel: supported | sampling/createMessage: supported | completion/complete: supported

### Compliance Violations
| ID | Source | Description | Severity |
| :--- | :--- | :--- | :---: |
| MCP-PROTO-ERR | `spec` | JSON-RPC Error Code Violation: Method Not Found | Low |
| MCP-PROTO-ERR | `spec` | JSON-RPC Error Code Violation: Invalid Params | Low |
| MCP-PROTO-JSONRPC | `spec` | Batch processing implementation is inconsistent or incomplete | Medium |
| MCP-PROTO-JSONRPC | `spec` | Notification handling violates JSON-RPC 2.0: Server MUST NOT respond to notifications | Critical |

## 12. Tool Validation

**Tools Discovered:** 41

### Tool: `tools/list (Schema Compliance)`

**Status:** ✅ Passed
**Execution Time:** 363.54ms
---

### Tool: `add_comment_to_pending_review`

**Status:** ✅ Passed
**Execution Time:** 792.66ms

#### Tool Metadata
| Property | Value |
| :--- | :--- |
| Display Title | Add review comment to the requester's latest pending pull request review |

---

### Tool: `add_issue_comment`

**Status:** ✅ Passed
**Execution Time:** 272.18ms

#### Tool Metadata
| Property | Value |
| :--- | :--- |
| Display Title | Add comment to issue |

---

### Tool: `add_reply_to_pull_request_comment`

**Status:** ✅ Passed
**Execution Time:** 273.65ms

#### Tool Metadata
| Property | Value |
| :--- | :--- |
| Display Title | Add reply to pull request comment |

---

### Tool: `create_branch`

**Status:** ✅ Passed
**Execution Time:** 362.14ms

#### Tool Metadata
| Property | Value |
| :--- | :--- |
| Display Title | Create branch |

---

### Tool: `create_or_update_file`

**Status:** ✅ Passed
**Execution Time:** 369.09ms

#### Tool Metadata
| Property | Value |
| :--- | :--- |
| Display Title | Create or update file |

---

### Tool: `create_pull_request`

**Status:** ✅ Passed
**Execution Time:** 279.64ms

#### Tool Metadata
| Property | Value |
| :--- | :--- |
| Display Title | Open new pull request |

---

### Tool: `create_repository`

**Status:** ✅ Passed
**Execution Time:** 215.47ms

#### Tool Metadata
| Property | Value |
| :--- | :--- |
| Display Title | Create repository |

---

### Tool: `delete_file`

**Status:** ✅ Passed
**Execution Time:** 448.61ms

#### Tool Metadata
| Property | Value |
| :--- | :--- |
| Display Title | Delete file |
| destructiveHint | True |

---

### Tool: `fork_repository`

**Status:** ✅ Passed
**Execution Time:** 297.81ms

#### Tool Metadata
| Property | Value |
| :--- | :--- |
| Display Title | Fork repository |

---

### Tool: `get_commit`

**Status:** ✅ Passed
**Execution Time:** 318.03ms

#### Tool Metadata
| Property | Value |
| :--- | :--- |
| Display Title | Get commit details |
| readOnlyHint | True |

---

### Tool: `get_file_contents`

**Status:** ✅ Passed
**Execution Time:** 399.18ms

#### Tool Metadata
| Property | Value |
| :--- | :--- |
| Display Title | Get file or directory contents |
| readOnlyHint | True |

---

### Tool: `get_label`

**Status:** ✅ Passed
**Execution Time:** 285.20ms

#### Tool Metadata
| Property | Value |
| :--- | :--- |
| Display Title | Get a specific label from a repository. |
| readOnlyHint | True |

---

### Tool: `get_latest_release`

**Status:** ✅ Passed
**Execution Time:** 297.27ms

#### Tool Metadata
| Property | Value |
| :--- | :--- |
| Display Title | Get latest release |
| readOnlyHint | True |

---

### Tool: `get_me`

**Status:** ✅ Passed
**Execution Time:** 304.39ms

#### Tool Metadata
| Property | Value |
| :--- | :--- |
| Display Title | Get my user profile |
| readOnlyHint | True |

---

### Tool: `get_release_by_tag`

**Status:** ✅ Passed
**Execution Time:** 345.46ms

#### Tool Metadata
| Property | Value |
| :--- | :--- |
| Display Title | Get a release by tag name |
| readOnlyHint | True |

---

### Tool: `get_tag`

**Status:** ✅ Passed
**Execution Time:** 319.69ms

#### Tool Metadata
| Property | Value |
| :--- | :--- |
| Display Title | Get tag details |
| readOnlyHint | True |

---

### Tool: `get_team_members`

**Status:** ✅ Passed
**Execution Time:** 255.51ms

#### Tool Metadata
| Property | Value |
| :--- | :--- |
| Display Title | Get team members |
| readOnlyHint | True |

---

### Tool: `get_teams`

**Status:** ✅ Passed
**Execution Time:** 241.87ms

#### Tool Metadata
| Property | Value |
| :--- | :--- |
| Display Title | Get teams |
| readOnlyHint | True |

---

### Tool: `issue_read`

**Status:** ✅ Passed
**Execution Time:** 201.88ms

#### Tool Metadata
| Property | Value |
| :--- | :--- |
| Display Title | Get issue details |
| readOnlyHint | True |

---

### Tool: `issue_write`

**Status:** ✅ Passed
**Execution Time:** 229.80ms

#### Tool Metadata
| Property | Value |
| :--- | :--- |
| Display Title | Create or update issue. |

---

### Tool: `list_branches`

**Status:** ✅ Passed
**Execution Time:** 369.10ms

#### Tool Metadata
| Property | Value |
| :--- | :--- |
| Display Title | List branches |
| readOnlyHint | True |

---

### Tool: `list_commits`

**Status:** ✅ Passed
**Execution Time:** 204.12ms

#### Tool Metadata
| Property | Value |
| :--- | :--- |
| Display Title | List commits |
| readOnlyHint | True |

---

### Tool: `list_issue_types`

**Status:** ✅ Passed
**Execution Time:** 275.68ms

#### Tool Metadata
| Property | Value |
| :--- | :--- |
| Display Title | List available issue types |
| readOnlyHint | True |

---

### Tool: `list_issues`

**Status:** ✅ Passed
**Execution Time:** 214.72ms

#### Tool Metadata
| Property | Value |
| :--- | :--- |
| Display Title | List issues |
| readOnlyHint | True |

---

### Tool: `list_pull_requests`

**Status:** ✅ Passed
**Execution Time:** 325.92ms

#### Tool Metadata
| Property | Value |
| :--- | :--- |
| Display Title | List pull requests |
| readOnlyHint | True |

---

### Tool: `list_releases`

**Status:** ✅ Passed
**Execution Time:** 335.74ms

#### Tool Metadata
| Property | Value |
| :--- | :--- |
| Display Title | List releases |
| readOnlyHint | True |

---

### Tool: `list_tags`

**Status:** ✅ Passed
**Execution Time:** 333.67ms

#### Tool Metadata
| Property | Value |
| :--- | :--- |
| Display Title | List tags |
| readOnlyHint | True |

---

### Tool: `merge_pull_request`

**Status:** ✅ Passed
**Execution Time:** 280.33ms

#### Tool Metadata
| Property | Value |
| :--- | :--- |
| Display Title | Merge pull request |

---

### Tool: `pull_request_read`

**Status:** ✅ Passed
**Execution Time:** 225.86ms

#### Tool Metadata
| Property | Value |
| :--- | :--- |
| Display Title | Get details for a single pull request |
| readOnlyHint | True |

---

### Tool: `pull_request_review_write`

**Status:** ✅ Passed
**Execution Time:** 213.71ms

#### Tool Metadata
| Property | Value |
| :--- | :--- |
| Display Title | Write operations (create, submit, delete) on pull request reviews. |

---

### Tool: `push_files`

**Status:** ✅ Passed
**Execution Time:** 403.44ms

#### Tool Metadata
| Property | Value |
| :--- | :--- |
| Display Title | Push files to repository |

---

### Tool: `request_copilot_review`

**Status:** ✅ Passed
**Execution Time:** 299.18ms

#### Tool Metadata
| Property | Value |
| :--- | :--- |
| Display Title | Request Copilot review |

---

### Tool: `run_secret_scanning`

**Status:** ✅ Passed
**Execution Time:** 119.00ms

#### Tool Metadata
| Property | Value |
| :--- | :--- |
| Display Title | Run Secret Scanning |
| readOnlyHint | True |
| openWorldHint | False |

---

### Tool: `search_code`

**Status:** ✅ Passed
**Execution Time:** 4369.93ms

#### Tool Metadata
| Property | Value |
| :--- | :--- |
| Display Title | Search code |
| readOnlyHint | True |

---

### Tool: `search_issues`

**Status:** ✅ Passed
**Execution Time:** 382.88ms

#### Tool Metadata
| Property | Value |
| :--- | :--- |
| Display Title | Search issues |
| readOnlyHint | True |

---

### Tool: `search_pull_requests`

**Status:** ✅ Passed
**Execution Time:** 397.58ms

#### Tool Metadata
| Property | Value |
| :--- | :--- |
| Display Title | Search pull requests |
| readOnlyHint | True |

---

### Tool: `search_repositories`

**Status:** ✅ Passed
**Execution Time:** 397.97ms

#### Tool Metadata
| Property | Value |
| :--- | :--- |
| Display Title | Search repositories |
| readOnlyHint | True |

---

### Tool: `search_users`

**Status:** ✅ Passed
**Execution Time:** 305.60ms

#### Tool Metadata
| Property | Value |
| :--- | :--- |
| Display Title | Search users |
| readOnlyHint | True |

---

### Tool: `sub_issue_write`

**Status:** ✅ Passed
**Execution Time:** 236.93ms

#### Tool Metadata
| Property | Value |
| :--- | :--- |
| Display Title | Change sub-issue |

---

### Tool: `update_pull_request`

**Status:** ✅ Passed
**Execution Time:** 284.95ms

#### Tool Metadata
| Property | Value |
| :--- | :--- |
| Display Title | Edit pull request |

---

### Tool: `update_pull_request_branch`

**Status:** ✅ Passed
**Execution Time:** 297.13ms

#### Tool Metadata
| Property | Value |
| :--- | :--- |
| Display Title | Update pull request branch |

---

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

## 13. Resource Capabilities

**Resources Discovered:** 0

## 14. AI Readiness Assessment

**AI Readiness Score:** ✅ **86/100** (Good)

This score measures how well the server's tool schemas are optimized for consumption by AI agents (LLMs).

| Criterion | What It Measures |
| :--- | :--- |
| **Description Completeness** | Do all tool parameters have descriptions? Missing descriptions increase hallucination risk. |
| **Type Specificity** | Are `string` parameters constrained with `enum`, `pattern`, or `format`? Vague types cause incorrect arguments. |
| **Token Efficiency** | Is the `tools/list` payload within context window limits? Estimated: ~25,152 tokens. |

### Findings

| Rule ID | Source | Coverage | Severity | Finding |
| :--- | :--- | :--- | :---: | :--- |
| `AI.TOOL.SCHEMA.STRING_CONSTRAINT_MISSING` | `heuristic` | 40/41 (98%) | 🟡 Medium | Tool 'add_comment_to_pending_review': 4/10 string parameters have no enum/pattern/format constraint |
| `AI.TOOL.SCHEMA.REQUIRED_ARRAY_SHAPE_MISSING` | `heuristic` | 1/41 (2%) | 🟡 Medium | Tool 'push_files': 1/5 required array parameters lack item schemas or minItems guidance |
| `AI.TOOL.SCHEMA.FORMAT_HINT_MISSING` | `heuristic` | 5/41 (12%) | 🔵 Low | Tool 'create_or_update_file': 2/7 string parameters look like structured values but do not declare format/pattern hints |
| `AI.TOOL.SCHEMA.ENUM_COVERAGE_MISSING` | `heuristic` | 4/41 (10%) | 🔵 Low | Tool 'issue_write': 1/13 string parameters look like fixed-choice fields but do not declare enum/const choices |
| `AI.TOOL.SCHEMA.TOKEN_BUDGET_WARNING` | `heuristic` | 1/41 (2%) | 🔵 Low | ℹ️ tools/list response is ~25,152 tokens — consider reducing descriptions for token efficiency. |

## 15. Optional MCP Capabilities

These probes check whether the server supports optional MCP features beyond the core primitives.

| Capability | Status |
| :--- | :--- |
| `declared capabilities` | ➖ completions, prompts, resources, tools |
| `roots/list` | ✅ supported |
| `logging/setLevel` | ✅ supported |
| `sampling/createMessage` | ✅ supported |
| `completion/complete` | ✅ supported |

## 16. Performance Metrics

**Status:** ❌ Failed
**Measurements:** unavailable
**Reason:** Operation timed out or was cancelled

**Critical Errors:**
- Operation timed out or was cancelled

## 17. Recommendations

- 💡 Consider improving overall compliance score to meet industry standards
- 💡 Review and address failed test cases to improve server stability
- 💡 Ensure full compliance with MCP protocol specification

---
*Report generated by mcpval — MCP Validator*
