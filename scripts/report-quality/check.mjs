import assert from 'node:assert/strict';
import { createHash } from 'node:crypto';
import { readFile, writeFile, mkdir } from 'node:fs/promises';
import path from 'node:path';
import { pathToFileURL } from 'node:url';
import { chromium } from 'playwright';
import AxeBuilder from '@axe-core/playwright';
import { createCanvas } from '@napi-rs/canvas';
import { getDocument } from 'pdfjs-dist/legacy/build/pdf.mjs';
import { exportReportPdf } from './export-pdf.mjs';

const directory = process.argv[2];
assert(directory, 'Usage: node scripts/report-quality/check.mjs <CLI-corpus-directory>');
const root = path.resolve(directory);
const output = path.join(root, 'browser');
await mkdir(output, { recursive: true });
const browser = await chromium.launch();
const receipt = { browser: browser.version(), scale: 1, printBackground: false, browserHeaders: false, margins: '14mm CSS', reports: [] };
const outcomes = ['Satisfied', 'Violated', 'Incomplete', 'ReviewRequired', 'NotApplicable', 'Error'];
try {
    const names = (await readFile(path.join(root, 'inputs', 'corpus.txt'), 'utf8')).trim().split(/\r?\n/);
    assert(names.length && names.every(name => /^[a-z-]+$/.test(name)), 'Invalid synthetic corpus manifest');
    assert.equal(new Set(names).size, names.length, 'Duplicate synthetic fixture names');
    for (const name of names) {
        const canonical = JSON.parse(await readFile(path.join(root, 'inputs', `${name}.json`), 'utf8'));
        const expected = canonical.assurance.claims.map(claim => [claim.claimId, typeof claim.outcome === 'number'
            ? outcomes[claim.outcome] : outcomes.find(outcome => outcome.toLowerCase() === claim.outcome.toLowerCase())])
            .sort(([a], [b]) => a.localeCompare(b, 'en'));
        const reference = createHash('sha256').update(canonical.run.validationId).digest('hex').slice(0, 12);
        for (const detail of ['minimal', 'full']) {
            const file = `${name}-${detail}`;
            const url = pathToFileURL(path.join(root, `${file}.html`)).href;
            const context = await browser.newContext();
            const page = await context.newPage();
            const activateLink = async locator => {
                const hash = await locator.getAttribute('href');
                await locator.focus();
                assert.notEqual(await locator.evaluate(element => getComputedStyle(element).outlineStyle), 'none', `Visible keyboard focus for ${hash}`);
                await page.keyboard.press('Enter');
                await page.waitForURL(url => url.hash === hash);
            };
            const network = [];
            const errors = [];
            page.on('request', request => { if (!request.url().startsWith('file:')) network.push(request.url()); });
            page.on('pageerror', error => errors.push(error.message));
            await page.goto(url);
            await page.emulateMedia({ colorScheme: 'light' });
            const lightCanvas = await page.evaluate(() => getComputedStyle(document.body).backgroundColor);
            await page.emulateMedia({ colorScheme: 'dark' });
            const darkCanvas = await page.evaluate(() => getComputedStyle(document.body).backgroundColor);
            assert.notEqual(lightCanvas, darkCanvas, `${file}: system theme follows the OS`);
            await page.locator('#report-theme').selectOption('light');
            assert.equal(await page.evaluate(() => getComputedStyle(document.body).backgroundColor), lightCanvas, `${file}: explicit theme overrides OS`);
            await page.locator('#report-theme').selectOption('system');
            assert.equal(await page.evaluate(() => getComputedStyle(document.body).backgroundColor), darkCanvas, `${file}: System restores OS preference`);
            await page.emulateMedia({ colorScheme: 'light' });
            const targetName = await page.locator('h1').textContent();
            const nextAction = await page.locator('.next-action-summary').allTextContents();
            for (const width of [1440, 768, 320]) {
                await page.setViewportSize({ width, height: 1000 });
                assert(await page.evaluate(() => document.documentElement.scrollWidth <= innerWidth + 1), `${file}: overflow at ${width}px`);
                const narrative = await page.locator('.decision-narrative').boundingBox();
                const facts = await page.locator('.decision-facts').boundingBox();
                if (width === 1440) assert(facts.x >= narrative.x + narrative.width, `${file}: desktop fact column`);
                if (width === 320) assert(facts.y >= narrative.y + narrative.height, `${file}: stacked fact column`);
                for (const theme of ['light', 'dark']) {
                    await page.locator('#report-theme').selectOption(theme);
                    const axe = await new AxeBuilder({ page }).withTags(['wcag2a', 'wcag2aa', 'wcag21aa', 'wcag22aa']).analyze();
                    assert.deepEqual(axe.violations.map(v => ({ id: v.id, nodes: v.nodes.map(n => n.target) })), [], `${file}/${theme}/${width}: accessibility`);
                    if (theme === 'dark' && width !== 768)
                        await page.screenshot({ path: path.join(output, `${file}-dark-${width}.png`) });
                }
                await page.locator('#report-theme').selectOption('light');
                if (width === 320) await page.screenshot({ path: path.join(output, `${file}-320.png`) });
            }
            await page.setViewportSize({ width: 1440, height: 1000 });
            const actual = await page.locator('article[data-claim-id]').evaluateAll(elements =>
                elements.map(element => [element.dataset.claimId, element.dataset.claimOutcome]).sort(([a], [b]) => a.localeCompare(b, 'en')));
            assert.deepEqual(actual, expected, `${file}: canonical claim parity`);
            const broken = await page.locator('a[href^="#"]').evaluateAll(links =>
                links.map(link => link.getAttribute('href')).filter(href => !document.getElementById(href.slice(1))));
            assert.deepEqual(broken, [], `${file}: direct links`);
            const ids = await page.locator('[id]').evaluateAll(elements => elements.map(element => element.id));
            assert.equal(new Set(ids).size, ids.length, `${file}: unique IDs`);
            await page.screenshot({ path: path.join(output, `${file}-opening.png`) });
            await page.keyboard.press('Tab');
            assert.equal(await page.locator(':focus').textContent(), 'Skip to assessment', `${file}: first keyboard destination`);
            await page.keyboard.press('Enter');
            const domain = page.locator('#technical-evidence details[id^="domain-"]').first();
            const hasDomain = await domain.count() > 0;
            if (hasDomain) {
                const summary = domain.locator(':scope > summary');
                const chevron = summary.locator('.disclosure-chevron');
                for (const width of [1440, 320]) {
                    await page.setViewportSize({ width, height: 1000 });
                    await domain.evaluate(element => { element.open = false; });
                    await summary.scrollIntoViewIfNeeded();
                    await summary.focus();
                    const initialHash = new URL(page.url()).hash;
                    const start = await summary.boundingBox();
                    const icon = await chevron.boundingBox();
                    const label = await summary.locator('.disclosure-label').boundingBox();
                    assert.equal(icon.width, 16, `${file}: 16px disclosure icon`);
                    assert(icon.x + icon.width <= label.x, `${file}: leading chevron stays adjacent to label`);
                    assert(icon.x < start.x + start.width / 2, `${file}: left-aligned disclosure affordance`);
                    const closedTransform = await chevron.evaluate(element => getComputedStyle(element, '::before').transform);
                    await page.screenshot({ path: path.join(output, `${file}-evidence-closed-${width}.png`) });
                    await page.keyboard.press('Enter');
                    assert(await domain.evaluate(element => element.open), `${file}: native Enter opens disclosure`);
                    const opened = await summary.boundingBox();
                    assert(Math.abs(opened.y - start.y) <= 2, `${file}: opening preserves summary position`);
                    assert.equal(new URL(page.url()).hash, initialHash, `${file}: disclosure does not navigate`);
                    assert.notEqual(await chevron.evaluate(element => getComputedStyle(element, '::before').transform),
                        closedTransform, `${file}: chevron changes direction`);
                    await page.screenshot({ path: path.join(output, `${file}-evidence-open-${width}.png`) });
                    await page.keyboard.press('Space');
                    assert(!await domain.evaluate(element => element.open), `${file}: native Space closes disclosure`);
                    const box = await summary.boundingBox();
                    await summary.click({ position: { x: box.width - 4, y: box.height / 2 } });
                    assert(await domain.evaluate(element => element.open), `${file}: entire summary row is clickable`);
                }
                await page.setViewportSize({ width: 1440, height: 1000 });
            }
            await page.locator('#expand-evidence').focus();
            await page.keyboard.press('Enter');
            assert.equal(await page.locator('details:not(.evidence-peek):not(.report-section):not([open])').count(), 0, `${file}: expand supporting evidence`);
            assert.equal(await page.locator('.evidence-preview').count(), 0, `${file}: bulk expansion does not duplicate claims`);
            const leaf = page.locator('article[data-claim-id] details > summary').first();
            const hasLeaf = await leaf.count() > 0;
            if (hasLeaf) {
                const treatment = await leaf.evaluate(element => {
                    const style = getComputedStyle(element);
                    return { radius: style.borderRadius, left: style.borderLeftWidth, right: style.borderRightWidth };
                });
                assert.deepEqual(treatment, { radius: '0px', left: '0px', right: '0px' }, `${file}: flat supporting record`);
                await leaf.scrollIntoViewIfNeeded();
                await page.screenshot({ path: path.join(output, `${file}-supporting-record.png`) });
            }
            for (const theme of ['light', 'dark']) {
                await page.locator('#report-theme').selectOption(theme);
                const expandedAxe = await new AxeBuilder({ page }).withTags(['wcag2a', 'wcag2aa', 'wcag21aa', 'wcag22aa']).analyze();
                assert.deepEqual(expandedAxe.violations.map(v => v.id), [], `${file}/${theme}: expanded accessibility`);
            }
            await page.locator('#report-theme').selectOption('light');
            await page.locator('#collapse-evidence').focus();
            await page.keyboard.press('Enter');
            assert.equal(await page.locator('details:not(.evidence-peek):not(.report-section)[open]').count(), 0, `${file}: collapse supporting evidence`);
            assert.equal(await page.locator('.report-section:not([open])').count(), 0, `${file}: section overview remains open`);
            const peek = page.locator('#priority-actions .evidence-peek').first();
            if (await peek.count()) {
                const summary = peek.locator(':scope > summary');
                await summary.scrollIntoViewIfNeeded();
                await summary.focus();
                const start = await summary.boundingBox();
                const hash = new URL(page.url()).hash;
                const target = await peek.getAttribute('data-claim-target');
                const claimId = await page.locator(`[id="${target}"]`).getAttribute('data-claim-id');
                await page.keyboard.press('Enter');
                await peek.locator('.evidence-preview').waitFor({ state: 'visible' });
                assert((await peek.textContent()).includes(claimId), `${file}: local evidence is from the exact canonical claim`);
                assert.equal(new URL(page.url()).hash, hash, `${file}: in-place evidence does not navigate`);
                assert(Math.abs((await summary.boundingBox()).y - start.y) <= 2, `${file}: local summary stays in place`);
                assert.equal(await page.locator('article[data-claim-id]').count(), expected.length, `${file}: previews do not duplicate canonical records`);
                const previewIds = await page.locator('[id]').evaluateAll(elements => elements.map(element => element.id));
                assert.equal(new Set(previewIds).size, previewIds.length, `${file}: preview has no duplicate IDs`);
                await page.screenshot({ path: path.join(output, `${file}-local-evidence.png`) });
                await summary.focus();
                await page.keyboard.press('Space');
                await page.waitForFunction(() => !document.querySelector('.evidence-preview'));
            }
            if (nextAction.length) {
                await activateLink(page.locator('.next-action-link'));
                assert.equal(await page.locator('.action-row').first().getAttribute('id'), new URL(page.url()).hash.slice(1));
            }
            const action = page.locator('#priority-actions a[href^="#domain-"]').first();
            let evidencePath = null;
            if (await action.count()) {
                await activateLink(action);
                const target = decodeURIComponent(new URL(page.url()).hash.slice(1));
                await activateLink(page.locator(`[id="${target}"] a`));
                const claimTarget = decodeURIComponent(new URL(page.url()).hash.slice(1));
                await page.locator(`[id="${claimTarget}"] details[open]`).waitFor({ state: 'visible' });
                await page.locator('#claim-search').fill('no-match-same-fragment');
                await activateLink(page.locator(`#priority-actions .ledger-body > p > a[href="#${claimTarget}"]`).first());
                assert(await page.locator(`[id="${claimTarget}"]`).isVisible(), `${file}: repeated fragment reveals filtered claim`);
                await page.screenshot({ path: path.join(output, `${file}-claim-path.png`) });
                await activateLink(page.locator(`[id="${claimTarget}"] a[href="#method-and-provenance"]`));
                assert.equal(new URL(page.url()).hash, '#method-and-provenance');
                evidencePath = { action: await action.textContent(), domain: target, claim: claimTarget, provenance: 'method-and-provenance' };
            }
            if (await page.locator('.claims-disclosure').getAttribute('open') === null) {
                await page.locator('.claims-disclosure > summary').focus();
                await page.keyboard.press('Enter');
            }
            await page.locator('#claim-search').fill('no-match-synthetic-filter');
            assert.equal(await page.locator('article[data-claim-id]:visible').count(), 0);
            if (actual.length) {
                const anchor = await page.locator('article[data-claim-id]').first().getAttribute('id');
                await page.goto(`${url}#${anchor}`);
                assert.equal(await page.locator('article[data-claim-id]:visible').count(), expected.length);
            }
            await page.locator('#claim-search').fill('no-match-synthetic-filter');
            await page.locator('details').evaluateAll(elements => elements.forEach(element => { element.open = false; }));
            await page.locator('#report-theme').selectOption('dark');
            if (await peek.count()) {
                await peek.evaluate(element => { element.open = true; });
                await page.waitForFunction(() => document.querySelector('.evidence-preview'));
            }
            await page.emulateMedia({ media: 'print' });
            assert.equal(await page.evaluate(() => getComputedStyle(document.documentElement).colorScheme), 'light', `${file}: print is light even when Dark is selected`);
            assert.equal(await page.locator('.evidence-preview:visible').count(), 0, `${file}: print omits open previews`);
            await page.emulateMedia({ media: null });
            const disclosureHeadings = await page.locator('summary').allTextContents();
            const pdfs = [];
            for (const format of ['A4', 'Letter']) {
                const pdf = await exportReportPdf(page, format);
                const pdfPath = path.join(output, `${file}-${format}.pdf`);
                await writeFile(pdfPath, pdf);
                const loading = getDocument({ data: new Uint8Array(pdf), useSystemFonts: true });
                const document = await loading.promise;
                const texts = [];
                for (let number = 1; number <= document.numPages; number++) {
                    const pdfPage = await document.getPage(number);
                    const content = await pdfPage.getTextContent();
                    const paper = pdfPage.getViewport({ scale: 1 });
                    for (const item of content.items.filter(item => item.str?.trim())) {
                        assert(item.transform[4] >= -1 && item.transform[4] + item.width <= paper.width + 1,
                            `${file}/${format}: horizontally clipped text on page ${number}`);
                        assert(item.transform[5] >= -1 && item.transform[5] <= paper.height + 1,
                            `${file}/${format}: vertically clipped text on page ${number}`);
                    }
                    const text = content.items.map(item => item.str ?? '').join(' ').replace(/\s+/g, ' ');
                    texts.push(text);
                    assert(text.includes(reference), `${file}/${format}: run reference on page ${number}`);
                    assert(text.includes(`Page ${number} of ${document.numPages}`), `${file}/${format}: footer ${number}`);
                    assert(text.length > 70, `${file}/${format}: unexpectedly blank page ${number}`);
                    if (number < document.numPages)
                        assert(!disclosureHeadings.some(heading => text.trim().endsWith(heading.trim())),
                            `${file}/${format}: orphaned disclosure heading on page ${number}`);
                    if ([1, Math.ceil(document.numPages / 2), document.numPages].includes(number)) {
                        const viewport = pdfPage.getViewport({ scale: 1.25 });
                        const canvas = createCanvas(Math.ceil(viewport.width), Math.ceil(viewport.height));
                        await pdfPage.render({ canvasContext: canvas.getContext('2d'), viewport }).promise;
                        await writeFile(path.join(output, `${file}-${format}-page-${number}.png`), canvas.toBuffer('image/png'));
                        if (number === 1 && format === 'A4') {
                            const context = canvas.getContext('2d');
                            const pixels = context.getImageData(0, 0, canvas.width, canvas.height);
                            for (let i = 0; i < pixels.data.length; i += 4) {
                                const gray = Math.round(.2126 * pixels.data[i] + .7152 * pixels.data[i + 1] + .0722 * pixels.data[i + 2]);
                                pixels.data[i] = pixels.data[i + 1] = pixels.data[i + 2] = gray;
                            }
                            context.putImageData(pixels, 0, 0);
                            await writeFile(path.join(output, `${file}-grayscale.png`), canvas.toBuffer('image/png'));
                        }
                    }
                }
                const text = texts.join(' ').replace(/\s+/g, ' ');
                assert(texts[0].includes('Server assessment'), `${file}/${format}: opening page`);
                assert(texts[0].replace(/\s/g, '').includes(targetName.replace(/\s/g, '')), `${file}/${format}: target on page one`);
                if (nextAction.length)
                    assert(texts[0].replace(/\s/g, '').includes(nextAction[0].replace(/\s/g, '')), `${file}/${format}: top action on page one`);
                assert(text.includes(`all ${expected.length} recorded claims`), `${file}/${format}: print filter reset`);
                for (const [id] of expected)
                    assert(text.replace(/\s/g, '').includes(id.replace(/\s/g, '')), `${file}/${format}: printed claim ${id}`);
                for (const heading of ['Coverage', 'Domain evidence', 'Run provenance', 'Appendix: findings and recorded claims'])
                    assert(text.includes(heading), `${file}/${format}: real heading retained: ${heading}`);
                await writeFile(path.join(output, `${file}-${format}.txt`), texts.join('\n\n--- PAGE ---\n\n'));
                pdfs.push({ format, pages: document.numPages, file: path.basename(pdfPath) });
                await loading.destroy();
            }
            assert.deepEqual(network, [], `${file}: offline requests`);
            assert.deepEqual(errors, [], `${file}: script errors`);
            await context.close();
            const noJs = await browser.newContext({ javaScriptEnabled: false, viewport: { width: 320, height: 1000 } });
            const plain = await noJs.newPage();
            await plain.goto(url);
            assert.equal(await plain.locator('article[data-claim-id]').count(), expected.length);
            assert(await plain.locator('#claim-filters').isHidden());
            assert(await plain.locator('#assessment-decision').isVisible());
            assert(await plain.locator('.theme-control').isHidden());
            await plain.emulateMedia({ colorScheme: 'dark' });
            assert.equal(await plain.evaluate(() => getComputedStyle(document.body).backgroundColor), darkCanvas, `${file}: no-JS system theme`);
            await plain.locator('details summary').first().focus();
            await plain.keyboard.press('Enter');
            await noJs.close();
            receipt.reports.push({
                name, detail, claims: expected.length, viewports: [1440, 768, 320], themes: ['light', 'dark'], axeViolations: 0, noJavaScript: 'passed',
                inputSha256: createHash('sha256').update(await readFile(path.join(root, 'inputs', `${name}.json`))).digest('hex'),
                htmlSha256: createHash('sha256').update(await readFile(path.join(root, `${file}.html`))).digest('hex'),
                evidencePath,
                disclosures: {
                    domainChecked: hasDomain,
                    flatRecordChecked: hasLeaf,
                    widths: hasDomain ? [1440, 320] : [],
                    nativeKeys: ['Enter', 'Space'],
                    maximumHeaderMovementPx: 2,
                    decorativeChevronSizePx: 16
                },
                pdfs
            });
            console.log(`${file}: browser, parity, offline, keyboard, no-JS and PDF checks passed`);
        }
    }
    await writeFile(path.join(output, 'receipt.json'), JSON.stringify(receipt, null, 2) + '\n');
    await writeFile(path.join(root, 'index.html'), `<!doctype html><html lang="en"><meta charset="utf-8"><meta name="viewport" content="width=device-width"><title>MCPVal synthetic report review</title>
<h1>MCPVal synthetic report review</h1><p>CLI-generated reports, not live target assessments. <a href="browser/receipt.json">Observed browser and print checks</a></p>
<table><caption>Generated review artifacts</caption><tr><th>Fixture / detail</th><th>HTML</th><th>Opening</th><th>A4 PDF</th><th>Letter PDF</th></tr>
${receipt.reports.map(r => { const f = `${r.name}-${r.detail}`; return `<tr><th>${r.name} / ${r.detail}</th><td><a href="${f}.html">Report</a></td><td><a href="browser/${f}-opening.png">Screen</a></td>${r.pdfs.map(p => `<td><a href="browser/${p.file}">${p.pages} pages</a></td>`).join('')}</tr>`; }).join('\n')}</table></html>`);
} finally {
    await browser.close();
}
