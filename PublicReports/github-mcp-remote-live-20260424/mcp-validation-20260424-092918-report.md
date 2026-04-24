# MCP Server Compliance & Validation Report
**Generated:** 2026-04-24 09:29:18 UTC

## 1. Executive Summary

| Metric | Value |
| :--- | :--- |
| **Server Endpoint** | `https://api.githubcopilot.com/mcp/` |
| **Validation ID** | `90a8d671-4478-4fdf-9c04-c5e3e86fc129` |
| **Overall Status** | ✅ **Passed** |
| **Compliance Score** | **73.4%** |
| **Compliance Profile** | `Authenticated (Inferred)` |
| **Duration** | 132.53s |
| **Transport** | HTTP |
| **Session Bootstrap** | ✅ Healthy |
| **Deferred Validation** | No — validation started from a clean bootstrap state. |
| **MCP Protocol Version (Effective)** | `2025-11-25` |
| **MCP Trust Level** | 🔵 **L4: Trusted — Meets enterprise AI safety requirements** |

## 2. Connectivity & Session Bootstrap

This section explains how the validator established initial connectivity and whether validation started from a clean initialize handshake or a deferred advisory state.

| Bootstrap Signal | Value |
| :--- | :--- |
| **Bootstrap State** | ✅ Healthy |
| **Validation Proceeded Under Deferment** | No — validation started from a clean bootstrap state. |
| **Initialize Handshake** | ✅ Initialize handshake succeeded. |
| **Handshake HTTP Status** | `HTTP 200` |
| **Handshake Duration** | 1531.4 ms |
| **Negotiated Protocol** | `2025-11-25` |
| **Observed Server Version** | `github-mcp-server/remote-db56d310138fc1df6a65d41403eb53a9e32f48bc` |
| **Server Profile Resolution** | `Authenticated (Inferred)` |

Validation began from a clean bootstrap with no deferred connectivity risk carried into later categories.

## 3. Priority Findings

These are the highest-signal outcomes from this validation run.

- MCP-PROTO-JSONRPC: Batch processing implementation is inconsistent or incomplete
- MCP-PROTO-ERR: JSON-RPC Error Code Violation: Method Not Found

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
| **Protocol Compliance** | 81% | MCP spec adherence, JSON-RPC compliance, response structures |
| **Security Posture** | 100% | Auth compliance, injection resistance, attack surface |
| **AI Safety** | 71% | Schema quality, destructive tool detection, exfiltration risk, prompt injection surface |
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
| **MUST** | 11 | 0 | 11 | ✅ Fully compliant |
| **SHOULD** | 3 | 2 | 5 | ⚠️ 2 penalties applied |
| **MAY** | 6 | 0 | 6 | ℹ️ Informational (no score impact) |

#### ⚠️ SHOULD Gaps (Score Penalties)

- SHOULD: String parameters have enum/pattern/format constraints (tools/list)
- SHOULD: tools/call result includes 'isError' field (tools/call)

#### ℹ️ Optional Features (MAY)

- ✅ MAY: Server supports logging/setLevel
- ✅ MAY: Server supports sampling/createMessage
- ✅ MAY: Server supports roots/list
- ✅ MAY: Server supports completion/complete
- ✅ MAY: Server supports resources/templates/list
- ✅ MAY: Tools include 'annotations' (readOnlyHint, destructiveHint)

## 6. Client Profile Compatibility

Documented host-side compatibility assessments derived from the neutral validation evidence collected during this run.

| Client Profile | Status | Requirements | Documentation |
| :--- | :--- | :--- | :--- |
| **Claude Code** | ⚠️ Compatible with warnings | 3 passed / 3 warnings / 0 failed | <https://docs.anthropic.com/en/docs/claude-code/mcp> |
| **VS Code Copilot Agent** | ⚠️ Compatible with warnings | 3 passed / 3 warnings / 0 failed | <https://code.visualstudio.com/docs/copilot/chat/mcp-servers> |
| **GitHub Copilot CLI** | ⚠️ Compatible with warnings | 2 passed / 2 warnings / 0 failed | <https://docs.github.com/en/copilot/how-tos/copilot-cli/customize-copilot/add-mcp-servers> |
| **GitHub Copilot Cloud Agent** | ⚠️ Compatible with warnings | 2 passed / 3 warnings / 0 failed | <https://docs.github.com/en/copilot/how-tos/use-copilot-agents/cloud-agent/extend-cloud-agent-with-mcp> |
| **Visual Studio Copilot** | ⚠️ Compatible with warnings | 3 passed / 3 warnings / 0 failed | <https://docs.github.com/en/copilot/how-tos/provide-context/use-mcp/extend-copilot-chat-with-mcp?tool=visualstudio> |

### Claude Code

**Status:** ⚠️ Compatible with warnings

Required compatibility checks passed; 3 advisory requirements still need follow-up.

| Requirement | Level | Outcome | Rule IDs | Affected Components | Details |
| :--- | :--- | :--- | :--- | :--- | :--- |
| Tool presentation and approval metadata is complete | Recommended | ⚠️ Warning | `MCP.GUIDELINE.TOOL.READONLY_HINT_MISSING`, `MCP.GUIDELINE.TOOL.DESTRUCTIVE_HINT_MISSING`, `MCP.GUIDELINE.TOOL.OPENWORLD_HINT_MISSING` | `add_comment_to_pending_review`, `add_issue_comment`, `add_reply_to_pull_request_comment` | Advisory tool guidance gaps affect 41/41 tool(s). |
| Tool schemas are clear enough for agent planning | Recommended | ⚠️ Warning | `AI.TOOL.SCHEMA.STRING_CONSTRAINT_MISSING`, `AI.TOOL.SCHEMA.FORMAT_HINT_MISSING`, `AI.TOOL.SCHEMA.ENUM_COVERAGE_MISSING` | `add_comment_to_pending_review`, `add_issue_comment`, `add_reply_to_pull_request_comment` | Advisory tool guidance gaps affect 41/41 tool(s). |
| Prompt metadata is descriptive enough for guided use | Recommended | ⚠️ Warning | `AI.PROMPT.ARGUMENTS.COMPLEXITY_GUIDANCE_MISSING` | `issue_to_fix_workflow` | Advisory prompt guidance gaps affect 1/2 prompt(s). |

#### Remediation

- ⚠️ **Tool presentation and approval metadata is complete:** Declare readOnlyHint so agents can distinguish read-only tools from state-changing tools.
- ⚠️ **Tool schemas are clear enough for agent planning:** Constrain string parameters with enum, pattern, or format metadata when possible.
- ⚠️ **Prompt metadata is descriptive enough for guided use:** Mention when a prompt expects multiple required inputs so callers can prepare the full argument set before execution.

### VS Code Copilot Agent

**Status:** ⚠️ Compatible with warnings

Required compatibility checks passed; 3 advisory requirements still need follow-up.

| Requirement | Level | Outcome | Rule IDs | Affected Components | Details |
| :--- | :--- | :--- | :--- | :--- | :--- |
| Tool presentation and approval metadata is complete | Recommended | ⚠️ Warning | `MCP.GUIDELINE.TOOL.READONLY_HINT_MISSING`, `MCP.GUIDELINE.TOOL.DESTRUCTIVE_HINT_MISSING`, `MCP.GUIDELINE.TOOL.OPENWORLD_HINT_MISSING` | `add_comment_to_pending_review`, `add_issue_comment`, `add_reply_to_pull_request_comment` | Advisory tool guidance gaps affect 41/41 tool(s). |
| Tool schemas are clear enough for agent planning | Recommended | ⚠️ Warning | `AI.TOOL.SCHEMA.STRING_CONSTRAINT_MISSING`, `AI.TOOL.SCHEMA.FORMAT_HINT_MISSING`, `AI.TOOL.SCHEMA.ENUM_COVERAGE_MISSING` | `add_comment_to_pending_review`, `add_issue_comment`, `add_reply_to_pull_request_comment` | Advisory tool guidance gaps affect 41/41 tool(s). |
| Prompt metadata is descriptive enough for guided use | Recommended | ⚠️ Warning | `AI.PROMPT.ARGUMENTS.COMPLEXITY_GUIDANCE_MISSING` | `issue_to_fix_workflow` | Advisory prompt guidance gaps affect 1/2 prompt(s). |

