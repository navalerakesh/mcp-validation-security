#!/usr/bin/env bash

set -euo pipefail

usage() {
  echo "Usage: $0 --version VERSION [--standalone PATH] [--nuget PACKAGE] [--container IMAGE]" >&2
  exit 64
}

version=""
standalone=""
nuget_package=""
container_image=""

while [[ $# -gt 0 ]]; do
  case "$1" in
    --version) version="${2:-}"; shift 2 ;;
    --standalone) standalone="${2:-}"; shift 2 ;;
    --nuget) nuget_package="${2:-}"; shift 2 ;;
    --container) container_image="${2:-}"; shift 2 ;;
    *) usage ;;
  esac
done

[[ -n "$version" ]] || usage
[[ -n "$standalone" || -n "$nuget_package" || -n "$container_image" ]] || usage

assert_version_output() {
  local output="$1"
  [[ "$output" == "mcpval $version" ]] || {
    echo "Version mismatch: expected 'mcpval $version', got '$output'." >&2
    exit 1
  }
}

assert_version() {
  local executable="$1"
  local output
  output=$(DOTNET_ROLL_FORWARD=Major "$executable" --version)
  assert_version_output "$output"
}

cleanup_container() {
  local status="$1" container_id="$2" smoke_root="$3"
  if [[ -n "$container_id" ]] && ! docker rm --force "$container_id" >/dev/null; then
    echo "Container smoke cleanup failed: $container_id" >&2
    status=1
  fi
  if ! rm -f "$smoke_root/report.html" || ! rmdir "$smoke_root"; then
    echo "Container smoke artifact cleanup failed: $smoke_root" >&2
    status=1
  fi
  exit "$status"
}

smoke_container() (
  local image="$1"
  local repo_root smoke_root container_id="" output container_exit
  repo_root="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
  smoke_root=$(mktemp -d "${TMPDIR:-/tmp}/mcpval-container-smoke.XXXXXX")
  trap 'cleanup_container "$?" "$container_id" "$smoke_root"' EXIT
  output=$(docker run --rm --network none --cap-drop ALL --security-opt no-new-privileges "$image" --version)
  assert_version_output "$output"
  container_id=$(docker create --network none --cap-drop ALL --security-opt no-new-privileges \
    --mount "type=bind,source=$repo_root/Mcp.Benchmark.Tests/Fixtures/Reports/controlled-identity-seed.json,target=/input/result.json,readonly" \
    "$image" report --input /input/result.json --format html --report-detail full --output /tmp/report.html)
  docker start --attach "$container_id"
  container_exit=$(docker inspect --format '{{.State.ExitCode}}' "$container_id")
  [[ "$container_exit" == 0 ]] || {
    echo "Container report generation failed with exit $container_exit." >&2
    exit "$container_exit"
  }
  docker cp "$container_id:/tmp/report.html" "$smoke_root/report.html"
  if [[ ! -s "$smoke_root/report.html" ]] ||
    ! grep -Fq '<!DOCTYPE html>' "$smoke_root/report.html" ||
    ! grep -Fq 'id="recorded-claims"' "$smoke_root/report.html" ||
    ! grep -Fq 'data-claim-id="' "$smoke_root/report.html"; then
    echo "Container did not produce a complete HTML report with recorded claims." >&2
    exit 1
  fi
)

if [[ -n "$container_image" ]]; then
  smoke_container "$container_image"
  echo "Container version and offline report smoke passed."
fi

if [[ -n "$standalone" ]]; then
  [[ -f "$standalone" ]] || { echo "Standalone artifact not found: $standalone" >&2; exit 1; }
  chmod +x "$standalone"
  assert_version "$standalone"
fi

if [[ -n "$nuget_package" ]]; then
  [[ -f "$nuget_package" ]] || { echo "NuGet package not found: $nuget_package" >&2; exit 1; }
  smoke_root=$(mktemp -d "${TMPDIR:-/tmp}/mcpval-nuget-smoke.XXXXXX")
  trap 'rm -rf "$smoke_root"' EXIT
  package_dir=$(cd "$(dirname "$nuget_package")" && pwd)
  if command -v cygpath >/dev/null 2>&1; then
    package_dir=$(cygpath -aw "$package_dir")
  fi
  cat > "$smoke_root/NuGet.Config" <<EOF
<?xml version="1.0" encoding="utf-8"?>
<configuration>
  <packageSources>
    <clear />
    <add key="local-smoke" value="$package_dir" />
  </packageSources>
</configuration>
EOF
  dotnet tool install McpVal \
    --tool-path "$smoke_root/tool" \
    --version "$version" \
    --configfile "$smoke_root/NuGet.Config"
  tool_executable="$smoke_root/tool/mcpval"
  if [[ -f "${tool_executable}.exe" ]]; then
    tool_executable="${tool_executable}.exe"
  fi
  assert_version "$tool_executable"
fi
