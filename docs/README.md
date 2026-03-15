# MCP Validator — Documentation

Index for project documentation. Each document is scoped and self-contained.

## Blog / Technical Analysis

| Topic | Document | Description |
| --- | --- | --- |
| MCP compliance costs | [The-Hidden-Cost-of-Non-Compliant-MCP-Servers.md](The-Hidden-Cost-of-Non-Compliant-MCP-Servers.md) | Token economics, hallucination cascades, security attack vectors, and cost modeling for non-compliant MCP servers |

## Architecture & Design

| Topic | Document | Description |
| --- | --- | --- |
| System design | [Design/Architecture.md](Design/Architecture.md) | Clean Architecture layers, validator pipeline, transport strategy |
| Component details | [Design/ComponentDesign.md](Design/ComponentDesign.md) | Service boundaries, DI registration, validator lifecycle |
| Technical reference | [Design/TechnicalArchitecture.md](Design/TechnicalArchitecture.md) | Code map, execution pipeline, key services |
| Schema registry | [Design/Schemas.md](Design/Schemas.md) | Versioned schema layout, registry APIs |

## Resources

| Item | Location |
| --- | --- |
| Sample HTML reports | [Resources/](Resources/) |
| GitHub MCP remote run | [Resources/GitHub-MCP-Remote-Run.md](Resources/GitHub-MCP-Remote-Run.md) |

## Contributing

1. Clone and create a feature branch
2. Build: `dotnet build` | Test: `dotnet test`
3. Keep docs in sync with code changes
4. Open a PR against `master`

## Issues

Open issues at [GitHub Issues](https://github.com/navalerakesh/mcp-validation-security/issues) with:
- Server endpoint and CLI version
- Command and arguments used
- Logs or report snippet