#### Remediation

- ⚠️ **Tool presentation and approval metadata is complete:** Declare readOnlyHint so agents can distinguish read-only tools from state-changing tools.
- ⚠️ **Tool schemas are clear enough for agent planning:** Constrain string parameters with enum, pattern, or format metadata when possible.
- ⚠️ **Prompt metadata is descriptive enough for guided use:** Mention when a prompt expects multiple required inputs so callers can prepare the full argument set before execution.

### GitHub Copilot CLI

**Status:** ⚠️ Compatible with warnings

Required compatibility checks passed; 2 advisory requirements still need follow-up.

| Requirement | Level | Outcome | Rule IDs | Affected Components | Details |
| :--- | :--- | :--- | :--- | :--- | :--- |
| Tool presentation and approval metadata is complete | Recommended | ⚠️ Warning | `MCP.GUIDELINE.TOOL.READONLY_HINT_MISSING`, `MCP.GUIDELINE.TOOL.DESTRUCTIVE_HINT_MISSING`, `MCP.GUIDELINE.TOOL.OPENWORLD_HINT_MISSING` | `add_comment_to_pending_review`, `add_issue_comment`, `add_reply_to_pull_request_comment` | Advisory tool guidance gaps affect 41/41 tool(s). |
| Tool schemas are clear enough for agent planning | Recommended | ⚠️ Warning | `AI.TOOL.SCHEMA.STRING_CONSTRAINT_MISSING`, `AI.TOOL.SCHEMA.FORMAT_HINT_MISSING`, `AI.TOOL.SCHEMA.ENUM_COVERAGE_MISSING` | `add_comment_to_pending_review`, `add_issue_comment`, `add_reply_to_pull_request_comment` | Advisory tool guidance gaps affect 41/41 tool(s). |

#### Remediation

- ⚠️ **Tool presentation and approval metadata is complete:** Declare readOnlyHint so agents can distinguish read-only tools from state-changing tools.
- ⚠️ **Tool schemas are clear enough for agent planning:** Constrain string parameters with enum, pattern, or format metadata when possible.

### GitHub Copilot Cloud Agent

**Status:** ⚠️ Compatible with warnings

Required compatibility checks passed; 3 advisory requirements still need follow-up.

| Requirement | Level | Outcome | Rule IDs | Affected Components | Details |
| :--- | :--- | :--- | :--- | :--- | :--- |
| Only the tool surface is currently consumed | Recommended | ⚠️ Warning | - | - | This profile currently consumes tools only; 2 prompt(s) will not contribute to compatibility. |
| Tool presentation and approval metadata is complete | Recommended | ⚠️ Warning | `MCP.GUIDELINE.TOOL.READONLY_HINT_MISSING`, `MCP.GUIDELINE.TOOL.DESTRUCTIVE_HINT_MISSING`, `MCP.GUIDELINE.TOOL.OPENWORLD_HINT_MISSING` | `add_comment_to_pending_review`, `add_issue_comment`, `add_reply_to_pull_request_comment` | Advisory tool guidance gaps affect 41/41 tool(s). |
| Tool schemas are clear enough for agent planning | Recommended | ⚠️ Warning | `AI.TOOL.SCHEMA.STRING_CONSTRAINT_MISSING`, `AI.TOOL.SCHEMA.FORMAT_HINT_MISSING`, `AI.TOOL.SCHEMA.ENUM_COVERAGE_MISSING` | `add_comment_to_pending_review`, `add_issue_comment`, `add_reply_to_pull_request_comment` | Advisory tool guidance gaps affect 41/41 tool(s). |

#### Remediation

- ⚠️ **Tool presentation and approval metadata is complete:** Declare readOnlyHint so agents can distinguish read-only tools from state-changing tools.
- ⚠️ **Tool schemas are clear enough for agent planning:** Constrain string parameters with enum, pattern, or format metadata when possible.

### Visual Studio Copilot

**Status:** ⚠️ Compatible with warnings

Required compatibility checks passed; 3 advisory requirements still need follow-up.

| Requirement | Level | Outcome | Rule IDs | Affected Components | Details |
| :--- | :--- | :--- | :--- | :--- | :--- |
| Tool presentation and approval metadata is complete | Recommended | ⚠️ Warning | `MCP.GUIDELINE.TOOL.READONLY_HINT_MISSING`, `MCP.GUIDELINE.TOOL.DESTRUCTIVE_HINT_MISSING`, `MCP.GUIDELINE.TOOL.OPENWORLD_HINT_MISSING` | `add_comment_to_pending_review`, `add_issue_comment`, `add_reply_to_pull_request_comment` | Advisory tool guidance gaps affect 41/41 tool(s). |
| Tool schemas are clear enough for agent planning | Recommended | ⚠️ Warning | `AI.TOOL.SCHEMA.STRING_CONSTRAINT_MISSING`, `AI.TOOL.SCHEMA.FORMAT_HINT_MISSING`, `AI.TOOL.SCHEMA.ENUM_COVERAGE_MISSING` | `add_comment_to_pending_review`, `add_issue_comment`, `add_reply_to_pull_request_comment` | Advisory tool guidance gaps affect 41/41 tool(s). |
| Prompt metadata is descriptive enough for guided use | Recommended | ⚠️ Warning | `AI.PROMPT.ARGUMENTS.COMPLEXITY_GUIDANCE_MISSING` | `issue_to_fix_workflow` | Advisory prompt guidance gaps affect 1/2 prompt(s). |

#### Remediation

- ⚠️ **Tool presentation and approval metadata is complete:** Declare readOnlyHint so agents can distinguish read-only tools from state-changing tools.
- ⚠️ **Tool schemas are clear enough for agent planning:** Constrain string parameters with enum, pattern, or format metadata when possible.
- ⚠️ **Prompt metadata is descriptive enough for guided use:** Mention when a prompt expects multiple required inputs so callers can prepare the full argument set before execution.

## 7. Validation Envelope

This section exposes the structured validation envelope used for layered reporting, coverage tracking, and pack provenance.

### Assessment Layers

| Layer | Status | Findings | Summary |
| :--- | :--- | :---: | :--- |
| `protocol-core` | ✅ Passed | 5 | declared capabilities: completions, prompts, resources, tools \| roots/list: supported \| logging/setLevel: supported \| sampling/createMessage: supported \| completion/complete: supported |
| `tool-surface` | ✅ Passed | 190 | - |
| `resource-surface` | ✅ Passed | 0 | - |
| `prompt-surface` | ✅ Passed | 1 | - |
| `security-boundaries` | ✅ Passed | 7 | - |
| `performance` | ✅ Passed | 1 | - |
| `error-handling` | ❌ Failed | 1 | Validated 5 error scenario(s); 4 handled correctly. |
| `client-profiles` | ✅ Passed | 0 | Evaluated 5 profile(s); 5 warning profile(s); 0 incompatible profile(s). |

### Scenario Outcomes

