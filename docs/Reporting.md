# Reports And Evidence

MCP Validator renders every report from one canonical `mcpval.assurance-result` JSON document. Renderers do not contact the target, rerun probes, or recompute validation decisions.

> **Breaking changes:** Report consumers upgrading to 3.0 must handle nullable measurements and per-run artifact paths. See the [upgrade checklist](SchemaCompatibility.md#upgrading-to-30); the package version is not the artifact schema version.

## Artifact Set

A validation run with `--output <directory>` writes:

Artifacts are committed together beneath a per-run `mcp-validation-*` subdirectory. Use returned paths rather than assuming a flat output directory; `.pending-*` staging directories are not completed bundles.

| Artifact | Purpose |
| --- | --- |
| `*-result.json` | Canonical typed evidence, claims, independent assessments, policy decision, and certificate eligibility |
| `*-report.html` | Accessible, print-ready human report with progressive disclosure |
| `*-report.md` | Portable human-readable review artifact |
| `*-results.sarif.json` | Actionable non-success claims for CI and code scanning |
| `*-results.junit.xml` | Assurance-dimension suites; incomplete evidence is skipped, never passed |
| `*-audit.json` | Execution plan, artifact digests, redaction mode, and operational provenance |
| `*-profile-summary.json` | Optional interpreted client compatibility results |

Experimental model evaluation and official conformance wrappers remain separate companion artifacts with their own provenance.

## Reading The HTML Report

The HTML report is ordered for decision review:

1. **Decision and scope:** target-led MCPVal masthead, recorded UTC time, profile and detail level. A dominant decision panel keeps execution, assurance, policy and certificate disposition distinct in a side column on desktop and stacked facts on mobile. Recorded interruption takes precedence. Up to two supporting cards distinguish agent advertisement/permission/observed execution and the next ranked action.
2. **Coverage:** independent assessment rows with named denominators and limitations. Native disclosures contain outcome counts, eligibility reasons, the decision trace and supporting measurements.
3. **Priority actions:** verb-led ordered remediation or evidence-collection rows, affected components, impact and links to domain/claim records where available. The opening card links to the first row using the same ranking. Owners and deadlines are not invented.
4. **Domain evidence:** visible summaries with named, bounded native disclosures for the claim index and existing bootstrap, protocol, authorization, tool/resource/prompt, extension, compatibility and runtime evidence. Expand/collapse controls are an optional local-script enhancement.
5. **Method and provenance:** run identity, profile/spec/source revisions and producer information.
6. **Claims appendix:** every recorded claim with its stable ID, outcome, authority and references, behind a clearly labeled disclosure after provenance. Both detail levels include a collapsible glossary; full adds detailed measurement sections. Main review sections start open and can be collapsed independently; supporting records start closed.

Inventory is an assessment product, not a failed certificate attempt. Its report says that the assessment completed and that a provider certificate was not issued by the selected profile. Missing or skipped active evidence remains incomplete and never earns pass credit.

Full detail is not full execution: `--report-detail full` exposes recorded evidence but cannot turn Safe inventory into comprehensive assurance. For prerequisite collection, active owned examples and signed identity-control reports, follow [assessment setup](../Mcp.Benchmark.CLI/README.md#choose-report-detail-and-assessment-scope-separately).

When inventory evidence is recorded, the opening reason explains that scope rather than repeating certificate non-issuance. Recorded interruptions, blocking findings and failed policy still take precedence; an empty artifact does not acquire a fabricated completion explanation.

The report follows the system light/dark preference and offers a local System/Light/Dark selector without browser storage. One shared palette governs cards, tables, text, controls and statuses. A CSP-hash-allowlisted local script provides filtering, in-place action evidence and deliberate deep-link navigation. There is no external active content. All claims remain in the static document without JavaScript. Links reveal the relevant disclosure, not every unrelated section. Links to included observations reach their exact rows; references without included payload are labeled as canonical references, not fabricated observations.

Main review sections have a consistent boundary, default-open native headings and flat supporting disclosures rather than repeated nested cards. The full row opens with a click, Enter or Space. A leading chevron and desktop Expand/Collapse hint clarify the affordance without contributing to its accessible name. Ordinary toggles do not navigate. Print retains the real section headings and all included evidence.

Action rows offer **Read evidence here**. This displays the existing canonical claim locally, without changing the URL, clearing filters or creating a second claim projection. The preview is removed on close and omitted from print; the canonical appendix remains complete. Without JavaScript, the same disclosure provides an explicit canonical-record link. Bulk supporting-evidence controls do not collapse the main reading path or instantiate every action preview.

The capability card labels **Advertised**, **Permitted** and **Observed** separately. Advertisement is not inferred from a nonempty catalog. Safe mode is discovery/static scope; Standard/Elevated mode indicates policy-gated active checks, not blanket tool permission. Snapshot-specific execution remains labeled with that scope, and missing runtime evidence is not converted into a successful invocation.

Zero catalog counts require a completed enumeration or a successful snapshot listing; an unpopulated default counter remains unavailable. Later successful execution records and a separate snapshot failure can both appear, without one hiding the other.

Recorded resource-template counts are shown as enumeration evidence, not successful resource reads or invented per-template records. Client comparisons name their dated requirement sets and explain that interpretation is not execution inside the client. Recorded request percentiles, sample windows, queue time, throughput and retry context are available even in minimal detail; discovery timings are not relabeled as load-test measurements.

Client interpretation retains exact, case-sensitive catalog tool identities, including names containing words such as `auth`. Only exact historical synthetic records are excluded when not confirmed by a recorded catalog. A catalog count alone cannot establish complete metadata, and skipped, authentication-required, unavailable or erroneous contract checks remain unverified rather than satisfied. Required warnings are not described as passed advisory checks.

Unknown discovery is not an absent capability: client requirements remain unverified until catalog or capability-declaration evidence establishes presence or absence. A completed empty catalog can establish absence; rejected items and partial enumeration cannot. Snapshot listing success alone does not prove a complete empty catalog; positive snapshot counts establish presence only. Call/read requirements additionally need successful per-item operation evidence with matching identities, not just passed metadata or category counts. Known contract findings remain visible even when no component identity was recovered, and a partial tool catalog cannot establish that every tool's metadata is complete.

Resource templates are a separately enumerated surface, not successful resource reads. Raw observed template counts include rejected entries; only recovered template evidence establishes an available surface, and structured template violations remain failures. An empty static resource list does not establish resource absence when template discovery is missing or templates were recorded. Successful static reads also cannot complete the combined resource contract while template discovery is unknown or blocked. This follows the separate `resources/list` and `resources/templates/list` surfaces in the [dated MCP resource specification](https://modelcontextprotocol.io/specification/2025-11-25/server/resources#resource-templates). Unverified catalog requirements include an actionable discovery follow-up rather than only a warning label.

The applied client-profile pack records implementation revision `2026-11-26.1`; client requirement-document revisions remain separately dated. Re-evaluation replaces that pack's earlier interpretation revision without deleting baseline evidence or rewriting neutral assurance.

Performance facts describe the recorded workload. The renderer does not invent a universal latency/error-rate service target or derive a new score from those measurements. Recorded calibration overrides and findings remain available with their own context.

The short run reference in the footer is the first 12 hexadecimal characters of SHA-256 over the recorded validation ID. It is a convenience identifier, not an artifact integrity digest; retain the full run identity and audit manifest.

### Action and scope interpretation

Priority groups use typed assessment domains and source authority, not words in finding prose or URLs. Recorded severity is preserved; absent or unrecognized severity is explicit. Distinct components and rule/claim identities remain separate actions even when their recommended text matches, and the complete action projection has no five-item cutoff.

Optional external checks and unselected extensions use the same selected-scope classifier as policy. Their findings remain visible in a separate final priority group rather than becoming the first required remediation. A successful comprehensive opening summarizes its recorded independent assessments and baseline coverage; a non-issuing profile is not a failed certificate attempt.

Authentication discovery is retained as authentication evidence, not as a discovered tool or successful invocation. Historical authentication-only rows do not count toward the HTML execution summary when identified by their authentication fields or status.

## Outcome Semantics

| Outcome | Meaning |
| --- | --- |
| `satisfied` | Applicable evidence met the typed requirement |
| `reviewRequired` | Advisory, interpreted, or policy evidence needs human review |
| `incomplete` | Applicable evidence was unavailable, skipped, blocked, or not performed |
| `violated` | Deterministic evidence contradicted a requirement |
| `error` | Validation or evidence integrity failed |
| `notApplicable` | The requirement does not apply to the observed target/profile |

A descriptive category score cannot override these outcomes or the independent assessments.

### Automation and diagnostic counts

Use the canonical selected-profile/policy disposition and recommended exit to decide whether a gate passed. JUnit cases and SARIF results are diagnostic projections, not alternative gate calculators. For example, a comprehensive assessment can satisfy its selected requirements while JUnit retains failures from optional external MAY checks. Runner checks, claims and diagnostics have different denominators; their counts must not be equated or genuine optional failures discarded.

## Offline Rendering

Render a saved canonical result without target access:

```bash
mcpval report \
  --input ./reports/mcp-validation-result.json \
  --format html
```

The persisted run schema version is authoritative during offline rendering. Renderer defaults are used only for legacy artifacts that do not contain canonical schema provenance.

Use `--report-detail minimal` for an executive report or `full` for the complete evidence appendix.

## PDF export

PDF remains an export of HTML, not a native `mcpval report --format pdf` format. The supported repeatable export uses the development-only pinned Playwright Chromium renderer:

```bash
npm ci --prefix scripts/report-quality --ignore-scripts
cd scripts/report-quality
PLAYWRIGHT_SKIP_BROWSER_GC=1 npx playwright install chromium
cd ../..
node scripts/report-quality/export-pdf.mjs ./report.html ./report-A4.pdf A4
node scripts/report-quality/export-pdf.mjs ./report.html ./report-Letter.pdf Letter
```

Use the [contributor environment](ContributorEnvironment.md) and Node 24. On Linux install browser dependencies with `playwright install --with-deps chromium`.

The verified configuration is Playwright **1.65.1**, Chromium **159.0.8154.6**, A4 or US Letter, scale **1**, backgrounds **off**, browser headers/footers **off**, and **14 mm CSS margins**. The export disables JavaScript and blocks HTTP(S) loading. CSS forces a light print palette, expands included disclosures and restores filtered claims. Decorative disclosure controls and transient previews are omitted; real section headings, domain captions and supporting fields remain. The page margins contain the short run reference and **Page X of Y**; the in-content footer retains producer/version and reference.

The [report checks](../CONTRIBUTING.md#report-changes) generate both papers and detail levels, reconcile printed claim IDs with canonical JSON, extract selectable page text, verify every numbered footer, and retain representative rasterized pages. Geometry/page totals are browser, font and platform dependent; the receipt records the actual totals.

**Browser fallback:** opening the local HTML and using the browser's Print/Save as PDF can produce a review copy. Turn off browser headers/footers and backgrounds, select the paper size and use 100% scale. Other engines may not support CSS margin counters or collapsed-content printing; an unnumbered fallback is not a qualified archival export. Native print dialogs and screen-reader acceptance need separate human verification. No PDF/A, tagged-PDF or universal accessibility certification is claimed.

## Local MCP Wrapper

`mcpval-localmcp` returns canonical JSON evidence through MCP `structuredContent`. It intentionally does not place large HTML or Markdown reports into model context. Temporary CLI report artifacts are deleted after each tool execution.

Generate durable human reports with the CLI `validate --output` or `report` command. Provider assurance through the wrapper is available only when the operator configures all signed trust inputs; the wrapper does not accept evidence paths or output directories from agent arguments.

## Sharing Reports Safely

- Review endpoint names, server metadata, findings, and remediation text before external distribution.
- Canonical transport `rawContent` is redacted from persisted artifacts.
- Credentials, authorization headers, cookies, tokens, and interactive auth state must never appear in reports.
- Preserve the JSON result and audit manifest with human reports when reproducibility matters.
- Treat target-controlled descriptions, schemas, prompts, resources, and findings as untrusted data even when safely encoded in HTML.
