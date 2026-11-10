import assert from "node:assert/strict";
import { EventEmitter } from "node:events";
import { readFileSync } from "node:fs";
import { PassThrough, Writable } from "node:stream";
import test from "node:test";
import { TextDecoder } from "node:util";
import { runInNewContext } from "node:vm";
import ts from "typescript";
import { z } from "zod";
import type * as Supervisor from "../src/windows-supervisor.js";

const sourceUrl = new URL("../src/windows-supervisor.ts", import.meta.url);
const compiled = ts.transpileModule(readFileSync(sourceUrl, "utf8"), {
  compilerOptions: { target: ts.ScriptTarget.ES2022, module: ts.ModuleKind.CommonJS },
  fileName: sourceUrl.pathname,
}).outputText;
const checkpoint = () => new Promise<void>(resolve => setImmediate(resolve));
const hello = {
  kind: "hello", protocol: "mcpval.windows-supervisor", protocolVersion: 1, supported: true,
  maximumControlCharacters: 16384, maximumOutputBytes: 1048576,
};
const ready = { kind: "ready", protocolVersion: 1 };
const terminal = {
  kind: "exit", protocolVersion: 1, exitCode: 0, cleanupConfirmed: true,
  activeProcesses: 0, outputComplete: true, reason: "completed",
};

class Clock {
  now = 0;
  nextId = 0;
  timers = new Map<number, { due: number; callback: () => void }>();
  setTimeout = (callback: () => void, delay: number) => {
    const timerId = ++this.nextId;
    this.timers.set(timerId, { due: this.now + delay, callback });
    return timerId;
  };
  clearTimeout = (timerId: number) => { this.timers.delete(timerId); };
  async advance(milliseconds: number) {
    const until = this.now + milliseconds;
    for (let dispatches = 0; ; dispatches++) {
      assert.ok(dispatches < 100, "Timer loop must remain bounded");
      const next = [...this.timers.entries()].sort((left, right) => left[1].due - right[1].due)[0];
      if (!next || next[1].due > until) break;
      this.now = next[1].due;
      this.timers.delete(next[0]);
      next[1].callback();
      await checkpoint();
    }
    this.now = until;
    await checkpoint();
  }
}

class FakeChild extends EventEmitter {
  stdout = new PassThrough();
  stderr = new PassThrough();
  writes: string[] = [];
  writeError: Error | undefined;
  stdin = new Writable({ write: (chunk, _encoding, callback) => {
    this.writes.push(chunk.toString());
    callback(this.writeError);
  } });
  kills = 0;
  unrefs = 0;
  kill() { this.kills++; return true; }
  unref() { this.unrefs++; }
  frame(value: unknown) { this.stdout.write(`${JSON.stringify(value)}\n`); }
  output(data: string | Buffer, stream = "stdout") {
    this.frame({ kind: "output", stream, data: Buffer.from(data).toString("base64") });
  }
  close(code: number | null = 0, signal: string | null = null) { this.emit("close", code, signal); }
}

function harness(options: { spawnThrows?: boolean; compiledSource?: string } = {}) {
  const child = new FakeChild();
  const clock = new Clock();
  const spawns: { file: string; args: string[]; options: unknown }[] = [];
  const imports: Record<string, unknown> = {
    "node:child_process": { spawn: (file: string, args: string[], spawnOptions: unknown) => {
      spawns.push({ file, args, options: spawnOptions });
      if (options.spawnThrows) throw new Error("private spawn detail");
      return child;
    } },
    "node:perf_hooks": { performance: { now: () => clock.now } },
    "node:util": { TextDecoder },
    zod: { z },
  };
  const module = { exports: {} };
  runInNewContext(options.compiledSource ?? compiled, {
    module, exports: module.exports, Buffer,
    require: (specifier: string) => {
      assert.ok(Object.hasOwn(imports, specifier), `Unexpected runtime import: ${specifier}`);
      return imports[specifier];
    },
    setTimeout: clock.setTimeout, clearTimeout: clock.clearTimeout,
  }, { filename: sourceUrl.pathname, timeout: 1000 });
  const supervisor = module.exports as typeof Supervisor;
  const start = (signal?: AbortSignal, timeoutMs?: number, args = ["validate", "--server", "https://example.test/mcp"]) =>
    supervisor.startWindowsSupervisedCli("C:\\tools\\mcpval.exe", args, { SAFE: "value" }, signal, timeoutMs);
  return { child, clock, spawns, start };
}