| Scenario | Status | Findings | Summary |
| :--- | :--- | :---: | :--- |
| `bootstrap-initialize-handshake` | ✅ Passed | 0 | Bootstrap health checks completed successfully. |
| `protocol-compliance-review` | ✅ Passed | 5 | declared capabilities: completions, prompts, resources, tools \| roots/list: supported \| logging/setLevel: supported \| sampling/createMessage: supported \| completion/complete: supported |
| `tool-catalog-smoke` | ✅ Passed | 198 | Tool validation completed. |
| `resource-catalog-smoke` | ✅ Passed | 0 | No resources were discovered during validation. |
| `prompt-catalog-smoke` | ✅ Passed | 1 | Prompt validation completed. |
| `security-authentication-challenge` | ✅ Passed | 0 | Evaluated 55 authentication challenge scenario(s). |
| `security-attack-simulations` | ➖ Skipped | 0 | Evaluated 7 attack simulation(s). |
| `error-handling-matrix` | ❌ Failed | 1 | Validated 5 error scenario(s); 4 handled correctly. |

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
| `protocol-mcp-proto-err` | `protocol-core` | `JSON-RPC Compliance` | `protocol-violation` | JSON-RPC Error Code Violation: Method Not Found |
| `protocol-mcp-proto-err-2` | `protocol-core` | `JSON-RPC Compliance` | `protocol-violation` | JSON-RPC Error Code Violation: Invalid Params |
| `protocol-mcp-proto-jsonrpc` | `protocol-core` | `JSON-RPC Compliance` | `protocol-violation` | Batch processing implementation is inconsistent or incomplete |
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
| `tool-get-me` | `tool-surface` | `get_me` | `tool-result` | ℹ️ MCP Note: result.isError field not present (SHOULD be included per spec) |
| `tool-get-release-by-tag` | `tool-surface` | `get_release_by_tag` | `tool-result` | Tool 'get_release_by_tag' does not declare annotations.destructiveHint. |
| `tool-get-tag` | `tool-surface` | `get_tag` | `tool-result` | Tool 'get_tag' does not declare annotations.destructiveHint. |
| `tool-get-team-members` | `tool-surface` | `get_team_members` | `tool-result` | Tool 'get_team_members' does not declare annotations.destructiveHint. |
| `tool-get-teams` | `tool-surface` | `get_teams` | `tool-result` | ℹ️ MCP Note: result.isError field not present (SHOULD be included per spec) |
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
| `tool-search-code` | `tool-surface` | `search_code` | `tool-result` | ℹ️ MCP Note: result.isError field not present (SHOULD be included per spec) |
| `tool-search-issues` | `tool-surface` | `search_issues` | `tool-result` | Tool 'search_issues' does not declare annotations.destructiveHint. |
| `tool-search-pull-requests` | `tool-surface` | `search_pull_requests` | `tool-result` | Tool 'search_pull_requests' does not declare annotations.destructiveHint. |
| `tool-search-repositories` | `tool-surface` | `search_repositories` | `tool-result` | ℹ️ MCP Note: result.isError field not present (SHOULD be included per spec) |
| `tool-search-users` | `tool-surface` | `search_users` | `tool-result` | ℹ️ MCP Note: result.isError field not present (SHOULD be included per spec) |
| `tool-sub-issue-write` | `tool-surface` | `sub_issue_write` | `tool-result` | Tool 'sub_issue_write' does not declare annotations.readOnlyHint. |
| `tool-update-pull-request` | `tool-surface` | `update_pull_request` | `tool-result` | Tool 'update_pull_request' does not declare annotations.readOnlyHint. |
| `tool-update-pull-request-branch` | `tool-surface` | `update_pull_request_branch` | `tool-result` | Tool 'update_pull_request_branch' does not declare annotations.readOnlyHint. |
| `prompt-assigncodingagent` | `prompt-surface` | `AssignCodingAgent` | `prompt-result` | ✅ prompts/get: messages[] array present (6 messages) |
| `prompt-issue-to-fix-workflow` | `prompt-surface` | `issue_to_fix_workflow` | `prompt-result` | ✅ prompts/get: messages[] array present (5 messages) |
| `auth-no-auth-initialize` | `security-boundaries` | `initialize` | `authentication-scenario` | ⚠️ COMPATIBLE: Discovery metadata was exposed without authentication. |
| `auth-no-auth-prompts-list` | `security-boundaries` | `prompts/list` | `authentication-scenario` | ⚠️ COMPATIBLE: Discovery metadata was exposed without authentication. |
| `auth-malformed-token-prompts-list` | `security-boundaries` | `prompts/list` | `authentication-scenario` | ✅ ALIGNED: Secure OAuth-style challenge returned with HTTP 400. |
| `auth-invalid-token-prompts-list` | `security-boundaries` | `prompts/list` | `authentication-scenario` | ✅ ALIGNED: Secure OAuth-style challenge returned with HTTP 400. |
| `auth-token-expired-prompts-list` | `security-boundaries` | `prompts/list` | `authentication-scenario` | ✅ ALIGNED: Secure OAuth-style challenge returned with HTTP 400. |
| `auth-invalid-scope-prompts-list` | `security-boundaries` | `prompts/list` | `authentication-scenario` | ✅ ALIGNED: Secure OAuth-style challenge returned with HTTP 400. |
| `auth-insufficient-permissions-prompts-list` | `security-boundaries` | `prompts/list` | `authentication-scenario` | ✅ ALIGNED: Secure OAuth-style challenge returned with HTTP 400. |
| `auth-revoked-token-prompts-list` | `security-boundaries` | `prompts/list` | `authentication-scenario` | ✅ ALIGNED: Secure OAuth-style challenge returned with HTTP 400. |
| `auth-wrong-audience-rfc-8707-prompts-list` | `security-boundaries` | `prompts/list` | `authentication-scenario` | ✅ ALIGNED: Secure OAuth-style challenge returned with HTTP 400. |
| `auth-valid-token-prompts-list` | `security-boundaries` | `prompts/list` | `authentication-scenario` | ✅ ALIGNED: Valid authentication was accepted and request processing proceeded. |
| `auth-no-auth-prompts-get` | `security-boundaries` | `prompts/get` | `authentication-scenario` | ⚠️ COMPATIBLE: Authentication appears to be enforced at the application layer instead of via an HTTP challenge. |
| `auth-malformed-token-prompts-get` | `security-boundaries` | `prompts/get` | `authentication-scenario` | ✅ ALIGNED: Secure OAuth-style challenge returned with HTTP 400. |
| `auth-invalid-token-prompts-get` | `security-boundaries` | `prompts/get` | `authentication-scenario` | ✅ ALIGNED: Secure OAuth-style challenge returned with HTTP 400. |
| `auth-token-expired-prompts-get` | `security-boundaries` | `prompts/get` | `authentication-scenario` | ✅ ALIGNED: Secure OAuth-style challenge returned with HTTP 400. |
| `auth-invalid-scope-prompts-get` | `security-boundaries` | `prompts/get` | `authentication-scenario` | ✅ ALIGNED: Secure OAuth-style challenge returned with HTTP 400. |
| `auth-insufficient-permissions-prompts-get` | `security-boundaries` | `prompts/get` | `authentication-scenario` | ✅ ALIGNED: Secure OAuth-style challenge returned with HTTP 400. |
| `auth-revoked-token-prompts-get` | `security-boundaries` | `prompts/get` | `authentication-scenario` | ✅ ALIGNED: Secure OAuth-style challenge returned with HTTP 400. |
| `auth-wrong-audience-rfc-8707-prompts-get` | `security-boundaries` | `prompts/get` | `authentication-scenario` | ✅ ALIGNED: Secure OAuth-style challenge returned with HTTP 400. |
| `auth-valid-token-prompts-get` | `security-boundaries` | `prompts/get` | `authentication-scenario` | ✅ ALIGNED: Valid authentication was accepted and request processing proceeded. |
| `auth-no-auth-resources-list` | `security-boundaries` | `resources/list` | `authentication-scenario` | ⚠️ COMPATIBLE: Discovery metadata was exposed without authentication. |
| `auth-malformed-token-resources-list` | `security-boundaries` | `resources/list` | `authentication-scenario` | ✅ ALIGNED: Secure OAuth-style challenge returned with HTTP 400. |
| `auth-invalid-token-resources-list` | `security-boundaries` | `resources/list` | `authentication-scenario` | ✅ ALIGNED: Secure OAuth-style challenge returned with HTTP 400. |
| `auth-token-expired-resources-list` | `security-boundaries` | `resources/list` | `authentication-scenario` | ✅ ALIGNED: Secure OAuth-style challenge returned with HTTP 400. |
| `auth-invalid-scope-resources-list` | `security-boundaries` | `resources/list` | `authentication-scenario` | ✅ ALIGNED: Secure OAuth-style challenge returned with HTTP 400. |
| `auth-insufficient-permissions-resources-list` | `security-boundaries` | `resources/list` | `authentication-scenario` | ✅ ALIGNED: Secure OAuth-style challenge returned with HTTP 400. |
| `auth-revoked-token-resources-list` | `security-boundaries` | `resources/list` | `authentication-scenario` | ✅ ALIGNED: Secure OAuth-style challenge returned with HTTP 400. |
| `auth-wrong-audience-rfc-8707-resources-list` | `security-boundaries` | `resources/list` | `authentication-scenario` | ✅ ALIGNED: Secure OAuth-style challenge returned with HTTP 400. |
| `auth-valid-token-resources-list` | `security-boundaries` | `resources/list` | `authentication-scenario` | ✅ ALIGNED: Valid authentication was accepted and request processing proceeded. |
| `auth-no-auth-resources-read` | `security-boundaries` | `resources/read` | `authentication-scenario` | ⚠️ COMPATIBLE: Authentication appears to be enforced at the application layer instead of via an HTTP challenge. |
| `auth-malformed-token-resources-read` | `security-boundaries` | `resources/read` | `authentication-scenario` | ✅ ALIGNED: Secure OAuth-style challenge returned with HTTP 400. |
| `auth-invalid-token-resources-read` | `security-boundaries` | `resources/read` | `authentication-scenario` | ✅ ALIGNED: Secure OAuth-style challenge returned with HTTP 400. |
| `auth-token-expired-resources-read` | `security-boundaries` | `resources/read` | `authentication-scenario` | ✅ ALIGNED: Secure OAuth-style challenge returned with HTTP 400. |
| `auth-invalid-scope-resources-read` | `security-boundaries` | `resources/read` | `authentication-scenario` | ✅ ALIGNED: Secure OAuth-style challenge returned with HTTP 400. |
| `auth-insufficient-permissions-resources-read` | `security-boundaries` | `resources/read` | `authentication-scenario` | ✅ ALIGNED: Secure OAuth-style challenge returned with HTTP 400. |
| `auth-revoked-token-resources-read` | `security-boundaries` | `resources/read` | `authentication-scenario` | ✅ ALIGNED: Secure OAuth-style challenge returned with HTTP 400. |
| `auth-wrong-audience-rfc-8707-resources-read` | `security-boundaries` | `resources/read` | `authentication-scenario` | ✅ ALIGNED: Secure OAuth-style challenge returned with HTTP 400. |
| `auth-valid-token-resources-read` | `security-boundaries` | `resources/read` | `authentication-scenario` | ✅ ALIGNED: Valid authentication was accepted and request processing proceeded. |
| `auth-no-auth-tools-list` | `security-boundaries` | `tools/list` | `authentication-scenario` | ⚠️ COMPATIBLE: Discovery metadata was exposed without authentication. |
| `auth-malformed-token-tools-list` | `security-boundaries` | `tools/list` | `authentication-scenario` | ✅ ALIGNED: Secure OAuth-style challenge returned with HTTP 400. |
| `auth-invalid-token-tools-list` | `security-boundaries` | `tools/list` | `authentication-scenario` | ✅ ALIGNED: Secure OAuth-style challenge returned with HTTP 400. |
| `auth-token-expired-tools-list` | `security-boundaries` | `tools/list` | `authentication-scenario` | ✅ ALIGNED: Secure OAuth-style challenge returned with HTTP 400. |
| `auth-invalid-scope-tools-list` | `security-boundaries` | `tools/list` | `authentication-scenario` | ✅ ALIGNED: Secure OAuth-style challenge returned with HTTP 400. |
| `auth-insufficient-permissions-tools-list` | `security-boundaries` | `tools/list` | `authentication-scenario` | ✅ ALIGNED: Secure OAuth-style challenge returned with HTTP 400. |
| `auth-revoked-token-tools-list` | `security-boundaries` | `tools/list` | `authentication-scenario` | ✅ ALIGNED: Secure OAuth-style challenge returned with HTTP 400. |
| `auth-wrong-audience-rfc-8707-tools-list` | `security-boundaries` | `tools/list` | `authentication-scenario` | ✅ ALIGNED: Secure OAuth-style challenge returned with HTTP 400. |
| `auth-valid-token-tools-list` | `security-boundaries` | `tools/list` | `authentication-scenario` | ✅ ALIGNED: Valid authentication was accepted and request processing proceeded. |
| `auth-no-auth-tools-call` | `security-boundaries` | `tools/call` | `authentication-scenario` | ⚠️ COMPATIBLE: Authentication appears to be enforced at the application layer instead of via an HTTP challenge. |
| `auth-malformed-token-tools-call` | `security-boundaries` | `tools/call` | `authentication-scenario` | ✅ ALIGNED: Secure OAuth-style challenge returned with HTTP 400. |
| `auth-invalid-token-tools-call` | `security-boundaries` | `tools/call` | `authentication-scenario` | ✅ ALIGNED: Secure OAuth-style challenge returned with HTTP 400. |
| `auth-token-expired-tools-call` | `security-boundaries` | `tools/call` | `authentication-scenario` | ✅ ALIGNED: Secure OAuth-style challenge returned with HTTP 400. |
| `auth-invalid-scope-tools-call` | `security-boundaries` | `tools/call` | `authentication-scenario` | ✅ ALIGNED: Secure OAuth-style challenge returned with HTTP 400. |
| `auth-insufficient-permissions-tools-call` | `security-boundaries` | `tools/call` | `authentication-scenario` | ✅ ALIGNED: Secure OAuth-style challenge returned with HTTP 400. |
| `auth-revoked-token-tools-call` | `security-boundaries` | `tools/call` | `authentication-scenario` | ✅ ALIGNED: Secure OAuth-style challenge returned with HTTP 400. |
| `auth-wrong-audience-rfc-8707-tools-call` | `security-boundaries` | `tools/call` | `authentication-scenario` | ✅ ALIGNED: Secure OAuth-style challenge returned with HTTP 400. |
| `auth-valid-token-tools-call` | `security-boundaries` | `tools/call` | `authentication-scenario` | ✅ ALIGNED: Valid authentication was accepted and request processing proceeded. |
| `attack-inj-001-input-validation` | `security-boundaries` | `INJ-001 (Input Validation)` | `attack-simulation` | {"jsonrpc":"2.0","id":"a692811b-bdc3-4115-a9f1-cb032768d4ec","result":{"content":[{"type":"text","text":"failed to get latest review for current user: Could not resolve to a Repository with the name '/'."}],"isError":true}} |
| `attack-inj-002-input-validation` | `security-boundaries` | `INJ-002 (Input Validation)` | `attack-simulation` | {"jsonrpc":"2.0","id":"e7fb8428-276d-4520-aea9-bf4cc2ba7b56","result":{"content":[{"type":"text","text":"failed to get latest review for current user: Could not resolve to a Repository with the name '/'."}],"isError":true}} |
| `attack-inj-003-input-validation` | `security-boundaries` | `INJ-003 (Input Validation)` | `attack-simulation` | {"jsonrpc":"2.0","id":"45a54af2-dad3-4d72-991a-d043ee6f15d7","result":{"content":[{"type":"text","text":"failed to get latest review for current user: Could not resolve to a Repository with the name '/'."}],"isError":true}} |
| `attack-mcp-sec-001` | `security-boundaries` | `MCP-SEC-001` | `attack-simulation` | Server handled malformed requests gracefully with standard errors. |
| `attack-mcp-sec-002` | `security-boundaries` | `MCP-SEC-002` | `attack-simulation` | Skipped: Metadata enumeration requires an advertised concrete resource URI; no like-for-like resource surface was available. |
| `attack-mcp-sec-003` | `security-boundaries` | `MCP-SEC-003` | `attack-simulation` | Server correctly rejected invalid schema. |
| `attack-mcp-ai-001` | `security-boundaries` | `MCP-AI-001` | `attack-simulation` | Error clarity: 100/100 (Good). Server error messages help AI self-correct. |
| `error-invalid-method-call` | `error-handling` | `invalid-method` | `error-scenario` | HTTP 400; HTTP 400 Bad Request. Body: JSON RPC not handled: "nonexistent_method_12345" unsupported  |
| `error-malformed-json-payload` | `error-handling` | `malformed-json` | `error-scenario` | HTTP 400; malformed payload: unmarshaling jsonrpc message: json: expected '"' at the beginning of a string value: invalid json  |
| `error-graceful-degradation-on-invalid-request` | `error-handling` | `invalid-request` | `error-scenario` | HTTP 400; malformed payload: invalid message version tag ""; expected "2.0"  |
| `error-timeout-handling-recovery` | `error-handling` | `timeout-handling` | `error-scenario` | Validator cancelled the HTTP request after the induced timeout window elapsed. |
| `error-connection-interruption-recovery` | `error-handling` | `connection-interruption` | `error-scenario` | Validator aborted the HTTP request body mid-stream to simulate a connection interruption. |

