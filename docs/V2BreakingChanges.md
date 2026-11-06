# V2 Intentional Breaking Changes

## Purpose

MCP Validator V2 intentionally breaks the unreleased V1 semantic contract to establish a provider-dependency-grade assurance model. The project has no production users requiring runtime compatibility, so retaining ambiguous score and verdict behavior would create long-term risk without user value.

This is an engineering migration record, not a public deprecation notice.

## Removed Contracts

- Global `ComplianceScore` as an automation gate
- L1-L5 trust level as the headline decision
- `BaselineVerdict`
- Generic overall pass/fail status as the only result
- Heuristic-driven core conformance rejection
- Cross-policy or cross-major baseline comparison
- Runtime loading of V1 canonical artifacts
- V1 integer-enum compatibility and result aliases

## Replacement Contracts

- Typed claims with authority, requirement level, applicability, confidence, and evidence references
- Independent core, security, operations, extension, and client assessments
- Explicit certificate eligibility
- Versioned assurance profiles and calibration thresholds
- Stable V2 exit semantics
- Canonical V2 JSON as the sole automation source

## User-Visible Impact

- Existing `1.x` baselines cannot be compared with V2 results.
- Exact scores, verdicts, decision IDs, audit hashes, and exit codes change.
- CI must gate on `policyDecision` and `certificateEligibility`, not score or trust level.
- Inventory runs can complete successfully while remaining certificate-ineligible.
- Heuristic risks are review findings unless explicitly promoted by policy.
- Offline report loading accepts V2 artifacts only.

## Migration Policy

No dual runtime contract or parallel validation workflow is maintained. Existing probes feed the V2 claim pipeline. Historical `1.x` reports remain readable as static files but are not accepted as V2 baselines or certificate evidence.

The release carrying this contract must use semantic version `2.0.0` and include schema, CLI, Action, npm wrapper, documentation, and artifact migration notes in the same commit series.
