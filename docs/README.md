# MCP Validator Documentation

This directory contains the durable product, architecture, and contributor documentation for MCP Validator. Start with the user guides, then move into the design references when you need implementation detail.

## Start Here

| Document | Purpose |
| --- | --- |
| [../README.md](../README.md) | Product overview, installation options, command summary, and package links |
| [../QUICKSTART.md](../QUICKSTART.md) | Fast path from installation to the first validation run |
| [Troubleshooting.md](Troubleshooting.md) | Common operational failures and the fastest remediation path |
| [FeatureMatrix.md](FeatureMatrix.md) | Current command surface, transport support, artifact set, and known limitations |
| [ProtocolCoverageMatrix.md](ProtocolCoverageMatrix.md) | Exact revision/transport/feature behavioral coverage and known limits |
| [AuthenticationCoverage.md](AuthenticationCoverage.md) | Credential acquisition, registration, conformance evidence, and explicit authentication limits |
| [CalibrationMethodology.md](CalibrationMethodology.md) | Versioned scoring policy, calibration corpus, mutation threshold, and false-positive/negative limits |
| [../CHANGELOG.md](../CHANGELOG.md) | Release-facing history for behavior, packaging, and reporting changes |

## Product And Operations

| Document | Purpose |
| --- | --- |
| [Resources/GitHub-MCP-Remote-Run.md](Resources/GitHub-MCP-Remote-Run.md) | Representative remote validation run and artifact walkthrough |
| [Resources/LiveCertificate-1.1.25-MicrosoftLearn.md](Resources/LiveCertificate-1.1.25-MicrosoftLearn.md) | Fresh public Microsoft Learn MCP health, discovery, safe-validation, redaction, and artifact-integrity certificate |
| [../SECURITY.md](../SECURITY.md) | Vulnerability reporting process and safe usage guidance |
| [../mcpval-mcp/README.md](../mcpval-mcp/README.md) | Local STDIO wrapper package for MCP-compatible desktop tooling |

## Architecture And Design

| Document | Purpose |
| --- | --- |
| [Design/Architecture.md](Design/Architecture.md) | High-level system boundaries, runtime flow, run-state diagram, and artifact model |
| [Design/ComponentDesign.md](Design/ComponentDesign.md) | Component ownership and interaction model across projects |
| [Design/TechnicalArchitecture.md](Design/TechnicalArchitecture.md) | Stable code map, execution lifecycle, run-state model, and extension points |
| [Design/ForwardArchitecturePlan.md](Design/ForwardArchitecturePlan.md) | Target-state boundary plan and longer-term architecture direction |
| [Design/Schemas.md](Design/Schemas.md) | Schema registry design, supported versions, and version-management rules |
| [Schemas/](Schemas/) | Versioned JSON Schema contracts for canonical machine artifacts |
| [Design/EnterpriseRenewalExecutionPlan.md](Design/EnterpriseRenewalExecutionPlan.md) | Active renewal milestones, ownership, scale invariants, and evidence gates |
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
- Generate live validation artifacts under the operating system temporary directory and delete them after review. Commit only synthetic, reviewed snapshots under test fixtures; permanent docs contain secret-free summaries rather than live captures.
- Keep only governing architecture execution plans with measurable evidence gates; remove disposable task notes and stale delivery trackers.

## When Opening An Issue

Include the following so a report can be reproduced quickly:

- The exact command and arguments used
- The MCP endpoint or STDIO command that was tested
- The `mcpval` version or release source
- A sanitized Markdown report, JSON result, or relevant session-log excerpt