## 8. Capability Snapshot

| Probe | Discovered | HTTP Status | Duration | Result |
| :--- | :---: | :---: | :---: | :--- |
| Tools/list | 41 | HTTP 200 | 361.4 ms | ✅ Listed<br/>Call ✅ |
| Resources/list | 0 | HTTP 200 | 129.2 ms | ✅ Listed |
| Prompts/list | 2 | HTTP 200 | 167.0 ms | ✅ Listed |

- **First Tool Probed:** `add_comment_to_pending_review`

## 9. Scoring Methodology

These notes explain how the overall score and blocking decision were calibrated for this run.

- GUIDANCE: 7 protected-endpoint authentication scenarios were secure but not fully aligned with the preferred MCP/OAuth challenge flow. Score reduced by 20%.
- Score meets the preferred target with non-blocking improvement opportunities.

## 10. Compliance Matrix

| Category | Status | Score | Issues |
| :--- | :---: | :---: | :---: |
| Protocol Compliance | ✅ Passed | 81.2% | **3** |
| Security Assessment | ✅ Passed | 100.0% | - |
| Tool Validation | ✅ Passed | 100.0% | - |
| Resource Capabilities | ✅ Passed | 100.0% | - |
| Prompt Capabilities | ✅ Passed | 100.0% | - |
| Performance | ✅ Passed | 100.0% | - |

