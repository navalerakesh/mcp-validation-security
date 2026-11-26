import assert from "node:assert/strict";
import { execFile } from "node:child_process";
import { promisify } from "node:util";
import { access, mkdtemp, mkdir, readdir, readFile, rm, writeFile } from "node:fs/promises";
import { tmpdir } from "node:os";
import { delimiter, dirname, join, parse, resolve } from "node:path";
import { fileURLToPath } from "node:url";
import test from "node:test";
import { setTimeout as delay } from "node:timers/promises";
import { Client } from "@modelcontextprotocol/sdk/client/index.js";
import { StdioClientTransport } from "@modelcontextprotocol/sdk/client/stdio.js";
import { config } from "../src/config.js";
import { parse as parseYaml } from "yaml";

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

test("Actual CLI help uses the execution command surface without reading config or launching targets", async () => {
  const cli = process.env.MCPVAL_INTEGRATION_CLI;
  assert.ok(cli, "A freshly built CLI is required.");
  const root = await mkdtemp(join(tmpdir(), "mcpval-help-contract-"));
  try {
    const manifestOutput = await promisify(execFile)(cli, ["config-schema", "--commands"],
      { cwd: root, timeout: 10_000, maxBuffer: 128 * 1024 });
    const manifest = record(JSON.parse(manifestOutput.stdout));
    assert.equal(manifest.documentType, "mcpval.command-manifest");
    assert.ok(Array.isArray(manifest.commands));
    assert.ok(Array.isArray(manifest.rootOptions));
    const rootHelp = await promisify(execFile)(cli, ["--help"], { cwd: root, timeout: 10_000 });
    for (const item of manifest.rootOptions) {
      const option = record(item);
      assert.ok(typeof option.name === "string" && rootHelp.stdout.includes(option.name));
    }
    assert.ok(manifest.rootOptions.some(item => record(item).name === "--version"));
    for (const [command, options] of [
      ["validate", ["--identity-baseline", "--validation-id", "--max-requests", "--allow-origin"]],
      ["health-check", ["--protocol-era", "--max-requests", "--allow-origin"]],
      ["discover", ["--protocol-era", "--max-requests", "--allow-origin"]],
      ["report", ["--output", "--report-detail"]],
      ["config-schema", ["--commands"]]
    ] as const) {
      const { stdout } = await promisify(execFile)(cli, [command, "--config", join(root, "does-not-exist.json"), "--help"],
        { cwd: root, timeout: 10_000, maxBuffer: 128 * 1024 });
      for (const option of options) assert.ok(stdout.includes(option), `${command} help omitted ${option}`);
      const description = record(manifest.commands.find(item => record(item).name === command));
      assert.ok(Array.isArray(description.options));
      for (const item of description.options) {
        const option = record(item);
        assert.ok(typeof option.name === "string" && stdout.includes(option.name), `${command} manifest advertises an absent help option`);
        assert.notEqual(option.name, "--version", "Root-only version must not be advertised on subcommands");
        if (option.name === "--token") {
          assert.equal(option.secrecy, "transient-secret");
          assert.equal(option.defaultValue, null);
        }
      }
    }
    assert.deepEqual(await readdir(root), []);
  } finally { await rm(root, { recursive: true, force: true }); }
});

