#!/usr/bin/env bash

set -euo pipefail

repo_root="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
cd "$repo_root"

dotnet build Mcp.Benchmark.Benchmarks/Mcp.Benchmark.Benchmarks.csproj \
  --no-restore \
  --configuration Release

benchmarks=$(dotnet run \
  --project Mcp.Benchmark.Benchmarks/Mcp.Benchmark.Benchmarks.csproj \
  --configuration Release \
  --no-build \
  -- \
  --list flat)

required_benchmarks=(
  ConfigurationCapture
  HostStartup
  Planning
  AssuranceReduction
  Scoring
  MarkdownRendering
)

for benchmark in "${required_benchmarks[@]}"; do
  if ! grep -Fq "$benchmark" <<< "$benchmarks"; then
    echo "Required benchmark '$benchmark' is missing from the Release benchmark catalog." >&2
    exit 1
  fi
done

echo "Benchmark contracts verified (${#required_benchmarks[@]} workloads)."
