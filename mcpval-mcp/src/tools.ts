/**
 * MCP Tool definitions for mcpval.
 * Each tool maps to a mcpval CLI command with structured input/output.
 */
import { z } from "zod";

// ─── Input Schemas (Zod) ─────────────────────────────────────

export const ValidateInputSchema = z.object({
  server: z.string().describe("MCP server endpoint URL or STDIO command to validate"),
  access: z
    .enum(["public", "authenticated"])
    .default("public")
    .describe("Server access intent — public (no auth) or authenticated (requires token)"),
  token: z.string().optional().describe("Bearer token for authenticated servers"),
  mcpspec: z.string().optional().describe("Target MCP spec profile (e.g., latest, 2025-11-25)"),
  verbose: z.boolean().default(false).describe("Include detailed output"),
});

export const HealthCheckInputSchema = z.object({
  server: z.string().describe("MCP server endpoint URL to check"),
  token: z.string().optional().describe("Bearer token if authentication is required"),
});

export const DiscoverInputSchema = z.object({
  server: z.string().describe("MCP server endpoint URL to discover"),
  token: z.string().optional().describe("Bearer token if authentication is required"),
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
