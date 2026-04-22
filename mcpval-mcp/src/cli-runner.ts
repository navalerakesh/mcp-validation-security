/**
 * CLI runner — executes the mcpval CLI and captures structured output.
 * Single responsibility: spawn process, capture stdout/stderr, parse results.
 */
import { execFile } from "node:child_process";
import { existsSync } from "node:fs";
import { mkdtemp, readdir, readFile, rm, writeFile } from "node:fs/promises";
import { tmpdir } from "node:os";
import { join } from "node:path";
import { promisify } from "node:util";
import { config } from "./config.js";

const execFileAsync = promisify(execFile);

export interface CliResult {
  exitCode: number;
  stdout: string;
  stderr: string;
  /** Parsed JSON result from the output directory, if available */
  resultJson?: Record<string, unknown>;
}

export interface CliInvocation {
  command: "validate" | "health-check" | "discover";
  args?: string[];
  configJson?: Record<string, unknown>;
  captureResultJson?: boolean;
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
export async function isCliAvailable(): Promise<boolean> {
  try {
    const cliPath = resolveCliPath();
    await execFileAsync(cliPath, ["--version"], { timeout: 10_000 });
    return true;
  } catch {
    return false;
  }
}

/**
 * Gets the installed CLI version.
 */
export async function getCliVersion(): Promise<string> {
  try {
    const cliPath = resolveCliPath();
    const { stdout } = await execFileAsync(cliPath, ["--version"], { timeout: 10_000 });
    return stdout.trim();
  } catch {
    return "not installed";
  }
}

/**
 * Runs mcpval with the given command payload and returns structured results.
 * @param invocation Command-specific invocation payload
 * @param extraEnv Additional environment variables
 */
export async function runCli(invocation: CliInvocation, extraEnv?: Record<string, string>): Promise<CliResult> {
  const cliPath = resolveCliPath();
  const tempRoot = await mkdtemp(join(tmpdir(), "mcpval-mcp-"));
  const outputDir = join(tempRoot, "artifacts");
  const configPath = invocation.configJson ? join(tempRoot, "config.json") : undefined;

  const fullArgs = [invocation.command, ...(invocation.args ?? [])];

  if (configPath && invocation.configJson) {
    await writeFile(configPath, `${JSON.stringify(invocation.configJson, null, 2)}\n`, "utf-8");
    fullArgs.push("--config", configPath);
  }

  if (invocation.captureResultJson) {
    fullArgs.push("--output", outputDir);
  }

  try {
    const { stdout, stderr } = await execFileAsync(cliPath, fullArgs, {
      timeout: config.defaultTimeoutMs,
      encoding: "utf-8",
      env: { ...process.env, ...extraEnv },
    });

    const resultJson = invocation.captureResultJson ? await loadResultJson(outputDir) : undefined;

    return { exitCode: 0, stdout, stderr, resultJson };
  } catch (error: unknown) {
    const err = error as { code?: number; stdout?: string; stderr?: string; message?: string };
    const resultJson = invocation.captureResultJson ? await loadResultJson(outputDir) : undefined;

    // Log to stderr for debugging (STDIO safe)
    console.error(`mcpval CLI execution failed: ${err.message ?? "unknown error"}`);
    return {
      exitCode: err.code ?? 1,
      stdout: err.stdout ?? "",
      stderr: err.stderr ?? (err.message ?? "CLI execution failed"),
      resultJson,
    };
  } finally {
    await rm(tempRoot, { recursive: true, force: true });
  }
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

