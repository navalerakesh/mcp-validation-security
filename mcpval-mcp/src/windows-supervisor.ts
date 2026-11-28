import { spawn, type ChildProcessWithoutNullStreams } from "node:child_process";
import { performance } from "node:perf_hooks";
import { TextDecoder } from "node:util";
import { z } from "zod";
import type { CliResult } from "./cli-runner.js";

const protocolVersion = 1;
const maximumControlCharacters = 16_384;
const maximumOutputBytes = 1_048_576;
const maximumOutputChunkBytes = 4096;
const maximumRunMs = 120_000;
const startupMs = 10_000;
const hostCleanupPhaseMs = 5000;
const cleanupMs = hostCleanupPhaseMs * 3;
const upgradeMessage = "Windows Job Object supervision is unavailable or incompatible. Upgrade the mcpval CLI selected by MCPVAL_CLI_PATH/PATH to a version supporting mcpval.windows-supervisor v1 and use a supported Windows host. No ordinary CLI fallback is permitted.";

const helloSchema = z.strictObject({
  kind: z.literal("hello"),
  protocol: z.literal("mcpval.windows-supervisor"),
  protocolVersion: z.literal(protocolVersion),
  supported: z.boolean(),
  maximumControlCharacters: z.literal(maximumControlCharacters),
  maximumOutputBytes: z.literal(maximumOutputBytes),
});
const readySchema = z.strictObject({
  kind: z.literal("ready"),
  protocolVersion: z.literal(protocolVersion),
});
const outputSchema = z.strictObject({
  kind: z.literal("output"),
  stream: z.enum(["stdout", "stderr"]),
  data: z.string().min(4).max(4 * Math.ceil(maximumOutputChunkBytes / 3)),
});
const exitSchema = z.strictObject({
  kind: z.literal("exit"),
  protocolVersion: z.literal(protocolVersion),
  exitCode: z.number().int().min(-2_147_483_648).max(2_147_483_647),
  cleanupConfirmed: z.boolean(),
  activeProcesses: z.literal(0).nullable(),
  outputComplete: z.boolean(),
  reason: z.string().min(1).max(maximumControlCharacters),
});
const frameSchema = z.discriminatedUnion("kind", [helloSchema, readySchema, outputSchema, exitSchema]);
const argumentsSchema = z.array(z.string().max(maximumControlCharacters).refine(argument => !argument.includes("\0")))
  .min(1).max(128).refine(arguments_ => ["--version", "validate", "health-check", "discover"].includes(arguments_[0]));

function parseFrame(line: string): z.infer<typeof frameSchema> {
  const parsed: unknown = JSON.parse(line);
  const keys = new Set<string>();
  for (const token of line.matchAll(/"(?:[^"\\]|\\.)*"/g)) {
    if (!/^\s*:/.test(line.slice(token.index! + token[0].length))) continue;
    const key: string = JSON.parse(token[0]);
    if (keys.has(key)) throw new Error("Duplicate supervisor field.");
    keys.add(key);
  }
  return frameSchema.parse(parsed);
}

