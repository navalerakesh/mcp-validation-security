# MCP Benchmark CLI

[![.NET](https://img.shields.io/badge/.NET-8.0-blue.svg)](https://dotnet.microsoft.com/)
[![License](https://img.shields.io/badge/License-MIT-green.svg)](LICENSE)

Benchmark and compliance CLI for Model Context Protocol (MCP) servers that helps developers ensure their implementations align with MCP specifications, security baselines, and performance expectations.

## About This Project

The **MCP Benchmark** CLI is an open-source tool created to support the growing MCP ecosystem. As the Model Context Protocol continues to evolve, this benchmark and validator aims to help developers build reliable and specification-compliant MCP servers.

Our goal is simple: provide practical benchmark and validation tools that make it easier for the community to adopt and implement MCP correctly. This project is not an official certification authority but rather a helpful resource for developers working with MCP implementations.

## What It Does

This benchmark/validator helps you check your MCP server against:

- **MCP Specification Compliance** - Validates against the official Model Context Protocol specification
- **JSON-RPC 2.0 Standards** - Ensures proper JSON-RPC message formatting and handling
- **Security Best Practices** - Tests for common security considerations in API implementations
- **Performance Characteristics** - Basic performance and reliability testing

## Quick Start

### Prerequisites

- [.NET 8.0 SDK](https://dotnet.microsoft.com/download/dotnet/8.0) or later
- Your MCP server running and accessible

### Installation

1. **Clone the repository:**
   ```bash
   git clone https://github.com/navalerakesh/mcp-validation-security.git
   cd mcp-validation-security
   ```

2. **Build the project:**
   ```bash
   dotnet build
   ```

3. **Run the benchmark/validator:**
   ```bash
   dotnet run --project Mcp.Benchmark.CLI -- validate -s http://localhost:3000
   ```

### Basic Usage

**Validate/benchmark your MCP server:**
```bash
# Basic validation
dotnet run --project Mcp.Benchmark.CLI -- validate -s http://localhost:3000

# With detailed output
dotnet run --project Mcp.Benchmark.CLI -- validate -s http://localhost:3000 --verbose

# With Bearer token for secured servers
dotnet run --project Mcp.Benchmark.CLI -- validate -s https://my-secure-mcp.azurecontainerapps.io/mcp --access authenticated -t "eyJhbGci..."

# With interactive authentication (device login / browser)
dotnet run --project Mcp.Benchmark.CLI -- validate -s https://my-secure-mcp.azurecontainerapps.io/mcp --access authenticated -i

# Generate an offline report from a saved result
dotnet run --project Mcp.Benchmark.CLI -- report --input publish/win-x64/mcp-validation-20251227-result.json --format html --output publish/win-x64/mcp-validation-report.html
```

**Check validator health:**
```bash
dotnet run --project Mcp.Benchmark.CLI -- health-check -s https://my-secure-mcp.azurecontainerapps.io/mcp --access authenticated -i
```

### Command Line Options

| Option | Alias | Description |
|--------|-------|-------------|
| `--server` | `-s` | MCP server endpoint URL (required) |
| `--output` | `-o` | Directory to write validation artifacts (Markdown + JSON) |
| `--mcpspec` | | Target MCP spec profile (e.g., `latest`, `2025-11-25`) |
| `--access` | | Declared server access intent (`public`, `authenticated`, `enterprise`, `unspecified`) |
| `--token` | `-t` | Bearer token for secured servers |
| `--interactive` | `-i` | Allow interactive/device authentication |
| `--timeout` | `-T` | Override request timeout in milliseconds (health-check only) |
| `--verbose` | `-v` | Enable verbose console output |
| `--config` | `-c` | Path to a JSON configuration file |
| `--list-spec-profiles` | | List supported MCP spec profiles and exit |

> Authenticated or enterprise servers must supply either `-t/--token` or `-i/--interactive`.

> The `health-check` and `discover` commands now understand the same `--access`, `-t/--token`, and `-i/--interactive` switches as `validate`, making it easier to automate secured environments even when you are only doing a quick probe.

## Configuration

### Session Storage and Logs

Every `mcpval` run now creates an isolated session under:

- Windows: `%LOCALAPPDATA%\McpCli\Sessions/<session-id>`
- macOS/Linux: `$HOME/.local/share/mcp-cli/Sessions/<session-id>`

Each session folder contains:

- `State/` for temporary artifacts generated during that command invocation.
- `Logs/` which captures a full text log (`mcpval-<session-id>.log`) from the internal logger.

You can safely delete old session folders to reclaim disk space; they are not reused across commands.

Create a validation configuration file to customize testing:

```json
{
  "serverUrl": "http://localhost:3000",
  "timeoutMs": 30000,
  "enableSecurity": true,
  "enablePerformance": true,
  "reportFormat": "json"
}
```

Use it with:
```bash
dotnet run --project Mcp.Benchmark.CLI -- validate --config validation-config.json
```

## Validation Categories

### Protocol Compliance
- MCP message structure validation
- Capability negotiation testing
- Tool and resource interaction patterns
- Transport protocol requirements

### JSON-RPC 2.0 Compliance
- Request/response format validation
- Error handling and codes
- Batch request processing
- ID correlation

### Security Testing
- Basic authentication validation
- Input sanitization checks
- Common vulnerability patterns
- Rate limiting behavior

### Performance Testing
- Response time measurements
- Concurrent request handling
- Resource usage patterns
- Timeout behavior

## Project Structure

```
├── Mcp.Benchmark.CLI/        # Command-line interface
├── Mcp.Benchmark.Core/       # Core validation logic
├── Mcp.Benchmark.Infrastructure/ # HTTP client and services
├── Mcp.Benchmark.Tests/      # Test suite
├── validation-configs/                   # Sample configurations
└── Documetns/                          # Additional documentation
```

## Contributing

We welcome contributions from the community! Whether you're fixing bugs, adding features, or improving documentation, your help makes this tool better for everyone.

### Ways to Contribute

- **Report Issues** - Found a bug or have a suggestion? Please open an issue
- **Submit Pull Requests** - Fix bugs, add features, or improve documentation
- **Share Feedback** - Let us know how you're using the tool and what could be improved
- **Test with Your Servers** - Help us ensure compatibility with different MCP implementations

### Development Setup

1. Fork the repository
2. Create a feature branch: `git checkout -b feature-name`
3. Make your changes and add tests
4. Run the test suite: `dotnet test`
5. Submit a pull request

## Testing

Run the test suite to ensure everything works correctly:

```bash
# Run all tests
dotnet test

# Run with detailed output
dotnet test --verbosity normal

# Run specific test categories
dotnet test --filter Category=Unit
dotnet test --filter Category=Integration
```

## License

This project is licensed under the MIT License - see the [LICENSE](LICENSE) file for details.

## Acknowledgments

This project builds upon the excellent work of the Model Context Protocol specification team and the broader open-source community. Open for contribution from anyone who wants to help make MCP implementations more reliable and accessible.

## Disclaimer

This validator is a community tool designed to help with MCP implementation testing. It is not an official certification tool and results should be considered as helpful guidance rather than authoritative compliance statements. Always refer to the official MCP specification for definitive requirements.

---

*Built with ❤️ for the MCP community*
