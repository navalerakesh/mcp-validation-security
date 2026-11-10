/**
 * CLI runner — executes the mcpval CLI and captures structured output.
 * Single responsibility: spawn process, capture stdout/stderr, parse results.
 */
import { spawn, type ChildProcess } from "node:child_process";
import { existsSync } from "node:fs";
import { mkdtemp, readdir, readFile, rm, writeFile } from "node:fs/promises";
import { tmpdir } from "node:os";
import { join } from "node:path";
import { config } from "./config.js";
import { startWindowsSupervisedCli } from "./windows-supervisor.js";

const activeExecutions = new Set<{ cancel: () => void; settled: Promise<CliResult>; cleanup: { failed: boolean } }>();
const activeInvocations = new Set<Promise<CliResult>>();
const unresolvedArtifactRoots = new Set<string>();
let shuttingDown = false;
let cleanupDegraded = false;

export async function shutdownCliProcesses(): Promise<void> {
  shuttingDown = true;
  const executions = [...activeExecutions];
  for (const execution of executions) execution.cancel();
  const outcomes = await Promise.allSettled([...executions.map(execution => execution.settled), ...activeInvocations]);
  if (executions.some(execution => execution.cleanup.failed) || outcomes.some(outcome => outcome.status === "rejected") || unresolvedArtifactRoots.size > 0)
    throw new Error("Owned CLI process or artifact cleanup failed after all cleanup tasks drained.");
}

export interface CliResult {
  exitCode: number;
  stdout: string;
  stderr: string;
  /** Parsed JSON result from the output directory, if available */
  resultJson?: Record<string, unknown>;
  processCleanupConfirmed?: boolean;
}

export interface CliInvocation {
  command: "validate" | "health-check" | "discover";
  args?: string[];
  configJson?: Record<string, unknown>;
  captureResultJson?: boolean;
  signal?: AbortSignal;
}

async function terminateOwnedTree(child: ChildProcess, abandoned: () => boolean): Promise<void> {
  if (!child.pid) return;
  const signalGroup = (signal: NodeJS.Signals | 0): boolean => {
    try { process.kill(-child.pid!, signal); return true; }
    catch (error) {
      if ((error as NodeJS.ErrnoException).code === "ESRCH") return false;
      throw error;
    }
  };
  if (!signalGroup("SIGTERM")) return;
  await new Promise<void>(resolve => setTimeout(resolve, config.processTerminationGraceMs));
  signalGroup("SIGKILL");
  while (signalGroup(0)) {
    if (abandoned()) throw new Error("Owned process-group exit remains unconfirmed.");
    await new Promise<void>(resolve => setTimeout(resolve, config.processExitPollMs));
  }
}

