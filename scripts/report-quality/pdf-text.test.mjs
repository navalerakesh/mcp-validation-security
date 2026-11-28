import assert from 'node:assert/strict';
import { test } from 'node:test';
import { extractPageText } from './pdf-text.mjs';

const heading = 'Appendix: findings and recorded claims';

test('retains the heading split at the fi ligature in the PR104 Linux PDF', () => {
    const items = ['Appendix:', ' ', 'fi', 'ndings and recorded claims'].map(str => ({ str, hasEOL: false }));
    assert.equal(extractPageText(items), heading);
});

test('preserves explicit spaces and line breaks, including empty EOL items', () => {
    assert.equal(extractPageText([
        { str: 'Appendix:', hasEOL: true },
        { str: 'findings' },
        { str: ' ' },
        { str: 'and recorded' },
        { str: '', hasEOL: true },
        { str: 'claims' }
    ]), heading);
});

test('ignores marked-content items without separating adjacent glyph runs', () => {
    assert.equal(extractPageText([
        { type: 'beginMarkedContent', tag: 'Span' },
        { str: 'Run pro' },
        { type: 'endMarkedContent' },
        { str: 'venance' }
    ]), 'Run provenance');
});

test('does not accept missing, truncated, reordered or incorrectly spaced headings', () => {
    for (const text of [
        '',
        'Appendix: findings and recorded',
        'Appendix: and recorded findings claims',
        'Appendix: fi ndings and recorded claims',
        'Appendix: findingsand recorded claims'
    ]) {
        assert.equal(extractPageText([{ str: text }]).includes(heading), false, text);
    }
    assert.equal(extractPageText([]), '');
});
