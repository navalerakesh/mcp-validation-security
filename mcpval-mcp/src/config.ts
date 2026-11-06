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

const localExecutionEnvironmentVariable = "MCPVAL_ENABLE_LOCAL_EXECUTION";
const localExecutionEnabled = process.env[localExecutionEnvironmentVariable]?.trim().toLowerCase() === "true";
const providerHarnessPath = process.env.MCPVAL_PROVIDER_HARNESS?.trim();
const providerAuthorizationKeyPath = process.env.MCPVAL_PROVIDER_AUTHORIZATION_KEY?.trim();
const providerConformanceResultsPath = process.env.MCPVAL_OFFICIAL_CONFORMANCE_RESULTS?.trim();

export const config = {
  /** CLI command name to invoke */
  cliCommand: "mcpval",

  /** Environment variable to override CLI path */
  cliPathEnvVar: "MCPVAL_CLI_PATH",

  /** Default timeout for CLI execution in milliseconds */
  defaultTimeoutMs: 120_000,

  /** Explicit authority for the wrapper to launch user-supplied local MCP commands. */
  localExecution: {
    environmentVariable: localExecutionEnvironmentVariable,
    enabled: localExecutionEnabled,
    disabledMessage:
      `Local MCP command execution is disabled. Validate a remote HTTPS endpoint, or set ${localExecutionEnvironmentVariable}=true when starting this wrapper in an isolated environment after reviewing the exact command.`,
  },

  /** Operator-owned provider assurance inputs. Paths are never accepted from agent tool arguments. */
  providerAssurance: {
    harnessPath: providerHarnessPath,
    authorizationKeyPath: providerAuthorizationKeyPath,
    conformanceResultsPath: providerConformanceResultsPath,
    executionMode: "standard" as const,
    enabled: Boolean(
      providerHarnessPath &&
      providerAuthorizationKeyPath &&
      providerConformanceResultsPath &&
      process.env.MCPVAL_ATTESTATION_PRIVATE_KEY_PEM),
  },

  /** MCP server metadata */
  server: {
    name: "mcpval",
    title: "MCP Validator Local MCP",
    version: packageVersion,
    description: localExecutionEnabled
      ? "Local MCP server that wraps the mcpval CLI to validate remote endpoints and explicitly authorized local commands."
      : "Local MCP server that wraps the mcpval CLI to validate remote MCP endpoints.",
    websiteUrl: "https://github.com/navalerakesh/mcp-validation-security/tree/main/mcpval-mcp",
    instructions:
      localExecutionEnabled
        ? `Use health_check first for a quick bootstrap and protocol check. Use discover for remote HTTP MCP endpoints when you need a capability catalog. Use validate for evidence-first assurance assessment; its default inventory profile uses safe mode and reports unperformed active security, execution, and load coverage as incomplete. Local commands execute with this process's privileges because ${localExecutionEnvironmentVariable}=true; review each command and run this wrapper in an isolated environment.`
        : `Use health_check first for a quick bootstrap and protocol check. Use discover for remote HTTP MCP endpoints when you need a capability catalog. Use validate for evidence-first assurance assessment; its default inventory profile uses safe mode and reports unperformed active security, execution, and load coverage as incomplete. Provide an absolute remote MCP endpoint URL. Local command execution is disabled unless the operator restarts this wrapper with ${localExecutionEnvironmentVariable}=true in an isolated environment.`,
  },
} as const;