## 11. Performance Calibration

Performance is assessed using a synthetic load probe rather than a workload-specific SLA benchmark.
For public or remote SaaS endpoints, partial failures under synthetic pressure are reported as advisory when they look like capacity limits or edge protections rather than protocol instability.

- Synthetic load probe executed against tools/list using 500 requests at concurrency 100.

## 12. Security Assessment

**Security Score:** 100.0%

### Authentication Analysis
| Scenario | Method | Expected | Actual | HTTP | Analysis | Status |
| :--- | :--- | :--- | :--- | :---: | :--- | :---: |
| No Auth - initialize | `initialize` | 4xx (Secure Rejection) | Discovery metadata returned | 200 | ⚠️ COMPATIBLE: Discovery metadata was exposed without authentication. | ✅ |
| No Auth - prompts/list | `prompts/list` | 4xx (Secure Rejection) | Discovery metadata returned | 200 | ⚠️ COMPATIBLE: Discovery metadata was exposed without authentication. | ✅ |
| Malformed Token - prompts/list | `prompts/list` | 4xx (Secure Rejection) | Challenge returned | 400 | ✅ ALIGNED: Secure OAuth-style challenge returned with HTTP 400. | ✅ |
| Invalid Token - prompts/list | `prompts/list` | 4xx (Secure Rejection) | Challenge returned | 400 | ✅ ALIGNED: Secure OAuth-style challenge returned with HTTP 400. | ✅ |
| Token Expired - prompts/list | `prompts/list` | 4xx (Secure Rejection) | Challenge returned | 400 | ✅ ALIGNED: Secure OAuth-style challenge returned with HTTP 400. | ✅ |
| Invalid Scope - prompts/list | `prompts/list` | 4xx (Secure Rejection) | Challenge returned | 400 | ✅ ALIGNED: Secure OAuth-style challenge returned with HTTP 400. | ✅ |
| Insufficient Permissions - prompts/list | `prompts/list` | 4xx (Secure Rejection) | Challenge returned | 400 | ✅ ALIGNED: Secure OAuth-style challenge returned with HTTP 400. | ✅ |
| Revoked Token - prompts/list | `prompts/list` | 4xx (Secure Rejection) | Challenge returned | 400 | ✅ ALIGNED: Secure OAuth-style challenge returned with HTTP 400. | ✅ |
| Wrong Audience (RFC 8707) - prompts/list | `prompts/list` | 4xx (Secure Rejection) | Challenge returned | 400 | ✅ ALIGNED: Secure OAuth-style challenge returned with HTTP 400. | ✅ |
| Valid Token - prompts/list | `prompts/list` | 200 + JSON-RPC Response | Authenticated request succeeded | 200 | ✅ ALIGNED: Valid authentication was accepted and request processing proceeded. | ✅ |
| No Auth - prompts/get | `prompts/get` | 4xx (Secure Rejection) | Request rejected in JSON-RPC layer | 200 | ⚠️ COMPATIBLE: Authentication appears to be enforced at the application layer instead of via an HTTP challenge. | ✅ |
| Malformed Token - prompts/get | `prompts/get` | 4xx (Secure Rejection) | Challenge returned | 400 | ✅ ALIGNED: Secure OAuth-style challenge returned with HTTP 400. | ✅ |
| Invalid Token - prompts/get | `prompts/get` | 4xx (Secure Rejection) | Challenge returned | 400 | ✅ ALIGNED: Secure OAuth-style challenge returned with HTTP 400. | ✅ |
| Token Expired - prompts/get | `prompts/get` | 4xx (Secure Rejection) | Challenge returned | 400 | ✅ ALIGNED: Secure OAuth-style challenge returned with HTTP 400. | ✅ |
| Invalid Scope - prompts/get | `prompts/get` | 4xx (Secure Rejection) | Challenge returned | 400 | ✅ ALIGNED: Secure OAuth-style challenge returned with HTTP 400. | ✅ |
| Insufficient Permissions - prompts/get | `prompts/get` | 4xx (Secure Rejection) | Challenge returned | 400 | ✅ ALIGNED: Secure OAuth-style challenge returned with HTTP 400. | ✅ |
| Revoked Token - prompts/get | `prompts/get` | 4xx (Secure Rejection) | Challenge returned | 400 | ✅ ALIGNED: Secure OAuth-style challenge returned with HTTP 400. | ✅ |
| Wrong Audience (RFC 8707) - prompts/get | `prompts/get` | 4xx (Secure Rejection) | Challenge returned | 400 | ✅ ALIGNED: Secure OAuth-style challenge returned with HTTP 400. | ✅ |
| Valid Token - prompts/get | `prompts/get` | 200 + JSON-RPC Response | Authenticated request returned JSON-RPC error | 200 | ✅ ALIGNED: Valid authentication was accepted and request processing proceeded. | ✅ |
| No Auth - resources/list | `resources/list` | 4xx (Secure Rejection) | Discovery metadata returned | 200 | ⚠️ COMPATIBLE: Discovery metadata was exposed without authentication. | ✅ |
| Malformed Token - resources/list | `resources/list` | 4xx (Secure Rejection) | Challenge returned | 400 | ✅ ALIGNED: Secure OAuth-style challenge returned with HTTP 400. | ✅ |
| Invalid Token - resources/list | `resources/list` | 4xx (Secure Rejection) | Challenge returned | 400 | ✅ ALIGNED: Secure OAuth-style challenge returned with HTTP 400. | ✅ |
| Token Expired - resources/list | `resources/list` | 4xx (Secure Rejection) | Challenge returned | 400 | ✅ ALIGNED: Secure OAuth-style challenge returned with HTTP 400. | ✅ |
| Invalid Scope - resources/list | `resources/list` | 4xx (Secure Rejection) | Challenge returned | 400 | ✅ ALIGNED: Secure OAuth-style challenge returned with HTTP 400. | ✅ |
| Insufficient Permissions - resources/list | `resources/list` | 4xx (Secure Rejection) | Challenge returned | 400 | ✅ ALIGNED: Secure OAuth-style challenge returned with HTTP 400. | ✅ |
| Revoked Token - resources/list | `resources/list` | 4xx (Secure Rejection) | Challenge returned | 400 | ✅ ALIGNED: Secure OAuth-style challenge returned with HTTP 400. | ✅ |
| Wrong Audience (RFC 8707) - resources/list | `resources/list` | 4xx (Secure Rejection) | Challenge returned | 400 | ✅ ALIGNED: Secure OAuth-style challenge returned with HTTP 400. | ✅ |
| Valid Token - resources/list | `resources/list` | 200 + JSON-RPC Response | Authenticated request succeeded | 200 | ✅ ALIGNED: Valid authentication was accepted and request processing proceeded. | ✅ |
| No Auth - resources/read | `resources/read` | 4xx (Secure Rejection) | Request rejected in JSON-RPC layer | 200 | ⚠️ COMPATIBLE: Authentication appears to be enforced at the application layer instead of via an HTTP challenge. | ✅ |
| Malformed Token - resources/read | `resources/read` | 4xx (Secure Rejection) | Challenge returned | 400 | ✅ ALIGNED: Secure OAuth-style challenge returned with HTTP 400. | ✅ |
| Invalid Token - resources/read | `resources/read` | 4xx (Secure Rejection) | Challenge returned | 400 | ✅ ALIGNED: Secure OAuth-style challenge returned with HTTP 400. | ✅ |
| Token Expired - resources/read | `resources/read` | 4xx (Secure Rejection) | Challenge returned | 400 | ✅ ALIGNED: Secure OAuth-style challenge returned with HTTP 400. | ✅ |
| Invalid Scope - resources/read | `resources/read` | 4xx (Secure Rejection) | Challenge returned | 400 | ✅ ALIGNED: Secure OAuth-style challenge returned with HTTP 400. | ✅ |
| Insufficient Permissions - resources/read | `resources/read` | 4xx (Secure Rejection) | Challenge returned | 400 | ✅ ALIGNED: Secure OAuth-style challenge returned with HTTP 400. | ✅ |
| Revoked Token - resources/read | `resources/read` | 4xx (Secure Rejection) | Challenge returned | 400 | ✅ ALIGNED: Secure OAuth-style challenge returned with HTTP 400. | ✅ |
| Wrong Audience (RFC 8707) - resources/read | `resources/read` | 4xx (Secure Rejection) | Challenge returned | 400 | ✅ ALIGNED: Secure OAuth-style challenge returned with HTTP 400. | ✅ |
| Valid Token - resources/read | `resources/read` | 200 + JSON-RPC Response | Authenticated request returned JSON-RPC error | 200 | ✅ ALIGNED: Valid authentication was accepted and request processing proceeded. | ✅ |
| No Auth - tools/list | `tools/list` | 4xx (Secure Rejection) | Discovery metadata returned | 200 | ⚠️ COMPATIBLE: Discovery metadata was exposed without authentication. | ✅ |
| Malformed Token - tools/list | `tools/list` | 4xx (Secure Rejection) | Challenge returned | 400 | ✅ ALIGNED: Secure OAuth-style challenge returned with HTTP 400. | ✅ |
| Invalid Token - tools/list | `tools/list` | 4xx (Secure Rejection) | Challenge returned | 400 | ✅ ALIGNED: Secure OAuth-style challenge returned with HTTP 400. | ✅ |
| Token Expired - tools/list | `tools/list` | 4xx (Secure Rejection) | Challenge returned | 400 | ✅ ALIGNED: Secure OAuth-style challenge returned with HTTP 400. | ✅ |
| Invalid Scope - tools/list | `tools/list` | 4xx (Secure Rejection) | Challenge returned | 400 | ✅ ALIGNED: Secure OAuth-style challenge returned with HTTP 400. | ✅ |
| Insufficient Permissions - tools/list | `tools/list` | 4xx (Secure Rejection) | Challenge returned | 400 | ✅ ALIGNED: Secure OAuth-style challenge returned with HTTP 400. | ✅ |
| Revoked Token - tools/list | `tools/list` | 4xx (Secure Rejection) | Challenge returned | 400 | ✅ ALIGNED: Secure OAuth-style challenge returned with HTTP 400. | ✅ |
| Wrong Audience (RFC 8707) - tools/list | `tools/list` | 4xx (Secure Rejection) | Challenge returned | 400 | ✅ ALIGNED: Secure OAuth-style challenge returned with HTTP 400. | ✅ |
| Valid Token - tools/list | `tools/list` | 200 + JSON-RPC Response | Authenticated request succeeded | 200 | ✅ ALIGNED: Valid authentication was accepted and request processing proceeded. | ✅ |
| No Auth - tools/call | `tools/call` | 4xx (Secure Rejection) | Request rejected in JSON-RPC layer | 200 | ⚠️ COMPATIBLE: Authentication appears to be enforced at the application layer instead of via an HTTP challenge. | ✅ |
| Malformed Token - tools/call | `tools/call` | 4xx (Secure Rejection) | Challenge returned | 400 | ✅ ALIGNED: Secure OAuth-style challenge returned with HTTP 400. | ✅ |
| Invalid Token - tools/call | `tools/call` | 4xx (Secure Rejection) | Challenge returned | 400 | ✅ ALIGNED: Secure OAuth-style challenge returned with HTTP 400. | ✅ |
| Token Expired - tools/call | `tools/call` | 4xx (Secure Rejection) | Challenge returned | 400 | ✅ ALIGNED: Secure OAuth-style challenge returned with HTTP 400. | ✅ |
| Invalid Scope - tools/call | `tools/call` | 4xx (Secure Rejection) | Challenge returned | 400 | ✅ ALIGNED: Secure OAuth-style challenge returned with HTTP 400. | ✅ |
| Insufficient Permissions - tools/call | `tools/call` | 4xx (Secure Rejection) | Challenge returned | 400 | ✅ ALIGNED: Secure OAuth-style challenge returned with HTTP 400. | ✅ |
| Revoked Token - tools/call | `tools/call` | 4xx (Secure Rejection) | Challenge returned | 400 | ✅ ALIGNED: Secure OAuth-style challenge returned with HTTP 400. | ✅ |
| Wrong Audience (RFC 8707) - tools/call | `tools/call` | 4xx (Secure Rejection) | Challenge returned | 400 | ✅ ALIGNED: Secure OAuth-style challenge returned with HTTP 400. | ✅ |
| Valid Token - tools/call | `tools/call` | 200 + JSON-RPC Response | Authenticated request returned JSON-RPC error | 200 | ✅ ALIGNED: Valid authentication was accepted and request processing proceeded. | ✅ |

