#!/usr/bin/env node
/**
 * mcpval-localmcp — MCP server that wraps the mcpval CLI.
 *
 * Exposes validation, health-check, and discovery as MCP tools
 * so AI agents can validate MCP servers through the protocol itself.
 *
 * Usage:
 *   npx mcpval-localmcp           (stdio transport)
 *   node dist/index.js           (after build)
 *
 * VS Code mcp.json:
 *   {
 *     "servers": {
 *       "mcpval": {
 *         "command": "npx",
 *         "args": ["-y", "mcpval-localmcp"],
 *         "type": "stdio"
 *       }
 *     }
 *   }
 */
import { McpServer } from "@modelcontextprotocol/sdk/server/mcp.js";
import { StdioServerTransport } from "@modelcontextprotocol/sdk/server/stdio.js";
import type { ToolAnnotations } from "@modelcontextprotocol/sdk/types.js";
import { config } from "./config.js";
import { ValidateInputSchema, HealthCheckInputSchema, DiscoverInputSchema } from "./tools.js";
import { handleValidate, handleHealthCheck, handleDiscover } from "./handlers.js";
import { getCliVersion } from "./cli-runner.js";

const validateAnnotations: ToolAnnotations = {
  title: "Validate MCP Server",
  readOnlyHint: false,
  destructiveHint: false,
  idempotentHint: false,
  openWorldHint: true,
};

const inspectionAnnotations: ToolAnnotations = {
  readOnlyHint: true,
  destructiveHint: false,
  idempotentHint: true,
  openWorldHint: true,
};

async function main(): Promise<void> {
  const cliVersion = await getCliVersion();

  const server = new McpServer({
    name: config.server.name,
    version: config.server.version,
  });

  // ─── Register Tools ──────────────────────────────────────────

  server.registerTool(
    "validate",
    {
      title: "Validate MCP Server",
      description: "Validate an MCP server for compliance, security, and AI safety. Assigns trust level L1-L5.",
      inputSchema: ValidateInputSchema.shape,
      annotations: validateAnnotations,
    },
    async ({ server: endpoint, access, token, interactive, mcpspec, policy, clientProfile, reportDetail, verbose }) => {
      try {
        const result = await handleValidate({
          server: endpoint,
          access,
          token,
          interactive,
          mcpspec,
          policy,
          clientProfile,
          reportDetail,
          verbose,
        });
        return { content: [{ type: "text" as const, text: result.text }], isError: result.isError };
      } catch (err) {
        return { content: [{ type: "text" as const, text: `Validation failed: ${(err as Error).message}` }], isError: true };
      }
    },
  );

  server.registerTool(
    "health_check",
    {
      title: "Health Check MCP Server",
      description: "Quick connectivity check — verifies MCP initialize handshake and protocol version.",
      inputSchema: HealthCheckInputSchema.shape,
      annotations: { ...inspectionAnnotations, title: "Health Check MCP Server" },
    },
    async ({ server: endpoint, access, token, interactive }) => {
      try {
        const result = await handleHealthCheck({ server: endpoint, access, token, interactive });
        return { content: [{ type: "text" as const, text: result.text }], isError: result.isError };
      } catch (err) {
        return { content: [{ type: "text" as const, text: `Health check failed: ${(err as Error).message}` }], isError: true };
      }
    },
  );

  server.registerTool(
    "discover",
    {
      title: "Discover MCP Server Capabilities",
      description: "Discover MCP server capabilities — lists tools, resources, and prompts.",
      inputSchema: DiscoverInputSchema.shape,
      annotations: { ...inspectionAnnotations, title: "Discover MCP Server Capabilities" },
    },
    async ({ server: endpoint, access, token, interactive, format }) => {
      try {
        const result = await handleDiscover({ server: endpoint, access, token, interactive, format });
        return { content: [{ type: "text" as const, text: result.text }], isError: result.isError };
      } catch (err) {
        return { content: [{ type: "text" as const, text: `Discovery failed: ${(err as Error).message}` }], isError: true };
      }
    },
  );

  // ─── Connect Transport ───────────────────────────────────────

  const transport = new StdioServerTransport();
  await server.connect(transport);

  // Log to stderr (STDIO servers must not write to stdout)
  console.error(`mcpval-localmcp started (CLI: ${cliVersion})`);
}

main().catch((err) => {
  console.error("Fatal:", err);
  process.exit(1);
});
