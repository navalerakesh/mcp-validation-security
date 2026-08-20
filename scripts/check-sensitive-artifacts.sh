#!/usr/bin/env bash

set -euo pipefail

repo_root="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
cd "$repo_root"

failure=0

while IFS= read -r path; do
  [[ -f "$path" ]] || continue

  case "$path" in
    .env|.env.*|*_headers.txt|*_cookies.txt|live-report-*|PublicReports/*|*.pem|*.p12|*.pfx|*.key)
      echo "Forbidden sensitive artifact path: $path" >&2
      failure=1
      ;;
  esac

done < <(git ls-files --cached --others --exclude-standard)

patterns=(
  'ghp_[A-Za-z0-9]{20,}'
  'github_pat_[A-Za-z0-9_]{20,}'
  'Authorization:[[:space:]]*Bearer[[:space:]]+[A-Za-z0-9._~+/-]{12,}'
  '(Set-Cookie|Cookie|Mcp-Session-Id):[[:space:]]*[^[:space:]]{8,}'
  '"(access_token|refresh_token|id_token|client_secret)"[[:space:]]*:[[:space:]]*"[^"]+"'
  '-----BEGIN ([A-Z0-9 ]+ )?PRIVATE KEY-----'
  '(^|[^A-Za-z0-9_])/Users/[A-Za-z0-9._-]+/'
  '(^|[^A-Za-z0-9_])/home/[A-Za-z0-9._-]+/'
)

for pattern in "${patterns[@]}"; do
  matches=$(git grep --untracked --exclude-standard -n -I -E -e "$pattern" -- \
    ':!scripts/check-sensitive-artifacts.sh' \
    ':!Mcp.Benchmark.Tests/Unit/Utilities/SessionLogRedactorTests.cs' || true)
  if [[ -n "$matches" ]]; then
    echo "Sensitive content pattern detected: $pattern" >&2
    printf '%s\n' "$matches" >&2
    failure=1
  fi
done

for pattern in "${patterns[@]}"; do
  matches=$(git grep --cached -n -I -E -e "$pattern" -- \
    ':!scripts/check-sensitive-artifacts.sh' \
    ':!Mcp.Benchmark.Tests/Unit/Utilities/SessionLogRedactorTests.cs' || true)
  if [[ -n "$matches" ]]; then
    echo "Sensitive staged-content pattern detected: $pattern" >&2
    printf '%s\n' "$matches" >&2
    failure=1
  fi
done

history_paths=$(git rev-list --objects --all \
  | cut -d' ' -f2- \
  | grep -E '(^|/)(PublicReports/|github_headers\.txt$|learn_headers\.txt$|learn_cookies\.txt$|live-report-)' \
  | sort -u || true)
if [[ -n "$history_paths" ]]; then
  history_count=$(printf '%s\n' "$history_paths" | wc -l | tr -d ' ')
  echo "Forbidden sensitive artifact paths remain reachable in Git history ($history_count paths)." >&2
  printf '%s\n' "$history_paths" | head -20 >&2
  if (( history_count > 20 )); then
    echo "... $((history_count - 20)) additional path(s) omitted." >&2
  fi
  echo "Rewrite public history or publish from a clean repository before release." >&2
  failure=1
fi

if (( failure != 0 )); then
  exit 1
fi

echo "Sensitive artifact check passed."
