<p align="center">
  <img src="https://raw.githubusercontent.com/navalerakesh/mcp-validation-security/main/assets/mcpval-icon.png" width="180" alt="MCP Validator Logo" />
</p>

# mcpval-localmcp

Local MCP server that wraps the [mcpval CLI](https://github.com/navalerakesh/mcp-validation-security) — validate MCP servers from any AI agent.

## Quick Start (npx — no install needed)

```bash
npx mcpval-localmcp
```

This starts the MCP server via stdio. Any MCP-compatible AI agent can connect to it.

## Install

```bash
# Global install (optional — npx works without this)
npm install -g mcpval-localmcp
```

**Prerequisite:** The `mcpval` CLI must be installed:
```bash
# Option 1: NuGet global tool (cross-platform, requires .NET 8)
dotnet tool install --global McpVal

# Option 2: Download standalone binary (no .NET needed)
# → https://github.com/navalerakesh/mcp-validation-security/releases
```

## Configure Your MCP Client

### VS Code / GitHub Copilot

Add to your `.vscode/mcp.json`:

```jsonc
{
  "servers": {
    "mcpval": {
      "command": "npx",
      "args": ["-y", "mcpval-localmcp"],
      "type": "stdio"
    }
  }
}
```

### Claude Desktop

Add to `claude_desktop_config.json`:

```json
{
  "mcpServers": {
    "mcpval": {
      "command": "npx",
      "args": ["-y", "mcpval-localmcp"]
    }
  }
}
```

### Cursor

Add to Cursor Settings → MCP Servers:

```json
{
  "mcpServers": {
    "mcpval": {
      "command": "npx",
      "args": ["-y", "mcpval-localmcp"]
    }
  }
}
```

### Windsurf

Add to `~/.windsurf/mcp.json`:

```json
{
  "mcpServers": {
    "mcpval": {
      "command": "npx",
      "args": ["-y", "mcpval-localmcp"]
    }
  }
}
```

### Any MCP Client

The server communicates via JSON-RPC over stdin/stdout. Point any MCP-compatible client at:

```bash
npx -y mcpval-localmcp
```

## Tools

| Tool | Description |
|:-----|:------------|
| `validate` | Validate an MCP server for compliance, security, AI safety, and trust level (L1-L5) |
| `health_check` | Quick connectivity check — verifies initialize handshake |
| `discover` | List tools, resources, and prompts exposed by a server |

## Examples

Once configured, ask your AI agent:

- "Validate the MCP server at https://my-server.com/mcp"
- "Check if https://api.example.com/mcp is healthy"
- "What tools does this MCP server expose?"

## Links

- [mcpval CLI](https://github.com/navalerakesh/mcp-validation-security) — the core validation tool
- [NuGet Package](https://www.nuget.org/packages/McpVal) — install via dotnet
- [MCP Specification](https://modelcontextprotocol.io/specification)
- [Report Issues](https://github.com/navalerakesh/mcp-validation-security/issues)
