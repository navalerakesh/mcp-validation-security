#!/usr/bin/env bash

set -euo pipefail

repo_root="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
output_dir="${MUTATION_OUTPUT_DIR:-$repo_root/Mcp.Benchmark.Tests/TestResults/stryker}"

cd "$repo_root"
dotnet tool restore
rm -rf "$output_dir"
mkdir -p "$output_dir"
dotnet stryker \
  --project Mcp.Benchmark.Core.csproj \
  --mutate '**/AssuranceAssessmentEngine.cs' \
  --mutate '**/ProviderHarnessEvaluator.cs' \
  --mutate '**/RuleCalibrationEvaluator.cs' \
  --mutate '**/ControlledStateEvaluator.cs' \
  --mutate '**/ControlledIdentityEvaluator.cs' \
  --mutate '**/ControlledEvidenceAdmission.cs' \
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
  --skip-version-check 2>&1 | tee "$output_dir/stryker.log"

inspection_exit=0
grep -Fq 'Safe Mode!' "$output_dir/stryker.log" || inspection_exit=$?
case "$inspection_exit" in
  0)
    printf '%s\n' 'Mutation gate failed: Stryker excluded a method after an instrumentation error.' >&2
    exit 1
    ;;
  1) ;;
  *)
    printf '%s\n' 'Mutation gate failed: instrumentation log could not be inspected.' >&2
    exit 1
    ;;
esac
