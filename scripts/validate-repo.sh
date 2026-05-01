#!/usr/bin/env bash

set -euo pipefail

repo_root="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
configuration="${CONFIGURATION:-Debug}"
run_dotnet=true
run_node=true
run_audits=true

usage() {
  cat <<'EOF'
Usage: scripts/validate-repo.sh [options]

Validates the repository locally by running the main build, test, and audit checks.

Options:
  --skip-dotnet   Skip .NET restore, build, test, and NuGet audit steps.
  --skip-node     Skip npm install, build, and npm audit steps.
  --skip-audits   Skip NuGet and npm audit steps.
  --configuration Build configuration for dotnet build/test. Defaults to Debug.
  --help          Show this help text.
EOF
}

while [[ $# -gt 0 ]]; do
  case "$1" in
    --skip-dotnet)
      run_dotnet=false
      shift
      ;;
    --skip-node)
      run_node=false
      shift
      ;;
    --skip-audits)
      run_audits=false
      shift
      ;;
    --configuration)
      configuration="$2"
      shift 2
      ;;
    --help|-h)
      usage
      exit 0
      ;;
    *)
      echo "Unknown option: $1" >&2
      usage
      exit 1
      ;;
  esac
done

require_command() {
  local command_name="$1"
  if ! command -v "$command_name" >/dev/null 2>&1; then
    echo "Required command not found: $command_name" >&2
    exit 1
  fi
}

run_nuget_audit() {
  local audit_file
  audit_file="$(mktemp)"
  trap 'rm -f "$audit_file"' RETURN

  dotnet list ./mcp-benchmark-validation.sln package --vulnerable --include-transitive --format json > "$audit_file"

  python3 - "$audit_file" <<'PY'
import json
import sys

severities = {"low": 1, "moderate": 2, "high": 3, "critical": 4}
threshold = severities["high"]
findings = []

def scan(node, project=None, framework=None):
    if isinstance(node, dict):
        project = node.get("path", project)
        framework = node.get("framework", framework)
        package_name = node.get("name")
        resolved_version = node.get("resolvedVersion") or node.get("requestedVersion")
        vulnerabilities = node.get("vulnerabilities")

        if package_name and isinstance(vulnerabilities, list):
            for vulnerability in vulnerabilities:
                severity = str(vulnerability.get("severity", "")).lower()
                if severities.get(severity, 0) >= threshold:
                    findings.append({
                        "severity": severity,
                        "package": package_name,
                        "version": resolved_version,
                        "project": project,
                        "framework": framework,
                    })

        for value in node.values():
            scan(value, project, framework)
    elif isinstance(node, list):
        for item in node:
            scan(item, project, framework)

with open(sys.argv[1], "r", encoding="utf-8") as handle:
    payload = json.load(handle)

scan(payload)

if findings:
    print("High or critical NuGet vulnerabilities detected:")
    for finding in findings:
        print(f"- {finding['severity']}: {finding['package']} {finding['version']} ({finding['project']} / {finding['framework']})")
    raise SystemExit(1)

print("No high or critical NuGet vulnerabilities detected.")
PY
}

echo "Repository root: $repo_root"
cd "$repo_root"

if $run_dotnet; then
  require_command dotnet
fi

if $run_node; then
  require_command npm
  require_command node
fi

if $run_audits && $run_dotnet; then
  require_command python3
fi

if $run_dotnet; then
  echo "==> Restoring .NET dependencies"
  dotnet restore ./mcp-benchmark-validation.sln --locked-mode

  echo "==> Building solution ($configuration)"
  dotnet build ./mcp-benchmark-validation.sln --configuration "$configuration" --no-restore

  echo "==> Running .NET tests ($configuration)"
  dotnet test ./mcp-benchmark-validation.sln --configuration "$configuration" --no-build

  if $run_audits; then
    echo "==> Running NuGet audit"
    run_nuget_audit
  fi
fi

if $run_node; then
  echo "==> Installing npm dependencies"
  pushd ./mcpval-mcp >/dev/null
  npm ci

  echo "==> Building npm package"
  npm run build

  if $run_audits; then
    echo "==> Running npm audit"
    npm audit --audit-level=high
  fi
  popd >/dev/null
fi

echo "Repository validation passed."