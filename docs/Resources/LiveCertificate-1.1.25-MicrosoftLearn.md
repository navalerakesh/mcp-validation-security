# MCP Validator 1.1.25 Microsoft Learn Live Certificate

Date: 2026-08-19 (local), 2026-08-20 UTC

Validator: `mcpval 1.1.25`

Base source revision: `3554e7556d05623d2e4c4df122b78a80ad45db50`

Toolchain: .NET SDK `8.0.419`, Node `24.19.0`

Target: `https://learn.microsoft.com/api/mcp`

Target implementation: Microsoft Learn MCP Server `1.0.0`

This certificate was produced from the reviewed renewal worktree before commit. It is live interoperability evidence, not provenance for a published immutable commit. Release CI must rerun it or equivalent checks after the release commit is frozen.

## Health

The public health check passed with:

- safe execution mode;
- strict redaction;
- HTTPS origin `https://learn.microsoft.com:443`;
- private-address blocking;
- bounded requests and responses.

## Discovery

The initial automatic modern discovery exposed a compatibility defect: the healthy endpoint returned a normal unsupported/error response for `server/discover`, but SDK response binding surfaced it as an unexpected exception.

The release candidate was corrected so automatic mode records typed modern-discovery failure, negotiates through legacy initialize, and returns the observed capabilities. Focused regressions pass 24/24. The rebuilt live discovery then passed with exit code `0` and reported:

- negotiated protocol `2025-06-18`;
- HTTP transport;
- validated tool, resource, and prompt list capabilities;
- three discovered Microsoft Learn tools.

## Full Safe Validation

The aligned run used:

```bash
mcpval validate \
  --server https://learn.microsoft.com/api/mcp \
  --access public \
  --mode safe \
  --policy advisory \
  --protocol-era legacy \
  --mcpspec 2025-06-18 \
  --max-requests 128 \
  --max-concurrency 2 \
  --timeout 30 \
  --redact-level strict \
  --trace off \
  --output "$TMPDIR/mcpval-final-review-matrix/microsoft-learn"
```

Observed result:

- handshake healthy, Microsoft Learn MCP Server `1.0.0`;
- three tools discovered and metadata/schema validated; this safe run does not claim tool invocation;
- resource and prompt list requests successful with empty catalogs;
- all documented client profiles compatible;
- canonical status `PartiallyCompleted`, evaluated-category compliance score `100.0`;
- baseline/protocol/coverage verdicts `ReviewRequired`;
- evidence coverage `8/16` (`0.5`), confidence `medium` (`0.6656`);
- benchmark trust `L3`, explicitly limited by incomplete evidence with protocol, security, and operations unevaluated.

The `100.0` score covers evaluated categories only. It is not a complete-conformance or release decision and does not offset missing evidence. The nonzero policy exit is expected for safe mode. Active malformed-message, error-code, notification, security, performance, and error-handling probes are intentionally not run, so the validator correctly refuses to claim complete conformance. It is not a target failure and must not be represented as a passing certification.

## Artifact Integrity

Temporary artifacts are under:

```text
$TMPDIR/mcpval-final-review-matrix/microsoft-learn
```

The output folder contains canonical JSON, Markdown, HTML, SARIF, JUnit, client-profile summary, and audit manifest. Its parent temporary root contains the console transcript and exit code.

Validation ID: `b4250cea-fd92-46bf-8697-62f54d6e6796`.

The audit manifest binds the final bytes of all six generated subject artifacts. Every recorded SHA-256 digest was recomputed and matched:

- profile summary: `a4690764e9c0af932360d25c437d941f169cb3182b6175d6a3765e9b5dee00f7`;
- HTML: `ce8bb40d3577f6036476361168058ec6f899172975e5c45613d557125e744d65`;
- Markdown: `d2080a776a1ef16ce857ab520e36781b42424506adead4da1aa1372b63ba5ba3`;
- canonical JSON: `2714491999135c5ec95d68afcee347d89e9f60a132b05ab4dc17e7dcbc39d49e`;
- JUnit: `ea5c3def1baaba0c670448623503e72fb9577a4839dc440f23c23f75691611f8`;
- SARIF: `4b6780552e6e02af69ce0a0539866fb42931cd3543a6dad2ab29ed0cbe65fbc9`.

Canonical redaction checks confirmed all transport `rawContent` values are null and common bearer/token/password/secret patterns are absent. JSON and XML parsed successfully. Profile summary, SARIF, JUnit, Markdown, and HTML preserve the canonical policy hold, deterministic verdicts, and incomplete-evidence semantics. False `invoked successfully` and `completed without blocking findings` claims are absent.

## Report UI Validation

The final hashed HTML was rendered with Playwright at `1440x900`, `1024x768`, and `390x844`. All three viewports had zero document overflow, zero elements outside the viewport except content inside intentional table scroll containers, zero clipped text, zero overflowing status chips, one H1, and no empty links. Dense evidence tables scroll horizontally at tablet and use stacked records or bounded scrolling on mobile. Accessibility inspection confirmed the page title, `Release Hold` H1, ordered H2 sections, table headers, authoritative verdict, and incomplete-evidence trust warning are exposed as readable text. The reader key exposes 13 definitions, expands remediation priorities and trust levels, and defines protocol, authority, percentile, unit, client, and identifier shorthand. Desktop and mobile screenshots were reviewed after the metric checks.

Live artifacts are intentionally not committed. The historical `PublicReports` captures are staged for deletion under the sensitive-artifact policy; this reviewed summary is the permanent repository record.

## Repository Validation After Live Fix

After correcting discovery fallback and report semantics/UI:

- 912/912 .NET Release tests passed;
- 14/14 npm tests passed;
- build completed with zero warnings and zero errors;
- NuGet and npm audits were clean;
- distribution contracts and static pipeline checks remained clean.

## Disposition

The code is a release candidate for the documented local/CI validator product. It is not a hosted Fleet readiness certificate or formal accreditation claim. Open-source publication remains blocked until forbidden historical captures are purged (or a clean repository is used), hosting protections are verified, and the remote release workflow passes for the sanitized immutable commit.
