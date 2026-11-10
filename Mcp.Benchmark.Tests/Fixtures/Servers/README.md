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

The compliant fixture optionally accepts `--audit-methods <absolute-output-path>` for controlled qualification. It appends method names and malformed-input markers only, never request arguments, tokens or response bodies. The audit is limited to 256 records per process and 128 characters per method. Use an owned temporary directory and remove it after the test. This fixture-side audit lets the real wrapper/CLI integration test verify Safe-mode non-execution independently of the validator's reported booleans.

`--audit-processes <absolute-output-path>` records only fixture PID and parent PID for termination checks. `--hold-method tools/list` deliberately withholds that response so cancellation/shutdown can be tested after receiver-side admission. These options are for owned local fixtures, not production target instrumentation.
