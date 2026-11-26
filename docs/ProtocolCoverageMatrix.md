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
| Explicit synthetic tool continuation | Unsupported | Bounded operator fixtures | Bounded operator fixtures | Exact request maps, request-local capabilities, opaque-state echo, fresh IDs, privacy and cancellation; no automatic model/consent/filesystem activity |
| Explicit synthetic prompt continuation | Unsupported | Bounded operator fixtures | Bounded operator fixtures | Same bounded continuation engine; missing/mismatched replies remain incomplete |
| Completed prompt content | Concrete dated schema | Concrete dated schema | Concrete dated schema | Generated and explicit requests, including post-continuation replies; response-union acceptance alone cannot establish completion |
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

## Recovery Capability Acceptance Ledger

The M01-M24 rows reconcile the recovery plan with the actual support boundary. They are not blanket certification of every behavior named in the plan. Native evidence, imported official evidence, controlled application evidence and unimplemented host behavior remain separate. The authority is the selected dated schema and pinned official requirement set, not this table. Final exact-source qualification is deferred until UI acceptance and review; no mutation gate is implied by these local receipts.

| ID | Supported evidence owner / scope | Remaining boundary |
| --- | --- | --- |
| M01 Revision/lifecycle | Dated registry, bootstrap and modern/legacy fixtures | Native platform and packaged final matrix |
| M02 JSON-RPC | Correlated ordinary requests, bounded framing, pinned official wire checks | Official missing/pending scenarios never become native pass credit |
| M03 Modern HTTP metadata | Governed SDK/raw request paths and receiver controls | Real intermediary deployments are not certified by local receivers |
| M04 HTTP/SSE | Bounded response/first-event validation and imported wire scenarios | Continued subscriptions and arbitrary stream topologies are not claimed |
| M05 STDIO | Owned process fixtures, request accounting, cancellation and cleanup | Native Windows/Linux execution receipts |
| M06 Tool schema/results | Dated schema validation, bounded inspection and explicit tool inputs | Inspection limits and pending official checks remain visible |
| M07 Tool effects/policy | Explicit admission and signed identity/state controls | No arbitrary-tool safety inference or production-independent observer claim |
| M08 Resources/templates | Independent catalog identities, all-content validation, bounded reads; imported template scenarios | Native resource continuation and exhaustive template expansion are not implemented |
| M09 Prompts | Independent name catalog, argument checks and explicit synthetic continuation | General interactive clients, automatic consent and model inference are excluded |
| M10 Completion | Correlated dated result checks using an observed prompt argument; imported scenarios | Resource-template-only preparation remains unattempted/unknown |
| M11 Pagination | Bounded native catalogs, cursor/page accounting and independent item completeness | Changed mutable catalogs are not automatically protocol violations |
| M12 Caching | Native list metadata/fingerprints and imported cache scenarios | Metadata is not proof of cross-principal runtime isolation |
| M13 MRTR | Exact finite tool/prompt reply plans; state echo, fresh IDs, capabilities and budgets | Resource replies remain input-required; imported wire evidence keeps its own scope |
| M14 Elicitation | Explicit synthetic form replies and imported scenarios | No URL-mode browser interaction or automatic user approval |
| M15 Roots/sampling/logging | Dated role/applicability checks and synthetic request-local replies | No real filesystem discovery, paid inference or sampling-tool execution |
| M16 Progress/subscriptions/cancellation | Bounded acknowledgment, correlation and owned cancellation controls | No rollback inference or indefinite target-event wait |
| M17 Core authorization | Governed outbound policy, metadata/synthetic checks and signed actor/scope/object matrix | Live issuer refresh/rotation and production denial observations need admitted evidence |
| M18 Enterprise auth extensions | Existing metadata validation and imported/controlled evidence lanes | Metadata is not client-credentials or ID-JAG acquisition proof |
| M19 Apps | Existing server metadata and bounded content-security pack | Host sandbox/postMessage/permission enforcement requires a host harness |
| M20 Skills | Existing Preview catalog/manifest/content pack | Host activation, cache tampering and fresh approval are not server-read claims |
| M21 Tasks | Negotiation plus pinned imported extension scenarios | Native lifecycle duplication remains deliberately deferred |
| M22 Server Cards | Watch only; conflicting maturity authority retained | No native server-card pack or permission to launch declared endpoints/packages |
| M23 Relay | Signed two-boundary import, real owned HTTP chain and orchestrator/report controls | Single front/backend only; no mixed-transport, fan-out or production-independence certificate |
| M24 Host/agent behavior | Dated ClientProfiles interpretation of neutral evidence | No actual client/agent launching or model-safety certificate |

### Candidate Adoption Decisions

These are reviewed adoption boundaries, not claims that newer SDKs or roadmap proposals have been implemented. Candidate versions/lifecycle signals come from the dated recovery study and [extension integration proposal](Design/ExtensionIntegrationProposal.md); they require renewed authoritative verification before adoption. Runtime pins remain in their owning manifests, not duplicated into a second execution registry.

| ID | Candidate | Decision / required qualification |
| --- | --- | --- |
| B01 | C# SDK 2.4.0 study | Production remains pinned to 2.2.0. No upgrade claimed; candidate modern/legacy, cancellation, discovery and auth corpus required before a dependency change |
| B02 | Split TypeScript SDK 2.4.0 study | Wrapper remains pinned to `@modelcontextprotocol/sdk` 1.32.2. Split packages are not a version substitution under the old package name |
| B03 | Tasks Beta | Import the existing pinned official evidence; do not duplicate lifecycle behavior or promise core promotion |
| B04 | Skills Beta | Keep the current Preview overlay and its authoritative pins; host activation remains separate |
| B05 | Server Cards | Watch; resolve Beta/Experimental authority conflict before support |
| B06 | Client credentials / enterprise-managed auth | Stable status does not establish flow coverage. Acquisition/refresh/denial/effective-caller evidence remains required for such claims |
| B07 | Draft CIMD / optional metadata corrections | Draft-only compatibility study; do not rewrite dated released schemas |
| B08 | DPoP / workload identity / delegation | Watch authoritative proposals; no new universal core requirement |
| B09 | HTTP-over-stdio / ETags / push delivery | Watch; no released-contract or supported-runtime claim |
| B10 | Progressive discovery / result redesign / file operations | Measure existing bounded catalogs; no invented mandatory search/upload RPC |

No candidate is auto-promoted. An adoption change needs immutable source pins, isolated positive/negative/unavailable fixtures, interoperating receipts, privacy/budget checks, report/CI compatibility and rollback notes. Existing [Dependabot configuration](../.github/dependabot.yml) schedules weekly NuGet/npm/Actions updates; that is not monitoring of protocol lifecycle/proposal authority. Broader upstream monitoring, candidate SDK experiments and enterprise/host harnesses are not completed by this ledger.

## Known Limits

- Hosted operation still requires network-enforced egress and disposable worker isolation; application controls are not a hosted sandbox.
- HTTP subscription validation proves first-event acknowledgment on a bounded stream. It does not wait indefinitely for target-generated change events.
- Tasks is not treated as a core RPC namespace unless promoted into the selected dated core revision. Official Tasks evidence can be imported, but native lifecycle duplication remains intentionally deferred.
- Apps analysis proves server-delivered metadata/content properties only. It does not claim host sandbox, CSP header, permission-policy, postMessage, approval, or teardown enforcement.
- Skills Preview proves server catalog/content consistency and detects advisory content risk. It does not prove host approval, activation, cache isolation, origin tagging, or digest enforcement.
- Live provider certificates remain separate from deterministic CI and use temporary, deleted artifacts.
