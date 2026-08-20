#!/usr/bin/env bash

set -euo pipefail

repo_root="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
output_dir="${MUTATION_OUTPUT_DIR:-$repo_root/Mcp.Benchmark.Tests/TestResults/stryker}"

cd "$repo_root"
dotnet tool restore
rm -rf "$output_dir"
dotnet stryker \
  --project Mcp.Benchmark.Core.csproj \
  --mutate '**/VerdictReducer.cs' \
  --test-project Mcp.Benchmark.Tests/Mcp.Benchmark.Tests.csproj \
  --test-runner vstest \
  --configuration Debug \
  --concurrency "${MUTATION_CONCURRENCY:-2}" \
  --reporter ClearText \
  --reporter Json \
  --output "$output_dir" \
  --break-at 90 \
  --threshold-low 90 \
  --threshold-high 95 \
  --skip-version-check
