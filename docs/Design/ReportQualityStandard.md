# MCPVal report quality and layout contract

This is the single product/design authority for reports. Shipped formats and export instructions belong in [Reporting](../Reporting.md); contributor commands belong in [Contributing](../../CONTRIBUTING.md#report-changes). The [report validation skill](../../.github/skills/report-quality-validation/SKILL.md) guides agent reviews, not CI enforcement.

## Evidence and readers

Reports help maintainers find the next action, reviewers understand the supported conclusion, investigators locate exact evidence, and auditors reconcile a run. Agents and CI consume canonical JSON or MCP structured content, not HTML summaries.

- Project saved canonical evidence through the existing factory, composer, theme and interactions. Renderers never contact targets or recompute assurance.
- Keep execution, assurance, policy and certificate eligibility/issuance independent. Missing, skipped, blocked, unavailable, inconclusive and not-applicable evidence never earn pass credit.
- Distinguish discovered capabilities, configured/permitted operations and observed runtime behavior. Identify imported conformance, client interpretation, deterministic checks, heuristics and model advice by their recorded authority and revision.
- Preserve redaction, inert untrusted text, CSP, offline operation, exact IDs and outcome semantics. Identical saved inputs and options produce byte-stable HTML, including across cultures.

## Fixed layout contract

| Order | Component | Required content |
| --- | --- | --- |
| 1 | Masthead | MCPVal wordmark and **Server assessment**; target name as the largest identity text and sole H1; endpoint; quiet recorded UTC run time, profile and detail level |
| 2 | Contents | Compact working links to Decision, Coverage, Actions, Evidence, Provenance and the claims appendix; omitted from print |
| 3 | Decision panel | Dominant outcome heading, principal recorded reason and consequential scope limit; compact fact column alongside on desktop, below on narrow screens |
| 4 | Decision facts | Execution, assurance, policy and certificate independently; subordinate context strip for profile/revision, scope/mode, transport, negotiated protocol and recommended exit |
| 5 | Supporting cards | Up to two: **Agent capability and execution** (advertised, permitted, observed) and **Next priority action** (existing first-ranked action, component, impact and evidence link); omit an empty action card |
| 6 | Coverage | Full-width domain table, independent outcome, named evidence denominator and material limitation; domain links; supporting outcome counts and decision trace |
| 7 | Priority actions | Ordered verb-led action rows using existing ranking; affected component, why it matters, correction or evidence collection, and exact supporting links; scoped empty state |
| 8 | Domain evidence | Visible domain summaries and clear native disclosure controls for bootstrap, protocol, authorization, tools/resources/prompts, extensions, compatibility and measurements |
| 9 | Method and provenance | Run/producer identity, profile/spec/source revisions, policy context and canonical artifact roles |
| 10 | Claims appendix | Complete recorded claims and evidence references after provenance; closed supporting disclosures do not remove data |
| 11 | Footer | Producer/version, short run reference, detail and provenance link |

### Decision rules

Recorded interruption takes precedence over apparent assurance success. Name the stage when recorded. Otherwise present inventory as inventory and CI/comprehensive/provider disposition through the shared exit-policy owner, retaining independent facts; never synthesize aggregate readiness from a core-only success. The primary reason comes from recorded execution errors, blocking core claims, failed policy, then eligibility explanations. A satisfied CI/comprehensive profile summarizes its recorded independent assessments and baseline coverage. Explain missing eligibility reasons explicitly.

Keep field order stable across profiles. Non-issuing profiles say "Profile does not issue certificates." Eligibility is not issuance. Missing optional facts are omitted only if irrelevant; relevant missing evidence is labeled. Display limitations before supporting disclosures. Use canonical order where it carries priority; otherwise use ordinal IDs and stable tie handling. Repeated claim IDs receive collision-safe anchors without dropping their records.

For the opening scope limit, present blocked, authentication-required, unavailable, inconclusive, then skipped declarations, with ordinal layer/scope ties. This is display precedence only, not a change to assessment or policy. With no declarations, state that coverage was not recorded.

### Component roles

- One tinted decision panel is the main emphasis, followed by at most two white supporting cards. Cards form a pair on desktop and stack on mobile; a lone card uses the available width. Additional notices are only for consequential scope/integrity limitations.
- Compare domains in tables, actions in ordered rows, and named facts in definition lists. Major reading sections use one consistent boundary and default-open native heading. Supporting records stay flat; do not nest decorative cards around every trace. Keep important findings, limits and action links visible.
- The capability card must not infer advertisements from catalog counts, execution from permission, or blanket tool permission from Standard/Elevated mode. If the result lacks per-operation admission evidence, say that active operations remain policy-gated. Unavailable advertisement/execution stays unavailable.
- The next-action card and full action list share one projection of the existing remediation ordering; the card links to the exact row instead of inventing a second ranking.
- Keep action priority separate from severity. Do not invent an owner, deadline or evidence link. A canonical reference without included payload remains an explicit reference, not a fabricated observation.
- A domain/action link must reach the exact claim where available. A direct link reveals its disclosure and clears conflicting claim filters. Without JavaScript, all records and native disclosures remain available.
- Primary action investigation opens an in-place copy of the existing encoded canonical record, not a second evidence projection. Instantiate it only on demand, remove duplicate identifiers and canonical-record markers, and release it on close. Do not instantiate all previews with bulk controls. Omit previews from print while retaining the complete canonical appendix. Explicit navigation links and no-script fallbacks remain available.
- Preserve useful domain-specific evidence, rather than replacing it with generic cards. Consolidate repeated recommendations and explain source authority near the evidence.

## Visual tokens

| Role | Token |
| --- | --- |
| Canvas / surface | `#F5F7FA` / `#FFFFFF` |
| Text / secondary | `#172333` / `#49586B` |
| Link / focus | `#174EA6`; underlined links; 2 px focus outline and offset |
| Separator / control boundary | `#D7DFE8` / `#66778A` |
| Satisfied | `#166534` on `#EAF6ED` |
| Review required | `#854D0E` on `#FFF4D8` |
| Incomplete / not applicable | `#44546A` on `#EEF2F7` |
| Violated / error | `#991B1B` on `#FDECEC` / `#6B217E` on `#F7EDF9` |
| Screen type | System sans; body 16 px/1.55; target 36 px and decision 32 px (both 26 narrow); section H2 23 px; card heading 17 px; action title 20 px; tables 14 px; metadata 13 px |
| Print type | Body 10.5-11 pt; tables 9-10 pt; target 22 pt; decision 18 pt; section H2 15 pt |
| Geometry | Maximum 1200 px; 16-40 px gutters; 8 px base spacing rhythm; 32-48 px between major sections; supporting cards 24 px padding (16 mobile); evidence rows 10-12 px vertical padding; cards/panel 8-12 px corners; no shadows |
| Decision surface | Restrained `#EDF3FA` tint and `#C6D5E8` edge, using the existing dark ink and link blue |

Use typography, spacing and aligned edges rather than decoration. No gauges, gradients, decorative illustrations or AI icons. Text labels carry status meaning in grayscale. Long IDs wrap without changing their copyable strings. Numeric comparisons use tabular figures. Two-dimensional tables may use labeled, keyboard-accessible local scrolling, never page-wide overflow.

The system preference chooses light/dark; a local selector may override it without storage. Define both variants once in the shared CSS palette, using `light-dark()` and `color-scheme`. Print always selects light. The verified browser contract is documented in Reporting; do not claim support for older engines without testing them.

| Dark role | Token |
| --- | --- |
| Canvas / surface | `#0F1723` / `#182332` |
| Text / secondary / link | `#E6EEF8` / `#B7C5D8` / `#9CC3FF` |
| Separator / control | `#33445A` / `#8D9FB5` |
| Satisfied | `#93E0AE` on `#143625` |
| Review | `#F4D180` on `#3B2C11` |
| Violated / error | `#FFB4B4` on `#441F27` / `#E0B4F0` on `#382142` |
| Neutral | `#C7D4E5` on `#243246` |
| Decision / edge | `#192E47` / `#536F92` |

### Native disclosure component

- Preserve native `details`/`summary`, keyboard operation and a visible focus outline. The full summary row is clickable. Do not auto-scroll on normal disclosure toggles.
- Replace the browser triangle with one leading, decorative 16 px chevron, using a consistent `currentColor` stroke. It points down when closed and up when open. Keep it and the desktop Expand/Collapse hint out of the accessible name with `aria-hidden`; omit that hint on narrow screens to preserve label width.
- Domain groups may have a fine boundary and quiet white surface. Routine records use flat rows and horizontal separators, with no individual rounded border. Hover uses a neutral tint; the open row and its body form one continuous surface.
- Use descriptive labels such as "Tool catalog records" and "Authentication observations and security records." Comparable records without additional detail stay in tables or lists.
- Print omits disclosure controls, not evidence or real headings. Domain captions and visible section headings preserve identity; labeled claim fields retain source, correction and references.

## HTML and print acceptance

Require landmarks, one H1, meaningful headings, table headers, visible focus, working anchors, keyboard operation, 320 CSS px reflow and WCAG AA contrast. Filters disclose criteria/counts/reset/empty state and never alter canonical data.

Print retains the reading order and all evidence included by the detail mode, regardless of collapsed disclosures or active filters. For representative runs, page one must contain target, decision, reason, principal limitation and the top action. Use selectable text, repeated table headers, 12-16 mm margins, readable grayscale labels and sensible breaks. Do not force large evidence sections onto a single page.

The supported archival export must show the short run reference and **Page X of Y** in page margins, with duplicate browser headers disabled. Verify A4 and US Letter at scale 1 with backgrounds disabled. Inspect the first, dense and final pages, changed page transitions, footer sequence and actual totals. Browser fallback, native assistive technology, human comprehension and tagged/PDF-A claims require separate evidence.

Before acceptance, compare actual CLI before/after outputs from identical synthetic inventory, mixed, interrupted, dense and sparse inputs in both detail modes. Include CI and comprehensive satisfied/failed/missing-prerequisite controls, preserving optional diagnostic failures in a passing selected scope. The corpus manifest is generated by the fixture owner and consumed by CLI/browser checks, not duplicated in scripts. Inspect an in-place action investigation and an explicit action -> domain -> exact claim -> provenance path. Automated parity, accessibility and PDF checks complement, but do not replace, human review of reading order, hierarchy and pagination.

## Reference guidance

- [WCAG 2.2](https://www.w3.org/TR/WCAG22/)
- [GOV.UK details](https://design-system.service.gov.uk/components/details/)
- [Carbon data tables](https://www.carbondesignsystem.com/building-blocks/core/components/data-table/guidelines)
- [Fluent responsible AI](https://fluent2.microsoft.design/responsible-AI/)
- [Chromium page margins and counters](https://developer.chrome.com/blog/print-margins)
