#!/usr/bin/env bash

set -euo pipefail

repo_root="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
verifier="$repo_root/scripts/verify-retry-payload.sh"
root=$(mktemp -d "${TMPDIR:-/tmp}/mcpval-retry-test.XXXXXX")
trap 'rm -rf "$root"' EXIT

mkdir -p "$root/expected" "$root/actual"
printf 'same\n' > "$root/expected/package"
printf 'same\n' > "$root/actual/package"

"$verifier" digest sha256:abc sha256:abc container
"$verifier" file "$root/expected/package" "$root/actual/package" release
"$verifier" tree "$root/expected" "$root/actual" nuget

docker_push_line="example: digest: sha256:0123456789abcdef size: 1234"
parsed_digest=$(awk '/digest: sha256:/{print $3}' <<< "$docker_push_line")
"$verifier" digest sha256:0123456789abcdef "$parsed_digest" "docker push parser"

printf 'changed\n' > "$root/actual/package"
if "$verifier" digest sha256:abc sha256:def container 2>/dev/null; then
  echo "Digest mismatch simulation unexpectedly passed." >&2
  exit 1
fi
if "$verifier" file "$root/expected/package" "$root/actual/package" release 2>/dev/null; then
  echo "File mismatch simulation unexpectedly passed." >&2
  exit 1
fi
if "$verifier" tree "$root/expected" "$root/actual" nuget 2>/dev/null; then
  echo "Tree mismatch simulation unexpectedly passed." >&2
  exit 1
fi

echo "Release retry acceptance and rejection simulations passed."