async function executeOwnedCli(file: string, args: string[], environment: NodeJS.ProcessEnv, signal?: AbortSignal, timeoutMs: number = config.defaultTimeoutMs): Promise<CliResult> {
  if (cleanupDegraded) return { exitCode: 70, stdout: "", stderr: "Owned CLI cleanup is unconfirmed; new launches are blocked.", processCleanupConfirmed: false };
  if (shuttingDown || signal?.aborted) return { exitCode: 124, stdout: "", stderr: "CLI operation cancelled before launch." };
  if (process.platform === "win32") {
    const execution = startWindowsSupervisedCli(file, args, environment, signal, timeoutMs);
    activeExecutions.add(execution);
    try { return await execution.settled; }
    finally {
      if (execution.cleanup.failed) cleanupDegraded = true;
      else activeExecutions.delete(execution);
    }
  }
  const child = spawn(file, args, {
    env: environment, windowsHide: true, detached: true, stdio: ["ignore", "pipe", "pipe"],
  });
  const stdout: Buffer[] = [];
  const stderr: Buffer[] = [];
  let retainedBytes = 0;
  let termination: Promise<void> | undefined;
  let forcedExit: number | undefined;
  let failure: string | undefined;
  const cleanup = { failed: false };
  let finished = false;
  let cleanupDeadline: ReturnType<typeof setTimeout> | undefined;
  let finish: (code: number | null) => void = () => {};
  const unconfirmed = () => {
    if (finished) return;
    cleanup.failed = true;
    cleanupDegraded = true;
    forcedExit = 70;
    failure = "CLI process-tree termination could not be confirmed; operation artifacts are retained.";
    child.stdout?.destroy();
    child.stderr?.destroy();
    child.unref();
    finish(null);
  };
  const stop = (exitCode?: number) => {
    if (exitCode !== undefined) forcedExit ??= exitCode;
    if (!finished && !cleanupDeadline) cleanupDeadline = setTimeout(unconfirmed, config.processCleanupTimeoutMs);
    termination ??= terminateOwnedTree(child, () => cleanup.failed).catch(unconfirmed);
  };
  const collect = (target: Buffer[], chunk: Buffer) => {
    const remaining = Math.max(0, config.maximumCliOutputBytes - retainedBytes);
    if (remaining > 0) target.push(Buffer.from(chunk.subarray(0, remaining)));
    retainedBytes += Math.min(remaining, chunk.length);
    if (chunk.length > remaining) { failure = "CLI output exceeded the configured byte limit."; stop(70); }
  };
  child.stdout!.on("data", (chunk: Buffer) => collect(stdout, chunk));
  child.stderr!.on("data", (chunk: Buffer) => collect(stderr, chunk));
  const streamFailure = () => { failure = "CLI output stream failed."; stop(70); };
  child.stdout!.on("error", streamFailure);
  child.stderr!.on("error", streamFailure);
  const onAbort = () => stop(124);
  signal?.addEventListener("abort", onAbort, { once: true });
  if (signal?.aborted) onAbort();
  const timeout = setTimeout(() => stop(124), timeoutMs);
  child.once("exit", () => stop());
  const settled = new Promise<CliResult>(resolve => {
    child.once("error", () => { failure = "CLI process could not be started or controlled."; forcedExit = 70; });
    finish = code => {
      if (finished) return;
      finished = true;
      clearTimeout(timeout);
      if (cleanupDeadline) clearTimeout(cleanupDeadline);
      signal?.removeEventListener("abort", onAbort);
      resolve({
        exitCode: forcedExit ?? code ?? 70,
        stdout: Buffer.concat(stdout).toString("utf8"),
        stderr: failure ?? Buffer.concat(stderr).toString("utf8"),
        processCleanupConfirmed: !cleanup.failed,
      });
    };
    child.once("close", async code => {
      stop();
      await termination;
      finish(code);
    });
  });
  const execution = { cancel: () => stop(124), settled, cleanup };
  activeExecutions.add(execution);
  try { return await settled; }
  finally { if (!cleanup.failed) activeExecutions.delete(execution); }
}

/**
 * Resolves the path to the mcpval CLI executable.
 * Priority: MCPVAL_CLI_PATH env var → known dotnet tool paths → PATH lookup.
 */
export function resolveCliPath(): string {
  // 1. Environment variable override
  const envPath = process.env[config.cliPathEnvVar];
  if (envPath && existsSync(envPath)) return envPath;

  // 2. Check known dotnet global tool install locations
  const home = process.env.HOME || process.env.USERPROFILE || "";
  if (home) {
    const knownPaths = [
      join(home, ".dotnet", "tools", "mcpval"),              // macOS / Linux
      join(home, ".dotnet", "tools", "mcpval.exe"),          // Windows
    ];
    for (const p of knownPaths) {
      if (existsSync(p)) return p;
    }
  }

  // 3. Default: assume it's on PATH
  return config.cliCommand;
}

/**
 * Checks if the mcpval CLI is available.
 */
