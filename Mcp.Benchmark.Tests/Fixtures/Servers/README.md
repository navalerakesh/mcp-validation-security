# Fixture Servers

This folder contains runnable STDIO MCP fixture servers used by integration tests and manual demos.

Profiles:

- `mcp-fixture-compliant.cjs` — exposes a healthy MCP surface with compliant tools, prompts, and resources.
- `mcp-fixture-partial.cjs` — exposes intentionally incomplete or malformed prompt and resource metadata.
- `mcp-fixture-unsafe.cjs` — exposes destructive and high-risk surfaces that should trigger safety findings.

Run them directly with Node.js:

```bash
node Mcp.Benchmark.Tests/Fixtures/Servers/mcp-fixture-compliant.cjs
node Mcp.Benchmark.Tests/Fixtures/Servers/mcp-fixture-partial.cjs
node Mcp.Benchmark.Tests/Fixtures/Servers/mcp-fixture-unsafe.cjs
```

These servers speak newline-delimited JSON-RPC over STDIO so they can be used with the repository's `StdioMcpClientAdapter` and any MCP-compatible process client.