#!/usr/bin/env bash

set -euo pipefail

repo_root="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
cd "$repo_root"

version=$(sed -n 's:.*<VersionPrefix>\([^<]*\)</VersionPrefix>.*:\1:p' Directory.Build.props)
[[ "$version" =~ ^[0-9]+\.[0-9]+\.[0-9]+(-[0-9A-Za-z.-]+)?$ ]] || {
  echo "Directory.Build.props must declare one exact SemVer VersionPrefix." >&2
  exit 1
}

# JavaScript reads VERSION from process.env.
# shellcheck disable=SC2016
VERSION="$version" node -e '
  const fs = require("fs");
  const globalJson = JSON.parse(fs.readFileSync("global.json", "utf8"));
  if (globalJson.sdk?.version !== "8.0.419") throw new Error("global.json must pin the reviewed .NET 8 SDK");
  if (globalJson.sdk?.rollForward !== "disable") throw new Error("SDK roll-forward must be disabled for exact reproducibility");
  const changelog = fs.readFileSync("CHANGELOG.md", "utf8");
  const escaped = process.env.VERSION.replace(/[.*+?^${}()|[\]\\]/g, "\\$&");
  if (!new RegExp(`^## ${escaped} - \\d{4}-\\d{2}-\\d{2}$`, "m").test(changelog)) {
    throw new Error(`CHANGELOG.md must contain a dated heading for ${process.env.VERSION}`);
  }
'

# JavaScript reads RELEASE_VERSION from process.env.
# shellcheck disable=SC2016
RELEASE_VERSION="$version" node -e '
  const fs = require("fs");
  const pkg = JSON.parse(fs.readFileSync("mcpval-mcp/package.json", "utf8"));
  if (!/^\d+\.\d+\.\d+(?:-[0-9A-Za-z.-]+)?$/.test(pkg.version)) throw new Error("npm package version is not exact SemVer");
  if (pkg.version !== process.env.RELEASE_VERSION) throw new Error(`npm version ${pkg.version} does not match release ${process.env.RELEASE_VERSION}`);
  const lock = JSON.parse(fs.readFileSync("mcpval-mcp/package-lock.json", "utf8"));
  if (lock.version !== pkg.version || lock.packages?.[""]?.version !== pkg.version) throw new Error("npm lock metadata version drift");
  if (pkg.dependencies?.["@modelcontextprotocol/sdk"] !== "1.30.0") throw new Error("MCP SDK must remain exactly pinned");
'

grep -Fq 'VersionPrefix' action.yml || {
  echo "Composite Action no longer resolves its default from VersionPrefix." >&2
  exit 1
}
grep -Fq "always() && inputs.upload-artifacts == 'true'" action.yml || {
  echo "Composite Action must upload validation evidence even when policy blocks the validation step." >&2
  exit 1
}
build_job=$(sed -n '/^  build-and-test:/,/^  security-analysis:/p' .github/workflows/ci.yml)
grep -Fq 'fetch-depth: 0' <<< "$build_job" || {
  echo "Build & Test must fetch complete history before the sensitive-artifact gate." >&2
  exit 1
}
grep -Fq -- "--filter 'FullyQualifiedName!~ValidatorOverheadBudgetTests'" <<< "$build_job" || {
  echo "The parallel Debug suite must exclude timing-sensitive performance budgets." >&2
  exit 1
}
grep -Fq -- "--filter 'FullyQualifiedName~ValidatorOverheadBudgetTests'" <<< "$build_job" || {
  echo "Build & Test must run performance budgets in the dedicated Release gate." >&2
  exit 1
}
if [[ $(grep -Fc -- '- rid: linux-arm64' .github/workflows/ci.yml) -lt 2 ]]; then
  echo "Linux ARM64 must be present in standalone packaging and uploaded-artifact smoke matrices." >&2
  exit 1
fi
grep -Fq -- "-printf '%f\\0' | sort -z)" .github/workflows/ci.yml || {
  echo "Release checksums must use portable bare filenames accepted by signature backfill." >&2
  exit 1
}
# Match the literal Docker build argument reference.
# shellcheck disable=SC2016
if ! grep -Fq 'ARG VERSION=' Dockerfile || ! grep -Fq -- '-p:Version="$VERSION"' Dockerfile; then
  echo "Dockerfile does not inject the exact release version." >&2
  exit 1
fi
grep -Fq 'mcr.microsoft.com/dotnet/sdk:8.0.419@sha256:' Dockerfile || {
  echo "Dockerfile build image must pin the exact reviewed SDK from global.json." >&2
  exit 1
}
grep -Fq 'mcr.microsoft.com/dotnet/runtime-deps:8.0.25-jammy-chiseled@sha256:' Dockerfile || {
  echo "Dockerfile runtime image must pin the SDK-compatible runtime patch." >&2
  exit 1
}

echo "Distribution contracts verified for $version."
