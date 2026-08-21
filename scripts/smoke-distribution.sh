#!/usr/bin/env bash

set -euo pipefail

usage() {
  echo "Usage: $0 --version VERSION [--standalone PATH] [--nuget PACKAGE]" >&2
  exit 64
}

version=""
standalone=""
nuget_package=""

while [[ $# -gt 0 ]]; do
  case "$1" in
    --version) version="${2:-}"; shift 2 ;;
    --standalone) standalone="${2:-}"; shift 2 ;;
    --nuget) nuget_package="${2:-}"; shift 2 ;;
    *) usage ;;
  esac
done

[[ -n "$version" ]] || usage
[[ -n "$standalone" || -n "$nuget_package" ]] || usage

assert_version() {
  local executable="$1"
  local output
  output=$(DOTNET_ROLL_FORWARD=Major "$executable" --version)
  [[ "$output" == "mcpval $version" ]] || {
    echo "Version mismatch: expected 'mcpval $version', got '$output'." >&2
    exit 1
  }
}

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
