# Validator Overhead Benchmark Baseline

This baseline is diagnostic, not a universal performance claim. Measurements vary by runtime, CPU, OS, power state, and repository revision.

## 2026-08-19 local short run

Environment: .NET 8.0.25, macOS 26.5.2, Apple M3 Ultra ARM64, BenchmarkDotNet 0.15.8, ShortRun (3 warmups, 3 iterations).

| Surface | Mean | Allocated |
| --- | ---: | ---: |
| Production CLI host construction | 66.488 ms | 151.52 KB |
| Configuration capture | 0.770 us | 3.48 KB |
| Planning | 1.198 ms | 765.72 KB |
| Verdict reduction | 137.047 us | 196.88 KB |
| Scoring | 1.248 us | 4.68 KB |
| Markdown rendering | 4.412 us | 24.59 KB |

Host construction uses the same dependency graph builder as the production CLI. Planning includes schema/applicability and execution-governance construction and is intentionally measured separately from target I/O. These elapsed-time means are historical diagnostics, not CI latency ceilings.

Release-mode CI enforces allocation budgets in [ValidatorOverheadBudgetTests](../Mcp.Benchmark.Tests/Unit/Performance/ValidatorOverheadBudgetTests.cs): host construction below 512 KiB, planning below 2 MiB, reduction and scoring each below 16 KiB, and Markdown rendering below 64 KiB per measured operation. The shared 1,000-item / 10,000-claim HTML workload is guarded below 256 MiB per generation. These are validator-overhead guards, not target-performance requirements; they do not certify elapsed time or browser performance.

The scale harness uses the same canonical fixtures at 10/100/1,000 catalog items and at least 100/1,000/10,000 claims. `ReportRendering` measures Markdown; `HtmlReportRendering` measures production HTML generation separately. Neither measures browser layout, interaction or PDF export. Compare identical harness/input bytes, build configuration, runtime and host conditions; retain raw samples, allocation results and uncertainty. A single mean or overlapping confidence intervals are not evidence of a statistically established improvement.

The HTML allocation guard uses the benchmark's fixture source directly, not a second approximation. A local .NET 8/macOS ARM64 comparison measured approximately 253.7 MiB before report integration, 301.9 MiB after integration, and 238.0 MiB after preserving identical output while sharing static disclosure markup and appending the claim ledger into the report buffer. The 256 MiB guard detects the observed regression without deleting evidence. These measurements are workload-specific managed allocations, not peak process memory or a universal report-size limit.

### Timing stability follow-up

Two sequential local repetitions with 12 warmups and 12 measured iterations stabilized the planning workloads (raw-sample coefficient of variation below 3.5%). The schema-catalog workload remained variable (approximately 34-50% across the observed cases). These observations do not establish a universal latency baseline or justify silently discarding slow samples.

```bash
dotnet run --project Mcp.Benchmark.Benchmarks -c Release -- --job short --filter '*ConfigPlanning*' '*SchemaCatalog*' '*.Planning' --warmupCount 12 --iterationCount 12 --exporters JSON
```

Inspect every raw `Workload/Actual` sample, not only the outlier-filtered summary. Planning and schema validation are different workloads; stable planning does not qualify schema timing. The shared-host schema timing limitation remains open rather than being hidden by a cache, relaxed threshold or unsupported speed claim.

Reproduce with:

```bash
dotnet run --project Mcp.Benchmark.Benchmarks -c Release -- --job short --filter '*'
```
