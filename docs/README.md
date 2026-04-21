# MCP Validator Documentation

This directory contains the durable product, architecture, and contributor documentation for MCP Validator. Start with the user guides, then move into the design references when you need implementation detail.

## Start Here

| Document | Purpose |
| --- | --- |
| [../README.md](../README.md) | Product overview, installation options, command summary, and package links |
| [../QUICKSTART.md](../QUICKSTART.md) | Fast path from installation to the first validation run |
| [Troubleshooting.md](Troubleshooting.md) | Common operational failures and the fastest remediation path |
| [FeatureMatrix.md](FeatureMatrix.md) | Current command surface, transport support, artifact set, and known limitations |
| [../CHANGELOG.md](../CHANGELOG.md) | Release-facing history for behavior, packaging, and reporting changes |

## Product And Operations

| Document | Purpose |
| --- | --- |
| [Resources/GitHub-MCP-Remote-Run.md](Resources/GitHub-MCP-Remote-Run.md) | Representative remote validation run and artifact walkthrough |
| [../SECURITY.md](../SECURITY.md) | Vulnerability reporting process and safe usage guidance |
| [../mcpval-mcp/README.md](../mcpval-mcp/README.md) | Local STDIO wrapper package for MCP-compatible desktop tooling |

## Architecture And Design

| Document | Purpose |
| --- | --- |
| [Design/Architecture.md](Design/Architecture.md) | High-level system boundaries, runtime flow, and artifact model |
| [Design/ComponentDesign.md](Design/ComponentDesign.md) | Component ownership and interaction model across projects |
| [Design/TechnicalArchitecture.md](Design/TechnicalArchitecture.md) | Stable code map, execution lifecycle, and extension points |
| [Design/ForwardArchitecturePlan.md](Design/ForwardArchitecturePlan.md) | Target-state boundary plan and longer-term architecture direction |
| [Design/Schemas.md](Design/Schemas.md) | Schema registry design, supported versions, and version-management rules |
| [../Mcp.Compliance.Spec/schema/README.md](../Mcp.Compliance.Spec/schema/README.md) | Embedded schema folder layout used by the spec project |

## Contribution And Development

| Document | Purpose |
| --- | --- |
| [ContributorEnvironment.md](ContributorEnvironment.md) | Local setup expectations for contributors |
| [../CONTRIBUTING.md](../CONTRIBUTING.md) | Branching, testing, review, and pull request workflow |
| [../Mcp.Benchmark.CLI/README.md](../Mcp.Benchmark.CLI/README.md) | Responsibilities of the CLI host project |
| [../Mcp.Benchmark.Tests/README.md](../Mcp.Benchmark.Tests/README.md) | Test suite layout, fixtures, and execution guidance |

## Documentation Rules

- Keep docs aligned with current CLI behavior, supported transports, and released artifact formats.
- Prefer durable references over time-bound task tracking or historical delivery notes.
- Keep generated example artifacts under `PublicReports/`; explain them from docs instead of duplicating their content in multiple places.
- Do not keep roadmap or task-tracking markdown files in the shipping documentation set.

## When Opening An Issue

Include the following so a report can be reproduced quickly:

- The exact command and arguments used
- The MCP endpoint or STDIO command that was tested
- The `mcpval` version or release source
- A sanitized Markdown report, JSON result, or relevant session-log excerpt
