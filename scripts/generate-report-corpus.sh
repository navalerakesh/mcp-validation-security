#!/usr/bin/env bash
set -euo pipefail

if [[ $# != 1 ]]; then
  echo 'Usage: bash scripts/generate-report-corpus.sh <output-directory>' >&2
  exit 64
fi
mkdir -p "$1"
output="$(cd "$1" && pwd)"
repo_root="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
cd "$repo_root"
MCPVAL_REPORT_CORPUS="$output/inputs" dotnet test Mcp.Benchmark.Tests/Mcp.Benchmark.Tests.csproj \
  --filter 'FullyQualifiedName~ReportQualityCorpusTests' --nologo
for name in inventory mixed interrupted dense sparse; do
  for detail in minimal full; do
    dotnet Mcp.Benchmark.CLI/bin/Debug/net8.0/mcpval.dll report \
      --input "$output/inputs/$name.json" --format html --report-detail "$detail" \
      --output "$output/$name-$detail.html" > "$output/$name-$detail.cli.log" 2>&1
  done
done
printf 'CLI report corpus: %s\n' "$output"
