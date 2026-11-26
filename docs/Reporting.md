# Reports And Evidence

MCP Validator renders every report from one canonical `mcpval.assurance-result` JSON document. Renderers do not contact the target, rerun probes, or recompute validation decisions.

## Artifact Set

A validation run with `--output <directory>` writes:

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
6. **Claims appendix:** every recorded claim with its stable ID, outcome, authority and references, behind a clearly labeled disclosure after provenance. Full detail includes detailed measurement sections and the glossary; minimal omits them. Detail level controls included content, not whether all sections start expanded.

Inventory is an assessment product, not a failed certificate attempt. Its report says that the assessment completed and that a provider certificate was not issued by the selected profile. Missing or skipped active evidence remains incomplete and never earns pass credit.

The light theme uses system fonts, white supporting cards, a cool-gray canvas, restrained status accents and text labels that remain meaningful in grayscale. A CSP-hash-allowlisted local script provides filtering and deep-link disclosure. There is no external active content. All claims remain in the static document without JavaScript. Links reveal the relevant disclosure, not every unrelated section. Links to included observations reach their exact rows; references without included payload are labeled as canonical references, not fabricated observations.

Domain groups have a fine boundary; routine source/reference disclosures are flat rows separated by quiet rules, not individual cards. The full row opens with a click, Enter or Space. A right-aligned chevron indicates closed/open state without contributing to its accessible name. Section headings and key limits remain visible; print removes the decorative controls while retaining captions, headings and all included evidence.

The capability card labels **Advertised**, **Permitted** and **Observed** separately. Advertisement is not inferred from a nonempty catalog. Safe mode is discovery/static scope; Standard/Elevated mode indicates policy-gated active checks, not blanket tool permission. Snapshot-specific execution remains labeled with that scope, and missing runtime evidence is not converted into a successful invocation.

Zero catalog counts require a completed enumeration or a successful snapshot listing; an unpopulated default counter remains unavailable. Later successful execution records and a separate snapshot failure can both appear, without one hiding the other.

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

The verified configuration is Playwright **1.65.1**, Chromium **159.0.8154.6**, A4 or US Letter, scale **1**, backgrounds **off**, browser headers/footers **off**, and **14 mm CSS margins**. The export disables JavaScript and blocks HTTP(S) loading. CSS expands included disclosures and restores filtered claims for print. Disclosure controls are omitted; domain names/counts remain in captions and supporting records retain their labeled fields. The page margins contain the short run reference and **Page X of Y**; the in-content footer retains producer/version and reference.

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
