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
    version: packageVersion,
  },
} as const;
