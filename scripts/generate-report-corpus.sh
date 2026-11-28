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
test -s "$output/inputs/corpus.txt"
while IFS= read -r name; do
  for detail in minimal full; do
    dotnet Mcp.Benchmark.CLI/bin/Debug/net8.0/mcpval.dll report \
      --input "$output/inputs/$name.json" --format html --report-detail "$detail" \
      --output "$output/$name-$detail.html" < /dev/null > "$output/$name-$detail.cli.log" 2>&1
    test -s "$output/$name-$detail.html"
  done
done < "$output/inputs/corpus.txt"
printf 'CLI report corpus: %s\n' "$output"
