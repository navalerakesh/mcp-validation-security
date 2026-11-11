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

Positive modern HTTP probes use `server/discover`, not the removed `ping` method. For `2026-07-28`, present tool `structuredContent` may be any JSON value, including explicit null, and must satisfy an advertised output schema. The field is object-shaped in `2025-06-18` and `2025-11-25`; the validator does not impose that later rule on extension fields in `2024-11-05` or `2025-03-26`. Absence and explicit null are distinct. The dated modern embedded schema has SHA-256 `e8c8ae56f7dd8eab465fb2740db54042761c4f9e0e19b23b98504cec27f4c91d`; the June-2025 introduction schema has SHA-256 `1bcc5c08022443ffb12aed2da6b5d59fb74fc46c3dc0b59a805c0fb29f212f4d`.

`roots/list` and `sampling/createMessage` are server-to-client requests, not server capability probes. Optional-method observations separate declarations from attempted probes and supported/unsupported/unknown evidence. A modern logging declaration does not prove a logging operation was exercised. Authentication failures, unavailable responses and local policy denial do not become advertised-capability mismatch findings.

Optional method evidence requires a correlated request ID, a valid dated response envelope and the method's result schema. Logging probes send a schema-valid level. Completion preparation currently selects an observed prompt argument from bounded catalog metadata; it never invents a reference or sends null parameters. A missing usable prompt reference, including resource-template-only completion surfaces, leaves completion unattempted and unknown rather than unsupported. Preparation inspects at most 4,096 argument entries; failures identify their preparatory method separately from the unattempted completion method.

SDK client caching uses one transient configuration/credential snapshot and unambiguous fingerprints of effective routing and credential fields. Caller cancellation stops that caller's wait; factory disposal owns cancellation and draining of initialization and invalidated cleanup. Cleanup failures remain errors rather than successful disposal receipts.

Ordinary STDIO RPCs retain selected protocol and process identity, reject unrelated response IDs, and tolerate at most 64 interleaved notifications within the response byte budget and one overall response timeout. Cancellation or an incomplete exchange makes the session unavailable for subsequent ordinary/raw/structured probes until explicit restart. Timeout recovery requires a correlated drained response and cannot convert parser-boundary evidence into recovery credit.

Configured Safe mode admits discovery and lifecycle operations, not active/raw protocol probe purposes merely because their payload names a discovery method. SDK HTTP envelopes are bounded and checked before dispatch; passive GET/HEAD and bodyless session-bearing DELETE cleanup remain available. Standard mode retains explicitly selected active behavior; elevated mode requires risk acknowledgement. These application checks are not a network sandbox or proof that arbitrary generated tool arguments are harmless.

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
| Extension settings | N/A | Validated | Validated | Typed Apps MIME types and Skills directory-read setting; arbitrary settings are not persisted |
| MCP Apps Stable | N/A | Validated | Validated | Safe linkage/settings checks; standard bounded UI reads and HTML/CSP/permission analysis |
| MCP Skills Beta Preview | N/A | Validated | Validated | Catalog/manifest security plus explicitly authorized bounded content verification; non-blocking extension verdict |
| Official conformance | Imported | Imported | Imported | Pinned wrapper provenance, target/revision binding, no duplicated wire scenarios |
| Tasks extension | Legacy task capabilities where declared | Validated negotiation | Validated negotiation | Dedicated stable finding; no invented RPC surface |
| Tool input/output schemas | Validated | Validated | Validated | Draft 2020-12 definition/runtime tests |
| Resource catalog processing | Validated | Validated | Validated | Configuration-owned bound and truncation evidence |
| Feature lifecycle | Validated | Validated | Validated | Active/deprecated/removed metadata; non-failing findings |

## Known Limits

- Hosted operation still requires network-enforced egress and disposable worker isolation; application controls are not a hosted sandbox.
- HTTP subscription validation proves first-event acknowledgment on a bounded stream. It does not wait indefinitely for target-generated change events.
- Tasks is not treated as a core RPC namespace unless promoted into the selected dated core revision. Official Tasks evidence can be imported, but native lifecycle duplication remains intentionally deferred.
- Apps analysis proves server-delivered metadata/content properties only. It does not claim host sandbox, CSP header, permission-policy, postMessage, approval, or teardown enforcement.
- Skills Preview proves server catalog/content consistency and detects advisory content risk. It does not prove host approval, activation, cache isolation, origin tagging, or digest enforcement.
- Live provider certificates remain separate from deterministic CI and use temporary, deleted artifacts.