for (const dryRun of [false, true]) {
test(`Actual Action publishes typed ${dryRun ? "plan-only" : "collected"} outputs while preserving default human output`, { timeout: 60_000 }, async () => {
  const cli = process.env.MCPVAL_INTEGRATION_CLI;
  assert.ok(cli, "A freshly built CLI is required.");
  const actionRoot = resolve(packageRoot, "..");
  const action = record(parseYaml(await readFile(join(actionRoot, "action.yml"), "utf8")));
  const steps = record(action.runs).steps;
  assert.ok(Array.isArray(steps));
  const validation = record(steps.find(item => record(item).name === "Run MCP validation"));
  const root = await mkdtemp(join(tmpdir(), "mcpval-action-real-cli-"));
  try {
    const bin = join(root, "bin");
    await mkdir(bin);
    await writeFile(join(bin, "mcpval"), '#!/usr/bin/env bash\nexec "$MCPVAL_ACTUAL_CLI" "$@"\n', { mode: 0o755 });
    const script = join(root, "validate.sh");
    assert.equal(typeof validation.run, "string");
    await writeFile(script, String(validation.run));
    const fixture = resolve(actionRoot, "Mcp.Benchmark.Tests/Fixtures/Servers/mcp-fixture-compliant.cjs");
    const auditMethods = join(root, "fixture-methods.jsonl");
    const inputs: Record<string, string> = Object.fromEntries(Object.entries(record(action.inputs))
      .map(([name, value]) => [name, String(record(value).default ?? "")]));
    Object.assign(inputs, {
      server: `${JSON.stringify(process.execPath)} ${JSON.stringify(fixture)} --audit-methods ${JSON.stringify(auditMethods)}`,
      "output-dir": join(root, "artifacts"), "protocol-era": "legacy", "github-summary": "false", "github-annotations": "false",
      "dry-run": String(dryRun)
    });
    const environment: NodeJS.ProcessEnv = {
      PATH: `${bin}${delimiter}${process.env.PATH ?? ""}`, HOME: root, DOTNET_CLI_HOME: root,
      TMPDIR: root, TEMP: root, TMP: root, DOTNET_NOLOGO: "1", DOTNET_CLI_TELEMETRY_OPTOUT: "1",
      GITHUB_OUTPUT: join(root, "github-output"), GITHUB_ACTION_PATH: actionRoot, MCPVAL_ACTUAL_CLI: resolve(cli)
    };
    for (const name of ["SYSTEMROOT", "WINDIR", "PATHEXT"]) if (process.env[name]) environment[name] = process.env[name];
    for (const [name, expression] of Object.entries(record(validation.env))) {
      const match = /^\$\{\{\s*inputs\.([a-z-]+)\s*\}\}$/.exec(String(expression));
      assert.ok(match);
      environment[name] = inputs[match[1]];
    }
    const bash = process.platform === "win32" ? join(process.env.ProgramFiles ?? "C:\\Program Files", "Git", "bin", "bash.exe") : "bash";
    const execution = await promisify(execFile)(bash, [script.replaceAll("\\", "/")],
      { cwd: root, env: environment, timeout: 45_000, maxBuffer: 1024 * 1024 });
    assert.ok(execution.stdout.includes("Running mcpval validation"));
    assert.ok(!execution.stdout.includes('"documentType":"mcpval.cli-result"'));
    if (dryRun) assert.ok(execution.stdout.includes("Dry run complete. No requests were sent."));
    else {
      assert.match(execution.stdout, /Assurance INVENTORY: core=[A-Za-z]+, security=[A-Za-z]+, operations=[A-Za-z]+/);
    }
    const outputs = Object.fromEntries((await readFile(environment.GITHUB_OUTPUT!, "utf8")).trim().split("\n").map(line => {
      const separator = line.indexOf("=");
      return [line.slice(0, separator), line.slice(separator + 1)];
    }));
    assert.equal(outputs["exit-code"], "0");
    assert.equal(outputs["artifact-state"], dryRun ? "plan-only" : "published");
    if (dryRun) {
      assert.equal(outputs["result-path"], "");
      await assert.rejects(access(auditMethods), { code: "ENOENT" });
      assert.ok((await readdir(outputs["invocation-directory"])).some(name => name.endsWith("-audit.json")));
      return;
    }
    const result = record(JSON.parse(await readFile(outputs["result-path"], "utf8")));
    assert.equal(outputs["validation-id"], record(result.run).validationId);
    assert.equal(outputs["target-identity-sha256"], record(result.run).targetIdentitySha256);
    assert.equal(outputs["policy-passed"], String(record(result.policyDecision).passed));
    assert.equal(record(record(record(result.run).capabilitySnapshot).payload).toolInvocationAttempted, false);
  } finally { await rm(root, { recursive: true, force: true }); }
});
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
    await assert.rejects(promisify(execFile)(cli, ["validate", "--server", server, "--mode", "standard", "--protocol-era", "legacy"], {
      cwd: root, env: environment, timeout: 10_000,
    }), error => {
      const failed = error as Error & { code?: number; stdout?: string; stderr?: string };
      return failed.code === 64 && /Comprehensive assessment requires/.test(`${failed.stdout ?? ""}${failed.stderr ?? ""}`);
    });
    await assert.rejects(access(processAudit), error => (error as NodeJS.ErrnoException).code === "ENOENT");
    await client.connect(transport);
    ownedWrapperPid = transport.pid;
    const surface = await client.listTools();
    assert.ok(surface.tools.some(tool => tool.name === "validate"));
    assert.ok(surface.tools.some(tool => tool.name === "health_check"));
    const health = await client.callTool({ name: "health_check", arguments: { server, protocolEra: "legacy" } }, undefined, { timeout: 60_000, signal: operationSignal });
    assert.equal(record(health.structuredContent).exitCode, 0);
    assert.equal(health.isError, false);

    const beforeDefault = await readFile(audit, "utf8");
    const defaultResponse = await client.callTool({
      name: "validate", arguments: { server, protocolEra: "legacy", mcpspec: "2025-11-25" },
    }, undefined, { timeout: 60_000, signal: operationSignal });
    assert.equal(defaultResponse.isError, true);
    assert.equal(record(defaultResponse.structuredContent).exitCode, 64);
    assert.equal(await readFile(audit, "utf8"), beforeDefault);

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
    assert.equal(result.documentSchemaVersion, "3.8.0");
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