# Changelog

All notable changes to this project will be documented in this file.

The format is based on Keep a Changelog, and this project follows semantic versioning for published artifacts.

## Unreleased

### Added
- First-class MCP guideline rule pack metadata for tool, prompt, resource, and protocol guidance findings.
- Structured optional capability findings for `roots/list`, `logging/setLevel`, `sampling/createMessage`, and `completion/complete`, including declaration mismatches.
- JUnit report output for CI systems that ingest test-style artifacts.
- Snapshot approval tests for Markdown, HTML, XML, SARIF, JUnit, and JSON report outputs.
- Reusable compliant, partially compliant, and unsafe MCP fixture profiles for regression testing.
- Runnable compliant, partially compliant, and unsafe STDIO MCP fixture servers for process-backed regression and demo coverage.
- Hardened Docker distribution path with GHCR publishing, OCI SBOMs, and provenance metadata.
- Additional AI-readiness schema findings for required arrays, fixed-choice string parameters, and missing structured format hints.
- Destructive-tool confirmation guidance heuristics for tools that declare `destructiveHint` without describing confirmation or warning expectations.
- Prompt metadata guidance findings for missing prompt descriptions, missing argument descriptions, multi-input complexity guidance, and safety-sensitive prompts without warning language.
- Tool pagination findings for unstable cursors and large unpaginated `tools/list` catalogs.
- Resource guidance findings for missing MIME metadata, unclear URI schemes, and parameterized resource templates without ergonomic metadata.

### Changed
- Capability negotiation now records optional capability support discovered during protocol validation.
- Contributor workflow now includes tracked repository validation scripts and fixture-profile guidance.
- Release distribution guidance now documents the GHCR image alongside NuGet, npm, and standalone binaries.

### Security
- Release outputs now include stronger supply-chain trust signals across standalone binaries, packages, and container images.