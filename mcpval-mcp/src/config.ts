import { readFileSync } from "fs";

/**
 * Configuration for the mcpval MCP server.
 * All settings are centralized here — no hardcoded values in tool handlers.
 */
const packageVersion = (() => {
  try {
    const packageJson = JSON.parse(readFileSync(new URL("../package.json", import.meta.url), "utf-8")) as {
      version?: string;
    };

    return packageJson.version ?? "0.0.0";
  } catch {
    return "0.0.0";
  }
})();

export const config = {
  /** CLI command name to invoke */
  cliCommand: "mcpval",

  /** Environment variable to override CLI path */
  cliPathEnvVar: "MCPVAL_CLI_PATH",

  /** Default timeout for CLI execution in milliseconds */
  defaultTimeoutMs: 120_000,

  /** MCP server metadata */
  server: {
    name: "mcpval",
    title: "MCP Validator Local MCP",
    version: packageVersion,
    description: "Local MCP server that wraps the mcpval CLI so MCP clients can validate remote and local servers through MCP tools.",
    websiteUrl: "https://github.com/navalerakesh/mcp-validation-security/tree/main/mcpval-mcp",
    instructions:
      "Use health_check first for a quick bootstrap and protocol check. Use discover for remote HTTP MCP endpoints when you need a capability catalog. Use validate for full compliance, security, and AI-safety analysis, especially for local or stdio targets. Provide server as either an absolute MCP endpoint URL or a stdio command.",
  },
} as const;
