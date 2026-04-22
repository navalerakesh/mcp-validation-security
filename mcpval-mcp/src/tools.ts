/**
 * MCP Tool definitions for mcpval.
 * Each tool maps to a mcpval CLI command with structured input/output.
 */
import { z } from "zod";

// ─── Input Schemas (Zod) ─────────────────────────────────────

const AccessIntentSchema = z
  .enum(["public", "authenticated", "enterprise", "unspecified"])
  .default("public")
  .describe("Declared server access intent so the CLI interprets auth expectations correctly.");

const ServerTargetSchema = z
  .string()
  .min(1)
  .describe("MCP server endpoint URL or STDIO command, for example https://example.test/mcp or npx -y @modelcontextprotocol/server-everything.");

export const ValidateInputSchema = z.object({
  server: ServerTargetSchema,
  access: AccessIntentSchema,
  token: z.string().optional().describe("Bearer token for authenticated servers"),
  interactive: z.boolean().default(false).describe("Allow the CLI to trigger an interactive auth flow when supported."),
  mcpspec: z.string().optional().describe("Target MCP spec profile (e.g., latest, 2025-11-25)"),
  policy: z.enum(["advisory", "balanced", "strict"]).optional().describe("Host-side gating mode. Does not change raw findings."),
  clientProfile: z.array(z.string()).optional().describe("Optional client compatibility profiles to evaluate. Omit to use the CLI default host set."),
  reportDetail: z.enum(["full", "minimal"]).optional().describe("Human report detail level."),
  verbose: z.boolean().default(false).describe("Include detailed output"),
});

export const HealthCheckInputSchema = z.object({
  server: ServerTargetSchema,
  access: AccessIntentSchema,
  token: z.string().optional().describe("Bearer token if authentication is required"),
  interactive: z.boolean().default(false).describe("Allow the CLI to trigger an interactive auth flow when supported."),
});

export const DiscoverInputSchema = z.object({
  server: ServerTargetSchema,
  access: AccessIntentSchema,
  token: z.string().optional().describe("Bearer token if authentication is required"),
  interactive: z.boolean().default(false).describe("Allow the CLI to trigger an interactive auth flow when supported."),
  format: z.enum(["json", "yaml", "table"]).default("json").describe("Discovery output format. json is usually easiest for agents to consume."),
});

// ─── Tool Metadata ───────────────────────────────────────────

export const tools = [
  {
    name: "validate",
    description:
      "Validate an MCP server for protocol compliance, security posture, AI safety, and trust level (L1-L5). " +
      "Tests JSON-RPC compliance, injection resistance, LLM-friendliness of errors, and schema quality.",
    inputSchema: ValidateInputSchema,
  },
  {
    name: "health_check",
    description:
      "Quick connectivity check on an MCP server. " +
      "Verifies the server responds to the MCP initialize handshake and reports protocol version.",
    inputSchema: HealthCheckInputSchema,
  },
  {
    name: "discover",
    description:
      "Discover the capabilities of an MCP server — lists tools, resources, and prompts it exposes.",
    inputSchema: DiscoverInputSchema,
  },
] as const;
