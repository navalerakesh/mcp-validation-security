# Contributing to MCP Validator

Thank you for contributing. This repository validates MCP servers for protocol compliance, security posture, AI safety, and trust-level assessment.

## Before You Start

- Read the contributor environment guide in [docs/ContributorEnvironment.md](docs/ContributorEnvironment.md).
- Keep changes aligned with the architecture notes in `docs/Design/`.
- Prefer root-cause fixes over compatibility shims or duplicated logic.

## Local Setup

Prerequisites:

- `.NET 8 SDK`
- `Node.js 20`
- `Git`
- `PowerShell 7` or `Bash` if you want to use the tracked validation scripts

Quick start:

```bash
git clone https://github.com/YOUR-USERNAME/mcp-validation-security.git
cd mcp-validation-security

dotnet restore ./mcp-benchmark-validation.sln
cd mcpval-mcp && npm ci && cd ..
```

Validate the repository before opening a PR:

```bash
./scripts/validate-repo.sh
```

On Windows PowerShell:

```powershell
./scripts/validate-repo.ps1
```

## Pull Requests

1. Create a branch from `main`.
2. Keep the change set focused.
3. Add or update tests when behavior changes.
4. Update docs when CLI, workflows, reporting, policy, or fixture behavior changes.
5. Record user-visible or adopter-relevant changes in [CHANGELOG.md](CHANGELOG.md) under `Unreleased`.
6. Open the PR against `main` and fill out the template.

## Release Notes Discipline

- Add changelog entries for new rules, scoring changes, policy behavior changes, new output formats, and packaging or distribution changes.
- Call out breaking changes explicitly.
- Avoid silent rule or policy shifts that would surprise downstream CI adopters.

## Standards

- Core models and contracts belong in `Mcp.Benchmark.Core`.
- Infrastructure integrations belong in `Mcp.Benchmark.Infrastructure`.
- CLI host behavior belongs in `Mcp.Benchmark.CLI`.
- Do not blur spec failures, guideline findings, and heuristic signals.
- Keep host policy behavior separate from raw validation evidence.

## Security Reporting

If you discover a vulnerability in this repository itself:

1. Do not open a public issue.
2. Follow the process in [SECURITY.md](SECURITY.md).
3. Include reproduction details and impact.

## Useful References

- [Project docs index](docs/README.md)
- [Changelog](CHANGELOG.md)
- [Quick start](QUICKSTART.md)
- [Security policy](SECURITY.md)
- [Open issues](https://github.com/navalerakesh/mcp-validation-security/issues)

By contributing, you agree that your contributions are licensed under the MIT License.

