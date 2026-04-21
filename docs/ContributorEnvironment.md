# Contributor Environment

This repository has two active toolchains:

- `.NET 8` for the validator, report generation, and tests
- `Node.js 20` for the local MCP wrapper package in `mcpval-mcp`

## Prerequisites

- [.NET 8 SDK](https://dotnet.microsoft.com/download/dotnet/8.0)
- [Node.js 20](https://nodejs.org/)
- npm (bundled with Node.js)
- Git
- PowerShell 7 or Bash if you want to use the repo validation scripts directly

`global.json` pins the expected .NET SDK family for this repository. If you have a newer SDK installed as well, `dotnet` should still resolve correctly through the pinned version policy.

## First-Time Setup

```bash
git clone https://github.com/navalerakesh/mcp-validation-security.git
cd mcp-validation-security

dotnet restore ./mcp-benchmark-validation.sln
cd mcpval-mcp && npm ci && cd ..
```

## Local Validation

Use the tracked repository validation scripts when you want the closest local equivalent of CI.

macOS or Linux:

```bash
./scripts/validate-repo.sh
```

Windows PowerShell:

```powershell
./scripts/validate-repo.ps1
```

Both scripts run:

- `.NET` restore, build, and solution tests
- `npm ci` and `npm run build` in `mcpval-mcp`
- high-severity `dotnet` and `npm` audits

Useful flags:

- `--skip-audits` when you only need fast build plus test confirmation
- `--skip-node` when you are working only in the .NET solution
- `--skip-dotnet` when you are iterating only on the npm wrapper

## Daily Commands

Run the CLI locally:

```bash
dotnet run --project Mcp.Benchmark.CLI -- validate --help
```

Build the wrapper package:

```bash
cd mcpval-mcp
npm run build
```

Run the full .NET suite directly:

```bash
dotnet test ./mcp-benchmark-validation.sln
```

## Working Agreement

- Keep docs in sync with behavior changes.
- Prefer root-cause fixes over one-off exceptions.
- If you change CI or release behavior, run the repository validation script before opening a PR.
