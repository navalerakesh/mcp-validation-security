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

Host construction uses the same dependency graph builder as the production CLI. Planning includes schema/applicability and execution-governance construction and is intentionally measured separately from target I/O. Release-mode CI regression tests cover all M7-required surfaces with per-operation ceilings set to ten times these reviewed means.

Reproduce with:

```bash
dotnet run --project Mcp.Benchmark.Benchmarks -c Release -- --job short --filter '*'
```