for (const receipt of [terminal, { ...terminal, exitCode: 7 }]) {
  test(`completed receipt without resume admission cannot authorize result consumption (${receipt.exitCode})`, async () => {
    const { child, start } = harness();
    const execution = start();
    child.frame(hello);
    child.frame(receipt);
    child.close(receipt.exitCode);
    const result = await execution.settled;
    assert.equal(result.exitCode, 70);
    assert.equal(result.processCleanupConfirmed, false);
    assert.equal(execution.cleanup.failed, true);
    assert.equal(child.kills, 0);
    assert.ok(!child.writes.some(line => JSON.parse(line).kind === "resume"));
  });
}

test("handshake precedes launch, preserves the executable/environment, and subtracts preflight time", async () => {
  const { child, clock, spawns, start } = harness();
  const execution = start(undefined, 9000);
  assert.equal(child.writes.length, 0);
  assert.deepEqual(JSON.parse(JSON.stringify(spawns)), [{
    file: "C:\\tools\\mcpval.exe", args: ["__mcpval-windows-supervisor"],
    options: { env: { SAFE: "value" }, shell: false, windowsHide: true, stdio: ["overlapped", "overlapped", "overlapped"] },
  }]);
  await clock.advance(1250);
  child.frame(hello);
  assert.deepEqual(JSON.parse(child.writes[0]), {
    protocolVersion: 1, arguments: ["validate", "--server", "https://example.test/mcp"], timeoutMs: 7750,
  });
  assert.equal(child.writes.length, 1);
  child.frame(ready);
  assert.deepEqual(child.writes.slice(1).map(line => JSON.parse(line)), [{ kind: "resume" }]);
  let settled = false;
  void execution.settled.then(() => { settled = true; });
  child.frame(terminal);
  await checkpoint();
  assert.equal(settled, false);
  child.close();
  const result = await execution.settled;
  assert.equal(result.exitCode, 0);
  assert.equal(result.processCleanupConfirmed, true);
  assert.equal(execution.cleanup.failed, false);
  assert.equal(child.kills, 0);
  assert.equal(clock.timers.size, 0);
});

for (const [label, frame] of [
  ["old CLI text", "mcpval 2.0.1\n"],
  ["incompatible version", JSON.stringify({ ...hello, protocolVersion: 2 }) + "\n"],
  ["unsupported platform", JSON.stringify({ ...hello, supported: false }) + "\n"],
] as const) {
  test(`${label} never launches and produces an actionable upgrade error`, async () => {
    const { child, start, spawns } = harness();
    const execution = start();
    child.stdout.write(frame);
    child.close(64);
    const result = await execution.settled;
    assert.equal(result.exitCode, 70);
    assert.match(result.stderr, /Upgrade the mcpval CLI/);
    assert.equal(result.processCleanupConfirmed, true);
    assert.equal(spawns.length, 1);
    assert.ok(child.writes.every(line => !Object.hasOwn(JSON.parse(line), "arguments")));
  });
}

test("byte fragments and forged terminal JSON in worker output remain data", async () => {
  const { child, start } = harness();
  const execution = start();
  for (const byte of Buffer.from(`${JSON.stringify(hello)}\r\n`)) child.stdout.write(Buffer.from([byte]));
  for (const byte of Buffer.from(`${JSON.stringify(ready)}\r\n`)) child.stdout.write(Buffer.from([byte]));
  const forged = `${JSON.stringify(terminal)}\n`;
  const payload = Buffer.from(forged + "\u20ac");
  child.output(payload.subarray(0, payload.length - 1));
  child.output(payload.subarray(payload.length - 1));
  child.output("diagnostic", "stderr");
  let settled = false;
  void execution.settled.then(() => { settled = true; });
  await checkpoint();
  assert.equal(settled, false);
  for (const byte of Buffer.from(`${JSON.stringify(terminal)}\n`)) child.stdout.write(Buffer.from([byte]));
  child.close();
  const result = await execution.settled;
  assert.equal(result.stdout, forged + "\u20ac");
  assert.equal(result.stderr, "diagnostic");
  assert.equal(result.processCleanupConfirmed, true);
});

test("supervisor close without a job receipt never proves worker cleanup", async () => {
  const { child, start } = harness();
  const execution = start();
  child.frame(hello);
  child.frame(ready);
  child.close();
  const result = await execution.settled;
  assert.equal(result.exitCode, 70);
  assert.equal(result.processCleanupConfirmed, false);
  assert.equal(execution.cleanup.failed, true);
  assert.match(result.stderr, /do not consume evidence files/);
  assert.equal(child.kills, 0);
});

