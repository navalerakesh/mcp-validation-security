/**
 * Configuration for the mcpval MCP server.
 * All settings are centralized here — no hardcoded values in tool handlers.
 */
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
    version: "2.0.0",
  },
} as const;
