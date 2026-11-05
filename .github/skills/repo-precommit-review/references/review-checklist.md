# MCP Validator Review Checklist

## Product Truth

- Does every decision derive from typed evidence and coverage?
- Can missing, skipped, unavailable, blocked, auth-required, inconclusive, or not-applicable evidence be mistaken for a pass?
- Are deterministic verdict, trust score, client interpretation, external conformance, and extension readiness separate?
- Are authority, requirement level, revision, confidence, and remediation explicit?

## Architecture And Single Ownership

- Is the change in the owning project and abstraction?
- Did the review search for an existing constant, registry, comparator, pack, rule, parser, fixture, projection, or script?
- Does the change create parallel orchestration, duplicate validation, copied scoring, or renderer-owned policy?
- Are official conformance checks reused rather than reimplemented?

## Protocol And Extension Fidelity

- Is applicability tied to the negotiated/selected dated revision?
- Is an extension stage current and sourced from the official lifecycle/specification rather than a historical SEP?
- Are legacy and modern method names/version semantics kept separate?
- Are undeclared optional features not-applicable rather than passing?

## Security And Reliability

- Are all external inputs bounded, schema-validated, redacted, and safely encoded?
- Are outbound requests, redirects, DNS, auth metadata, resource reads, and auxiliary origins governed?
- Is mutable state isolated per run and safe under cancellation/concurrency?
- Are subprocesses shell-free, environment-allowlisted, bounded, and cleaned up?
- Can active probes mutate state, create tasks, execute tools/scripts/HTML, or consume excessive resources without explicit authority?

## Durable Outputs

- Does canonical evidence drive every renderer and automation output?
- Are JSON/schema compatibility, SARIF/JUnit/XML parity, HTML accessibility/encoding, audit digests, baseline compatibility, and attestation subjects addressed?
- Does offline replay remain target-free and deterministic?

## Tests And Release Impact

- Is there a focused falsifying test for the root hypothesis?
- Are negative, malformed, unavailable, auth, timeout, cancellation, and hostile-content cases covered where relevant?
- Do snapshots change only for reviewed semantic reasons?
- Are package locks, SBOM/provenance/signature inputs, action/container/standalone packaging, and distribution contracts affected?
- Are docs, feature/coverage matrices, schemas, and changelog aligned with shipped behavior?