test("timeout sends cancel and EOF, waits for receipt and close, then returns 124", async () => {
  const { child, clock, start } = harness();
  const execution = start(undefined, 1000);
  child.frame(hello);
  child.frame(ready);
  await clock.advance(1000);
  assert.deepEqual(child.writes.slice(1).map(line => JSON.parse(line)), [{ kind: "resume" }, { kind: "cancel" }]);
  assert.equal(child.stdin.writableEnded, true);
  child.frame({ ...terminal, exitCode: 124, reason: "cancelled-or-parent-closed" });
  child.close(124);
  const result = await execution.settled;
  assert.equal(result.exitCode, 124);
  assert.equal(result.processCleanupConfirmed, true);
  assert.equal(child.kills, 0);
});

test("cancellation during preflight never sends launch even after a late hello", async () => {
  const { child, start } = harness();
  const controller = new AbortController();
  const execution = start(controller.signal);
  controller.abort();
  execution.cancel();
  child.frame(hello);
  child.frame({ ...terminal, exitCode: 64, reason: "invalid-or-missing-launch" });
  child.close(64);
  assert.deepEqual(child.writes.map(line => JSON.parse(line)), [{ kind: "cancel" }]);
  assert.equal((await execution.settled).exitCode, 124);
  assert.equal(execution.cleanup.failed, false);
});

test("missing receipt and missing close settle at the cleanup deadline without any signal or cleanup credit", async () => {
  const { child, clock, start } = harness();
  const execution = start(undefined, 1000);
  child.frame(hello);
  await clock.advance(16_000);
  assert.equal(child.kills, 0);
  const result = await execution.settled;
  assert.equal(result.exitCode, 70);
  assert.equal(result.processCleanupConfirmed, false);
  assert.equal(execution.cleanup.failed, true);
  assert.equal(child.unrefs, 1);
  assert.equal(clock.timers.size, 0);
});

test("decoded output aggregate cap applies across both streams", async () => {
  const { child, start } = harness();
  const execution = start();
  child.frame(hello);
  child.frame(ready);
  for (let index = 0; index < 256; index++) child.output(Buffer.alloc(4096, 65), index % 2 ? "stderr" : "stdout");
  child.output("overflow");
  child.frame(terminal);
  child.close();
  const result = await execution.settled;
  assert.equal(result.exitCode, 70);
  assert.equal(result.processCleanupConfirmed, false);
  assert.equal(Buffer.byteLength(result.stdout), 524288);
  assert.equal(execution.cleanup.failed, true);
});

for (const [label, frame] of [
  ["wrong protocol name", { ...hello, protocol: "other" }],
  ["wrong control limit", { ...hello, maximumControlCharacters: 16385 }],
  ["wrong output limit", { ...hello, maximumOutputBytes: 1048577 }],
  ["unknown hello field", { ...hello, future: true }],
  ["nonboolean support", { ...hello, supported: "true" }],
  ["terminal before hello", terminal],
  ["ready before hello", ready],
  ["output before hello", { kind: "output", stream: "stdout", data: "YQ==" }],
] as const) {
  test(`${label} fails capability negotiation without sending arguments`, async () => {
    const { child, start } = harness();
    const execution = start();
    child.frame(frame);
    child.close();
    const result = await execution.settled;
    assert.equal(result.exitCode, 70);
    assert.match(result.stderr, /Upgrade the mcpval CLI/);
    assert.equal(result.processCleanupConfirmed, true);
    assert.ok(child.writes.every(line => !Object.hasOwn(JSON.parse(line), "arguments")));
  });
}

