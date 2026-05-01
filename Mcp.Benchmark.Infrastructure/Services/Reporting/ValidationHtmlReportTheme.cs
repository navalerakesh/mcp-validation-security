namespace Mcp.Benchmark.Infrastructure.Services.Reporting;

internal static class ValidationHtmlReportTheme
{
    public static string BuildCss()
    {
    return """
:root {
    color-scheme: light;
    --report-bg: #dde4eb;
    --report-bg-glow-a: rgba(54, 95, 161, 0.08);
    --report-bg-glow-b: rgba(168, 93, 25, 0.08);
    --report-bg-glow-c: rgba(15, 23, 38, 0.05);
    --surface: rgba(255, 255, 255, 0.96);
    --surface-strong: #ffffff;
    --surface-muted: #f5f7fa;
    --surface-emphasis: #eef2f6;
    --line: #c9d2dd;
    --line-strong: #aebbc9;
    --text: #223042;
    --text-strong: #101827;
    --text-muted: #4f5e72;
    --text-soft: #6a7789;
    --shadow-soft: 0 12px 30px rgba(15, 23, 38, 0.06);
    --shadow-panel: 0 18px 42px rgba(15, 23, 38, 0.08);
    --radius-shell: 22px;
    --radius-panel: 16px;
    --radius-card: 14px;
    --tone-success: #1f6a52;
    --tone-success-soft: #eef6f1;
    --tone-success-line: #8fbca5;
    --tone-warning: #a85d19;
    --tone-warning-soft: #faf1e6;
    --tone-warning-line: #dbb085;
    --tone-danger: #a33446;
    --tone-danger-soft: #f9ecef;
    --tone-danger-line: #d7a1aa;
    --tone-info: #365fa1;
    --tone-info-soft: #edf2fb;
    --tone-info-line: #9bb2dd;
    --tone-neutral: #596577;
    --tone-neutral-soft: #f2f5f8;
    --tone-neutral-line: #c8d0d9;
}
* { box-sizing: border-box; }
html, body { margin: 0; padding: 0; }
body {
    font-family: "Aptos", "Segoe UI Variable", "Segoe UI", "Helvetica Neue", Arial, sans-serif;
    background:
        radial-gradient(circle at top left, var(--report-bg-glow-a), transparent 34%),
        radial-gradient(circle at top right, var(--report-bg-glow-b), transparent 30%),
        radial-gradient(circle at 50% 18%, var(--report-bg-glow-c), transparent 38%),
        var(--report-bg);
    color: var(--text);
    padding: 24px;
}
.report-shell {
    max-width: 1260px;
    margin: 0 auto;
    background: rgba(255,255,255,0.98);
    border: 1px solid var(--line-strong);
    border-radius: var(--radius-shell);
    box-shadow: var(--shadow-panel);
    overflow: hidden;
    position: relative;
}
.report-shell::before {
    content: "";
    position: absolute;
    inset: 0 0 auto 0;
    height: 6px;
    background: linear-gradient(90deg, var(--tone-danger), var(--tone-warning), var(--tone-info));
    opacity: 0.2;
    pointer-events: none;
}
.report-content {
    position: relative;
    z-index: 1;
    padding: 22px 24px 28px;
}
.hero {
    border-radius: calc(var(--radius-shell) - 4px);
    padding: 28px;
    background: linear-gradient(180deg, rgba(255,255,255,0.98), rgba(246,248,251,0.98));
    border: 1px solid var(--line);
    box-shadow: var(--shadow-soft);
}
.hero-grid {
    display: grid;
    grid-template-columns: minmax(0, 1.75fr) minmax(300px, 0.85fr);
    gap: 20px;
    align-items: start;
}
.hero-main {
    min-width: 0;
}
.eyebrow {
    font-size: 0.74rem;
    letter-spacing: 0.18em;
    text-transform: uppercase;
    color: var(--text-soft);
    font-weight: 700;
}
.hero-title {
    margin: 10px 0 10px;
    color: var(--text-strong);
    font-size: clamp(2rem, 3vw, 3.05rem);
    line-height: 1.04;
    letter-spacing: -0.04em;
}
.hero-subtitle {
    margin: 0;
    max-width: 840px;
    color: var(--text-muted);
    font-size: 1rem;
    line-height: 1.58;
}
.hero-facts,
.fact-grid {
    margin-top: 20px;
    display: grid;
    grid-template-columns: repeat(2, minmax(0, 1fr));
    border-top: 1px solid var(--line);
    border-left: 1px solid var(--line);
}
.fact-item {
    background: rgba(255,255,255,0.66);
    border-right: 1px solid var(--line);
    border-bottom: 1px solid var(--line);
    padding: 12px 14px;
    min-height: 76px;
}
.fact-item__label {
    font-size: 0.72rem;
    letter-spacing: 0.12em;
    text-transform: uppercase;
    color: var(--text-soft);
    font-weight: 700;
}
.fact-item__value {
    margin-top: 7px;
    color: var(--text-strong);
    font-size: 0.95rem;
    line-height: 1.45;
    overflow-wrap: anywhere;
    word-break: break-word;
}
.hero-meta {
    margin-top: 24px;
    display: grid;
    grid-template-columns: repeat(auto-fit, minmax(180px, 1fr));
    gap: 14px;
}
.meta-card {
    background: rgba(255,255,255,0.72);
    border: 1px solid var(--line);
    border-radius: 16px;
    padding: 14px 16px;
}
.meta-label {
    font-size: 0.72rem;
    letter-spacing: 0.12em;
    text-transform: uppercase;
    color: var(--text-soft);
}
.meta-value {
    margin-top: 7px;
    color: var(--text-strong);
    font-size: 0.98rem;
    line-height: 1.45;
    word-break: break-word;
}
.hero-side {
    display: flex;
    flex-direction: column;
    gap: 12px;
}
.panel {
    background: var(--surface);
    border: 1px solid var(--line);
    border-radius: var(--radius-panel);
    box-shadow: var(--shadow-soft);
    padding: 18px;
}
.panel--soft {
    background: linear-gradient(180deg, rgba(255,255,255,0.98), rgba(246,248,251,0.98));
}
.status-panel {
    min-width: 0;
}
.panel--tone-success { border-color: var(--tone-success-line); background: linear-gradient(180deg, rgba(255,255,255,0.99), var(--tone-success-soft)); }
.panel--tone-warning { border-color: var(--tone-warning-line); background: linear-gradient(180deg, rgba(255,255,255,0.99), rgba(250,241,230,0.82)); }
.panel--tone-danger { border-color: var(--tone-danger-line); background: linear-gradient(180deg, rgba(255,255,255,0.99), rgba(249,236,239,0.82)); }
.panel--tone-info { border-color: var(--tone-info-line); background: linear-gradient(180deg, rgba(255,255,255,0.99), var(--tone-info-soft)); }
.panel--tone-neutral { border-color: var(--tone-neutral-line); background: linear-gradient(180deg, rgba(255,255,255,0.99), var(--tone-neutral-soft)); }
.status-chip,
.tone-chip {
    display: inline-flex;
    align-items: center;
    gap: 8px;
    border-radius: 999px;
    padding: 6px 12px;
    font-size: 0.78rem;
    font-weight: 700;
    letter-spacing: 0.06em;
    text-transform: uppercase;
    border: 1px solid transparent;
}
.status-chip::before,
.tone-chip::before {
    content: "";
    width: 9px;
    height: 9px;
    border-radius: 50%;
    background: currentColor;
    opacity: 0.75;
}
.tone-success, .status-chip--success { color: var(--tone-success); background: var(--tone-success-soft); border-color: var(--tone-success-line); }
.tone-warning, .status-chip--warning { color: var(--tone-warning); background: var(--tone-warning-soft); border-color: var(--tone-warning-line); }
.tone-danger, .status-chip--danger { color: var(--tone-danger); background: var(--tone-danger-soft); border-color: var(--tone-danger-line); }
.tone-info, .status-chip--info { color: var(--tone-info); background: var(--tone-info-soft); border-color: var(--tone-info-line); }
.tone-neutral, .status-chip--neutral { color: var(--tone-neutral); background: var(--tone-neutral-soft); border-color: var(--tone-neutral-line); }
.focus-value {
    font-size: clamp(1.55rem, 1.8vw, 2.15rem);
    letter-spacing: -0.04em;
    color: var(--text-strong);
    margin-top: 12px;
    font-weight: 700;
    line-height: 1.15;
    overflow-wrap: anywhere;
}
.focus-label {
    margin-top: 6px;
    font-size: 0.88rem;
    color: var(--text-muted);
    line-height: 1.45;
}
.section-heading-row {
    display: flex;
    justify-content: space-between;
    gap: 16px;
    align-items: flex-start;
}
.brief-grid {
    display: grid;
    grid-template-columns: minmax(0, 1.2fr) minmax(320px, 0.9fr);
    gap: 18px;
    margin-top: 18px;
}
.brief-side {
    display: grid;
    gap: 12px;
}
.decision-brief,
.report-note,
.status-callout {
    border: 1px solid var(--line);
    border-left-width: 4px;
    border-radius: var(--radius-card);
    background: var(--surface-muted);
    padding: 16px 18px;
}
.decision-brief--tone-success,
.report-note--success { border-left-color: var(--tone-success); background: linear-gradient(180deg, rgba(255,255,255,0.98), rgba(238,246,241,0.88)); }
.decision-brief--tone-warning,
.report-note--warning,
.status-callout--warning { border-left-color: var(--tone-warning); background: linear-gradient(180deg, rgba(255,255,255,0.98), rgba(250,241,230,0.88)); }
.decision-brief--tone-danger,
.report-note--danger { border-left-color: var(--tone-danger); background: linear-gradient(180deg, rgba(255,255,255,0.98), rgba(249,236,239,0.88)); }
.decision-brief--tone-info,
.report-note--info,
.status-callout--info { border-left-color: var(--tone-info); background: linear-gradient(180deg, rgba(255,255,255,0.98), rgba(237,242,251,0.88)); }
.decision-brief--tone-neutral,
.report-note--neutral { border-left-color: var(--tone-neutral-line); background: linear-gradient(180deg, rgba(255,255,255,0.98), rgba(242,245,248,0.9)); }
.decision-title {
    margin: 8px 0 8px;
    font-size: 1.28rem;
    color: var(--text-strong);
    line-height: 1.2;
    letter-spacing: -0.02em;
}
.decision-summary {
    margin: 0;
    color: var(--text-muted);
    line-height: 1.58;
}
.report-note__eyebrow {
    font-size: 0.72rem;
    letter-spacing: 0.14em;
    text-transform: uppercase;
    color: var(--text-soft);
    font-weight: 700;
}
.report-note__title {
    margin: 6px 0 0;
    font-size: 1.08rem;
    line-height: 1.22;
    color: var(--text-strong);
}
.report-note__empty {
    margin: 10px 0 0;
    color: var(--text-muted);
    line-height: 1.56;
}
.ledger-group {
    margin-top: 18px;
}
.ledger-list {
    display: grid;
    gap: 12px;
    margin-top: 14px;
}
.ledger-entry {
    display: grid;
    grid-template-columns: minmax(210px, 250px) minmax(0, 1fr);
    gap: 18px;
    border: 1px solid var(--line);
    border-left-width: 4px;
    border-radius: var(--radius-card);
    background: rgba(255,255,255,0.98);
    padding: 16px 18px;
}
.ledger-entry--success { border-left-color: var(--tone-success); background: linear-gradient(180deg, rgba(255,255,255,0.98), rgba(238,246,241,0.72)); }
.ledger-entry--warning { border-left-color: var(--tone-warning); }
.ledger-entry--danger { border-left-color: var(--tone-danger); }
.ledger-entry--info { border-left-color: var(--tone-info); background: linear-gradient(180deg, rgba(255,255,255,0.98), rgba(237,242,251,0.72)); }
.ledger-entry--neutral { border-left-color: var(--tone-neutral-line); background: linear-gradient(180deg, rgba(255,255,255,0.98), rgba(242,245,248,0.78)); }
.ledger-rail {
    display: grid;
    align-content: start;
    gap: 8px;
    min-width: 0;
}
.ledger-body {
    min-width: 0;
}
.ledger-kicker {
    font-size: 0.72rem;
    letter-spacing: 0.14em;
    text-transform: uppercase;
    color: var(--text-soft);
    font-weight: 700;
}
.ledger-title {
    font-size: 1rem;
    line-height: 1.34;
    color: var(--text-strong);
    font-weight: 700;
    overflow-wrap: anywhere;
}
.ledger-subtle {
    color: var(--text-muted);
    font-size: 0.88rem;
    line-height: 1.45;
    overflow-wrap: anywhere;
}
.ledger-chip-row {
    display: flex;
    flex-wrap: wrap;
    gap: 8px;
}
.ledger-copy {
    margin: 0;
    color: var(--text);
    line-height: 1.62;
    overflow-wrap: anywhere;
}
.ledger-copy + .ledger-copy {
    margin-top: 10px;
}
.ledger-links {
    margin-top: 10px;
    color: var(--text-muted);
    line-height: 1.5;
    overflow-wrap: anywhere;
}
.ledger-code,
.ledger-code-block {
    overflow-wrap: anywhere;
}
.ledger-code code,
.ledger-code-block code {
    display: inline-block;
    max-width: 100%;
    white-space: normal;
    overflow-wrap: anywhere;
}
.ledger-code-block {
    margin-top: 10px;
    color: var(--text-muted);
    line-height: 1.5;
}
.ledger-code-block code {
    display: block;
    margin-top: 6px;
    padding: 8px 10px;
    background: rgba(24,34,49,0.05);
}
.probe-list {
    display: grid;
    gap: 12px;
    margin-top: 16px;
}
.probe-row {
    border: 1px solid var(--line);
    border-left-width: 4px;
    border-radius: var(--radius-card);
    background: rgba(255,255,255,0.98);
    padding: 14px 16px;
}
.probe-row--success { border-left-color: var(--tone-success); background: linear-gradient(180deg, rgba(255,255,255,0.98), rgba(238,246,241,0.72)); }
.probe-row--warning { border-left-color: var(--tone-warning); }
.probe-row--danger { border-left-color: var(--tone-danger); }
.probe-row--info { border-left-color: var(--tone-info); background: linear-gradient(180deg, rgba(255,255,255,0.98), rgba(237,242,251,0.72)); }
.probe-row__heading {
    display: flex;
    justify-content: space-between;
    gap: 12px;
    align-items: start;
}
.probe-row__metrics {
    display: flex;
    flex-wrap: wrap;
    gap: 8px 12px;
    margin-top: 10px;
}
.probe-metric {
    display: inline-flex;
    gap: 6px;
    padding: 4px 8px;
    border-radius: 999px;
    background: var(--surface-emphasis);
    color: var(--text);
    font-size: 0.84rem;
}
.probe-row__note {
    margin-top: 10px;
    color: var(--text-muted);
    line-height: 1.56;
    overflow-wrap: anywhere;
}
.auth-surface-list {
    display: grid;
    gap: 12px;
    margin-top: 14px;
}
.auth-surface {
    border: 1px solid var(--line);
    border-radius: var(--radius-card);
    background: rgba(255,255,255,0.98);
    padding: 13px 15px;
}
.auth-surface__header {
    display: flex;
    justify-content: space-between;
    gap: 12px;
    align-items: start;
}
.auth-surface__title {
    margin-top: 2px;
    font-size: 1.06rem;
    line-height: 1.26;
    color: var(--text-strong);
    font-weight: 700;
    overflow-wrap: anywhere;
}
.auth-surface__meta {
    margin-top: 4px;
    color: var(--text-muted);
    font-size: 0.88rem;
    line-height: 1.5;
}
.auth-scenario-list {
    display: grid;
    gap: 8px;
    margin-top: 10px;
}
.auth-scenario {
    border: 1px solid var(--line);
    border-left-width: 4px;
    border-radius: 12px;
    background: var(--surface-muted);
    padding: 10px 12px;
}
.auth-scenario--success { border-left-color: var(--tone-success); background: linear-gradient(180deg, rgba(255,255,255,0.98), rgba(238,246,241,0.68)); }
.auth-scenario--warning { border-left-color: var(--tone-warning); }
.auth-scenario--danger { border-left-color: var(--tone-danger); }
.auth-scenario--info { border-left-color: var(--tone-info); background: linear-gradient(180deg, rgba(255,255,255,0.98), rgba(237,242,251,0.68)); }
.auth-scenario--neutral { border-left-color: var(--tone-neutral-line); background: linear-gradient(180deg, rgba(255,255,255,0.98), rgba(242,245,248,0.72)); }
.auth-scenario__header {
    display: flex;
    justify-content: space-between;
    gap: 10px;
    align-items: start;
}
.auth-scenario__title {
    font-size: 0.98rem;
    line-height: 1.3;
    color: var(--text-strong);
    font-weight: 700;
}
.auth-scenario__comparison {
    display: grid;
    grid-template-columns: repeat(2, minmax(0, 1fr));
    gap: 8px;
    margin-top: 8px;
}
.auth-fact {
    border-radius: 10px;
    background: rgba(255,255,255,0.76);
    border: 1px solid rgba(174, 187, 201, 0.42);
    padding: 8px 10px;
}
.auth-fact__label {
    color: var(--text-soft);
    font-size: 0.72rem;
    letter-spacing: 0.08em;
    text-transform: uppercase;
    font-weight: 700;
}
.auth-fact__value {
    margin-top: 5px;
    color: var(--text);
    line-height: 1.46;
    font-size: 0.92rem;
    overflow-wrap: anywhere;
}
.auth-scenario__metrics {
    display: flex;
    flex-wrap: wrap;
    gap: 8px;
    margin-top: 8px;
}
.auth-scenario__metrics--dense {
    gap: 6px;
}
.auth-scenario__metrics--dense .probe-metric {
    background: rgba(255,255,255,0.78);
}
.auth-scenario--compact {
    padding-bottom: 8px;
}
.auth-scenario__detail-grid {
    display: grid;
    grid-template-columns: repeat(auto-fit, minmax(260px, 1fr));
    gap: 8px;
}
.auth-scenario__block {
    margin-top: 8px;
    padding-top: 8px;
    border-top: 1px solid rgba(174, 187, 201, 0.4);
}
.auth-scenario__block--emphasis {
    padding: 8px 10px;
    border-radius: 10px;
    background: rgba(255,255,255,0.56);
    border-top-color: transparent;
}
.auth-scenario__block-label {
    color: var(--text-soft);
    font-size: 0.72rem;
    letter-spacing: 0.08em;
    text-transform: uppercase;
    font-weight: 700;
}
.auth-scenario__block-value {
    margin-top: 5px;
    color: var(--text);
    line-height: 1.48;
    font-size: 0.92rem;
    overflow-wrap: anywhere;
}
.compact-list {
    margin: 12px 0 0;
    padding-left: 18px;
    color: var(--text);
}
.compact-list--tight {
    margin-top: 10px;
}
.compact-list li {
    margin-top: 8px;
    line-height: 1.58;
}
.score-strip {
    display: grid;
    grid-template-columns: repeat(auto-fit, minmax(180px, 1fr));
    gap: 12px;
    margin-top: 18px;
}
.score-pill {
    --score-pill-eyebrow: var(--text-soft);
    --score-pill-meta: var(--text-muted);
    border: 1px solid var(--line);
    border-radius: var(--radius-card);
    background: linear-gradient(180deg, rgba(255,255,255,0.98), rgba(248,250,252,0.94));
    box-shadow: 0 8px 18px rgba(24,34,49,0.04);
    padding: 14px 16px;
}
.score-pill--success { border-color: var(--tone-success-line); background: linear-gradient(180deg, rgba(255,255,255,0.98), rgba(238,246,241,0.86)); --score-pill-eyebrow: var(--tone-success); --score-pill-meta: var(--tone-success); }
.score-pill--warning { border-color: var(--tone-warning-line); background: linear-gradient(180deg, rgba(255,255,255,0.98), rgba(250,241,230,0.72)); }
.score-pill--danger { border-color: var(--tone-danger-line); background: linear-gradient(180deg, rgba(255,255,255,0.98), rgba(249,236,239,0.72)); }
.score-pill--info { border-color: var(--tone-info-line); background: linear-gradient(180deg, rgba(255,255,255,0.98), rgba(237,242,251,0.86)); --score-pill-eyebrow: var(--tone-info); --score-pill-meta: var(--tone-info); }
.score-pill--neutral { border-color: var(--tone-neutral-line); background: linear-gradient(180deg, rgba(255,255,255,0.98), rgba(242,245,248,0.9)); --score-pill-eyebrow: var(--tone-neutral); --score-pill-meta: var(--tone-neutral); }
.score-pill__eyebrow {
    font-size: 0.72rem;
    letter-spacing: 0.12em;
    text-transform: uppercase;
    color: var(--score-pill-eyebrow);
    font-weight: 700;
}
.score-pill__value {
    margin-top: 8px;
    font-size: 1.65rem;
    line-height: 1.05;
    letter-spacing: -0.04em;
    color: var(--text-strong);
    font-weight: 700;
}
.score-pill__meta {
    margin-top: 7px;
    color: var(--score-pill-meta);
    font-size: 0.88rem;
    line-height: 1.45;
}
.panel-grid {
    display: grid;
    grid-template-columns: repeat(auto-fit, minmax(220px, 1fr));
    gap: 16px;
    margin-top: 24px;
}
.metric-card {
    --metric-eyebrow-color: var(--text-soft);
    --metric-support-color: var(--text-muted);
    border-radius: var(--radius-card);
    border: 1px solid var(--line);
    padding: 18px 18px 16px;
    background: linear-gradient(180deg, rgba(255,255,255,0.96), rgba(248,250,252,0.9));
    min-height: 152px;
    box-shadow: 0 10px 28px rgba(24,34,49,0.05);
}
.metric-card--success { border-color: var(--tone-success-line); background: linear-gradient(180deg, rgba(255,255,255,0.96), rgba(238,246,241,0.84)); --metric-eyebrow-color: var(--tone-success); --metric-support-color: var(--tone-success); }
.metric-card--warning { border-color: var(--tone-warning-line); background: linear-gradient(180deg, rgba(255,255,255,0.95), rgba(250,241,230,0.8)); }
.metric-card--danger { border-color: var(--tone-danger-line); background: linear-gradient(180deg, rgba(255,255,255,0.95), rgba(249,236,239,0.8)); }
.metric-card--info { border-color: var(--tone-info-line); background: linear-gradient(180deg, rgba(255,255,255,0.95), rgba(237,242,251,0.84)); --metric-eyebrow-color: var(--tone-info); --metric-support-color: var(--tone-info); }
.metric-card--neutral { border-color: var(--tone-neutral-line); background: linear-gradient(180deg, rgba(255,255,255,0.95), rgba(242,245,248,0.88)); --metric-eyebrow-color: var(--tone-neutral); --metric-support-color: var(--tone-neutral); }
.metric-eyebrow {
    font-size: 0.72rem;
    letter-spacing: 0.14em;
    text-transform: uppercase;
    color: var(--metric-eyebrow-color);
    font-weight: 700;
}
.metric-value {
    margin-top: 12px;
    font-size: 2rem;
    line-height: 1;
    letter-spacing: -0.04em;
    color: var(--text-strong);
    font-weight: 700;
}
.metric-label {
    margin-top: 10px;
    font-size: 1rem;
    color: var(--text-strong);
    font-weight: 600;
}
.metric-support {
    margin-top: 8px;
    color: var(--metric-support-color);
    font-size: 0.9rem;
    line-height: 1.55;
}
.stack-grid {
    display: grid;
    grid-template-columns: repeat(2, minmax(0, 1fr));
    gap: 18px;
    margin-top: 22px;
}
.stack-list {
    display: grid;
    gap: 12px;
}
.stack-item {
    border-radius: 16px;
    border: 1px solid var(--line);
    background: rgba(255,255,255,0.74);
    padding: 14px 16px;
}
.stack-item__index {
    font-size: 0.72rem;
    letter-spacing: 0.14em;
    text-transform: uppercase;
    color: var(--text-soft);
    font-weight: 700;
}
.stack-item__text {
    margin-top: 8px;
    color: var(--text);
    line-height: 1.62;
    font-size: 0.95rem;
}
.section {
    margin-top: 16px;
}
.section-card {
    border-radius: var(--radius-panel);
    background: linear-gradient(180deg, rgba(255,255,255,0.94), rgba(248,250,252,0.9));
    border: 1px solid var(--line);
    box-shadow: var(--shadow-soft);
    overflow: hidden;
}
.section-card--tone-success { border-color: var(--tone-success-line); background: linear-gradient(180deg, rgba(255,255,255,0.98), rgba(238,246,241,0.9)); }
.section-card--tone-warning { border-color: var(--tone-warning-line); background: linear-gradient(180deg, rgba(255,255,255,0.98), rgba(250,241,230,0.88)); }
.section-card--tone-danger { border-color: var(--tone-danger-line); background: linear-gradient(180deg, rgba(255,255,255,0.98), rgba(249,236,239,0.88)); }
.section-card--tone-info { border-color: var(--tone-info-line); background: linear-gradient(180deg, rgba(255,255,255,0.98), rgba(237,242,251,0.9)); }
.section-card--tone-neutral { border-color: var(--tone-neutral-line); background: linear-gradient(180deg, rgba(255,255,255,0.98), rgba(242,245,248,0.92)); }
.section-card__summary {
    list-style: none;
    cursor: pointer;
    display: grid;
    gap: 14px;
    padding: 18px;
}
.section-card__summary::-webkit-details-marker {
    display: none;
}
.section-card__summary::marker {
    display: none;
}
.section-card__summary-main {
    min-width: 0;
}
.section-title--card,
.section-intro--card {
    max-width: none;
}
.section-card__abstract {
    display: grid;
    grid-template-columns: repeat(auto-fit, minmax(148px, 1fr));
    gap: 10px;
}
.section-abstract {
    border-radius: 14px;
    border: 1px solid rgba(174, 187, 201, 0.5);
    background: rgba(255,255,255,0.82);
    padding: 12px 13px;
    min-width: 0;
}
.section-abstract--success { border-color: var(--tone-success-line); background: rgba(238,246,241,0.9); }
.section-abstract--warning { border-color: var(--tone-warning-line); background: rgba(250,241,230,0.9); }
.section-abstract--danger { border-color: var(--tone-danger-line); background: rgba(249,236,239,0.9); }
.section-abstract--info { border-color: var(--tone-info-line); background: rgba(237,242,251,0.9); }
.section-abstract--neutral { border-color: var(--tone-neutral-line); background: rgba(242,245,248,0.92); }
.section-abstract__label {
    font-size: 0.68rem;
    letter-spacing: 0.12em;
    text-transform: uppercase;
    color: var(--text-soft);
    font-weight: 700;
}
.section-abstract__value {
    margin-top: 7px;
    color: var(--text-strong);
    font-size: 1rem;
    line-height: 1.34;
    font-weight: 700;
    overflow-wrap: anywhere;
}
.section-card__toggle-row {
    display: inline-flex;
    align-items: center;
    gap: 10px;
    justify-self: start;
    margin-top: 2px;
    padding: 8px 12px;
    border: 1px solid rgba(54, 95, 161, 0.22);
    border-radius: 999px;
    background: rgba(255,255,255,0.92);
    box-shadow: 0 6px 16px rgba(15, 23, 38, 0.08);
    color: var(--text-strong);
}
.section-card__summary:hover .section-card__toggle-row,
.section-card__summary:focus-visible .section-card__toggle-row {
    border-color: rgba(54, 95, 161, 0.4);
    background: rgba(255,255,255,0.98);
}
.section-card__toggle-label {
    font-size: 0.76rem;
    letter-spacing: 0.12em;
    text-transform: uppercase;
    font-weight: 800;
}
.section-card__toggle-label--expanded {
    display: none;
}
.section-card__toggle-icon {
    position: relative;
    width: 18px;
    height: 18px;
    border: 1.5px solid currentColor;
    border-radius: 999px;
    flex: 0 0 auto;
}
.section-card__toggle-icon::before {
    content: "";
    position: absolute;
    left: 5px;
    top: 4px;
    width: 5px;
    height: 5px;
    border-right: 2px solid currentColor;
    border-bottom: 2px solid currentColor;
    transform: rotate(45deg);
    transition: transform 160ms ease, top 160ms ease;
}
.section-card[open] .section-card__toggle-label--collapsed {
    display: none;
}
.section-card[open] .section-card__toggle-label--expanded {
    display: inline;
}
.section-card[open] .section-card__toggle-icon::before {
    transform: rotate(225deg);
    top: 7px;
}
.section-card__content {
    border-top: 1px solid rgba(174, 187, 201, 0.45);
    padding: 0 18px 18px;
}
.section-card__content > :first-child {
    margin-top: 14px;
}
.section-shell {
    border-radius: var(--radius-panel);
    background: linear-gradient(180deg, rgba(255,255,255,0.9), rgba(248,250,252,0.86));
    border: 1px solid var(--line);
    box-shadow: var(--shadow-soft);
    padding: 18px;
}
.section-kicker {
    font-size: 0.72rem;
    letter-spacing: 0.16em;
    text-transform: uppercase;
    color: var(--text-soft);
    font-weight: 700;
}
.section-title {
    margin: 7px 0 5px;
    font-size: 1.34rem;
    line-height: 1.14;
    letter-spacing: -0.03em;
    color: var(--text-strong);
}
.section-intro {
    margin: 0;
    color: var(--text-muted);
    line-height: 1.56;
    max-width: 920px;
}
.section-shell > .section-kicker,
.section-shell > .section-title,
.section-shell > .section-intro {
    text-align: center;
}
.section-shell > .section-intro {
    margin-left: auto;
    margin-right: auto;
}
.principle-rail {
    display: grid;
    grid-template-columns: repeat(auto-fit, minmax(170px, 1fr));
    gap: 10px;
    margin-top: 18px;
}
.principle-card {
    border-radius: 16px;
    border: 1px solid rgba(174, 187, 201, 0.48);
    background: rgba(255,255,255,0.82);
    padding: 13px 14px;
    box-shadow: 0 8px 20px rgba(24,34,49,0.035);
}
.principle-card__title {
    font-size: 0.72rem;
    letter-spacing: 0.14em;
    text-transform: uppercase;
    color: var(--text-soft);
    font-weight: 700;
}
.principle-card__copy {
    margin-top: 8px;
    color: var(--text);
    line-height: 1.52;
    font-size: 0.92rem;
}
.distribution-summary {
    margin-top: 14px;
    padding: 12px 14px;
    border: 1px solid var(--line);
    border-radius: var(--radius-card);
    background: rgba(255,255,255,0.96);
}
.distribution-summary__title {
    font-size: 0.8rem;
    letter-spacing: 0.14em;
    text-transform: uppercase;
    color: var(--text-soft);
    font-weight: 700;
}
.distribution-summary__cards {
    display: grid;
    grid-template-columns: repeat(auto-fit, minmax(140px, 1fr));
    gap: 8px;
    margin-top: 10px;
}
.distribution-card {
    border: 1px solid var(--line);
    border-radius: 12px;
    background: linear-gradient(180deg, rgba(255,255,255,0.96), rgba(248,250,252,0.92));
    box-shadow: 0 6px 14px rgba(24,34,49,0.03);
    padding: 9px 11px;
}
.distribution-card--success { border-color: var(--tone-success-line); background: linear-gradient(180deg, rgba(255,255,255,0.96), rgba(238,246,241,0.78)); }
.distribution-card--warning { border-color: var(--tone-warning-line); background: linear-gradient(180deg, rgba(255,255,255,0.96), rgba(250,241,230,0.72)); }
.distribution-card--danger { border-color: var(--tone-danger-line); background: linear-gradient(180deg, rgba(255,255,255,0.96), rgba(249,236,239,0.72)); }
.distribution-card--info { border-color: var(--tone-info-line); background: linear-gradient(180deg, rgba(255,255,255,0.96), rgba(237,242,251,0.78)); }
.distribution-card--neutral { border-color: var(--tone-neutral-line); background: linear-gradient(180deg, rgba(255,255,255,0.96), rgba(242,245,248,0.84)); }
.distribution-card__label {
    font-size: 0.72rem;
    letter-spacing: 0.1em;
    text-transform: uppercase;
    color: var(--text-soft);
    font-weight: 700;
}
.distribution-card__value {
    margin-top: 7px;
    font-size: 1.34rem;
    line-height: 1;
    letter-spacing: -0.03em;
    color: var(--text-strong);
    font-weight: 700;
}
.distribution-card__meta {
    margin-top: 5px;
    color: var(--text-muted);
    font-size: 0.84rem;
    line-height: 1.4;
}
.distribution-bar {
    display: flex;
    gap: 4px;
    margin-top: 10px;
    height: 10px;
    padding: 1px;
    border-radius: 999px;
    background: var(--surface-emphasis);
}
.distribution-segment {
    min-width: 14px;
    border-radius: 999px;
    opacity: 0.88;
}
.distribution-segment--success { background: var(--line-strong); }
.distribution-swatch--success { background: var(--tone-success); }
.distribution-segment--warning { background: var(--tone-warning-line); }
.distribution-swatch--warning { background: var(--tone-warning); }
.distribution-segment--danger { background: var(--tone-danger-line); }
.distribution-swatch--danger { background: var(--tone-danger); }
.distribution-segment--info { background: var(--line); }
.distribution-swatch--info { background: var(--tone-info); }
.distribution-segment--neutral { background: var(--tone-neutral-line); }
.distribution-swatch--neutral { background: var(--tone-neutral); }
.distribution-legend {
    display: flex;
    flex-wrap: wrap;
    gap: 8px 14px;
    margin-top: 10px;
}
.distribution-legend__item {
    display: inline-flex;
    align-items: center;
    gap: 8px;
    color: var(--text);
    font-size: 0.9rem;
}
.distribution-swatch {
    width: 10px;
    height: 10px;
    border-radius: 50%;
    flex: 0 0 auto;
}
.dual-grid {
    display: grid;
    grid-template-columns: 1.25fr 1fr;
    gap: 14px;
}
.list-panel {
    display: grid;
    gap: 10px;
    margin-top: 14px;
}
.finding-row {
    display: grid;
    grid-template-columns: auto 1fr;
    gap: 10px;
    align-items: start;
    border-radius: 16px;
    border: 1px solid var(--line);
    background: rgba(255,255,255,0.76);
    padding: 12px 14px;
}
.finding-bullet {
    width: 11px;
    height: 11px;
    border-radius: 50%;
    margin-top: 7px;
}
.finding-row--success .finding-bullet { background: var(--tone-success); }
.finding-row--warning .finding-bullet { background: var(--tone-warning); }
.finding-row--danger .finding-bullet { background: var(--tone-danger); }
.finding-row--info .finding-bullet { background: var(--tone-info); }
.finding-row--neutral .finding-bullet { background: var(--tone-neutral); }
.finding-copy {
    color: var(--text);
    line-height: 1.58;
    font-size: 0.95rem;
}
.summary-grid {
    display: grid;
    grid-template-columns: repeat(auto-fit, minmax(220px, 1fr));
    gap: 12px;
    margin-top: 14px;
}
.summary-card {
    border-radius: 16px;
    border: 1px solid var(--line);
    background: rgba(255,255,255,0.78);
    padding: 13px 14px;
}
.summary-card__label {
    font-size: 0.72rem;
    letter-spacing: 0.14em;
    text-transform: uppercase;
    color: var(--text-soft);
}
.summary-card__value {
    margin-top: 8px;
    color: var(--text-strong);
    font-size: 1.02rem;
    line-height: 1.45;
    font-weight: 600;
}
.card-grid {
    display: grid;
    grid-template-columns: repeat(auto-fit, minmax(240px, 1fr));
    gap: 12px;
    margin-top: 14px;
}
.evidence-card {
    border-radius: 18px;
    border: 1px solid var(--line);
    background: rgba(255,255,255,0.82);
.authority-summary-grid {
    display: grid;
    grid-template-columns: repeat(auto-fit, minmax(260px, 1fr));
    gap: 12px;
    margin-top: 14px;
}
.authority-card {
    border-radius: 18px;
    border: 1px solid var(--line);
    background: rgba(255,255,255,0.88);
    padding: 14px;
    box-shadow: 0 8px 24px rgba(24,34,49,0.04);
}
.authority-card--success { border-color: var(--tone-success-line); background: linear-gradient(180deg, rgba(255,255,255,0.96), rgba(238,246,241,0.82)); }
.authority-card--warning { border-color: var(--tone-warning-line); background: linear-gradient(180deg, rgba(255,255,255,0.96), rgba(250,241,230,0.78)); }
.authority-card--danger { border-color: var(--tone-danger-line); background: linear-gradient(180deg, rgba(255,255,255,0.96), rgba(249,236,239,0.78)); }
.authority-card--info { border-color: var(--tone-info-line); background: linear-gradient(180deg, rgba(255,255,255,0.96), rgba(237,242,251,0.82)); }
.authority-card--neutral { border-color: var(--tone-neutral-line); background: linear-gradient(180deg, rgba(255,255,255,0.96), rgba(242,245,248,0.88)); }
.authority-card__header {
    display: flex;
    justify-content: space-between;
    gap: 12px;
    align-items: flex-start;
}
.authority-card__eyebrow {
    font-size: 0.72rem;
    letter-spacing: 0.14em;
    text-transform: uppercase;
    color: var(--text-soft);
    font-weight: 700;
}
.authority-card__title {
    margin-top: 6px;
    font-size: 1.05rem;
    line-height: 1.2;
    color: var(--text-strong);
    font-weight: 700;
}
.authority-card__metrics {
    display: grid;
    grid-template-columns: repeat(2, minmax(0, 1fr));
    gap: 8px;
    margin-top: 12px;
}
.authority-card__metric {
    border-radius: 12px;
    border: 1px solid rgba(174, 187, 201, 0.45);
    background: rgba(255,255,255,0.7);
    padding: 8px 10px;
}
.authority-card__metric-label {
    font-size: 0.7rem;
    letter-spacing: 0.12em;
    text-transform: uppercase;
    color: var(--text-soft);
    font-weight: 700;
}
.authority-card__metric-value {
    margin-top: 6px;
    color: var(--text-strong);
    font-size: 0.94rem;
    line-height: 1.4;
    font-weight: 600;
    overflow-wrap: anywhere;
}
.authority-card__label {
    margin-top: 12px;
    font-size: 0.72rem;
    letter-spacing: 0.12em;
    text-transform: uppercase;
    color: var(--text-soft);
    font-weight: 700;
}
.authority-card__empty {
    margin: 10px 0 0;
    color: var(--text-muted);
    line-height: 1.56;
}
    padding: 14px;
    box-shadow: 0 8px 24px rgba(24,34,49,0.04);
}
.evidence-card__header {
    display: flex;
    justify-content: space-between;
    gap: 12px;
    align-items: flex-start;
}
.evidence-card__title {
    font-size: 1.02rem;
    font-weight: 700;
    color: var(--text-strong);
}
.evidence-card__subtle {
    margin-top: 4px;
    color: var(--text-soft);
    font-size: 0.84rem;
}
.evidence-stats {
    display: flex;
    flex-wrap: wrap;
    gap: 12px;
    margin-top: 12px;
}
.evidence-stat {
    min-width: 110px;
}
.evidence-stat__label {
    font-size: 0.7rem;
    letter-spacing: 0.12em;
    text-transform: uppercase;
    color: var(--text-soft);
}
.evidence-stat__value {
    margin-top: 6px;
    font-size: 1.2rem;
    color: var(--text-strong);
    font-weight: 700;
}
.evidence-note {
    margin-top: 10px;
    font-size: 0.92rem;
    line-height: 1.58;
    color: var(--text-muted);
}
.table-shell {
    margin-top: 14px;
    border: 1px solid var(--line);
    border-radius: 18px;
    overflow: auto;
    background: rgba(255,255,255,0.98);
}
.table-shell--compact {
    margin-top: 14px;
}
.data-table {
    width: 100%;
    border-collapse: collapse;
    font-size: 0.92rem;
}
.data-table thead {
    background: var(--surface-emphasis);
}
.data-table th,
.data-table td {
    padding: 12px 14px;
    border-bottom: 1px solid var(--line);
    text-align: left;
    vertical-align: top;
    overflow-wrap: anywhere;
}
.data-table th {
    color: var(--text-soft);
    font-size: 0.74rem;
    letter-spacing: 0.12em;
    text-transform: uppercase;
    font-weight: 700;
}
.data-table tbody tr:nth-child(even) {
    background: rgba(247,249,252,0.62);
}
.data-table tbody tr:last-child td {
    border-bottom: 0;
}
code {
    font-family: "Cascadia Code", "SFMono-Regular", Consolas, monospace;
    font-size: 0.88em;
    background: rgba(24,34,49,0.06);
    padding: 2px 6px;
    border-radius: 6px;
}
a {
    color: var(--tone-info);
    text-decoration: none;
}
a:hover {
    text-decoration: underline;
}
.inline-list {
    display: flex;
    flex-wrap: wrap;
    gap: 8px;
    margin-top: 14px;
}
.tag {
    display: inline-flex;
    align-items: center;
    border-radius: 999px;
    padding: 6px 11px;
    border: 1px solid var(--line);
    background: rgba(255,255,255,0.82);
    color: var(--text);
    font-size: 0.82rem;
}
.rich-list {
    margin: 0;
    padding-left: 18px;
    color: var(--text);
}
.rich-list li {
    margin-top: 8px;
    line-height: 1.64;
}
.cell-title {
    font-weight: 700;
    color: var(--text-strong);
}
.table-subtle {
    margin-top: 6px;
    color: var(--text-muted);
    font-size: 0.86rem;
    line-height: 1.5;
    overflow-wrap: anywhere;
}
.check-summary {
    display: flex;
    flex-wrap: wrap;
    gap: 8px;
    font-variant-numeric: tabular-nums;
}
.check-summary span {
    display: inline-flex;
    align-items: center;
    gap: 4px;
    border-radius: 999px;
    padding: 4px 8px;
    background: var(--surface-emphasis);
    color: var(--text);
    font-size: 0.82rem;
}
.capability-note {
    line-height: 1.55;
    color: var(--text-muted);
}
.status-callout__title {
    font-size: 0.95rem;
    font-weight: 700;
    color: var(--text-strong);
}
.status-callout__body {
    margin-top: 6px;
    color: var(--text-muted);
    line-height: 1.55;
}
.minor-title {
    margin: 0;
    font-size: 1rem;
    color: var(--text-strong);
}
.appendix-grid {
    display: grid;
    grid-template-columns: repeat(2, minmax(0, 1fr));
    gap: 20px;
    margin-top: 18px;
}
.producer-signature {
    margin-top: 24px;
    color: var(--text-soft);
    font-size: 0.86rem;
    text-align: center;
}
.producer-signature a {
    color: inherit;
    text-decoration-line: underline;
    text-decoration-color: rgba(106, 119, 137, 0.85);
    text-decoration-thickness: 1.5px;
    text-underline-offset: 0.18em;
}
.producer-signature a:hover {
    text-decoration-color: currentColor;
}
.producer-signature__divider {
    display: inline-block;
    margin: 0 8px;
    color: var(--line-strong);
}
.footer {
    margin-top: 10px;
    padding: 10px 6px 0;
    color: var(--text-soft);
    font-size: 0.84rem;
    text-align: center;
}
@media (max-width: 960px) {
    body { padding: 18px; }
    .report-content { padding: 18px; }
    .hero { padding: 22px; }
    .hero-grid,
    .dual-grid,
    .brief-grid,
    .appendix-grid {
        grid-template-columns: 1fr;
    }
    .stack-grid {
        align-items: start;
        grid-template-columns: 1fr;
    }
    .hero-facts,
    .fact-grid {
        grid-template-columns: 1fr;
    }
    .ledger-entry {
        grid-template-columns: 1fr;
    }
    .auth-surface__header,
    .probe-row__heading {
        flex-direction: column;
    }
    .auth-scenario__comparison {
        grid-template-columns: 1fr;
    }
    .auth-scenario__detail-grid {
        grid-template-columns: 1fr;
    }
    .section-card__abstract,
    .principle-rail {
        grid-template-columns: 1fr;
    }
    .section-heading-row {
        flex-direction: column;
    }
    .authority-card__header {
        flex-direction: column;
    }
    .authority-card__metrics {
        grid-template-columns: 1fr;
    }
}
@media print {
    @page {
        size: A4;
        margin: 12mm;
    }
    body {
        background: #ffffff;
        padding: 0;
        color: #1b2431;
    }
    .report-shell {
        max-width: none;
        border: 0;
        box-shadow: none;
        background: #ffffff;
    }
    .report-shell::before {
        display: none;
    }
    .report-content {
        padding: 0;
    }
    .hero,
    .panel,
    .section-card,
    .section-shell,
    .distribution-summary,
    .auth-surface,
    .auth-scenario,
    .ledger-entry,
    .probe-row,
    .metric-card,
    .evidence-card,
    .summary-card,
    .section-abstract,
    .principle-card,
    .table-shell,
    .meta-card,
    .stack-item,
    .finding-row {
        box-shadow: none;
        background: #ffffff;
    }
    .hero,
    .panel,
    .section-card,
    .section-shell,
    .distribution-summary,
    .auth-surface,
    .auth-scenario,
    .ledger-entry,
    .probe-row,
    .metric-card,
    .evidence-card,
    .summary-card,
    .section-abstract,
    .principle-card,
    .table-shell,
    .meta-card,
    .stack-item,
    .finding-row,
    .summary-grid,
    .card-grid,
    .panel-grid,
    .hero-meta,
    .table-shell,
    .section {
        break-inside: avoid;
        page-break-inside: avoid;
    }
    a {
        color: inherit;
        text-decoration: none;
    }
    .section-card__toggle-row {
        display: none;
    }
    details.section-card > summary {
        list-style: none;
    }
    details.section-card > summary::-webkit-details-marker {
        display: none;
    }
    details.section-card > .section-card__content {
        display: block !important;
    }
}
""";
    }

    public static string ToCssTone(HtmlReportTone tone)
    {
        return tone switch
        {
            HtmlReportTone.Success => "success",
            HtmlReportTone.Warning => "warning",
            HtmlReportTone.Danger => "danger",
            HtmlReportTone.Info => "info",
            _ => "neutral"
        };
    }
}
