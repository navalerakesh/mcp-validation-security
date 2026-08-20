# Authentication Coverage

This matrix separates credential acquisition from server conformance. Acquiring or failing to acquire a credential is client-side evidence; it is not proof that an MCP server is conformant or nonconformant. No credential value, authorization code, PKCE verifier, or OAuth state belongs in a persisted report.

## Acquisition And Registration

| Surface | Engine support | Deterministic certificate | Current boundary |
| --- | --- | --- | --- |
| Supplied bearer credential | `tokenRef` environment references; legacy inline input is redacted on persistence | Secret-reference resolution, clone, adapter, and artifact tests | Possession is not authorization correctness |
| Noninteractive provider | `INoninteractiveCredentialProvider` returns a `SecretRef` | Contract compiles; raw token is excluded from the port | No built-in CI/orchestrator provider package yet |
| Azure/GitHub CLI strategy | Existing provider-specific acquisition adapter | Command construction and noninteractive behavior tests | External CLI/session behavior is not a server verdict |
| Device code | Existing configured MSAL provider | Local provider behavior only | Provider-specific; no callback contract is imposed |
| Authorization code + PKCE | Run-owned coordinator, cryptographic state, S256, exact redirect, RFC 9207 issuer checks, expiry, one-time completion | Positive, redirect/state/issuer mismatch, replay, expiry, redaction, and provider-isolation tests | No built-in browser callback host or token-exchange provider |
| Static client registration | Typed client ID and exact registered redirect configuration | Positive evidence and invalid configuration tests | Registration at the authorization server is operator-owned |
| Client ID metadata document | HTTPS document fetch and exact `client_id`/redirect evaluation | Positive fetch path and identity mismatch tests | Private-key client authentication is not executed |
| Legacy dynamic registration | Explicit declared mode | Declared-not-evaluated evidence test | No live registration request is sent |
| Enterprise-managed authorization | ID-JAG grant-profile discovery and JWT-bearer consistency evaluation | Positive and contradictory metadata tests | ID-JAG exchange is not executed |

## Server Conformance

| Behavior | Evidence | Status |
| --- | --- | --- |
| `401`/`403` rejection behavior | Safe read-only no-token, malformed, expired, scope, permission, query-token, controlled-audience, and supplied-token probes | Validated for observed protected discovery methods |
| `WWW-Authenticate` | Bounded grammar-aware Bearer parsing, exact parameters, complete `403 insufficient_scope` checks, provider-requested scope union, least privilege, and one-retry maximum | Validated when a noninteractive provider can acquire the controlled credentials |
| Protected resource metadata | Challenge-selected URL or ordered endpoint-path/origin-root RFC 9728 fallback, governed fetch, resource/server/bearer-method checks | Validated |
| Authorization server metadata | Ordered RFC 8414 and OpenID Connect path-insertion/path-appending variants, exact issuer, HTTPS endpoints, S256 | Validated |
| Resource and audience | Canonical resource matching plus wrong-audience and query-token discriminating probes | Validated for observable behavior |
| Token passthrough/confused deputy | Query-token acceptance plus opt-in controlled valid wrong-audience token rejection/acceptance | Validated as acceptance indicators; unchanged downstream forwarding is not claimed without a controlled sink |
| Enterprise ID-JAG | Grant-profile and JWT-bearer metadata consistency | Discovery only; no live token exchange claim |

## Evidence Rules

- Missing credentials produce acquisition or coverage evidence, never an automatic server failure.
- Metadata blocked by governed egress is reported as blocked coverage, not invalid server metadata.
- Secret references may be persisted; resolved values may not.
- Authorization state, authorization codes, PKCE verifiers, identity assertions, ID-JAGs, and access tokens are transient secrets.
- Live provider certificates are separate from deterministic CI and use temporary, deleted artifacts.
- Controlled audience and step-up probes require `conformanceCredentials` resource/scope declarations plus an `INoninteractiveCredentialProvider`; providers return transient secret references. Synthetic invalid JWTs never count as authoritative audience evidence.

## Extension Identifiers

- Enterprise-managed authorization extension: `io.modelcontextprotocol/enterprise-managed-authorization`
- ID-JAG grant profile: `urn:ietf:params:oauth:grant-profile:id-jag`
- JWT bearer access-token grant: `urn:ietf:params:oauth:grant-type:jwt-bearer`

Normative extension reference: <https://modelcontextprotocol.io/extensions/auth/enterprise-managed-authorization>