for (const [label, frame] of [
  ["duplicate hello", hello],
  ["duplicate ready", ready],
  ["ready version mismatch", { ...ready, protocolVersion: 2 }],
  ["ready unknown field", { ...ready, future: true }],
  ["ready missing version", { kind: "ready" }],
  ["ready untyped version", { ...ready, protocolVersion: "1" }],
  ["terminal version mismatch", { ...terminal, protocolVersion: 2 }],
  ["unknown terminal field", { ...terminal, future: true }],
  ["fractional exit code", { ...terminal, exitCode: 0.5 }],
  ["out-of-range exit code", { ...terminal, exitCode: 2147483648 }],
  ["nonzero active processes", { ...terminal, activeProcesses: 1 }],
  ["null active processes", { ...terminal, activeProcesses: null }],
  ["cleanup not confirmed", { ...terminal, cleanupConfirmed: false }],
  ["incomplete output", { ...terminal, outputComplete: false }],
  ["missing reason", { ...terminal, reason: undefined }],
  ["unknown output stream", { kind: "output", stream: "control", data: "YQ==" }],
  ["unknown output field", { kind: "output", stream: "stdout", data: "YQ==", extra: true }],
  ["untyped output data", { kind: "output", stream: "stdout", data: 1 }],
  ["empty output chunk", { kind: "output", stream: "stdout", data: "" }],
  ["invalid base64 alphabet", { kind: "output", stream: "stdout", data: "!!!!" }],
  ["missing base64 padding", { kind: "output", stream: "stdout", data: "YQ" }],
  ["noncanonical base64 padding bits", { kind: "output", stream: "stdout", data: "YR==" }],
  ["base64 whitespace", { kind: "output", stream: "stdout", data: "YQ==\n" }],
  ["base64url", { kind: "output", stream: "stdout", data: "____" }],
  ["oversized decoded chunk", { kind: "output", stream: "stdout", data: Buffer.alloc(4097).toString("base64") }],
  ["nested object", { kind: "output", stream: "stdout", data: { nested: true } }],
] as const) {
  test(`${label} fails closed after launch even if followed by a clean receipt`, async () => {
    const { child, start, clock } = harness();
    const execution = start();
    child.frame(hello);
    child.frame(ready);
    child.frame(frame);
    child.frame(terminal);
    child.close();
    const result = await execution.settled;
    assert.equal(result.exitCode, 70);
    assert.equal(result.processCleanupConfirmed, false);
    assert.equal(execution.cleanup.failed, true);
    assert.equal(clock.timers.size, 0);
  });
}

for (const [label, bytes] of [
  ["duplicate terminal", Buffer.from(`${JSON.stringify(terminal)}\n`)],
  ["output after terminal", Buffer.from('{"kind":"output","stream":"stdout","data":"YQ=="}\n')],
  ["partial trailing frame", Buffer.from("{")],
] as const) {
  test(`${label} invalidates terminal evidence before close`, async () => {
    const { child, start } = harness();
    const execution = start();
    child.frame(hello);
    child.frame(ready);
    child.frame(terminal);
    child.stdout.write(bytes);
    child.close();
    assert.equal((await execution.settled).processCleanupConfirmed, false);
  });
}

for (const [label, bytes] of [
  ["duplicate JSON keys", Buffer.from(JSON.stringify(terminal).replace('"exitCode":0', '"exitCode":70,"exitCode":0') + "\n")],
  ["escaped duplicate JSON keys", Buffer.from(JSON.stringify(terminal).replace('"exitCode":0', '"exitCode":70,"exit\\u0043ode":0') + "\n")],
  ["truncated terminal", Buffer.from(JSON.stringify(terminal))],
  ["invalid UTF-8", Buffer.from([0xff, 10])],
  ["empty line", Buffer.from("\n")],
  ["array frame", Buffer.from("[]\n")],
] as const) {
  test(`${label} cannot confirm cleanup`, async () => {
    const { child, start } = harness();
    const execution = start();
    child.frame(hello);
    child.frame(ready);
    child.stdout.write(bytes);
    child.close();
    const result = await execution.settled;
    assert.equal(result.exitCode, 70);
    assert.equal(result.processCleanupConfirmed, false);
  });
}

test("frames are bounded while fragmented, before a newline arrives", async () => {
  const { child, start } = harness();
  const execution = start();
  child.frame(hello);
  child.stdout.write(Buffer.alloc(16384, 32));
  assert.equal(child.writes.length, 1);
  child.stdout.write(Buffer.from(" "));
  assert.equal(child.writes.length, 2);
  assert.deepEqual(JSON.parse(child.writes[1]), { kind: "cancel" });
  child.close();
  assert.equal((await execution.settled).processCleanupConfirmed, false);
});

test("frame bounds are per frame, not per incoming buffer, and output at the cap is complete", async () => {
  const { child, start } = harness();
  const execution = start();
  child.frame(hello);
  child.frame(ready);
  const frame = JSON.stringify({ kind: "output", stream: "stdout", data: Buffer.alloc(4096, 65).toString("base64") });
  child.stdout.write((frame + "\n").repeat(256) + JSON.stringify(terminal) + "\n");
  child.close();
  const result = await execution.settled;
  assert.equal(result.exitCode, 0);
  assert.equal(result.processCleanupConfirmed, true);
  assert.equal(Buffer.byteLength(result.stdout), 1048576);
});

