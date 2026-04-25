import assert from "node:assert/strict";
import { chmod, mkdtemp, writeFile } from "node:fs/promises";
import { tmpdir } from "node:os";
import { dirname, join } from "node:path";
import test from "node:test";
import { fileURLToPath } from "node:url";
import { Client } from "@modelcontextprotocol/sdk/client/index.js";
import { StdioClientTransport } from "@modelcontextprotocol/sdk/client/stdio.js";
import { config } from "../src/config.js";

const packageRoot = dirname(fileURLToPath(new URL("../package.json", import.meta.url)));
const tsxCliPath = join(packageRoot, "node_modules", "tsx", "dist", "cli.mjs");
const serverEntryPath = join(packageRoot, "src", "index.ts");

const runnerScript = `#!/usr/bin/env node
const args = process.argv.slice(2);
if (args.includes("--version")) {
  process.stdout.write("mcpval test harness\\n");
  process.exit(0);
}

process.stdout.write("ok\\n");
process.exit(0);
`;

async function createMockCli(): Promise<string> {
  const dir = await mkdtemp(join(tmpdir(), "mcpval-localmcp-server-test-"));
  const scriptPath = join(dir, "mock-cli.mjs");
  await writeFile(scriptPath, runnerScript, "utf-8");
  await chmod(scriptPath, 0o755);
  return scriptPath;
}

test("server exposes rich initialize metadata and guidance", async () => {
  const mockCliPath = await createMockCli();
  const client = new Client({ name: "mcpval-localmcp-test-client", version: "1.0.0" });
  const transport = new StdioClientTransport({
    command: process.execPath,
    args: [tsxCliPath, serverEntryPath],
    cwd: packageRoot,
    env: {
      ...process.env,
      [config.cliPathEnvVar]: mockCliPath,
    },
    stderr: "pipe",
  });

  try {
    await client.connect(transport);

    const serverInfo = client.getServerVersion();
    assert.equal(serverInfo?.name, config.server.name);
    assert.equal(serverInfo?.title, config.server.title);
    assert.equal(serverInfo?.description, config.server.description);
    assert.equal(serverInfo?.websiteUrl, config.server.websiteUrl);

    const instructions = client.getInstructions();
    assert.equal(instructions, config.server.instructions);
    assert.match(instructions ?? "", /Use health_check first/);
    assert.match(instructions ?? "", /Use validate for full compliance/);

    const { tools } = await client.listTools();
    const validateTool = tools.find((tool) => tool.name === "validate");
    assert.ok(validateTool);
    assert.equal(validateTool?.title, "Validate MCP Server");
    assert.equal(validateTool?.annotations?.openWorldHint, true);

    const discoverTool = tools.find((tool) => tool.name === "discover");
    assert.ok(discoverTool);
    assert.equal(discoverTool?.annotations?.readOnlyHint, true);
    assert.equal(discoverTool?.annotations?.idempotentHint, true);
  } finally {
    await client.close().catch(() => undefined);
    await transport.close().catch(() => undefined);
  }
});