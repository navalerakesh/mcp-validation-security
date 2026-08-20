# CI Policy Examples

Pin the Action to a released major/minor or immutable commit according to your governance policy.

## Advisory

Collect evidence without blocking on validation findings:

```yaml
- uses: navalerakesh/mcp-validation-security@v1
  with:
    server: https://example.com/mcp
    policy: advisory
```

## Regression gate

Store an approved canonical result as a protected CI artifact, then block only regressions:

```yaml
- uses: navalerakesh/mcp-validation-security@v1
  with:
    server: https://example.com/mcp
    policy: balanced
    baseline: ./approved-result.json
    regression-only: 'true'
    output-format: json
```

Waivers belong in the checked-in validation configuration and require owner, reason, selector scope, and expiry. Raw findings remain unchanged.

## Strict admission

Require strict deterministic policy and preserve JSON, SARIF, JUnit, audit, Markdown, and HTML artifacts:

```yaml
- uses: navalerakesh/mcp-validation-security@v1
  with:
    server: https://example.com/mcp
    access: authenticated
    token: ${{ secrets.MCP_TOKEN }}
    policy: strict
    upload-artifacts: 'true'
```

Use environment protections for authenticated targets. Never place tokens in endpoint query strings, workflow summaries, or artifact names. The Action exposes typed inputs rather than free-form command arguments.