test("maximum-size terminal and escaped quoted text remain valid structured data", async () => {
  const { child, start } = harness();
  const execution = start();
  child.frame(hello);
  child.frame(ready);
  const quotedReason = 'completed with "exitCode":0 and \\';
  const base = { ...terminal, reason: quotedReason };
  const reason = quotedReason + "a".repeat(16384 - Buffer.byteLength(JSON.stringify(base)));
  const line = JSON.stringify({ ...terminal, reason });
  assert.equal(Buffer.byteLength(line), 16384);
  child.stdout.write(line + "\n");
  child.close();
  assert.equal((await execution.settled).processCleanupConfirmed, true);
});

for (const source of ["stdin", "stdout", "stderr", "child"] as const) {
  test(`${source} error never turns supervisor closure into worker cleanup proof`, async () => {
    const { child, start } = harness();
    const execution = start();
    child.frame(hello);
    (source === "child" ? child : child[source]).emit("error", new Error("private channel failure"));
    child.frame(terminal);
    child.close();
    const result = await execution.settled;
    assert.equal(result.exitCode, 70);
    assert.equal(result.processCleanupConfirmed, false);
    assert.doesNotMatch(result.stderr, /private channel failure/);
  });
}

test("asynchronous launch write failure is uncertain even if the buffer may have reached the host", async () => {
  const { child, start } = harness();
  const execution = start();
  child.writeError = new Error("private write failure");
  child.frame(hello);
  await checkpoint();
  child.frame(terminal);
  child.close();
  const result = await execution.settled;
  assert.equal(result.exitCode, 70);
  assert.equal(result.processCleanupConfirmed, false);
  assert.doesNotMatch(result.stderr, /private write failure/);
});

test("raw supervisor stderr is never retained or interpreted as worker output", async () => {
  const { child, start } = harness();
  const execution = start();
  child.frame(hello);
  child.stderr.write(`${JSON.stringify(terminal)} private diagnostic`);
  child.close();
  const result = await execution.settled;
  assert.equal(result.processCleanupConfirmed, false);
  assert.doesNotMatch(result.stderr, /private diagnostic/);
});

test("stdout EOF with a partial terminal fails before child close", async () => {
  const { child, start } = harness();
  const execution = start();
  child.frame(hello);
  child.stdout.end(JSON.stringify(terminal));
  await checkpoint();
  assert.equal(child.stdin.writableEnded, true);
  child.close();
  assert.equal((await execution.settled).processCleanupConfirmed, false);
});

test("exit is not close, and terminal evidence may drain after exit", async () => {
  const { child, start } = harness();
  const execution = start();
  child.frame(hello);
  child.frame(ready);
  child.emit("exit", 0);
  child.output("drained");
  child.frame(terminal);
  child.close();
  const result = await execution.settled;
  assert.equal(result.stdout, "drained");
  assert.equal(result.processCleanupConfirmed, true);
});

test("terminal receipt without supervisor close settles bounded and unconfirmed, without root kill", async () => {
  const { child, clock, start } = harness();
  const execution = start();
  child.frame(hello);
  child.frame(terminal);
  await clock.advance(20_000);
  const result = await execution.settled;
  assert.equal(result.exitCode, 70);
  assert.equal(result.processCleanupConfirmed, false);
  assert.equal(child.kills, 0);
  assert.equal(execution.cleanup.failed, true);
  child.close();
  assert.equal(execution.cleanup.failed, true);
});

test("cleanup deadline never earns cleanup credit from a late receipt or close and never signals", async () => {
  const { child, clock, start } = harness();
  const execution = start(undefined, 1000);
  child.frame(hello);
  await clock.advance(16_000);
  assert.equal(child.kills, 0);
  child.frame(terminal);
  child.close();
  const result = await execution.settled;
  assert.equal(result.processCleanupConfirmed, false);
  assert.equal(execution.cleanup.failed, true);
});

test("an unavailable signal API is never called and cleanup still settles bounded", async () => {
  const { child, clock, start } = harness();
  const execution = start(undefined, 1000);
  child.frame(hello);
  child.kill = () => { child.kills++; throw new Error("private kill failure"); };
  await clock.advance(21_000);
  assert.equal((await execution.settled).processCleanupConfirmed, false);
  assert.equal(child.kills, 0);
});

