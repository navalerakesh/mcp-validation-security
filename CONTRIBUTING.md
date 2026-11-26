# Contributing to MCP Validator

Thank you for contributing. This repository validates MCP servers for protocol compliance, security posture, AI safety, and trust-level assessment.

## Before You Start

- Read the contributor environment guide in [docs/ContributorEnvironment.md](docs/ContributorEnvironment.md).
- Keep changes aligned with the architecture notes in `docs/Design/`.
- Prefer root-cause fixes over compatibility shims or duplicated logic.

## Local Setup

Prerequisites:

- `.NET 8 SDK`
- `Node.js 24.19.0` (reviewed baseline; Node 24 required)
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

When using GitHub Copilot agents, run the workspace `repo-precommit-review` skill before committing, followed by `repo-build-release-gate`. The first performs repository-contextual semantic review; the second runs mandatory and change-specific executable gates. See [.github/copilot-instructions.md](.github/copilot-instructions.md) for the governing agent workflow. These local skills supplement rather than replace required GitHub checks.

## Report changes

Read the [report quality and layout contract](docs/Design/ReportQualityStandard.md). From the repository root:

```bash
dotnet test Mcp.Benchmark.Tests/Mcp.Benchmark.Tests.csproj --filter 'FullyQualifiedName~Unit.Reporting|FullyQualifiedName~ReportCommandTests|FullyQualifiedName~PublicArtifactSchemaTests'
bash scripts/generate-report-corpus.sh ./artifacts/report-quality
npm ci --prefix scripts/report-quality --ignore-scripts
cd scripts/report-quality
PLAYWRIGHT_SKIP_BROWSER_GC=1 npx playwright install chromium
cd ../..
node scripts/report-quality/check.mjs ./artifacts/report-quality
```

On Linux, use `playwright install --with-deps chromium` to supply browser OS dependencies. The browser tooling is development-only; it is not shipped in the CLI or MCP wrapper.

The corpus uses nine saved/synthetic cases: inventory, mixed/provider, interrupted, dense, sparse, CI and comprehensive satisfied/failed/missing-prerequisite controls. Both HTML detail levels use the actual CLI. A generated manifest owns the case list for shell and browser checks. The public-only signed identity seed was generated with an ephemeral key that was discarded; it supports recorded-time replay, not live-target certification. Do not relax signature, run/target binding or redaction checks to make a rendering fixture pass.

The browser check produces screenshots, A4/Letter PDFs, extracted page text and a machine-readable receipt under the ignored output directory. It checks exact claim parity, local anchors, light/dark reflow and accessibility, in-place evidence without navigation or duplicate IDs, keyboard operation, offline/no-JavaScript reading, print scope and every numbered footer.

Before modifying the renderer, retain the generated baseline. Render the same input bytes with the new build and compare them. Review HTML snapshots only after inspecting generated output; use `UPDATE_SNAPSHOTS=1` with the single HTML snapshot selector, not the entire snapshot suite.

The path-triggered Report Quality workflow runs these commands independently of Copilot. Attach the receipt and representative screens/PDFs to the review, not to tracked source. Human review remains required for reading order, hierarchy and pagination. Native screen-reader and second-engine print acceptance must be reported separately.

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
