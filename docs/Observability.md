# Performance And Observability

MCP Validator separates target measurements from validator overhead.

## Canonical metrics

`run.operationalMetrics` records:

- deterministic validator-local stage wall time, total run wall time, and stable stage/rule-projection durations;
- request budget limit and requests started/completed/failed;
- target request latency p50/p95/p99;
- validator queue p95, retry count/delay, throughput, and error rate;
- response bodies rejected by configured size limits.

Target latency measures each HTTP/STDIO request/response-header cycle and excludes validator queueing and retry backoff. Total run duration is wall time; validator overhead sums deterministic local scenario, scoring, trust, and verdict stages, so target waiting and exporter work are not mislabeled as validator processing. Performance-test metrics remain a separate controlled load-probe result.

Operational request failure means a transport exception, missing response, non-success HTTP status, or policy rejection. A received JSON-RPC error envelope is a completed transport exchange and remains protocol evidence; it does not by itself increase the operational error rate.

Latency and queue percentiles retain at most 4,096 samples per run in bounded circular buffers. Request totals remain exact up to the governed maximum request budget of 100,000; percentiles are a bounded representative window for longer runs.

## OpenTelemetry

Instrumentation uses `ActivitySource` and `Meter` name `McpVal.Validation`, version `1.0.0`. Set an absolute HTTP(S) `OTEL_EXPORTER_OTLP_ENDPOINT` to enable the official OTLP trace and metric exporter. No exporter is registered by default.

Stable attributes are limited to run correlation ID, request budget, stage name, and bounded state labels. Endpoints, tool names, prompts, resource URIs, credentials, payloads, and error text are never telemetry dimensions.

The production validator implements `ICorrelatedMcpValidatorService`, so the CLI session ID is assigned before the run Activity starts and matches the canonical artifact. Legacy third-party `IMcpValidatorService` implementations that do not implement the correlated capability can only have their returned artifact ID aligned after execution; their own provider-specific traces cannot be retroactively correlated by mcpval.

Key instruments:

- `mcpval.target.request.duration`
- `mcpval.validator.queue.duration`
- `mcpval.target.retry.delay`
- `mcpval.validator.stage.duration`
- `mcpval.target.requests`
- `mcpval.target.retries`
- `mcpval.target.response.rejections`

## Health and readiness

`health-check` answers whether a target can complete a basic connection/initialize path. It is not a conformance, security, authorization, or readiness certificate. A healthy target may still fail validation; an authentication-protected target may be healthy while authoritative validation remains blocked pending credentials.

Future hosted workers must expose worker liveness/readiness separately from target health and validation verdicts.

## Reproducibility

The BenchmarkDotNet project measures production CLI host construction, configuration capture, planning, verdict reduction, scoring, and Markdown rendering. Run:

```bash
dotnet run --project Mcp.Benchmark.Benchmarks -c Release -- --filter '*'
```

Release-mode CI per-operation budgets are set to ten times the reviewed BenchmarkDotNet means and catch order-of-magnitude regressions rather than noisy microsecond changes. Controlled performance fixtures use deterministic response values; live endpoints are not regression baselines.
