namespace Mcp.Benchmark.Infrastructure.Services.Reporting;

internal static class ValidationHtmlReportTheme
{
    public static string BuildCss() => """
:root {
    color-scheme: light;
    --canvas: #f5f7fa;
    --surface: #ffffff;
    --text: #172333;
    --muted: #49586b;
    --link: #174ea6;
    --line: #d7dfe8;
    --control: #66778a;
    --success: #166534;
    --success-bg: #eaf6ed;
    --warning: #854d0e;
    --warning-bg: #fff4d8;
    --danger: #991b1b;
    --danger-bg: #fdecec;
    --neutral: #44546a;
    --neutral-bg: #eef2f7;
    --error: #6b217e;
    --error-bg: #f7edf9;
    --decision-bg: #edf3fa;
    --decision-line: #c6d5e8;
}
* { box-sizing: border-box; }
html { font-size: 16px; scroll-padding-top: 1rem; }
body {
    margin: 0;
    padding: 2rem;
    font: 1rem/1.55 system-ui, -apple-system, "Segoe UI", sans-serif;
    color: var(--text);
    background: var(--canvas);
    overflow-wrap: anywhere;
}
.report-shell { max-width: 75rem; margin: auto; }
.report-content { padding: 1rem 0 2rem; }
h1, h2, h3 { line-height: 1.25; color: var(--text); }
h1 { font-size: 2rem; margin: .5rem 0; letter-spacing: -.025em; }
h2 { font-size: 1.4375rem; margin: 0 0 1rem; }
h3 { font-size: 1.0625rem; margin: 1.5rem 0 .5rem; }
p { margin: .5rem 0 1rem; }
p, .compact-list, .assessment-list { max-width: 75ch; }
a { color: var(--link); text-underline-offset: .2em; }
a:hover { text-decoration-thickness: 2px; }
:focus-visible { outline: 2px solid var(--link); outline-offset: 2px; }
:target { scroll-margin-top: 1rem; }
article:target { outline: 2px solid var(--link); outline-offset: .25rem; }
tr:target { outline: 2px solid var(--link); outline-offset: -2px; }
code, pre { font-family: ui-monospace, SFMono-Regular, Consolas, monospace; font-size: .875em; }
pre, code { white-space: pre-wrap; overflow-wrap: anywhere; }
.skip-link { position: absolute; left: -10000px; }
.skip-link:focus { left: 1rem; top: .25rem; z-index: 10; background: white; padding: .5rem; }
.masthead-brand { display: flex; align-items: baseline; gap: 1rem; }
.wordmark { font-size: 1rem; font-weight: 800; letter-spacing: -.025em; color: var(--link); }
.report-kind { border-left: 1px solid var(--line); padding-left: 1rem; color: var(--muted); font-size: .875rem; }
.assessment-masthead { padding-bottom: 1.25rem; border-bottom: 1px solid var(--line); }
.target-identity { margin: .75rem 0 .25rem; font-size: 2.25rem; font-weight: 750; line-height: 1.15; }
.target-endpoint { color: var(--muted); font-size: .875rem; margin: 0 0 .625rem; }
.report-metadata { margin: 0; font-size: .8125rem; color: var(--muted); }
.assessment-contents ul { display: flex; flex-wrap: wrap; gap: .25rem 1.5rem; list-style: none; padding: 0; margin: 1rem 0 1.5rem; font-size: .875rem; }
.assessment-contents a, .decision-forward { display: inline-block; padding: .25rem 0; min-height: 1.5rem; }
.decision-panel { background: var(--decision-bg); border: 1px solid var(--decision-line); border-left: 4px solid var(--link); border-radius: .75rem; padding: 2rem; }
.decision-grid { display: grid; grid-template-columns: minmax(0, 1.6fr) minmax(0, 1fr); gap: 2rem; }
.decision-narrative { min-width: 0; }
.decision-panel h2 { font-size: 2rem; letter-spacing: -.02em; line-height: 1.18; margin: .5rem 0 1rem; }
.decision-panel .section-kicker { margin: 0; color: var(--link); font-weight: 650; }
.decision-reason { font-size: 1.0625rem; line-height: 1.5; }
.scope-limit { color: var(--muted); font-size: .875rem; margin-bottom: 0; }
.assessment-facts { margin: 1rem 0; }
.assessment-facts > div { display: grid; grid-template-columns: minmax(8rem, 12rem) minmax(0, 1fr); gap: 1rem; padding: .25rem 0; }
.assessment-facts dt { font-weight: 600; }
.assessment-facts dd { margin: 0; min-width: 0; }
.decision-panel .assessment-facts { font-size: .8125rem; }
.decision-panel .decision-facts { margin: 0; border-left: 1px solid var(--decision-line); padding-left: 1.5rem; }
.decision-panel .decision-facts > div { grid-template-columns: 1fr; gap: .2rem; padding: .5rem 0; }
.decision-facts dt { color: var(--muted); }
.decision-facts dd { font-weight: 550; }
.decision-panel .decision-context { display: grid; grid-template-columns: repeat(5, minmax(0, 1fr)); gap: 1rem; border-top: 1px solid var(--decision-line); margin: 1.5rem 0 0; padding-top: 1rem; }
.decision-panel .decision-context > div { display: block; padding: 0; }
.decision-context dt { color: var(--muted); margin-bottom: .25rem; }
.decision-context dd { font-weight: 550; }
.decision-forward { font-size: .875rem; margin-top: 1rem; }
.supporting-cards { display: grid; grid-template-columns: repeat(auto-fit, minmax(min(100%, 28rem), 1fr)); gap: 1.5rem; margin-top: 1.5rem; }
.support-card { background: var(--surface); border: 1px solid var(--line); border-radius: .75rem; padding: 1.5rem; min-width: 0; }
.support-card h2 { font-size: 1.0625rem; margin: 0 0 1rem; }
.support-card-heading { display: flex; flex-wrap: wrap; justify-content: space-between; align-items: baseline; gap: .5rem; }
.agent-facts { margin: 0; font-size: .875rem; }
.agent-facts > div { display: grid; grid-template-columns: 8.5rem minmax(0, 1fr); gap: .75rem; padding: .625rem 0; border-top: 1px solid var(--line); }
.agent-facts dt { color: var(--muted); font-weight: 600; }
.agent-facts dd { margin: 0; }
.catalog-summary { margin: .75rem 0; color: var(--muted); font-size: .8125rem; }
.support-card a { font-size: .875rem; }
.next-action-summary { margin: 0 0 .75rem; font-size: 1.25rem; line-height: 1.4; }
.action-component, .action-impact { font-size: .875rem; margin: .75rem 0; }
.action-kind { color: var(--muted); font-size: .8125rem; margin: 0 0 .75rem; }
.assessment-section, .section { padding: 2rem 0; border-bottom: 1px solid var(--line); }
.assessment-section .assessment-section { padding: 1rem 0; }
.assessment-context, .eyebrow, .section-kicker { font-size: .8125rem; color: var(--muted); font-weight: 600; }
.assessment-reference { padding: 0; margin: 0; border-top: 1px solid var(--line); }
summary {
    display: flex;
    align-items: center;
    justify-content: space-between;
    gap: 1rem;
    list-style: none;
    cursor: pointer;
    font-weight: 600;
    min-height: 2.75rem;
    padding: .75rem .5rem;
    color: var(--text);
    background: transparent;
    border: 0;
    border-radius: 0;
}
summary::-webkit-details-marker { display: none; }
summary::marker { content: ""; }
summary:hover { background: var(--neutral-bg); }
summary:focus-visible { outline-offset: -2px; }
.disclosure-label { min-width: 0; }
.disclosure-chevron { flex: 0 0 16px; width: 16px; height: 16px; color: var(--muted); position: relative; }
.disclosure-chevron::before {
    content: "";
    position: absolute;
    width: 8px;
    height: 8px;
    left: 4px;
    top: 2px;
    border-right: 1.5px solid currentColor;
    border-bottom: 1.5px solid currentColor;
    transform: rotate(45deg);
}
details[open] > summary .disclosure-chevron::before { transform: rotate(225deg); top: 6px; }
.disclosure-outcomes { display: block; margin: .25rem 0 0; font-size: .8125rem; font-weight: 400; color: var(--muted); }
.assessment-reference[open] { padding-bottom: 1rem; background: var(--surface); }
.assessment-reference[open] > summary { background: var(--surface); }
.assessment-reference[open] > summary:hover { background: var(--neutral-bg); }
.disclosure-group { margin: 1rem 0; border: 1px solid var(--line); border-radius: .5rem; background: var(--surface); }
.disclosure-group > summary { padding: 1rem; border-radius: .5rem; }
.disclosure-group[open] > summary { border-radius: .5rem .5rem 0 0; border-bottom: 1px solid var(--line); }
.disclosure-group > .assessment-section__content { padding: 0 1rem; }
.disclosure-group > table { margin: 0; }
.claims-disclosure > :not(summary) { margin-left: 1rem; margin-right: 1rem; }
.data-table { width: 100%; border-collapse: collapse; table-layout: fixed; font-size: .875rem; line-height: 1.45; margin: 1rem 0; }
.domain-evidence-table th:first-child { width: 18%; }
.domain-evidence-table th:nth-child(2) { width: 16%; }
.domain-evidence-table th:nth-child(3) { width: 24%; }
.domain-evidence-table th:nth-child(4) { width: 42%; }
caption { text-align: left; font-weight: 600; margin-bottom: .5rem; color: var(--muted); }
thead { background: var(--canvas); }
th, td { padding: .625rem .75rem; text-align: left; vertical-align: top; border-bottom: 1px solid var(--line); overflow-wrap: anywhere; }
th { font-weight: 600; }
.table-cell--numeric { text-align: right; font-variant-numeric: tabular-nums; }
.table-shell, .table-scroll { max-width: 100%; overflow-x: auto; }
.table-subtle, .ledger-subtle, .evidence-note, .evidence-card__subtle { font-size: .8125rem; color: var(--muted); }
.priority-list { padding-left: 1.5rem; }
.priority-list > li { padding-left: .5rem; }
.priority-list > li::marker { font-weight: 700; }
.action-row { padding: 1.25rem .5rem; border-top: 1px solid var(--line); }
.action-title { font-size: 1.125rem; margin: .5rem 0; }
.action-category { color: var(--muted); font-size: .8125rem; }
.report-controls { display: flex; flex-wrap: wrap; gap: .75rem; margin: 1rem 0; }
.compact-list { padding-left: 1.25rem; }
.compact-list > li { margin: .75rem 0; }
.ledger-entry, .auth-surface, .auth-scenario, .report-note, .evidence-card,
.authority-card, .metric-card, .reader-key-item { border-top: 1px solid var(--line); padding: 1rem 0; }
.ledger-title, .auth-surface__title, .auth-scenario__title, .evidence-card__title,
.authority-card__title, .scope-rail__title { font-weight: 650; }
.ledger-kicker, .report-note__eyebrow, .authority-card__eyebrow { font-size: .8125rem; color: var(--muted); }
.ledger-links { margin: .5rem 0; font-size: .875rem; }
.ledger-code-block { padding: .75rem 0; }
.ledger-copy { margin-top: .5rem; }
.ledger-chip-row, .auth-surface__meta, .auth-scenario__metrics, .inline-list { display: flex; flex-wrap: wrap; align-items: baseline; gap: .5rem 1rem; }
.tone-chip, .tag { display: inline-block; font-size: .8125rem; padding: .125rem .5rem; border-radius: .25rem; font-weight: 600; max-width: 100%; }
.tone-success { color: var(--success); background: var(--success-bg); }
.tone-warning { color: var(--warning); background: var(--warning-bg); }
.tone-danger { color: var(--danger); background: var(--danger-bg); }
.tone-info, .tone-neutral, .tag { color: var(--neutral); background: var(--neutral-bg); }
.assessment-claim[data-claim-outcome="Error"] > h3 { color: var(--error); }
.summary-grid, .card-grid, .panel-grid, .authority-summary-grid, .auth-scenario__detail-grid,
.auth-scenario__comparison, .reader-key-grid, .appendix-grid { display: grid; grid-template-columns: repeat(auto-fit, minmax(min(100%, 20rem), 1fr)); gap: 1rem 2rem; }
.summary-card, .fact-item, .auth-fact { padding: .5rem 0; min-width: 0; }
.summary-card__label, .fact-item__label, .auth-fact__label, .auth-scenario__block-label,
.evidence-stat__label, .authority-card__metric-label, .metric-label { font-size: .8125rem; color: var(--muted); }
.summary-card__value, .fact-item__value, .auth-fact__value { font-weight: 550; }
.auth-scenario__block { padding: .5rem 0; }
.evidence-stats, .authority-card__metrics { display: flex; flex-wrap: wrap; gap: 1rem; }
.scope-rail { padding-top: 1rem; }
.scope-rail__items { display: grid; grid-template-columns: repeat(auto-fit, minmax(min(100%, 16rem), 1fr)); gap: .5rem 1.5rem; margin-top: .5rem; }
.scope-item { display: flex; flex-wrap: wrap; gap: .5rem; font-size: .875rem; }
.scope-item__label { color: var(--muted); }
.scope-dot { display: none; }
.status-callout { padding: 1rem; border: 1px solid var(--line); background: var(--canvas); }
.status-callout__title { font-weight: 600; }
.claim-filters { display: flex; flex-wrap: wrap; gap: .75rem; align-items: end; margin: 1rem 0; }
.claim-filters label { display: grid; gap: .25rem; flex: 1 1 12rem; min-width: 0; }
input, select, button { font: inherit; color: var(--text); background: white; padding: .5rem; border: 1px solid var(--control); border-radius: .25rem; min-height: 2.75rem; max-width: 100%; }
button { cursor: pointer; }
button:disabled { color: var(--muted); cursor: default; }
#claim-filter-status { flex-basis: 100%; }
.assessment-claim { border-top: 1px solid var(--line); padding: .5rem 0 1rem; }
.assessment-claim > p { margin: .5rem 0; }
.assessment-claim .assessment-reference { margin-top: .75rem; font-size: .875rem; }
.print-claims-scope { display: none; }
.footer { border-top: 1px solid var(--line); padding-top: 1rem; margin-top: 2rem; font-size: .8125rem; color: var(--muted); }
[hidden] { display: none !important; }
@media (max-width: 760px) {
    body { padding: 0; }
    .report-content { padding: 1rem; }
    h1, .target-identity { font-size: 1.625rem; }
    .decision-panel { padding: 1rem; }
    .decision-grid { grid-template-columns: 1fr; gap: 1rem; }
    .decision-panel h2 { font-size: 1.625rem; }
    .decision-panel .decision-facts { border-left: 0; border-top: 1px solid var(--decision-line); padding: .75rem 0 0; }
    .decision-panel .decision-context { grid-template-columns: repeat(2, minmax(0, 1fr)); }
    .support-card { padding: 1rem; }
    .agent-facts > div { grid-template-columns: 1fr; gap: .25rem; }
    .assessment-facts > div { grid-template-columns: 1fr; gap: .125rem; padding: .375rem 0; }
    .data-table { font-size: .875rem; }
    th, td { padding: .5rem .375rem; }
    .layered-report-table { min-width: 42rem; }
}
@page {
    margin: 14mm;
    @bottom-left { font: 8pt sans-serif; color: #49586b; }
    @bottom-right { content: "Page " counter(page) " of " counter(pages); font: 8pt sans-serif; color: #49586b; }
}
@media print {
    html { font-size: 10.5pt; }
    body { padding: 0; color: #172333; background: white; line-height: 1.4; }
    .report-shell { max-width: none; }
    .report-content { padding: 0; }
    h1, .target-identity { font-size: 22pt; }
    h2 { font-size: 15pt; }
    h3 { font-size: 11pt; }
    h1, h2, h3, summary, caption { break-after: avoid; }
    p, li { orphans: 3; widows: 3; }
    .assessment-masthead { padding-bottom: .75rem; }
    .assessment-contents, .skip-link, .claim-filters, #claim-filter-empty, .decision-forward { display: none !important; }
    .decision-panel { margin-top: .75rem; padding: 1rem; break-inside: avoid; background: white; border-color: #66778a; }
    .decision-grid { grid-template-columns: minmax(0, 1.5fr) minmax(0, 1fr); gap: 1rem; }
    .decision-panel h2 { font-size: 18pt; }
    .decision-panel .decision-facts { border-left: 1px solid #66778a; border-top: 0; padding: 0 0 0 1rem; font-size: 9pt; }
    .decision-panel .decision-context { grid-template-columns: repeat(5, minmax(0, 1fr)); font-size: 8.5pt; margin-top: .75rem; padding-top: .75rem; gap: .75rem; }
    .decision-panel .section-kicker { color: inherit; }
    .supporting-cards { grid-template-columns: minmax(0, 1fr) minmax(0, 1fr); gap: 1rem; margin-top: 1rem; break-inside: avoid; }
    .support-card { padding: 1rem; border-color: #66778a; }
    .support-card:only-child { grid-column: 1 / -1; }
    .support-card h2 { font-size: 11pt; margin-bottom: .5rem; }
    .next-action-summary { font-size: 11pt; }
    .agent-facts > div { grid-template-columns: 1fr; gap: .15rem; padding: .35rem 0; }
    .agent-facts, .action-impact, .action-component, .support-card a { font-size: 9pt; }
    .report-controls { display: none !important; }
    .assessment-section, .section { padding: 1rem 0; }
    .assessment-claim[hidden], .print-claims-scope { display: block !important; }
    details::details-content { display: contents !important; content-visibility: visible !important; }
    details { display: contents; }
    .assessment-reference, .disclosure-group { border: 0; margin: 0; padding: 0; background: white; }
    .disclosure-group > .assessment-section__content { padding: 0; }
    .claims-disclosure > :not(summary) { margin-left: 0; margin-right: 0; }
    .disclosure-chevron { display: none; }
    summary { display: none; }
    summary::-webkit-details-marker { display: none; }
    .summary-grid, .card-grid, .panel-grid, .authority-summary-grid, .auth-scenario__detail-grid,
    .auth-scenario__comparison, .reader-key-grid, .appendix-grid { display: block; }
    .decision-fact-grid, .provenance-facts { display: grid; grid-template-columns: repeat(3, minmax(0, 1fr)); gap: .25rem 1rem; }
    .summary-card--decision-fact, .fact-item { padding: .25rem 0; }
    .ledger-entry { padding: .75rem 0; }
    .assessment-claim p { margin: .375rem 0; }
    .table-shell, .table-scroll { overflow: visible; }
    .data-table, .layered-report-table { min-width: 0; width: 100%; font-size: 9pt; table-layout: fixed; }
    .coverage-detail-table .table-col--scope { width: 24%; }
    .coverage-detail-table .table-col--reason { width: 24%; }
    .coverage-detail-table .table-col--layer { width: 12%; }
    .coverage-detail-table .table-col--status,
    .coverage-detail-table .table-col--blocker,
    .coverage-detail-table .table-col--confidence,
    .coverage-detail-table .table-col--probe { width: 10%; }
    th, td { padding: .4rem; }
    thead { display: table-header-group; break-after: avoid-page; }
    tbody tr:first-child { break-before: avoid-page; }
    tfoot { display: table-footer-group; }
    tr { break-inside: avoid; }
    .ledger-entry, .assessment-claim, .table-shell, .section { break-inside: auto; }
    .tone-chip, .tag { color: #172333; background: white; border: 1px solid #66778a; }
    .assessment-claim[data-claim-outcome="Error"] > h3 { color: inherit; }
    a { color: inherit; }
    .footer { margin-top: 1rem; }
}
""";

    public static string ToCssTone(HtmlReportTone tone) => tone switch
    {
        HtmlReportTone.Success => "success",
        HtmlReportTone.Warning => "warning",
        HtmlReportTone.Danger => "danger",
        HtmlReportTone.Info => "info",
        _ => "neutral"
    };
}
