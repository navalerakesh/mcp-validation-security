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
grep -Fq '<clear />' action.yml || {
  echo "Composite Action local package smoke must use an isolated NuGet source configuration." >&2
  exit 1
}
# Match the literal shell assignment in the smoke script.
# shellcheck disable=SC2016
grep -Fq 'package_dir=$(cygpath -aw "$package_dir")' scripts/smoke-distribution.sh || {
  echo "NuGet package smoke must convert embedded Git Bash feed paths for Windows." >&2
  exit 1
}
grep -Fq 'process.platform === "win32" ? "npm.cmd" : "npm"' scripts/smoke-npm-package.mjs || {
  echo "npm package smoke must invoke the Windows npm command explicitly." >&2
  exit 1
}
grep -Fq 'const child = spawn(process.execPath, [executable]' scripts/smoke-npm-package.mjs || {
  echo "npm package smoke must launch the installed server without a platform shell wrapper." >&2
  exit 1
}
grep -Fq 'await exited.catch(() => undefined)' scripts/smoke-npm-package.mjs || {
  echo "npm package smoke must await child termination before deleting its temporary install." >&2
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
package_job=$(sed -n '/^  package:/,/^  standalone-artifact-smoke:/p' .github/workflows/ci.yml)
grep -Fq 'needs: [build-and-test]' <<< "$package_job" || {
  echo "Standalone packaging must run during pull-request validation without release reservation." >&2
  exit 1
}
if grep -Fq 'release-reservation' <<< "$package_job"; then
  echo "Standalone packaging must not depend on the main-only release reservation job." >&2
  exit 1
fi
install_smoke_job=$(sed -n '/^  package-install-smoke:/,/^  package-attest:/p' .github/workflows/ci.yml)
if grep -Fq "github.event_name == 'push'" <<< "$install_smoke_job"; then
  echo "Cross-platform package install smoke must run on pull requests." >&2
  exit 1
fi
main_push_guard="if: github.ref == 'refs/heads/main' && github.event_name == 'push'"
assert_main_push_guard() {
  local job_name="$1"
  local job_block="$2"
  grep -Fq "$main_push_guard" <<< "$job_block" || {
    echo "$job_name must be restricted to push events on refs/heads/main." >&2
    exit 1
  }
}

release_reservation_job=$(sed -n '/^  release-reservation:/,/^  package:/p' .github/workflows/ci.yml)
publish_job=$(sed -n '/^  publish:/,/^  nuget-package:/p' .github/workflows/ci.yml)
package_attest_job=$(sed -n '/^  package-attest:/,/^  nuget-publish:/p' .github/workflows/ci.yml)
nuget_publish_job=$(sed -n '/^  nuget-publish:/,/^  npm-publish:/p' .github/workflows/ci.yml)
npm_publish_job=$(sed -n '/^  npm-publish:/,/^  docker-publish:/p' .github/workflows/ci.yml)
docker_publish_job=$(sed -n '/^  docker-publish:/,$p' .github/workflows/ci.yml)

assert_main_push_guard "Release reservation" "$release_reservation_job"
assert_main_push_guard "GitHub Release publication" "$publish_job"
assert_main_push_guard "Package attestation" "$package_attest_job"
assert_main_push_guard "NuGet publication" "$nuget_publish_job"
assert_main_push_guard "npm publication" "$npm_publish_job"
assert_main_push_guard "GHCR publication" "$docker_publish_job"

for prerequisite in package standalone-artifact-smoke docker-package package-install-smoke nuget-package npm-package; do
  grep -Fq "$prerequisite" <<< "$release_reservation_job" || {
    echo "Release reservation must wait for $prerequisite before creating an immutable tag." >&2
    exit 1
  }
done

environment_count=$(grep -Ec '^    environment: (NuGet|Npm|Ghcr)' .github/workflows/ci.yml || true)
[[ "$environment_count" -eq 3 ]] || {
  echo "Exactly NuGet, Npm, and Ghcr may be deployment environments in the release workflow." >&2
  exit 1
}
grep -Fq 'environment: NuGet' <<< "$nuget_publish_job" || { echo "NuGet publication must own the NuGet environment." >&2; exit 1; }
grep -Fq 'environment: Npm' <<< "$npm_publish_job" || { echo "npm publication must own the Npm environment." >&2; exit 1; }
grep -Fq 'environment: Ghcr' <<< "$docker_publish_job" || { echo "GHCR publication must own the Ghcr environment." >&2; exit 1; }

nuget_package_job=$(sed -n '/^  nuget-package:/,/^  npm-package:/p' .github/workflows/ci.yml)
npm_package_job=$(sed -n '/^  npm-package:/,/^  docker-package:/p' .github/workflows/ci.yml)
for package_block in "$nuget_package_job" "$npm_package_job"; do
  if grep -Eq 'environment:|attestations: write|id-token: write' <<< "$package_block"; then
    echo "Pull-request package preparation must not receive deployment environments or OIDC/attestation write permissions." >&2
    exit 1
  fi
done
grep -Fq 'package-attest' <<< "$nuget_publish_job" || {
  echo "NuGet publication must wait for the main-only package attestation job." >&2
  exit 1
}
grep -Fq 'for attempt in {1..60}' .github/workflows/ci.yml || {
  echo "Registry publication must allow bounded propagation before verification." >&2
  exit 1
}
# Match the literal workflow variable in the signature-audit command.
# shellcheck disable=SC2016
grep -Fq 'npm install --ignore-scripts "mcpval-localmcp@$version"' .github/workflows/ci.yml || {
  echo "npm signature audit must install the published registry package." >&2
  exit 1
}
grep -Fq 'startswith("candidate-")' .github/workflows/ci.yml || {
  echo "npm publication must remove stale candidate tags after promotion." >&2
  exit 1
}
# Match the literal workflow digest comparison.
# shellcheck disable=SC2016
grep -Fq 'immutable_digest" == "$current_channel_digest' .github/workflows/ci.yml || {
  echo "Legacy container aliases must resolve only by exact immutable-tag digest match." >&2
  exit 1
}
# Match the literal GitHub expression in the workflow.
# shellcheck disable=SC2016
grep -Fq 'python -m zipfile -e "${{ matrix.archive }}" extracted' .github/workflows/ci.yml || {
  echo "Windows standalone smoke must extract ZIP artifacts with a ZIP-aware tool." >&2
  exit 1
}
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