export async function getCliAvailability(signal?: AbortSignal): Promise<CliResult> {
  try {
    const cliPath = resolveCliPath();
    return await executeOwnedCli(cliPath, ["--version"], process.env, signal, config.cliVersionTimeoutMs);
  } catch {
    return { exitCode: 70, stdout: "", stderr: "CLI availability could not be established." };
  }
}

export async function isCliAvailable(signal?: AbortSignal): Promise<boolean> {
  return (await getCliAvailability(signal)).exitCode === 0;
}

/**
 * Gets the installed CLI version.
 */
export async function getCliVersion(): Promise<string> {
  try {
    const cliPath = resolveCliPath();
    const result = await executeOwnedCli(cliPath, ["--version"], process.env, undefined, config.cliVersionTimeoutMs);
    return result.exitCode === 0 ? result.stdout.trim() : "not installed";
  } catch {
    return "not installed";
  }
}

/**
 * Runs mcpval with the given command payload and returns structured results.
 * @param invocation Command-specific invocation payload
 * @param extraEnv Additional environment variables
 */
export function runCli(invocation: CliInvocation, extraEnv?: Record<string, string>): Promise<CliResult> {
  const pending = runCliInvocation(invocation, extraEnv);
  activeInvocations.add(pending);
  return pending.finally(() => activeInvocations.delete(pending));
}

async function runCliInvocation(invocation: CliInvocation, extraEnv?: Record<string, string>): Promise<CliResult> {
  if (cleanupDegraded) return { exitCode: 70, stdout: "", stderr: "Owned CLI cleanup is unconfirmed; new launches are blocked.", processCleanupConfirmed: false };
  const cliPath = resolveCliPath();
  const tempRoot = await mkdtemp(join(tmpdir(), "mcpval-mcp-"));
  const outputDir = join(tempRoot, "artifacts");
  const configPath = invocation.configJson ? join(tempRoot, "config.json") : undefined;

  const fullArgs = [invocation.command, ...(invocation.args ?? [])];
  let preserveArtifacts = false;

  try {
    if (configPath && invocation.configJson) {
      await writeFile(configPath, `${JSON.stringify(invocation.configJson, null, 2)}\n`, { encoding: "utf-8", mode: 0o600 });
      fullArgs.push("--config", configPath);
    }
    if (invocation.captureResultJson) fullArgs.push("--output", outputDir);
    const execution = await executeOwnedCli(cliPath, fullArgs, { ...process.env, ...extraEnv }, invocation.signal);

    preserveArtifacts = execution.processCleanupConfirmed === false;
    const resultJson = invocation.captureResultJson && !preserveArtifacts ? await loadResultJson(outputDir) : undefined;

    return { ...execution, resultJson };
  } catch {
    const resultJson = invocation.captureResultJson ? await loadResultJson(outputDir) : undefined;

    console.error("mcpval CLI invocation failed before completion.");
    return {
      exitCode: 70,
      stdout: "",
      stderr: "CLI invocation failed before completion.",
      resultJson,
    };
  } finally {
    if (!preserveArtifacts) {
      try { await rm(tempRoot, { recursive: true, force: true }); }
      catch {
        unresolvedArtifactRoots.add(tempRoot);
        cleanupDegraded = true;
        throw new Error("Owned CLI artifact cleanup failed; artifacts are retained and further launches are blocked.");
      }
    }
  }
}

export function normalizeCliExitCode(error: { code?: unknown; killed?: boolean }): number {
  if (error.killed) return 124;
  return typeof error.code === "number" && Number.isInteger(error.code)
    ? error.code
    : 70;
}

async function loadResultJson(dir: string): Promise<Record<string, unknown> | undefined> {
  try {
    const files = await readdir(dir);
    const resultFile = files.find((f) => f.endsWith("-result.json"));
    if (!resultFile) return undefined;

    const content = await readFile(join(dir, resultFile), "utf-8");
    return JSON.parse(content);
  } catch {
    return undefined;
  }
}

