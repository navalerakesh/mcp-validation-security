# MCP Validator 1.1.25 Release-Candidate Validation Matrix

Date: 2026-08-19 local / 2026-08-20 UTC

Validator: `mcpval 1.1.25`

This matrix validates the release candidate across public HTTP, authenticated modern HTTP, modern STDIO, legacy STDIO, and the production local MCP wrapper. Live artifacts remain in operating-system temporary storage and are not committed.

## Targets And Disposition

| Target | Validation ID | Transport / Profile | Execution | Status / Score | Deterministic Verdicts | Evidence | Trust | Strict Policy |
| --- | --- | --- | --- | --- | --- | --- | --- | --- |
| Microsoft Learn | `b4250cea-fd92-46bf-8697-62f54d6e6796` | HTTP, `2025-06-18` legacy, public | Safe | Partially completed / 100.0% evaluated-only | Review required / Review required / Review required | 50%, medium | L3 Acceptable, evidence-limited | Blocked |
| GitHub MCP | `acdf4f3b-4ccb-4528-b5d4-7a3471b5162d` | HTTP, `2026-07-28` modern, authenticated | Safe | Failed / 75.0% | Reject / Reject / Review required | 50%, medium | L2 Caution, evidence-limited | Blocked |
| Modern fixture | `d91097f3-e6a9-4f85-a971-28f1e1d73540` | STDIO, `2026-07-28` modern, local | Standard active | Passed / 100.0% | Conditionally acceptable / Conditionally acceptable / Trusted | 100%, high | L5 High Assurance | Passed |
| Legacy fixture | `f6d3c983-e55a-4401-81ec-6e5eed618e96` | STDIO, `2025-11-25` legacy, local | Standard active | Passed / 100.0% | Conditionally acceptable / Conditionally acceptable / Trusted | 100%, high | L5 High Assurance | Passed |
| `mcpval-localmcp` | `f95a7458-f18b-4d25-b90f-f7080cb32aa7` | STDIO, `2025-11-25` legacy, local | Safe | Partially completed / 75.0% | Review required / Review required / Review required | 50%, medium | L3 Acceptable, evidence-limited | Blocked |

Verdict order is baseline / protocol / coverage. Scores are descriptive; deterministic verdicts and strict policy are authoritative.

## Modern Protocol Evidence

The modern STDIO run requested, advertised, negotiated, and validated schema version `2026-07-28`. It exercised `server/discover`, required per-request metadata, `Mcp-Method` HTTP semantics through the GitHub run, modern `resultType`, list cache metadata, active malformed-message/error handling, tools, resources, prompts, performance, and security probes.

All functional categories passed at 100% in the final modern fixture run. The run produced 100% applicable evidence coverage with high confidence, trusted coverage verdict, L5 trust, strict-policy pass, and exit code `0`. The equivalent active legacy STDIO run also passed at 100% with L5 trust and strict-policy exit `0`.

GitHub's modern endpoint health check passed through `server/discover`. Its safe validation discovered 44 tools, 4 resources, and 2 prompts. Strict policy rejected specification/schema, annotation, and client-profile findings; trust is capped at L2 because deterministic verdicts reject while active security remains unevaluated.

## Artifact Audit

Each target produced six subject artifacts plus its audit manifest:

- canonical JSON;
- Markdown;
- HTML;
- SARIF;
- JUnit XML;
- client-profile summary;
- audit manifest.

Across five targets, all 30 subject hashes were recomputed and matched. JSON and XML parsed successfully. All canonical transport `rawContent` values were null. The active GitHub token was passed by environment `SecretRef`, never as a command argument or persisted value; exact-token scans found zero matches. The two controlled active targets passed strict policy with exit code `0`; the three external or intentionally safe targets blocked with exit code `1`. Every deterministic reject was paired with L1 or L2 trust.

All five manifests bind the same Release validator SHA-256: `5e8d0069acc1ddf00829e446046e3b491c7beccbec95f3312df29374f8faba8a`.

## Release Gate

- 912/912 .NET tests passed in Release.
- 14/14 npm tests passed.
- TypeScript typecheck passed.
- Report snapshots passed without rewriting unrelated JSON snapshot changes.

The local code and Release test matrix meet the release-candidate quality gate. The known live-capture history is locked to reviewed path and Git-object digests pending long-term remediation. Fresh protected pull-request checks and the protected remote release workflow must pass before publication. External target blocks are target/scope outcomes, not validator release failures.
