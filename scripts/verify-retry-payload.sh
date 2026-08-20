#!/usr/bin/env bash

set -euo pipefail

mode="${1:-}"
expected="${2:-}"
actual="${3:-}"
label="${4:-release payload}"

case "$mode" in
  digest)
    [[ -n "$expected" && "$expected" == "$actual" ]] || {
      echo "$label digest mismatch: expected '$expected', got '$actual'." >&2
      exit 1
    }
    ;;
  file)
    [[ -f "$expected" && -f "$actual" ]] || {
      echo "$label comparison requires two files." >&2
      exit 1
    }
    cmp --silent "$expected" "$actual" || {
      echo "$label has different bytes." >&2
      exit 1
    }
    ;;
  tree)
    [[ -d "$expected" && -d "$actual" ]] || {
      echo "$label comparison requires two directories." >&2
      exit 1
    }
    diff -qr "$expected" "$actual" >/dev/null || {
      echo "$label has different package payloads." >&2
      exit 1
    }
    ;;
  *)
    echo "Usage: $0 digest EXPECTED ACTUAL [LABEL] | file EXPECTED ACTUAL [LABEL] | tree EXPECTED ACTUAL [LABEL]" >&2
    exit 64
    ;;
esac
