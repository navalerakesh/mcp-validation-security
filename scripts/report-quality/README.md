# Report quality checks

Run the [contributor report checks](../../CONTRIBUTING.md#report-changes) for the
CLI-generated corpus, browser checks and A4/Letter export.

The PDF text extractor preserves PDF.js text-item whitespace and line endings.
Item boundaries are not word boundaries: a font or ligature can split `findings`
into `fi` and `ndings`. Inserting a space there falsely fails the complete-heading
check on Linux. Do not remove real spaces or relax the required heading text.

Run the focused extraction regressions without a browser:

```bash
node --test scripts/report-quality/pdf-text.test.mjs
```

The Report Quality workflow runs this command in its **PDF text extraction
regressions** step before the renderer and browser checks.
