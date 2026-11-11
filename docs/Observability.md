# Performance And Observability

MCP Validator separates target measurements from validator overhead.

## Canonical metrics

`run.operationalMetrics` records:

- deterministic validator-local stage wall time, total run wall time, and stable stage/rule-projection durations;
- request budget limit and requests started/completed/failed;
- target request latency p50/p95/p99;
- validator queue p95, retry count/delay, throughput, and error rate;
- response bodies rejected by configured size limits.

Target latency measures each HTTP/STDIO request/response-header cycle and excludes validator queueing and retry backoff. Total run duration is wall time; validator overhead sums deterministic local scenario, claim projection, assessment, scoring, and policy stages, so target waiting and exporter work are not mislabeled as validator processing. Performance-test metrics remain a separate controlled load-probe result.

Operational request failure means a transport exception, missing response, non-success HTTP status, or policy rejection. A received JSON-RPC error envelope is a completed transport exchange and remains protocol evidence; it does not by itself increase the operational error rate.

Latency and queue percentiles retain at most 4,096 valid samples in bounded circular buffers. Each window records observed, retained, discarded and rejected counts and declares `mostRecent` sampling. It is not a random representative sample or a whole-run percentile once older observations are discarded. Negative/non-finite duration samples are rejected without losing request outcome counts. Human reports display this scope explicitly.

No latency/queue samples produce null percentiles, not zero. Error rate is null without completed requests; throughput is null without a positive elapsed duration. A measured zero duration remains zero. Legacy artifacts with zero denominators are normalized to unavailable metric values during loading, while measured zero is preserved and the original contract version is retained in `run.sourceDocumentSchemaVersion`.

`validatorProcessResources` records an actual process CPU-time delta and working-set endpoints around the validation run. Its scope is explicitly `validatorProcess` and includes concurrent process activity; it is not isolated per-request CPU, target-server CPU, a memory peak/average, or proof of a leak. Unavailable/regressing counter observations remain null. Target resource fields remain unmeasured unless real telemetry supplies them; successful RPCs cannot establish resource-limit compliance, and failed RPCs alone cannot establish resource exhaustion.

Resource probes preserve authentication-required, policy-blocked, unavailable, timed-out and local framework-error outcomes separately. Already-dispatched work remains owned after a later dispatch refusal. Request failures can be retained as transport evidence, but do not establish which server resource was exhausted.

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
