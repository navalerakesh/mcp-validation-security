# Protocol Coverage Matrix

This matrix describes executable MCP Validator coverage. `Validated` means deterministic tests exercise the behavior. `Schema` means the embedded schema is present but no complete behavioral certificate is claimed. `N/A` means the feature does not apply to that revision or transport.

## Revision And Transport

| Protocol revision | Era | HTTP mode | STDIO | Negotiation behavior |
| --- | --- | --- | --- | --- |
| `2024-11-05` | Legacy | SSE | Validated | Explicit profile; no Streamable HTTP checks |
| `2025-03-26` | Legacy | Streamable HTTP with fallback | Validated | Explicit profile; batch JSON-RPC applicable |
| `2025-06-18` | Legacy | Streamable HTTP with fallback | Validated | Explicit profile; batch probe excluded |
| `2025-11-25` | Legacy | Streamable HTTP with fallback | Validated | Newest legacy profile; deprecated logging lifecycle recorded |
| `2026-07-28` | Modern | Streamable HTTP only | Validated | `server/discover`, stateless per-request metadata, explicit downgrade rejection |
| `latest` | Modern alias | Streamable HTTP only | Validated | Resolves visibly to `2026-07-28` |

`--protocol-era legacy --mcpspec latest` resolves to `2025-11-25`. Explicit cross-era combinations fail before target contact.

## Feature Coverage

| Feature | Legacy | Modern HTTP | Modern STDIO | Evidence |
| --- | --- | --- | --- | --- |
| Version selection | Validated | Validated | Validated | Era plan, SDK mode, negotiated mismatch tests |
| Initialize lifecycle | Validated | N/A | N/A | Legacy initialize and session tests |
| `server/discover` | N/A | Validated | Validated | Full embedded-schema response validation |
| Per-request protocol/capability/client metadata | N/A | Validated | Validated | Wire-level request tests |
| Protocol header and metadata agreement | N/A | Validated | N/A | Match/mismatch HTTP probes |
| Unsupported version `-32022` | N/A | Validated | Schema | Typed HTTP error probe |
| `resultType` | Backward-compatible absence | Validated | Validated | `complete`, `input_required`, malformed tests |
| Multi-round-trip continuation shape | N/A | Validated | Validated | Embedded `InputRequiredResult` schema validation |
| List cache metadata | N/A | Validated | Validated through common calls | Required `cacheScope`, `ttlMs`, collection checks |
| List repeatability | Guideline | Validated | Validated through common calls | Canonical page/cursor fingerprints |
| Subscription acknowledgment | Legacy methods where advertised | Validated | Validated | First SSE event and process-backed STDIO tests |
| Subscription cancellation/close | Legacy unsubscribe where advertised | Stream close bounded by HTTP probe | Validated | Correlated STDIO close result |
| Extension IDs | N/A | Validated | Validated | Typed discovery evidence |
| Tasks extension | Legacy task capabilities where declared | Validated negotiation | Validated negotiation | Dedicated stable finding; no invented RPC surface |
| Tool input/output schemas | Validated | Validated | Validated | Draft 2020-12 definition/runtime tests |
| Resource catalog processing | Validated | Validated | Validated | Configuration-owned bound and truncation evidence |
| Feature lifecycle | Validated | Validated | Validated | Active/deprecated/removed metadata; non-failing findings |

## Known Limits

- Hosted operation still requires network-enforced egress and disposable worker isolation; application controls are not a hosted sandbox.
- HTTP subscription validation proves first-event acknowledgment on a bounded stream. It does not wait indefinitely for target-generated change events.
- Extension-specific behavior beyond schemas and declared negotiation requires an extension-owned versioned pack. Tasks is not treated as a core RPC namespace unless an extension contract defines one.
- Live provider certificates remain separate from deterministic CI and use temporary, deleted artifacts.