### Adversarial Input Handling

> **Legend:**
> * **REFLECTED / UNSAFE ECHO**: The server reflected the payload back without sanitization. No execution confirmed.
> * **BLOCKED**: The server correctly rejected or sanitized the input.
> * **SKIPPED**: Test could not be executed (e.g., no tools discovered for injection target).

| Attack Vector | Description | Result | Analysis |
| :--- | :--- | :---: | :--- |
| INJ-001 (Input Validation) | Simulated INJ-001 (Input Validation) attack on add_comment_to_pending_review | 🛡️ BLOCKED | `{"jsonrpc":"2.0","id":"a692811b-bdc3-4115-a9f1-cb032768d4ec","result":{"content":[{"type":"text","text":"failed to get latest review for current user: Could not resolve to a Repository with the name '/'."}],"isError":true}}` |
| INJ-002 (Input Validation) | Simulated INJ-002 (Input Validation) attack on add_comment_to_pending_review | 🛡️ BLOCKED | `{"jsonrpc":"2.0","id":"e7fb8428-276d-4520-aea9-bf4cc2ba7b56","result":{"content":[{"type":"text","text":"failed to get latest review for current user: Could not resolve to a Repository with the name '/'."}],"isError":true}}` |
| INJ-003 (Input Validation) | Simulated INJ-003 (Input Validation) attack on add_comment_to_pending_review | 🛡️ BLOCKED | `{"jsonrpc":"2.0","id":"45a54af2-dad3-4d72-991a-d043ee6f15d7","result":{"content":[{"type":"text","text":"failed to get latest review for current user: Could not resolve to a Repository with the name '/'."}],"isError":true}}` |
| MCP-SEC-001 | JSON-RPC Error Smuggling | 🛡️ BLOCKED | `Server handled malformed requests gracefully with standard errors.` |
| MCP-SEC-002 | Metadata Enumeration | ⏭️ SKIPPED | `Skipped: Metadata enumeration requires an advertised concrete resource URI; no like-for-like resource surface was available.` |
| MCP-SEC-003 | Schema Confusion | 🛡️ BLOCKED | `Server correctly rejected invalid schema.` |
| MCP-AI-001 | Hallucination Fuzzing | 🛡️ BLOCKED | `Error clarity: 100/100 (Good). Server error messages help AI self-correct.` |