export function startWindowsSupervisedCli(
  file: string,
  args: string[],
  env: NodeJS.ProcessEnv,
  signal?: AbortSignal,
  timeoutMs: number = maximumRunMs,
): { cancel: () => void; settled: Promise<CliResult>; cleanup: { failed: boolean } } {
  const started = performance.now();
  const cleanup = { failed: false };
  const immediate = (exitCode: number, stderr: string) => ({
    cancel: () => {}, cleanup,
    settled: Promise.resolve<CliResult>({ exitCode, stdout: "", stderr, processCleanupConfirmed: true }),
  });
  if (signal?.aborted) return immediate(124, "CLI operation cancelled before supervision started.");
  const validatedArgs = argumentsSchema.safeParse(args);
  if (!validatedArgs.success || !Number.isFinite(timeoutMs) || timeoutMs < 1 || !file || file.includes("\0"))
    return immediate(64, "Windows supervisor arguments or execution budget are invalid.");
  const budgetMs = Math.min(Math.floor(timeoutMs), maximumRunMs);
  const launchArguments = validatedArgs.data;
  if (JSON.stringify({ protocolVersion, arguments: launchArguments, timeoutMs: budgetMs }).length > maximumControlCharacters)
    return immediate(64, "Windows supervisor launch exceeds its control limit.");

  let child: ChildProcessWithoutNullStreams;
  try {
    child = spawn(file, ["__mcpval-windows-supervisor"], {
      env, shell: false, windowsHide: true, stdio: ["overlapped", "overlapped", "overlapped"],
    });
  } catch {
    return immediate(70, upgradeMessage);
  }

  let resolve!: (result: CliResult) => void;
  const settled = new Promise<CliResult>(accept => { resolve = accept; });
  const stdout = Buffer.alloc(maximumOutputBytes);
  const stderr = Buffer.alloc(maximumOutputBytes);
  let stdoutBytes = 0;
  let stderrBytes = 0;
  const pending = Buffer.alloc(maximumControlCharacters);
  let pendingBytes = 0;
  let helloReceived = false;
  let launchSent = false;
  let readyReceived = false;
  let resumeSent = false;
  let stopping = false;
  let invalid = false;
  let finished = false;
  let closed = false;
  let forcedExit: number | undefined;
  let failure: string | undefined;
  let receipt: z.infer<typeof exitSchema> | undefined;
  let startupTimer: ReturnType<typeof setTimeout> | undefined;
  let operationTimer: ReturnType<typeof setTimeout> | undefined;
  let cleanupTimer: ReturnType<typeof setTimeout> | undefined;

  const finish = (confirmed: boolean) => {
    if (finished) return;
    finished = true;
    cleanup.failed = !confirmed;
    for (const timer of [startupTimer, operationTimer, cleanupTimer]) clearTimeout(timer);
    signal?.removeEventListener("abort", cancel);
    child.stdout.removeListener("data", collect);
    child.stdin.destroy();
    child.stdout.destroy();
    child.stderr.destroy();
    if (!closed) child.unref();
    resolve({
      exitCode: confirmed ? forcedExit ?? receipt?.exitCode ?? 70 : 70,
      stdout: stdout.subarray(0, stdoutBytes).toString("utf8"),
      stderr: confirmed ? failure ?? stderr.subarray(0, stderrBytes).toString("utf8")
        : `${failure ?? "Windows supervisor did not complete."} Process cleanup is unconfirmed; retain operation artifacts and do not consume evidence files.`,
      processCleanupConfirmed: confirmed,
    });
  };
  const awaitCleanup = () => {
    if (cleanupTimer !== undefined || finished) return;
    cleanupTimer = setTimeout(() => finish(false), cleanupMs);
  };
  const stop = (exitCode: number, message: string) => {
    if (finished) return;
    if (exitCode === 70 || forcedExit === undefined) { forcedExit = exitCode; failure = message; }
    clearTimeout(startupTimer);
    clearTimeout(operationTimer);
    awaitCleanup();
    if (stopping) return;
    stopping = true;
    try { child.stdin.end(`${JSON.stringify({ kind: "cancel" })}\n`, controlWriteComplete); }
    catch { controlFailure(); }
  };
  const fail = (message: string) => {
    if (finished) return;
    invalid = true;
    stop(70, helloReceived ? message : `${upgradeMessage} ${message}`);
  };
  const controlFailure = () => fail("Windows supervisor control channel failed.");
  const controlWriteComplete = (error?: Error | null) => { if (error) controlFailure(); };
  const cancel = () => stop(124, "CLI operation cancelled.");

  const acceptFrame = (line: string) => {
    const frame = parseFrame(line);
    if (receipt) throw new Error("Frame after terminal receipt.");
    if (frame.kind === "hello") {
      if (helloReceived || !frame.supported) throw new Error("Duplicate or unsupported hello.");
      helloReceived = true;
      clearTimeout(startupTimer);
      if (stopping) return;
      const remainingMs = Math.floor(budgetMs - (performance.now() - started));
      if (remainingMs <= 0) { stop(124, "CLI execution deadline expired during preflight."); return; }
      const launch = JSON.stringify({ protocolVersion, arguments: launchArguments, timeoutMs: remainingMs });
      launchSent = true;
      try { child.stdin.write(`${launch}\n`, controlWriteComplete); }
      catch { controlFailure(); }
      return;
    }
    if (!helloReceived || (!launchSent && !stopping)) throw new Error("Frame before launch.");
    if (frame.kind === "ready") {
      if (!launchSent || readyReceived) throw new Error("Ready before launch or duplicate ready.");
      readyReceived = true;
      if (stopping) return;
      if (performance.now() - started >= budgetMs) {
        stop(124, "CLI execution deadline expired before worker admission.");
        return;
      }
      resumeSent = true;
      try { child.stdin.write(`${JSON.stringify({ kind: "resume" })}\n`, controlWriteComplete); }
      catch { controlFailure(); }
      return;
    }
    if (frame.kind === "output") {
      if (!resumeSent || !/^(?:[A-Za-z0-9+/]{4})*(?:[A-Za-z0-9+/]{2}==|[A-Za-z0-9+/]{3}=)?$/.test(frame.data))
        throw new Error("Invalid output encoding.");
      const decoded = Buffer.from(frame.data, "base64");
      if (decoded.toString("base64") !== frame.data || decoded.length > maximumOutputChunkBytes ||
          stdoutBytes + stderrBytes + decoded.length > maximumOutputBytes)
        throw new Error("Invalid or oversized output.");
      if (frame.stream === "stdout") { decoded.copy(stdout, stdoutBytes); stdoutBytes += decoded.length; }
      else { decoded.copy(stderr, stderrBytes); stderrBytes += decoded.length; }
      return;
    }
    if (!resumeSent && (frame.exitCode === 0 || frame.reason === "completed"))
      throw new Error("Worker completion requires resume admission.");
    receipt = frame;
    clearTimeout(operationTimer);
    awaitCleanup();
    if (!frame.cleanupConfirmed || frame.activeProcesses !== 0 || !frame.outputComplete)
      fail("Windows supervisor cleanup or output receipt is unconfirmed.");
  };
  const collect = (chunk: Buffer) => {
    if (finished || invalid) return;
    let offset = 0;
    try {
      while (offset < chunk.length && !invalid) {
        if (receipt) throw new Error("Bytes after terminal receipt.");
        const newline = chunk.indexOf(10, offset);
        const end = newline < 0 ? chunk.length : newline;
        const length = end - offset;
        if (pendingBytes + length > pending.length) throw new Error("Oversized supervisor frame.");
        chunk.copy(pending, pendingBytes, offset, end);
        pendingBytes += length;
        if (newline < 0) return;
        const line = new TextDecoder("utf-8", { fatal: true }).decode(pending.subarray(0, pendingBytes));
        pendingBytes = 0;
        acceptFrame(line);
        offset = newline + 1;
      }
    } catch { fail("Windows supervisor emitted malformed, out-of-order, or oversized protocol output."); }
  };

  child.stdout.on("data", collect);
  child.stdout.on("error", () => fail("Windows supervisor output stream failed."));
  child.stderr.on("data", (chunk: Buffer) => {
    if (chunk.length > 0) fail("Windows supervisor emitted unexpected diagnostic output.");
  });
  child.stderr.on("error", () => fail("Windows supervisor diagnostic stream failed."));
  child.stdin.on("error", controlFailure);
  child.stdout.once("end", () => {
    if (pendingBytes !== 0 || (launchSent && !receipt)) fail("Windows supervisor output is truncated or missing its terminal receipt.");
  });
  child.on("error", () => fail("Windows supervisor could not be started or controlled."));
  child.once("exit", () => {
    if (!launchSent && !stopping) fail("Windows supervisor exited before a compatible launch handshake.");
    awaitCleanup();
  });
  child.once("close", (code: number | null, closeSignal: NodeJS.Signals | null) => {
    if (finished) return;
    closed = true;
    if (pendingBytes !== 0) fail("Windows supervisor output ended with a truncated frame.");
    if (!launchSent) {
      if (!stopping) fail("Windows supervisor closed without a compatible launch handshake.");
      finish(true);
      return;
    }
    const confirmed = !invalid && receipt?.cleanupConfirmed === true && receipt.activeProcesses === 0 &&
      receipt.outputComplete && code !== null && !closeSignal && (code >>> 0) === (receipt.exitCode >>> 0);
    finish(confirmed);
  });
  startupTimer = setTimeout(() => fail("Windows supervisor hello deadline expired."), startupMs);
  operationTimer = setTimeout(() => stop(124, "CLI execution deadline expired."),
    Math.max(0, budgetMs - (performance.now() - started)));
  signal?.addEventListener("abort", cancel, { once: true });
  if (signal?.aborted) cancel();
  return { cancel, settled, cleanup };
}