test("pre-aborted requests do not start a supervisor", async () => {
  const { start, spawns } = harness();
  const controller = new AbortController();
  controller.abort();
  const execution = start(controller.signal);
  const result = await execution.settled;
  assert.equal(result.exitCode, 124);
  assert.equal(result.processCleanupConfirmed, true);
  assert.equal(spawns.length, 0);
});

test("post-launch AbortSignal cancellation uses only the cancel protocol and EOF", async () => {
  const { child, start } = harness();
  const controller = new AbortController();
  const execution = start(controller.signal);
  child.frame(hello);
  child.frame(ready);
  controller.abort();
  execution.cancel();
  assert.equal(child.writes.length, 3);
  assert.deepEqual(JSON.parse(child.writes[1]), { kind: "resume" });
  assert.deepEqual(JSON.parse(child.writes[2]), { kind: "cancel" });
  assert.equal(child.stdin.writableEnded, true);
  child.frame({ ...terminal, exitCode: 124 });
  child.close(124);
  assert.equal((await execution.settled).exitCode, 124);
  assert.equal(child.kills, 0);
});

test("cancel without a receipt returns 70, not 124, after launch", async () => {
  const { child, start } = harness();
  const execution = start();
  child.frame(hello);
  execution.cancel();
  child.close(124);
  const result = await execution.settled;
  assert.equal(result.exitCode, 70);
  assert.equal(result.processCleanupConfirmed, false);
});

test("startup deadline is fixed despite partial hello traffic", async () => {
  const { child, clock, start } = harness();
  const execution = start();
  await clock.advance(9999);
  child.stdout.write("{");
  await clock.advance(1);
  assert.equal(child.stdin.writableEnded, true);
  child.close(64);
  const result = await execution.settled;
  assert.equal(result.exitCode, 70);
  assert.match(result.stderr, /Upgrade the mcpval CLI/);
  assert.ok(child.writes.every(line => !Object.hasOwn(JSON.parse(line), "arguments")));
});

test("whole execution deadline can expire before startup deadline without launching", async () => {
  const { child, clock, start } = harness();
  const execution = start(undefined, 1000);
  await clock.advance(1000);
  child.frame(hello);
  child.close(64);
  const result = await execution.settled;
  assert.equal(result.exitCode, 124);
  assert.equal(result.processCleanupConfirmed, true);
  assert.ok(child.writes.every(line => !Object.hasOwn(JSON.parse(line), "arguments")));
});

test("no hello and no close is bounded, actionable, and remains in failed cleanup state", async () => {
  const { child, clock, start } = harness();
  const execution = start();
  await clock.advance(30_000);
  const result = await execution.settled;
  assert.equal(result.exitCode, 70);
  assert.match(result.stderr, /Upgrade the mcpval CLI/);
  assert.equal(execution.cleanup.failed, true);
  assert.equal(child.kills, 0);
});

for (const args of [[], ["report"], ["validate", "bad\0arg"], ["validate", ...Array(128).fill("arg")], ["validate", "x".repeat(16384)]]) {
  test(`invalid launch arguments are rejected before spawn (${args.length} arguments, ${args[0] ?? "empty"})`, async () => {
    const { start, spawns } = harness();
    const result = await start(undefined, undefined, args).settled;
    assert.equal(result.exitCode, 64);
    assert.equal(result.processCleanupConfirmed, true);
    assert.equal(spawns.length, 0);
  });
}

for (const timeoutMs of [0, -1, 0.5, Number.NaN, Number.POSITIVE_INFINITY]) {
  test(`invalid execution budget ${timeoutMs} is rejected before spawn`, async () => {
    const { start, spawns } = harness();
    assert.equal((await start(undefined, timeoutMs).settled).exitCode, 64);
    assert.equal(spawns.length, 0);
  });
}

for (const command of ["--version", "validate", "health-check", "discover"]) {
  test(`${command} is allowed, the run budget is capped, and caller mutations cannot change launch`, async () => {
    const { child, start, clock } = harness();
    const args = [command, ...Array(127).fill("arg")];
    const execution = start(undefined, 999999, args);
    args[0] = "report";
    clock.now = 12.5;
    child.frame(hello);
    const launch = JSON.parse(child.writes[0]);
    assert.equal(launch.timeoutMs, 119987);
    assert.equal(launch.arguments.length, 128);
    assert.equal(launch.arguments[0], command);
    child.frame(ready);
    child.frame(terminal);
    child.close();
    assert.equal((await execution.settled).exitCode, 0);
  });
}