## 13. Protocol Compliance

**Status Detail:** declared capabilities: completions, prompts, resources, tools | roots/list: supported | logging/setLevel: supported | sampling/createMessage: supported | completion/complete: supported

### Compliance Violations
| ID | Source | Description | Severity | Recommendation |
| :--- | :--- | :--- | :---: | :--- |
| MCP-PROTO-ERR | `spec` | JSON-RPC Error Code Violation: Method Not Found | Low | This specific error code test failed. Ensure the server returns standard JSON-RPC error codes for known error conditions: -32700 (parse error), -32600 (invalid request), -32601 (method not found), -32602 (invalid params), -32603 (internal error). See https://spec.modelcontextprotocol.io/specification/2025-11-25/basic/json-rpc#errors |
| MCP-PROTO-ERR | `spec` | JSON-RPC Error Code Violation: Invalid Params | Low | This specific error code test failed. Ensure the server returns standard JSON-RPC error codes for known error conditions: -32700 (parse error), -32600 (invalid request), -32601 (method not found), -32602 (invalid params), -32603 (internal error). See https://spec.modelcontextprotocol.io/specification/2025-11-25/basic/json-rpc#errors |
| MCP-PROTO-JSONRPC | `spec` | Batch processing implementation is inconsistent or incomplete | Medium | If the server accepts batch requests, it must process each request independently and return an array of responses in any order. Ensure batch handling does not drop or duplicate responses. See https://www.jsonrpc.org/specification#batch |

## 14. Tool Validation

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

### Tool: `tools/list (Schema Compliance)`

**Status:** ✅ Passed
**Execution Time:** 361.37ms
---

### Tool: `add_comment_to_pending_review`

**Status:** ✅ Passed
**Execution Time:** 861.45ms

#### Tool Metadata
| Property | Value |
| :--- | :--- |
| Display Title | Add review comment to the requester's latest pending pull request review |

---

### Tool: `add_issue_comment`

**Status:** ✅ Passed
**Execution Time:** 800.30ms

#### Tool Metadata
| Property | Value |
| :--- | :--- |
| Display Title | Add comment to issue |

---

### Tool: `add_reply_to_pull_request_comment`

**Status:** ✅ Passed
**Execution Time:** 485.31ms

#### Tool Metadata
| Property | Value |
| :--- | :--- |
| Display Title | Add reply to pull request comment |

---

### Tool: `create_branch`

**Status:** ✅ Passed
**Execution Time:** 549.35ms

#### Tool Metadata
| Property | Value |
| :--- | :--- |
| Display Title | Create branch |

---

### Tool: `create_or_update_file`

**Status:** ✅ Passed
**Execution Time:** 757.39ms

#### Tool Metadata
| Property | Value |
| :--- | :--- |
| Display Title | Create or update file |

---

### Tool: `create_pull_request`

**Status:** ✅ Passed
**Execution Time:** 382.83ms

#### Tool Metadata
| Property | Value |
| :--- | :--- |
| Display Title | Open new pull request |

---

### Tool: `create_repository`

**Status:** ✅ Passed
**Execution Time:** 421.55ms

#### Tool Metadata
| Property | Value |
| :--- | :--- |
| Display Title | Create repository |

---

### Tool: `delete_file`

**Status:** ✅ Passed
**Execution Time:** 533.82ms

#### Tool Metadata
| Property | Value |
| :--- | :--- |
| Display Title | Delete file |
| destructiveHint | True |

---

### Tool: `fork_repository`

**Status:** ✅ Passed
**Execution Time:** 560.48ms

#### Tool Metadata
| Property | Value |
| :--- | :--- |
| Display Title | Fork repository |

---

### Tool: `get_commit`

**Status:** ✅ Passed
**Execution Time:** 534.30ms

#### Tool Metadata
| Property | Value |
| :--- | :--- |
| Display Title | Get commit details |
| readOnlyHint | True |

---

### Tool: `get_file_contents`

**Status:** ✅ Passed
**Execution Time:** 667.11ms

#### Tool Metadata
| Property | Value |
| :--- | :--- |
| Display Title | Get file or directory contents |
| readOnlyHint | True |

---

### Tool: `get_label`

**Status:** ✅ Passed
**Execution Time:** 536.95ms

#### Tool Metadata
| Property | Value |
| :--- | :--- |
| Display Title | Get a specific label from a repository. |
| readOnlyHint | True |

---

### Tool: `get_latest_release`

**Status:** ✅ Passed
**Execution Time:** 423.48ms

#### Tool Metadata
| Property | Value |
| :--- | :--- |
| Display Title | Get latest release |
| readOnlyHint | True |

---

### Tool: `get_me`

**Status:** ✅ Passed
**Execution Time:** 352.24ms

#### Tool Metadata
| Property | Value |
| :--- | :--- |
| Display Title | Get my user profile |
| readOnlyHint | True |

---

### Tool: `get_release_by_tag`

**Status:** ✅ Passed
**Execution Time:** 424.79ms

#### Tool Metadata
| Property | Value |
| :--- | :--- |
| Display Title | Get a release by tag name |
| readOnlyHint | True |

---

### Tool: `get_tag`

**Status:** ✅ Passed
**Execution Time:** 388.74ms

#### Tool Metadata
| Property | Value |
| :--- | :--- |
| Display Title | Get tag details |
| readOnlyHint | True |

---

### Tool: `get_team_members`

**Status:** ✅ Passed
**Execution Time:** 341.00ms

#### Tool Metadata
| Property | Value |
| :--- | :--- |
| Display Title | Get team members |
| readOnlyHint | True |

---

### Tool: `get_teams`

**Status:** ✅ Passed
**Execution Time:** 337.59ms

#### Tool Metadata
| Property | Value |
| :--- | :--- |
| Display Title | Get teams |
| readOnlyHint | True |

---

### Tool: `issue_read`

**Status:** ✅ Passed
**Execution Time:** 330.07ms

#### Tool Metadata
| Property | Value |
| :--- | :--- |
| Display Title | Get issue details |
| readOnlyHint | True |

---

### Tool: `issue_write`

**Status:** ✅ Passed
**Execution Time:** 398.39ms

#### Tool Metadata
| Property | Value |
| :--- | :--- |
| Display Title | Create or update issue. |

---

### Tool: `list_branches`

