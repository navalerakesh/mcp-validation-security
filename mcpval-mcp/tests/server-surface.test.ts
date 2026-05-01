import assert from "node:assert/strict";
import { spawn, type ChildProcessWithoutNullStreams } from "node:child_process";
import { chmod, mkdtemp, writeFile } from "node:fs/promises";
import { tmpdir } from "node:os";
import { dirname, join } from "node:path";
import type { Readable } from "node:stream";
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
    const capabilities = client.getServerCapabilities();
    assert.equal(serverInfo?.name, config.server.name);
    assert.equal(serverInfo?.title, config.server.title);
    assert.equal(serverInfo?.description, config.server.description);
    assert.equal(serverInfo?.websiteUrl, config.server.websiteUrl);
    assert.equal(capabilities?.logging !== undefined, true);
    assert.equal(capabilities?.resources?.listChanged, true);
    assert.equal(capabilities?.prompts?.listChanged, true);
    assert.equal(capabilities?.tools?.listChanged, true);

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

    const { resources } = await client.listResources();
    assert.equal(resources.length, 1);
    assert.equal(resources[0]?.uri, "mcpval://server/metadata");

    const { prompts } = await client.listPrompts();
    assert.ok(prompts.some((prompt) => prompt.name === "validate-mcp-server"));
  } finally {
    await client.close().catch(() => undefined);
    await transport.close().catch(() => undefined);
  }
});

test("stdio transport returns standard JSON-RPC errors for malformed input", async () => {
  const mockCliPath = await createMockCli();
  const child = startServerProcess(mockCliPath);
  const nextLine = createLineReader(child.stdout);

  try {
    child.stdin.write("{ invalid json\n");
    const parseError = JSON.parse(await nextLine()) as JsonRpcErrorResponse;
    assert.equal(parseError.error.code, -32700);

    child.stdin.write(`${JSON.stringify({ method: "test", id: 1, params: {} })}\n`);
    const invalidRequest = JSON.parse(await nextLine()) as JsonRpcErrorResponse;
    assert.equal(invalidRequest.id, 1);
    assert.equal(invalidRequest.error.code, -32600);

    child.stdin.write(
      `${JSON.stringify({
        jsonrpc: "2.0",
        id: "init",
        method: "initialize",
        params: {
          protocolVersion: "2025-11-25",
          capabilities: {},
          clientInfo: { name: "raw-stdio-test", version: "1.0.0" },
        },
      })}\n`,
    );

    const initialize = JSON.parse(await nextLine()) as InitializeResponse;
    assert.equal(initialize.id, "init");
    assert.equal(initialize.result.capabilities.logging !== undefined, true);
    assert.equal(initialize.result.capabilities.resources?.listChanged, true);
    assert.equal(initialize.result.capabilities.prompts?.listChanged, true);
    assert.equal(initialize.result.capabilities.tools?.listChanged, true);
  } finally {
    child.kill();
  }
});

interface JsonRpcErrorResponse {
  id?: string | number;
  error: {
    code: number;
  };
}

interface InitializeResponse {
  id: string;
  result: {
    capabilities: {
      logging?: Record<string, unknown>;
      resources?: { listChanged?: boolean };
      prompts?: { listChanged?: boolean };
      tools?: { listChanged?: boolean };
    };
  };
}

function startServerProcess(mockCliPath: string): ChildProcessWithoutNullStreams {
  return spawn(process.execPath, [tsxCliPath, serverEntryPath], {
    cwd: packageRoot,
    env: {
      ...process.env,
      [config.cliPathEnvVar]: mockCliPath,
    },
    stdio: ["pipe", "pipe", "pipe"],
  });
}

function createLineReader(stream: Readable): () => Promise<string> {
  let buffer = "";
  const waiters: Array<(line: string) => void> = [];

  stream.setEncoding("utf8");
  stream.on("data", (chunk: string) => {
    buffer += chunk;
    drainLines();
  });

  return () =>
    new Promise<string>((resolve, reject) => {
      const line = shiftLine();
      if (line !== undefined) {
        resolve(line);
        return;
      }

      const timeout = setTimeout(() => reject(new Error("Timed out waiting for stdout line.")), 2_000);
      waiters.push((next) => {
        clearTimeout(timeout);
        resolve(next);
      });
    });

  function drainLines(): void {
    while (waiters.length > 0) {
      const line = shiftLine();
      if (line === undefined) {
        return;
      }

      waiters.shift()?.(line);
    }
  }

  function shiftLine(): string | undefined {
    const newlineIndex = buffer.indexOf("\n");
    if (newlineIndex < 0) {
      return undefined;
    }

    const line = buffer.slice(0, newlineIndex).replace(/\r$/, "");
    buffer = buffer.slice(newlineIndex + 1);
    return line;
  }
}