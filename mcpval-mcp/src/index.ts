#!/usr/bin/env node
/**
 * mcpval-mcp — MCP server that wraps the mcpval CLI.
 *
 * Exposes validation, health-check, and discovery as MCP tools
 * so AI agents can validate MCP servers through the protocol itself.
 *
 * Usage:
 *   npx mcpval-mcp              (stdio transport)
 *   node dist/index.js           (after build)
 *
 * VS Code mcp.json:
 *   {
 *     "servers": {
 *       "mcpval": {
 *         "command": "npx",
 *         "args": ["-y", "mcpval-mcp"],
 *         "type": "stdio"
 *       }
 *     }
 *   }
 */
import { McpServer } from "@modelcontextprotocol/sdk/server/mcp.js";
import { StdioServerTransport } from "@modelcontextprotocol/sdk/server/stdio.js";
import { config } from "./config.js";
import { ValidateInputSchema, HealthCheckInputSchema, DiscoverInputSchema } from "./tools.js";
import { handleValidate, handleHealthCheck, handleDiscover } from "./handlers.js";
import { getCliVersion } from "./cli-runner.js";

async function main(): Promise<void> {
  const cliVersion = await getCliVersion();

  const server = new McpServer({
    name: config.server.name,
    version: config.server.version,
  });

  // ─── Register Tools ──────────────────────────────────────────

  server.tool(
    "validate",
    "Validate an MCP server for compliance, security, and AI safety. Assigns trust level L1-L5.",
    ValidateInputSchema.shape,
    async ({ server: endpoint, access, token, mcpspec, verbose }) => {
      try {
        const result = await handleValidate({ server: endpoint, access, token, mcpspec, verbose });
        return { content: [{ type: "text" as const, text: result }] };
      } catch (err) {
        return { content: [{ type: "text" as const, text: `Validation failed: ${(err as Error).message}` }], isError: true };
      }
    },
  );

  server.tool(
    "health_check",
    "Quick connectivity check — verifies MCP initialize handshake and protocol version.",
    HealthCheckInputSchema.shape,
    async ({ server: endpoint, token }) => {
      try {
        const result = await handleHealthCheck({ server: endpoint, token });
        return { content: [{ type: "text" as const, text: result }] };
      } catch (err) {
        return { content: [{ type: "text" as const, text: `Health check failed: ${(err as Error).message}` }], isError: true };
      }
    },
  );

  server.tool(
    "discover",
    "Discover MCP server capabilities — lists tools, resources, and prompts.",
    DiscoverInputSchema.shape,
    async ({ server: endpoint, token }) => {
      try {
        const result = await handleDiscover({ server: endpoint, token });
        return { content: [{ type: "text" as const, text: result }] };
      } catch (err) {
        return { content: [{ type: "text" as const, text: `Discovery failed: ${(err as Error).message}` }], isError: true };
      }
    },
  );

  // ─── Connect Transport ───────────────────────────────────────

  const transport = new StdioServerTransport();
  await server.connect(transport);

  // Log to stderr (STDIO servers must not write to stdout)
  console.error(`mcpval-mcp started (CLI: ${cliVersion})`);
}

main().catch((err) => {
  console.error("Fatal:", err);
  process.exit(1);
});
