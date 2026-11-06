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

1. **Assessment framing and tested scope** identify the assurance profile, execution mode, and active operations that were or were not attempted.
2. **Assurance decision** summarizes satisfied, review-required, incomplete, violated, and error claims.
3. **Remediation and decision trace** explain why the result landed where it did and which evidence should be addressed first.
4. **Independent assessments** keep core conformance, security assurance, operational readiness, extension readiness, and client compatibility separate.
5. **Capability and operational evidence** describe observed tools, resources, prompts, bootstrap behavior, and measurements.
6. **Appendices** retain detailed findings, methodology notes, and terminology without displacing the decision surface.

Inventory is an assessment product, not a failed certificate attempt. Its report says that the assessment completed and that a provider certificate was not issued by the selected profile. Missing or skipped active evidence remains incomplete and never earns pass credit.

Semantic color is limited to status chips, dots, and rails. Text labels always carry the same meaning, so color is not the only signal. The document contains no scripts or externally loaded active content, supports keyboard disclosure controls, includes mobile table containment, and has print styles for PDF export.

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

## Local MCP Wrapper

`mcpval-localmcp` returns canonical JSON evidence through MCP `structuredContent`. It intentionally does not place large HTML or Markdown reports into model context. Temporary CLI report artifacts are deleted after each tool execution.

Generate durable human reports with the CLI `validate --output` or `report` command. Provider assurance through the wrapper is available only when the operator configures all signed trust inputs; the wrapper does not accept evidence paths or output directories from agent arguments.

## Sharing Reports Safely

- Review endpoint names, server metadata, findings, and remediation text before external distribution.
- Canonical transport `rawContent` is redacted from persisted artifacts.
- Credentials, authorization headers, cookies, tokens, and interactive auth state must never appear in reports.
- Preserve the JSON result and audit manifest with human reports when reproducibility matters.
- Treat target-controlled descriptions, schemas, prompts, resources, and findings as untrusted data even when safely encoded in HTML.
