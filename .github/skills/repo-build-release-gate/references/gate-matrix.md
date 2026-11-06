# MCP Validator Commit Gate Matrix

All changes run the mandatory gates in `repo-build-release-gate`. Add these checks by changed surface.

| Changed surface | Required additional local evidence |
| --- | --- |
| Core contracts, canonical result, evidence, coverage, policy, verdict | Focused unit/contract tests; full .NET suite; JSON schema compatibility; report snapshot/parity tests; architecture tests |
| Protocol/schema/version/extension packs | Official source/revision/digest review; schema registry and applicability tests; legacy/modern fixtures; official conformance differential where available; feature/coverage docs |
| Scoring, trust, reducers, calibration | Golden/differential/monotonicity tests; decision-policy/rule revision update; `scripts/validate-mutation.sh`; calibration and snapshot review |
| HTTP/auth/network/security | Focused adversarial tests for SSRF, redirect, DNS, origin, token/audience/issuer, timeout, cancellation, bounds, and redaction; authorized live certificate when required |
| STDIO/process execution | Process fixture tests; argument/environment isolation; cancellation/timeout/process-tree cleanup; no-shell assertion; package smoke |
| Reports/renderers/schemas | Hostile-text encoding; accessibility/offline checks; byte stability; Markdown/HTML/XML/SARIF/JUnit/JSON parity; schema snapshots; report snapshots |
| CLI options/config/action inputs | Parse/help/config precedence tests; dry-run plan; structured stdout/stderr; exit-code matrix; action contract/shell tests; docs/examples |
| npm wrapper | `npm ci`, `npm test`, `npm run typecheck`, `npm run build`, `npm audit --audit-level=high`, `npm pack --dry-run`; packed wrapper smoke when behavior changes |
| NuGet/package/container/standalone/release | Release build; pack; `scripts/smoke-distribution.sh` for applicable artifacts; package install smoke; checksums/SBOM/provenance/signature contract review; retry/promotion scripts |
| GitHub workflows/actions | `docker run --rm -v "$PWD":/repo -w /repo rhysd/actionlint:1.7.7@sha256:887a259a5a534f3c4f36cb02dca341673c6089431057242cdc931e9f133147e9`; distribution contracts; permission/environment/pin review |
| Documentation/Markdown | Repository markdownlint command from `.github/workflows/docs-lint.yml`; exact lychee command from the same workflow; examples checked against CLI behavior |
| Benchmarks/performance | Dedicated Release performance-budget tests and BenchmarkDotNet workload; no wall-clock unit-test gate substitution |
| Fuzz/parser/importer/hostile input | Focused property/fuzz replay; deterministic regression for every discovered counterexample; bounds and malformed corpus |

## GitHub-Only Or Environment-Dependent Gates

Record these as residual until required checks complete:

- CodeQL and dependency review;
- multi-OS/RID package and clean-install matrices;
- GitHub artifact attestations and protected-environment approvals;
- registry indexing/publish verification;
- container multi-architecture builds where local builders are unavailable;
- signing identities, transparency logs, and release publication.

Local readiness requires every locally reproducible prerequisite to pass. Merge/release readiness requires the protected GitHub checks and environment approvals as well.
