namespace Mcp.Benchmark.Infrastructure.Services.Reporting;

internal static class ValidationHtmlReportInteractions
{
    public static string BuildScript() => """
(() => {
    const sectionControls = document.querySelector('.report-controls');
    if (sectionControls) {
        document.getElementById('expand-evidence').addEventListener('click', () =>
            document.querySelectorAll('details').forEach(detail => { detail.open = true; }));
        document.getElementById('collapse-evidence').addEventListener('click', () =>
            document.querySelectorAll('details').forEach(detail => { detail.open = false; }));
        sectionControls.hidden = false;
    }
    let printDetails = [];
    window.addEventListener('beforeprint', () => {
        printDetails = Array.from(document.querySelectorAll('details:not([open])'));
        printDetails.forEach(element => { element.open = true; });
    });
    window.addEventListener('afterprint', () => {
        printDetails.forEach(element => { element.open = false; });
        printDetails = [];
    });
    const section = document.getElementById('recorded-claims');
    const controls = document.getElementById('claim-filters');
    const search = document.getElementById('claim-search');
    const outcome = document.getElementById('claim-outcome');
    const reset = document.getElementById('claim-filter-reset');
    const status = document.getElementById('claim-filter-status');
    const empty = document.getElementById('claim-filter-empty');
    if (!section || !controls || !search || !outcome || !reset || !status || !empty) return;
    const entries = Array.from(section.querySelectorAll('article[data-claim-id]'), element => ({
        element,
        outcome: element.dataset.claimOutcome,
        text: element.textContent.toLowerCase()
    }));
    const apply = () => {
        const query = search.value.slice(0, 512).trim().toLowerCase();
        let visible = 0;
        for (const entry of entries) {
            const matches = (!query || entry.text.includes(query)) && (!outcome.value || entry.outcome === outcome.value);
            entry.element.hidden = !matches;
            if (matches) visible++;
        }
        status.textContent = `${visible} of ${entries.length} recorded claims`;
        empty.hidden = visible !== 0 || entries.length === 0;
        reset.disabled = !search.value && !outcome.value;
    };
    const clear = () => { search.value = ''; outcome.value = ''; apply(); };
    const revealAnchor = () => {
        let id;
        try { id = decodeURIComponent(location.hash.slice(1)); } catch { return; }
        const target = document.getElementById(id);
        if (!target) return;
        if (section.contains(target)) clear();
        for (let parent = target.parentElement; parent; parent = parent.parentElement)
            if (parent.tagName === 'DETAILS') parent.open = true;
        if (target.matches('article[data-claim-id]'))
            target.querySelectorAll('details').forEach(element => { element.open = true; });
        if (target.tagName === 'DETAILS') target.open = true;
        target.scrollIntoView();
    };
    search.addEventListener('input', apply);
    outcome.addEventListener('change', apply);
    reset.addEventListener('click', clear);
    window.addEventListener('hashchange', revealAnchor);
    document.addEventListener('click', event => {
        const link = event.target instanceof Element ? event.target.closest('a[href^="#"]') : null;
        if (link && link.hash === location.hash) revealAnchor();
    });
    apply();
    controls.hidden = false;
    revealAnchor();
})();
""";
}