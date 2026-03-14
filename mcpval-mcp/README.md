# mcpval-mcp

MCP server that wraps the [mcpval CLI](https://github.com/navalerakesh/mcp-benchmark-validation) — validate MCP servers from any AI agent.

## Install

```bash
npm install -g mcpval-mcp
```

**Prerequisite:** The `mcpval` CLI must be installed:
```bash
dotnet tool install --global Mcp.Benchmark.CLI
```

## Usage

### VS Code / GitHub Copilot

Add to your `mcp.json`:

```jsonc
{
  "servers": {
    "mcpval": {
      "command": "npx",
      "args": ["-y", "mcpval-mcp"],
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
      "args": ["-y", "mcpval-mcp"]
    }
  }
}
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

- [mcpval CLI](https://github.com/navalerakesh/mcp-benchmark-validation) — the core validation tool
- [MCP Specification](https://modelcontextprotocol.io/specification)
- [Report Issues](https://github.com/navalerakesh/mcp-benchmark-validation/issues)
