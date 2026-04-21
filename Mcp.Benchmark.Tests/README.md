# Mcp.Benchmark.Tests

This project contains the automated test suite for MCP Validator. It verifies architectural boundaries, isolated business logic, integration flows, report rendering, and fixture-backed STDIO behavior.

## Project Layout

```text
Mcp.Benchmark.Tests/
├── Architecture/   # Dependency and layering rules
├── Unit/           # Fast isolated tests for services, validators, scoring, and commands
├── Integration/    # Cross-component and transport-aware tests
└── Fixtures/       # Reusable MCP responses, sample servers, and snapshots
```

Key fixture assets:

- `Fixtures/McpFixtureProfiles.cs` provides reusable compliant, partially compliant, and unsafe MCP response sets.
- `Fixtures/Servers/` contains runnable STDIO sample servers for process-backed validation scenarios.
- `Fixtures/ReportSnapshots/` protects executive report output from unintended regressions.

## Running Tests

Run the full suite:

```bash
dotnet test
```

Run a filtered slice:

```bash
dotnet test --filter Category=Unit
dotnet test --filter Category=Integration
```

## Adding Coverage

- Add unit tests under `Unit/` when the behavior is deterministic and dependency boundaries can be isolated.
- Add integration tests under `Integration/` when transport behavior, serialization, or orchestration matters.
- Add architecture tests under `Architecture/` when a dependency rule or boundary must stay enforced.
- Update snapshots when a report change is intentional and reviewed, not as a shortcut around a regression.

## Fixture Servers

You can run the local STDIO fixture servers directly for manual validation and debugging:

```bash
node Mcp.Benchmark.Tests/Fixtures/Servers/mcp-fixture-compliant.cjs
node Mcp.Benchmark.Tests/Fixtures/Servers/mcp-fixture-partial.cjs
node Mcp.Benchmark.Tests/Fixtures/Servers/mcp-fixture-unsafe.cjs
```

## Tooling

- `xUnit` for test execution
- `FluentAssertions` for readable assertions
- `Moq` for mocking direct dependencies in unit tests
- `WireMock.Net` for HTTP-backed integration scenarios
- `NetArchTest.Rules` for architectural guardrails