**Status:** ✅ Passed
**Execution Time:** 471.64ms

#### Tool Metadata
| Property | Value |
| :--- | :--- |
| Display Title | List branches |
| readOnlyHint | True |

---

### Tool: `list_commits`

**Status:** ✅ Passed
**Execution Time:** 286.45ms

#### Tool Metadata
| Property | Value |
| :--- | :--- |
| Display Title | List commits |
| readOnlyHint | True |

---

### Tool: `list_issue_types`

**Status:** ✅ Passed
**Execution Time:** 273.66ms

#### Tool Metadata
| Property | Value |
| :--- | :--- |
| Display Title | List available issue types |
| readOnlyHint | True |

---

### Tool: `list_issues`

**Status:** ✅ Passed
**Execution Time:** 274.11ms

#### Tool Metadata
| Property | Value |
| :--- | :--- |
| Display Title | List issues |
| readOnlyHint | True |

---

### Tool: `list_pull_requests`

**Status:** ✅ Passed
**Execution Time:** 455.10ms

#### Tool Metadata
| Property | Value |
| :--- | :--- |
| Display Title | List pull requests |
| readOnlyHint | True |

---

### Tool: `list_releases`

**Status:** ✅ Passed
**Execution Time:** 333.19ms

#### Tool Metadata
| Property | Value |
| :--- | :--- |
| Display Title | List releases |
| readOnlyHint | True |

---

### Tool: `list_tags`

**Status:** ✅ Passed
**Execution Time:** 370.56ms

#### Tool Metadata
| Property | Value |
| :--- | :--- |
| Display Title | List tags |
| readOnlyHint | True |

---

### Tool: `merge_pull_request`

**Status:** ✅ Passed
**Execution Time:** 395.18ms

#### Tool Metadata
| Property | Value |
| :--- | :--- |
| Display Title | Merge pull request |

---

### Tool: `pull_request_read`

**Status:** ✅ Passed
**Execution Time:** 328.48ms

#### Tool Metadata
| Property | Value |
| :--- | :--- |
| Display Title | Get details for a single pull request |
| readOnlyHint | True |

---

### Tool: `pull_request_review_write`

**Status:** ✅ Passed
**Execution Time:** 364.63ms

#### Tool Metadata
| Property | Value |
| :--- | :--- |
| Display Title | Write operations (create, submit, delete) on pull request reviews. |

---

### Tool: `push_files`

**Status:** ✅ Passed
**Execution Time:** 453.41ms

#### Tool Metadata
| Property | Value |
| :--- | :--- |
| Display Title | Push files to repository |

---

### Tool: `request_copilot_review`

**Status:** ✅ Passed
**Execution Time:** 376.72ms

#### Tool Metadata
| Property | Value |
| :--- | :--- |
| Display Title | Request Copilot review |

---

### Tool: `run_secret_scanning`

**Status:** ✅ Passed
**Execution Time:** 233.67ms

#### Tool Metadata
| Property | Value |
| :--- | :--- |
| Display Title | Run Secret Scanning |
| readOnlyHint | True |
| openWorldHint | False |

---

### Tool: `search_code`

**Status:** ✅ Passed
**Execution Time:** 6355.54ms

#### Tool Metadata
| Property | Value |
| :--- | :--- |
| Display Title | Search code |
| readOnlyHint | True |

---

### Tool: `search_issues`

**Status:** ✅ Passed
**Execution Time:** 417.78ms

#### Tool Metadata
| Property | Value |
| :--- | :--- |
| Display Title | Search issues |
| readOnlyHint | True |

---

### Tool: `search_pull_requests`

**Status:** ✅ Passed
**Execution Time:** 544.33ms

#### Tool Metadata
| Property | Value |
| :--- | :--- |
| Display Title | Search pull requests |
| readOnlyHint | True |

---

### Tool: `search_repositories`

**Status:** ✅ Passed
**Execution Time:** 598.42ms

#### Tool Metadata
| Property | Value |
| :--- | :--- |
| Display Title | Search repositories |
| readOnlyHint | True |

---

### Tool: `search_users`

**Status:** ✅ Passed
**Execution Time:** 432.82ms

#### Tool Metadata
| Property | Value |
| :--- | :--- |
| Display Title | Search users |
| readOnlyHint | True |

---

### Tool: `sub_issue_write`

**Status:** ✅ Passed
**Execution Time:** 481.85ms

#### Tool Metadata
| Property | Value |
| :--- | :--- |
| Display Title | Change sub-issue |

---

### Tool: `update_pull_request`

**Status:** ✅ Passed
**Execution Time:** 370.86ms

#### Tool Metadata
| Property | Value |
| :--- | :--- |
| Display Title | Edit pull request |

---

### Tool: `update_pull_request_branch`

**Status:** ✅ Passed
**Execution Time:** 346.30ms

#### Tool Metadata
| Property | Value |
| :--- | :--- |
| Display Title | Update pull request branch |

---

### Tool Catalog Advisory Breakdown

Remaining tool-catalog debt is grouped by authority so MCP specification failures are not conflated with guidance or AI-oriented heuristics.

| Authority | Active Rules | Coverage | Highest Severity | Representative Gaps |
| :--- | :---: | :--- | :---: | :--- |
| Spec | 1 | 5/41 (12%) | 🔵 Low | tools/call result.isError field not present |
| Guideline | 4 | 41/41 (100%) | 🔵 Low | Tool 'add_comment_to_pending_review' does not declare annotations.idempotentHint.<br />Tool 'add_comment_to_pending_review' does not declare annotations.destructiveHint. |
| Heuristic | 7 | 41/41 (100%) | 🟠 High | 🔴 LLM-Friendliness: 10/100 (Anti-LLM) — Error will cause AI hallucination/loops<br />Tool 'add_comment_to_pending_review': 4/10 string parameters have no enum/pattern/format constraint |

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

## 15. Resource Capabilities

**Resources Discovered:** 0

## 16. AI Readiness Assessment

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

## 17. Optional MCP Capabilities

These probes check whether the server supports optional MCP features beyond the core primitives.

| Capability | Status |
| :--- | :--- |
| `declared capabilities` | ➖ completions, prompts, resources, tools |
| `roots/list` | ✅ supported |
| `logging/setLevel` | ✅ supported |
| `sampling/createMessage` | ✅ supported |
| `completion/complete` | ✅ supported |

## 18. Performance Metrics

| Metric | Result | Verdict |
| :--- | :--- | :--- |
| **Avg Latency** | 143.39ms | ✅ Good |
| **P95 Latency** | 202.00ms | - |
| **Throughput** | 5.03 req/sec | - |
| **Error Rate** | 0.00% | ✅ Clean |
| **Requests** | 500/500 successful | - |

## 19. Recommendations

- 💡 Spec: If the server accepts batch requests, it must process each request independently and return an array of responses in any order. Ensure batch handling does not drop or duplicate responses. See https://www.jsonrpc.org/specification#batch
- 💡 Spec: This specific error code test failed. Ensure the server returns standard JSON-RPC error codes for known error conditions: -32700 (parse error), -32600 (invalid request), -32601 (method not found), -32602 (invalid params), -32603 (internal error). See https://spec.modelcontextprotocol.io/specification/2025-11-25/basic/json-rpc#errors
- 💡 Heuristic: Return specific, structured errors that identify the invalid argument and expected shape Gap affects 3/41 tools
- 💡 Heuristic: Constrain string parameters with enum, pattern, or format metadata when possible Gap affects 40/41 tools
- 💡 Heuristic: Add explicit confirmation, approval, or warning language so agents know human review is expected before destructive execution Gap affects 1/41 tool
- 💡 Heuristic: Required array parameters should declare items schemas and minItems when empty arrays are not meaningful Gap affects 1/41 tool

---
*Produced with [MCP Validator](https://github.com/navalerakesh/mcp-validation-security) · [McpVal on NuGet](https://www.nuget.org/packages/McpVal#versions-body-tab)*
