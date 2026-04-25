/**
 * MCP Tool definitions for mcpval.
 * Each tool maps to a mcpval CLI command with structured input/output.
 */
import { z } from "zod";

const ClientProfileIds = [
  "all",
  "claude-code",
  "vscode-copilot-agent",
  "github-copilot-cli",
  "github-copilot-cloud-agent",
  "visual-studio-copilot",
  "claude",
  "vscode",
  "vscode-copilot",
  "copilot-cli",
  "cloud-agent",
  "copilot-cloud-agent",
  "visual-studio",
  "vs-copilot",
] as const;

const RemoteServerTargetSchema = z
  .string()
  .regex(/^(https?:\/\/|wss?:\/\/)\S+$/, "Use an absolute MCP endpoint URL such as https://example.test/mcp or wss://example.test/mcp.")
  .describe("Absolute MCP endpoint URL, for example https://example.test/mcp or wss://example.test/mcp.");

const StdioCommandTargetSchema = z
  .string()
  .regex(/^(?!https?:\/\/)(?!wss?:\/\/)\S(?:.*\S)?$/, "Use a non-empty stdio command such as npx -y @modelcontextprotocol/server-everything.")
  .describe("STDIO command, for example npx -y @modelcontextprotocol/server-everything.");

const BearerTokenSchema = z
  .string()
  .regex(/^\S+$/, "Bearer tokens must not contain whitespace.")
  .describe("Bearer token for authenticated servers. Supply the opaque token value without a Bearer prefix.");

const McpSpecSchema = z
  .string()
  .regex(/^(latest|\d{4}-\d{2}-\d{2})$/, "Use 'latest' or a dated MCP spec profile such as 2025-11-25.")
  .describe("Target MCP spec profile, either 'latest' or a dated profile such as 2025-11-25.");

const ClientProfileSchema = z
  .enum(ClientProfileIds)
  .describe("Client compatibility profile to evaluate. Use 'all' to run every supported profile.");

// ─── Input Schemas (Zod) ─────────────────────────────────────

const AccessIntentSchema = z
  .enum(["public", "authenticated", "enterprise", "unspecified"])
  .default("public")
  .describe("Declared server access intent so the CLI interprets auth expectations correctly.");

const ServerTargetSchema = z
  .union([RemoteServerTargetSchema, StdioCommandTargetSchema])
  .describe("MCP server target as an absolute endpoint URL or a local stdio command.");

export const ValidateInputSchema = z.object({
  server: ServerTargetSchema,
  access: AccessIntentSchema,
  token: BearerTokenSchema.optional(),
  interactive: z.boolean().default(false).describe("Allow the CLI to trigger an interactive auth flow when supported."),
  mcpspec: McpSpecSchema.optional(),
  policy: z.enum(["advisory", "balanced", "strict"]).optional().describe("Host-side gating mode. Does not change raw findings."),
  clientProfile: z.array(ClientProfileSchema).optional().describe("Optional client compatibility profiles to evaluate. Omit to use the CLI default host set."),
  reportDetail: z.enum(["full", "minimal"]).optional().describe("Human report detail level."),
  verbose: z.boolean().default(false).describe("Include detailed output"),
});

export const HealthCheckInputSchema = z.object({
  server: ServerTargetSchema,
  access: AccessIntentSchema,
  token: BearerTokenSchema.optional(),
  interactive: z.boolean().default(false).describe("Allow the CLI to trigger an interactive auth flow when supported."),
});

export const DiscoverInputSchema = z.object({
  server: ServerTargetSchema,
  access: AccessIntentSchema,
  token: BearerTokenSchema.optional(),
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
