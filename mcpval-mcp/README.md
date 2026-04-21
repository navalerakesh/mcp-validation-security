<p align="center">
  <img src="https://raw.githubusercontent.com/navalerakesh/mcp-validation-security/main/assets/mcpval-icon.png" width="180" alt="MCP Validator Logo" />
</p>

# mcpval-localmcp

Local MCP server that wraps the [mcpval CLI](https://github.com/navalerakesh/mcp-validation-security) — validate MCP servers from any AI agent.

## Quick Start (npx — no install needed)

```bash
npx -y mcpval-localmcp
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

If the CLI is installed outside your default `PATH`, set `MCPVAL_CLI_PATH` to the full executable path in your MCP client configuration.

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

## Troubleshooting

**"mcpval CLI is not installed or not on PATH"**

The MCP server needs the `mcpval` CLI accessible on PATH. If you installed via `dotnet tool install --global McpVal` but the MCP server can't find it, add `env` to your MCP config:

```jsonc
{
  "servers": {
    "mcpval": {
      "command": "npx",
      "args": ["-y", "mcpval-localmcp"],
      "type": "stdio",
      "env": {
        "PATH": "${env:HOME}/.dotnet/tools:${env:PATH}"
      }
    }
  }
}
```

Alternatively, download a standalone binary from [Releases](https://github.com/navalerakesh/mcp-validation-security/releases) and place it on your PATH.

If you prefer not to modify `PATH`, point directly at the CLI executable:

```jsonc
{
  "servers": {
    "mcpval": {
      "command": "npx",
      "args": ["-y", "mcpval-localmcp"],
      "type": "stdio",
      "env": {
        "MCPVAL_CLI_PATH": "/absolute/path/to/mcpval"
      }
    }
  }
}
```

On Windows, use the full path to `mcpval.exe`.

## Tools

| Tool | Description |
|:-----|:------------|
| `validate` | Validate an MCP server for compliance, security, AI safety, and trust level (L1-L5) |
| `health_check` | Quick connectivity check — verifies initialize handshake |
| `discover` | List tools, resources, and prompts exposed by a remote MCP server |

`discover` follows the upstream CLI behavior and is currently intended for remote HTTP MCP endpoints. Use `validate` when you need richer evidence for a local STDIO target.

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
