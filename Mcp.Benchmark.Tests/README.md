# Mcp.Benchmark.Tests

This project contains the automated test suite for the MCP Benchmark. It follows industry best practices for structure, naming, and coverage.

## Project Structure

The test project mirrors the structure of the source code to ensure discoverability and separation of concerns.

```text
Mcp.Benchmark.Tests/
├── Architecture/               # Tests that enforce architectural rules (e.g., Dependency direction)
├── Unit/                       # Fast, isolated tests that do not require external resources
│   ├── Services/               # Tests for Core and Infrastructure services
│   ├── Validators/             # Tests for specific validation logic
│   └── Scoring/                # Tests for scoring algorithms
├── Integration/                # Tests that verify interactions between components (uses WireMock)
└── Fixtures/                   # Shared test setup and data (e.g., Mock Servers)
```

## Running Tests

You can run the tests using the .NET CLI or Visual Studio.

### Run All Tests
```bash
dotnet test
```

### Run Specific Categories
Tests are categorized using Traits. You can filter by category:

```bash
# Run only Unit tests
dotnet test --filter Category=Unit

# Run only Integration tests
dotnet test --filter Category=Integration
```

## Key Technologies

*   **xUnit**: The testing framework.
*   **Moq**: For mocking dependencies in Unit tests.
*   **FluentAssertions**: For readable and expressive assertions.
*   **WireMock.Net**: For mocking the MCP server in Integration tests.
*   **NetArchTest.Rules**: For enforcing architectural constraints.

## Adding New Tests

1.  **Unit Tests**: Place in `Unit/{Folder}` matching the source file location. Mock all dependencies.
2.  **Integration Tests**: Place in `Integration/`. Use `McpServerTestFixture` to spin up a mock MCP server.
3.  **Architecture Tests**: Add to `Architecture/` if you need to enforce new design rules.

## Best Practices

*   **Naming**: Use `MethodName_StateUnderTesting_ExpectedBehavior` (e.g., `CalculateScore_WithNoResults_ReturnsZero`).
*   **Arrange-Act-Assert**: Structure all tests clearly with these three sections.
*   **Mocking**: Only mock immediate dependencies. Do not mock data objects (DTOs).
