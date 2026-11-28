export function extractPageText(items) {
    // PDF.js items can split a word at a font/ligature boundary; only recorded spaces and EOLs separate text.
    return items.map(item => (item.str ?? '') + (item.hasEOL ? '\n' : '')).join('').replace(/\s+/g, ' ');
}
