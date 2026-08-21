#!/usr/bin/env node

import assert from "node:assert/strict";
import { spawn } from "node:child_process";
import { mkdtemp, readFile, rm } from "node:fs/promises";
import { tmpdir } from "node:os";
import { join, resolve } from "node:path";
import { execFile } from "node:child_process";
import { promisify } from "node:util";

const execFileAsync = promisify(execFile);
const npmCommand = process.platform === "win32" ? "npm.cmd" : "npm";
const npmOptions = (cwd) => ({ cwd, shell: process.platform === "win32" });
const [packageArg, expectedVersion] = process.argv.slice(2);
if (!packageArg || !expectedVersion) {
  console.error("Usage: smoke-npm-package.mjs PACKAGE.tgz VERSION");
  process.exit(64);
}

const packagePath = resolve(packageArg);
const root = await mkdtemp(join(tmpdir(), "mcpval-npm-smoke-"));
try {
  await execFileAsync(npmCommand, ["init", "-y"], npmOptions(root));
  await execFileAsync(npmCommand, ["install", "--ignore-scripts", packagePath], npmOptions(root));
  const installedPackage = JSON.parse(await readFile(join(root, "node_modules", "mcpval-localmcp", "package.json"), "utf8"));
  assert.equal(installedPackage.version, expectedVersion);

  const binTarget = typeof installedPackage.bin === "string"
    ? installedPackage.bin
    : installedPackage.bin?.["mcpval-localmcp"];
  assert.equal(typeof binTarget, "string");
  const executable = join(root, "node_modules", "mcpval-localmcp", binTarget);
  const child = spawn(process.execPath, [executable], { cwd: root, stdio: ["pipe", "pipe", "pipe"] });
  const exited = new Promise((resolveExit, reject) => {
    child.once("exit", (code, signal) => resolveExit({ code, signal }));
    child.once("error", reject);
  });
  let stdout = "";
  const response = new Promise((resolveResponse, reject) => {
    const timeout = setTimeout(() => reject(new Error("Timed out waiting for MCP initialize response.")), 10_000);
    child.stdout.setEncoding("utf8");
    child.stdout.on("data", chunk => {
      stdout += chunk;
      const newline = stdout.indexOf("\n");
      if (newline >= 0) {
        clearTimeout(timeout);
        resolveResponse(JSON.parse(stdout.slice(0, newline)));
      }
    });
    child.once("error", reject);
    child.once("exit", code => {
      if (code && !stdout.includes("\n")) reject(new Error(`Installed MCP server exited ${code} before initialize.`));
    });
  });

  try {
    child.stdin.write(`${JSON.stringify({ jsonrpc: "2.0", id: 1, method: "initialize", params: { protocolVersion: "2025-11-25", capabilities: {}, clientInfo: { name: "package-smoke", version: "1" } } })}\n`);
    const message = await response;
    assert.equal(message.jsonrpc, "2.0");
    assert.equal(message.id, 1);
    assert.equal(message.result?.serverInfo?.name, "mcpval");
    assert.equal(message.result?.serverInfo?.version, expectedVersion);
  } finally {
    child.stdin.end();
    if (child.exitCode === null && child.signalCode === null) child.kill("SIGTERM");
    await exited.catch(() => undefined);
  }
} finally {
  await rm(root, { recursive: true, force: true });
}