test("synchronous spawn failure is actionable and does not leak exception details", async () => {
  const { start } = harness({ spawnThrows: true });
  const result = await start().settled;
  assert.equal(result.exitCode, 70);
  assert.equal(result.processCleanupConfirmed, true);
  assert.match(result.stderr, /Upgrade the mcpval CLI/);
  assert.doesNotMatch(result.stderr, /private spawn detail/);
});

test("asynchronous spawn failure without close has a bounded unresolved cleanup outcome", async () => {
  const { child, start, clock } = harness();
  const execution = start();
  child.emit("error", new Error("private spawn detail"));
  await clock.advance(20_000);
  const result = await execution.settled;
  assert.equal(result.exitCode, 70);
  assert.equal(result.processCleanupConfirmed, false);
  assert.match(result.stderr, /Upgrade the mcpval CLI/);
});

for (const [code, signal] of [[null, "SIGTERM"], [70, null], [null, null]] as const) {
  test(`abnormal supervisor close (${code}, ${signal}) invalidates clean terminal evidence`, async () => {
    const { child, start } = harness();
    const execution = start();
    child.frame(hello);
    child.frame(ready);
    child.frame(terminal);
    child.close(code, signal);
    assert.equal((await execution.settled).processCleanupConfirmed, false);
  });
}

test("abnormal-close assertions reject removal of the final close guard", async () => {
  const guard = "code !== null && !closeSignal && (code >>> 0) === (receipt.exitCode >>> 0)";
  assert.ok(compiled.includes(guard), "Mutation must target the actual compiled close guard");
  const mutant = compiled.replace(guard, "true");
  for (const [code, signal] of [[null, "SIGTERM"], [70, null], [null, null]] as const) {
    const qualify = async (compiledSource: string) => {
      const { child, start } = harness({ compiledSource });
      const execution = start();
      child.frame(hello);
      child.frame(ready);
      child.frame(terminal);
      child.close(code, signal);
      assert.equal((await execution.settled).processCleanupConfirmed, false);
    };
    await qualify(compiled);
    await assert.rejects(qualify(mutant), assert.AssertionError);
  }
});

test("nonzero worker exit is preserved with receipt plus matching supervisor close", async () => {
  const { child, start } = harness();
  const execution = start();
  child.frame(hello);
  child.frame(ready);
  child.frame({ ...terminal, exitCode: 2 });
  child.close(2);
  const result = await execution.settled;
  assert.equal(result.exitCode, 2);
  assert.equal(result.processCleanupConfirmed, true);
});

for (const stopping of [false, true]) {
  test(`output before admission fails closed (stopping: ${stopping})`, async () => {
    const { child, start } = harness();
    const execution = start();
    child.frame(hello);
    if (stopping) {
      execution.cancel();
      child.frame(ready);
    }
    child.output("unadmitted");
    child.frame(terminal);
    child.close();
    const result = await execution.settled;
    assert.equal(result.exitCode, 70);
    assert.equal(result.stdout, "");
    assert.equal(result.processCleanupConfirmed, false);
    assert.equal(child.writes.some(line => JSON.parse(line).kind === "resume"), false);
    assert.equal(child.kills, 0);
  });
}

for (const action of ["cancel", "timeout", "elapsed-before-timer"] as const) {
  test(`ready after ${action} never resumes and preserves a matching cancellation receipt`, async () => {
    const { child, clock, start } = harness();
    const execution = start(undefined, 1000);
    child.frame(hello);
    if (action === "cancel") execution.cancel();
    else if (action === "timeout") await clock.advance(1000);
    else clock.now = 1000;
    child.frame(ready);
    assert.deepEqual(child.writes.slice(1).map(line => JSON.parse(line)), [{ kind: "cancel" }]);
    assert.equal(child.stdin.writableEnded, true);
    child.frame({ ...terminal, exitCode: 124, reason: "cancelled-before-resume" });
    child.close(124);
    const result = await execution.settled;
    assert.equal(result.exitCode, 124);
    assert.equal(result.processCleanupConfirmed, true);
    assert.equal(child.kills, 0);
  });
}

test("ready just within budget resumes without extending the original deadline", async () => {
  const { child, clock, start } = harness();
  const execution = start(undefined, 1000);
  child.frame(hello);
  await clock.advance(999);
  child.frame(ready);
  assert.deepEqual(JSON.parse(child.writes[1]), { kind: "resume" });
  await clock.advance(1);
  assert.deepEqual(JSON.parse(child.writes[2]), { kind: "cancel" });
  child.frame({ ...terminal, exitCode: 124 });
  child.close(124);
  assert.equal((await execution.settled).exitCode, 124);
  assert.equal(child.kills, 0);
});

