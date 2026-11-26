import assert from 'node:assert/strict';
import { pathToFileURL } from 'node:url';
import path from 'node:path';
import { writeFile } from 'node:fs/promises';
import { chromium } from 'playwright';

export async function exportReportPdf(page, format) {
    assert(['A4', 'Letter'].includes(format), 'Paper must be A4 or Letter');
    return page.pdf({ format, scale: 1, printBackground: false, displayHeaderFooter: false, preferCSSPageSize: false });
}

if (process.argv[1] && import.meta.url === pathToFileURL(path.resolve(process.argv[1])).href) {
    const [input, output, format = 'A4'] = process.argv.slice(2);
    assert(input && output, 'Usage: node scripts/report-quality/export-pdf.mjs <report.html> <output.pdf> [A4|Letter]');
    assert(path.resolve(input) !== path.resolve(output), 'Input and output must differ');
    const browser = await chromium.launch();
    try {
        const context = await browser.newContext({ javaScriptEnabled: false });
        await context.route(/^https?:/, route => route.abort());
        const page = await context.newPage();
        await page.goto(pathToFileURL(path.resolve(input)).href);
        assert.equal(await page.locator('.wordmark').textContent(), 'MCPVal', 'Expected an MCPVal report');
        const bytes = await exportReportPdf(page, format);
        await writeFile(output, bytes);
        console.log(`Exported ${format} using Chromium ${browser.version()}; scale 1, backgrounds/headers off, CSS margins.`);
    } finally {
        await browser.close();
    }
}
