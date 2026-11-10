import assert from "node:assert/strict";
import { access, mkdtemp, readdir, readFile, rm } from "node:fs/promises";
import { tmpdir } from "node:os";
import { dirname, join, parse, resolve } from "node:path";
import { fileURLToPath } from "node:url";
import test from "node:test";
import { setTimeout as delay } from "node:timers/promises";
import { Client } from "@modelcontextprotocol/sdk/client/index.js";
import { StdioClientTransport } from "@modelcontextprotocol/sdk/client/stdio.js";
import { config } from "../src/config.js";

const packageRoot = dirname(fileURLToPath(new URL("../package.json", import.meta.url)));

function record(value: unknown): Record<string, unknown> {
  assert.ok(value !== null && typeof value === "object" && !Array.isArray(value));
  return value as Record<string, unknown>;
}

async function waitFor(check: () => Promise<boolean>, signal?: AbortSignal): Promise<void> {
  const deadline = AbortSignal.any([AbortSignal.timeout(10_000), ...(signal ? [signal] : [])]);
  while (!(await check())) await delay(20, undefined, { signal: deadline });
}

function isAlive(pid: unknown): boolean {
  assert.ok(typeof pid === "number" && Number.isInteger(pid) && pid > 0 && pid !== process.pid);
  try { process.kill(pid, 0); return true; }
  catch (error) {
    if ((error as NodeJS.ErrnoException).code === "ESRCH") return false;
    throw error;
  }
}