for (const frame of [
  '{"kind":"ready","protocolVersion":1,"protocolVersion":1}',
  '{"kind":"ready","protocolVersion":1,"protocol\\u0056ersion":1}',
  '{"kind":"ready","protocolVersion":2}',
  '{"kind":"ready","protocolVersion":1,"extra":true}',
  '{"kind":"ready"}',
  '{"kind":"ready","protocolVersion":null}',
]) {
  test(`invalid first ready never authorizes resume: ${frame}`, async () => {
    const { child, start } = harness();
    const execution = start();
    child.frame(hello);
    child.stdout.write(frame + "\n");
    child.frame(ready);
    child.frame(terminal);
    child.close();
    assert.equal((await execution.settled).processCleanupConfirmed, false);
    assert.equal(child.writes.some(line => JSON.parse(line).kind === "resume"), false);
    assert.equal(child.kills, 0);
  });
}

test("duplicate ready while stopping still invalidates cleanup proof", async () => {
  const { child, start } = harness();
  const execution = start();
  child.frame(hello);
  execution.cancel();
  child.frame(ready);
  child.frame(ready);
  child.frame({ ...terminal, exitCode: 124 });
  child.close(124);
  assert.equal((await execution.settled).processCleanupConfirmed, false);
  assert.deepEqual(child.writes.slice(1).map(line => JSON.parse(line)), [{ kind: "cancel" }]);
  assert.equal(child.kills, 0);
});

test("ready after a cancelled preflight cannot substitute for launch", async () => {
  const { child, start } = harness();
  const execution = start();
  execution.cancel();
  child.frame(hello);
  child.frame(ready);
  child.close(64);
  assert.equal((await execution.settled).exitCode, 70);
  assert.deepEqual(child.writes.map(line => JSON.parse(line)), [{ kind: "cancel" }]);
  assert.equal(child.kills, 0);
});

for (const exitCode of [64, 70, 124]) {
  test(`terminal before ready preserves startup failure or cancellation ${exitCode}`, async () => {
    const { child, start } = harness();
    const execution = start();
    child.frame(hello);
    child.frame({ ...terminal, exitCode, reason: "startup-failed-or-cancelled" });
    child.close(exitCode);
    const result = await execution.settled;
    assert.equal(result.exitCode, exitCode);
    assert.equal(result.processCleanupConfirmed, true);
    assert.equal(child.writes.length, 1);
    assert.equal(child.kills, 0);
  });
}

for (const synchronous of [false, true]) {
  test(`resume write failure is unconfirmed (synchronous: ${synchronous})`, async () => {
    const { child, start } = harness();
    const execution = start();
    child.frame(hello);
    await checkpoint();
    if (synchronous) child.stdin.write = () => { throw new Error("private resume failure"); };
    else child.writeError = new Error("private resume failure");
    child.frame(ready);
    await checkpoint();
    child.output("not evidence");
    child.frame(terminal);
    child.close();
    const result = await execution.settled;
    assert.equal(result.exitCode, 70);
    assert.equal(result.processCleanupConfirmed, false);
    assert.equal(result.stdout, "");
    assert.doesNotMatch(result.stderr, /private resume failure/);
    assert.equal(child.kills, 0);
  });
}

test("coalesced ready and output are admitted in order with exactly one resume", async () => {
  const { child, start } = harness();
  const execution = start();
  child.frame(hello);
  child.stdout.write(JSON.stringify(ready) + '\n{"kind":"output","stream":"stdout","data":"YQ=="}\n');
  child.frame(terminal);
  child.close();
  const result = await execution.settled;
  assert.equal(result.stdout, "a");
  assert.equal(result.processCleanupConfirmed, true);
  assert.deepEqual(child.writes.slice(1).map(line => JSON.parse(line)), [{ kind: "resume" }]);
});

test("ready after terminal invalidates the receipt without resuming", async () => {
  const { child, start } = harness();
  const execution = start();
  child.frame(hello);
  child.frame({ ...terminal, exitCode: 70 });
  child.frame(ready);
  child.close(70);
  assert.equal((await execution.settled).processCleanupConfirmed, false);
  assert.equal(child.writes.some(line => JSON.parse(line).kind === "resume"), false);
  assert.equal(child.kills, 0);
});