for (const termination of ["request-cancellation", "host-shutdown"] as const) {
test(`SDK wrapper actual CLI and owned fixture: ${termination}`, { timeout: 120_000 }, async context => {
  const cli = process.env.MCPVAL_INTEGRATION_CLI;
  assert.ok(cli, "Set MCPVAL_INTEGRATION_CLI to the freshly built CLI apphost; this integration gate does not skip missing prerequisites.");
  await access(cli);
  const fixture = resolve(packageRoot, "../Mcp.Benchmark.Tests/Fixtures/Servers/mcp-fixture-compliant.cjs");
  await access(fixture);
  const root = await mkdtemp(join(tmpdir(), "mcpval-real-cli-integration-"));
  const lifecycle = new AbortController();
  const operationSignal = AbortSignal.any([context.signal, lifecycle.signal]);
  const client = new Client({ name: "mcpval-integration", version: "1" });
  const environment: Record<string, string> = {
    PATH: process.env.PATH ?? "",
    HOME: root, DOTNET_CLI_HOME: root, TMPDIR: root, TMP: root, TEMP: root,
    USERPROFILE: root, APPDATA: join(root, "AppData", "Roaming"), LOCALAPPDATA: join(root, "AppData", "Local"),
    HOMEDRIVE: process.platform === "win32" ? parse(root).root.slice(0, 2) : "/",
    HOMEPATH: process.platform === "win32" ? root.slice(2) : root,
    DOTNET_NOLOGO: "1", DOTNET_CLI_TELEMETRY_OPTOUT: "1",
    [config.cliPathEnvVar]: resolve(cli),
    [config.localExecution.environmentVariable]: "true",
  };
  for (const name of ["SYSTEMROOT", "WINDIR", "PATHEXT"]) {
    const value = process.env[name];
    if (value) environment[name] = value;
  }
  const transport = new StdioClientTransport({
    command: process.execPath,
    args: [process.env.MCPVAL_INTEGRATION_SERVER ?? join(packageRoot, "dist/index.js")],
    cwd: root, env: environment, stderr: "pipe",
  });
  const audit = join(root, "fixture-methods.jsonl");
  const processAudit = join(root, "fixture-processes.jsonl");
  let ownedWrapperPid: number | null = null;
  const server = `${JSON.stringify(process.execPath)} ${JSON.stringify(fixture)} --audit-methods ${JSON.stringify(audit)} --audit-processes ${JSON.stringify(processAudit)}`;
  const waitForOwnedExit = () => waitFor(async () => {
    let rows: string;
    try { rows = await readFile(processAudit, "utf8"); }
    catch (error) { if ((error as NodeJS.ErrnoException).code === "ENOENT") return false; throw error; }
    return rows.trim().split("\n").every(line => {
      const observed = record(JSON.parse(line));
      return !isAlive(observed.pid) && !isAlive(observed.parentPid);
    });
  });
  try {
    await client.connect(transport);
    ownedWrapperPid = transport.pid;
    const surface = await client.listTools();
    assert.ok(surface.tools.some(tool => tool.name === "validate"));
    assert.ok(surface.tools.some(tool => tool.name === "health_check"));
    const health = await client.callTool({ name: "health_check", arguments: { server, protocolEra: "legacy" } }, undefined, { timeout: 60_000, signal: operationSignal });
    assert.equal(record(health.structuredContent).exitCode, 0);
    assert.equal(health.isError, false);

    const response = await client.callTool({
      name: "validate", arguments: { server, protocolEra: "legacy", mcpspec: "2025-11-25", assurance: "inventory" },
    }, undefined, { timeout: 90_000, signal: operationSignal });
    const envelope = record(response.structuredContent);
    assert.equal(envelope.command, "validate");
    assert.equal(envelope.exitCode, 0);
    assert.equal(response.isError, false);
    assert.match(JSON.stringify(envelope.contentTrust), /untrusted-target-derived/);
    const result = record(envelope.result);
    assert.equal(result.documentType, "mcpval.assurance-result");
    assert.equal(result.documentSchemaVersion, "2.0.0");
    assert.equal(record(result.run).executionMode, "safe");
    assert.equal(record(result.serverConfig).protocolEra, "legacy");
    const tools = record(record(result.measurements).toolValidation);
    assert.equal(tools.toolsDiscovered, 2);
    assert.ok(Array.isArray(tools.toolResults) && tools.toolResults.length > 0);
    assert.ok(tools.toolResults.every(tool => record(tool).executionSuccessful === false));
    const assurance = record(result.assurance);
    assert.equal(record(assurance.profile).profileId, "inventory");
    assert.ok(Array.isArray(assurance.claims) && assurance.claims.length > 0);
    assert.ok(!JSON.stringify(result).includes("MCPVAL_TOKEN"));
    const observed = (await readFile(audit, "utf8")).trim().split("\n").map(line => record(JSON.parse(line)).method);
    assert.ok(observed.includes("initialize") && observed.includes("tools/list"));
    assert.ok(!observed.some(method => ["tools/call", "resources/read", "prompts/get"].includes(String(method))),
      "Safe-mode non-execution must be proven by the target-side method audit, not only reported booleans.");
    const allowedMethods = new Set(["initialize", "notifications/initialized", "notifications/cancelled", "ping",
      "tools/list", "resources/list", "resources/templates/list", "prompts/list"]);
    assert.ok(observed.every(method => allowedMethods.has(String(method))), "Safe fixture received an unexpected method or malformed probe.");
    assert.ok(!(await readdir(root)).some(name => name.startsWith("mcpval-mcp-")), "Wrapper temporary artifacts must be removed after returning canonical data.");
    const cancel = new AbortController();
    const pending = client.callTool({ name: "validate", arguments: {
      server: `${server} --hold-method tools/list`, protocolEra: "legacy", mcpspec: "2025-11-25", assurance: "inventory",
    } }, undefined, { timeout: 60_000, signal: AbortSignal.any([operationSignal, cancel.signal]) })
      .then(reply => ({ rejected: false as const, reply }), () => ({ rejected: true as const }));
    await waitFor(async () => {
      const methods = (await readFile(audit, "utf8")).trim().split("\n").slice(observed.length);
      return methods.some(line => record(JSON.parse(line)).method === "tools/list");
    }, operationSignal);
    const wrapperPid = transport.pid;
    if (termination === "request-cancellation") cancel.abort();
    else if (process.platform === "win32") await transport.close();
    else {
      assert.ok(wrapperPid);
      process.kill(wrapperPid, "SIGTERM");
    }
    const outcome = await pending;
    if (termination === "request-cancellation") assert.equal(outcome.rejected, true);
    else if (!outcome.rejected) {
      assert.equal(outcome.reply.isError, true);
      assert.equal(record(outcome.reply.structuredContent).exitCode, 124);
    }
    await waitForOwnedExit();
    if (termination === "host-shutdown" && wrapperPid) await waitFor(async () => !isAlive(wrapperPid));
    await waitFor(async () => !(await readdir(root)).some(name => name.startsWith("mcpval-mcp-")));
  } finally {
    lifecycle.abort();
    ownedWrapperPid ??= transport.pid;
    const cleanupErrors: unknown[] = [];
    try { await client.close(); } catch (error) { cleanupErrors.push(error); }
    try { await transport.close(); } catch (error) { cleanupErrors.push(error); }
    if (ownedWrapperPid) await waitFor(async () => !isAlive(ownedWrapperPid));
    await waitForOwnedExit();
    await rm(root, { recursive: true, force: true });
    if (cleanupErrors.length) throw new AggregateError(cleanupErrors, "Integration transport cleanup failed.");
  }
});
}