import assert from "node:assert/strict";
import { EventEmitter } from "node:events";
import { readFileSync } from "node:fs";
import { join } from "node:path";
import test from "node:test";
import { TextDecoder } from "node:util";
import { runInNewContext } from "node:vm";
import ts from "typescript";
import { z } from "zod";
import { config } from "../src/config.js";
import type * as CliRunner from "../src/cli-runner.js";
import type * as Handlers from "../src/handlers.js";
import type * as WindowsSupervisor from "../src/windows-supervisor.js";

const checkpoint = () => new Promise<void>(resolve => setImmediate(resolve));

function deferred<Value>() {
  let resolve!: (value: Value) => void;
  let reject!: (error: unknown) => void;
  const promise = new Promise<Value>((accept, fail) => { resolve = accept; reject = fail; });
  return { promise, resolve, reject };
}

function observe<Value>(promise: Promise<Value>) {
  const state: { status: "pending" | "fulfilled" | "rejected"; value?: Value; error?: unknown } = { status: "pending" };
  const done = promise.then(
    value => { state.status = "fulfilled"; state.value = value; },
    error => { state.status = "rejected"; state.error = error; },
  );
  return { state, done };
}

const compiled = new Map<string, string>();

function loadActual<Exports>(name: string, imports: Record<string, unknown>, globals: Record<string, unknown>): Exports {
  const sourceUrl = new URL(`../src/${name}.ts`, import.meta.url);
  let output = compiled.get(name);
  if (!output) {
    output = ts.transpileModule(readFileSync(sourceUrl, "utf8"), {
      compilerOptions: { target: ts.ScriptTarget.ES2022, module: ts.ModuleKind.CommonJS },
      fileName: sourceUrl.pathname,
    }).outputText;
    compiled.set(name, output);
  }
  const module = { exports: {} };
  runInNewContext(output, {
    module, exports: module.exports, Buffer,
    console: { error: () => {} },
    require: (specifier: string) => {
      assert.ok(Object.hasOwn(imports, specifier), `Unexpected VM import: ${specifier}`);
      return imports[specifier];
    },
    ...globals,
  }, { filename: sourceUrl.pathname, timeout: 1_000 });
  return module.exports as Exports;
}

class Scheduler {
  now = 0;
  nextId = 0;
  pending = new Map<number, { due: number; delay: number; callback: () => void }>();
  dispatched: number[] = [];

  setTimeout = (callback: () => void, delay: number) => {
    const timerId = ++this.nextId;
    this.pending.set(timerId, { due: this.now + delay, delay, callback });
    return timerId;
  };

  clearTimeout = (timerId: number) => { this.pending.delete(timerId); };

  async advance(milliseconds: number) {
    const until = this.now + milliseconds;
    const dispatchBound = this.pending.size * (3 + Math.ceil(
      Math.min(milliseconds, config.processCleanupTimeoutMs) / config.processExitPollMs,
    ));
    let dispatches = 0;
    for (;;) {
      const next = [...this.pending.entries()].sort((left, right) => left[1].due - right[1].due)[0];
      if (!next || next[1].due > until) break;
      assert.ok(++dispatches <= dispatchBound, "Timer dispatch exceeded the configured cleanup polling budget");
      const [timerId, timer] = next;
      this.now = timer.due;
      this.pending.delete(timerId);
      this.dispatched.push(timer.delay);
      timer.callback();
      await checkpoint();
    }
    this.now = until;
    await checkpoint();
  }
}

class OutputStream extends EventEmitter {
  destroyed = false;
  destroyCount = 0;
  destroy() { this.destroyed = true; this.destroyCount++; }
}

class ControlStream extends OutputStream {
  writes: string[] = [];
  writeError: Error | undefined;
  writableEnded = false;
  write(data: string, callback: (error?: Error) => void) {
    this.writes.push(data);
    callback(this.writeError);
    return !this.writeError;
  }
  end(data: string, callback: (error?: Error) => void) {
    this.writableEnded = true;
    this.write(data, callback);
  }
}

class OwnedChild extends EventEmitter {
  stdin = new ControlStream();
  stdout = new OutputStream();
  stderr = new OutputStream();
  exitCode: number | null = null;
  signalCode: string | null = null;
  unrefCount = 0;
  killCount = 0;
  constructor(readonly pid: number) { super(); }
  kill() { this.killCount++; return true; }
  unref() { this.unrefCount++; }
  exit(code = 0) { this.exitCode = code; this.emit("exit", code); }
  close(code = 0) { this.exitCode = code; this.emit("close", code); }
  frame(value: unknown) { this.stdout.emit("data", Buffer.from(`${JSON.stringify(value)}\n`)); }
}

const supervisorHello = {
  kind: "hello", protocol: "mcpval.windows-supervisor", protocolVersion: 1, supported: true,
  maximumControlCharacters: 16384, maximumOutputBytes: 1048576,
};
const supervisorTerminal = {
  kind: "exit", protocolVersion: 1, exitCode: 0, cleanupConfirmed: true,
  activeProcesses: 0, outputComplete: true, reason: "completed",
};

function runnerHarness(options: {
  platform?: "linux" | "win32";
  terminationError?: Error;
  remove?: (root: string) => Promise<void>;
} = {}) {
  const scheduler = new Scheduler();
  const children: OwnedChild[] = [];
  const directories = new Set<string>();
  const created: string[] = [];
  const removed: string[] = [];
  const signals: { pid: number; signal: string }[] = [];
  const groups = new Set<number>();
  const probes: number[] = [];
  const supervisorSpawns: { file: string; args: string[] }[] = [];
  let artifactReads = 0;
  const timers = { setTimeout: scheduler.setTimeout, clearTimeout: scheduler.clearTimeout };
  const windowsSupervisor = loadActual<typeof WindowsSupervisor>("windows-supervisor", {
    "node:child_process": {
      spawn: (file: string, args: string[]) => {
        assert.equal(options.platform, "win32");
        supervisorSpawns.push({ file, args: [...args] });
        const child = new OwnedChild(4000 + children.length);
        children.push(child);
        return child;
      },
    },
    "node:perf_hooks": { performance: { now: () => scheduler.now } },
    "node:util": { TextDecoder },
    zod: { z },
  }, timers);
  const runner = loadActual<typeof CliRunner>("cli-runner", {
    "./config.js": { config },
    "./windows-supervisor.js": windowsSupervisor,
    "node:path": { join },
    "node:os": { tmpdir: () => "/virtual" },
    "node:fs": { existsSync: () => false },
    "node:fs/promises": {
      mkdtemp: async (prefix: string) => {
        const root = `${prefix}${created.length + 1}`;
        created.push(root);
        directories.add(root);
        return root;
      },
      rm: async (root: string) => {
        removed.push(root);
        await options.remove?.(root);
        directories.delete(root);
      },
      writeFile: async () => {},
      readdir: async () => { artifactReads++; return []; },
      readFile: async () => { assert.fail("Unexpected artifact read"); },
    },
    "node:child_process": {
      spawn: () => {
        assert.notEqual(options.platform, "win32", "Windows must not fall back to ordinary CLI spawn");
        const child = new OwnedChild(4000 + children.length);
        children.push(child);
        groups.add(child.pid);
        return child;
      },
    },
  }, {
    process: {
      env: {}, platform: options.platform ?? "linux",
      kill: (pid: number, signal: string | 0) => {
        assert.ok(pid < 0, "The OS mock expects a process-group operation");
        if (signal === 0) probes.push(pid);
        else signals.push({ pid, signal });
        if (options.terminationError) throw options.terminationError;
        if (!groups.has(-pid)) throw Object.assign(new Error("No such process group"), { code: "ESRCH" });
        return true;
      },
    },
    ...timers,
  });
  return { runner, scheduler, children, directories, created, removed, signals, groups, probes, supervisorSpawns, artifactReads: () => artifactReads };
}

const cleanupError = /Owned CLI process or artifact cleanup failed after all cleanup tasks drained/;
const artifactCleanupError = /Owned CLI artifact cleanup failed; artifacts are retained and further launches are blocked/;

test("shutdown drains other owned execution and invocation cleanup after rm rejects", { timeout: 5_000 }, async () => {
  const removalFailure = new Error("injected rm failure");
  const secondRemoval = deferred<void>();
  const harness = runnerHarness({ remove: async root => {
    if (root === harness.created[0]) throw removalFailure;
    await secondRemoval.promise;
  } });
  const first = observe(harness.runner.runCli({ command: "validate" }));
  const second = observe(harness.runner.runCli({ command: "discover" }));
  await checkpoint();
  assert.equal(harness.children.length, 2);
  const shutdown = observe(harness.runner.shutdownCliProcesses());
  harness.children[0].close();
  harness.groups.clear();
  await harness.scheduler.advance(config.processTerminationGraceMs);
  assert.deepEqual(harness.signals, [
    { pid: -4000, signal: "SIGTERM" }, { pid: -4001, signal: "SIGTERM" },
    { pid: -4000, signal: "SIGKILL" }, { pid: -4001, signal: "SIGKILL" },
  ]);
  assert.deepEqual(harness.probes, [-4000, -4001]);
  assert.equal(first.state.status, "rejected");
  assert.match(String(first.state.error), artifactCleanupError);
  assert.equal(second.state.status, "pending");
  assert.equal(shutdown.state.status, "pending", "rm rejection must not short-circuit shutdown");

  harness.children[1].close();
  await checkpoint();
  assert.deepEqual(harness.removed, harness.created);
  assert.equal(second.state.status, "pending");
  assert.equal(shutdown.state.status, "pending", "child close alone does not drain invocation cleanup");
  secondRemoval.resolve();
  await Promise.all([first.done, second.done, shutdown.done]);
  assert.equal(second.state.value?.processCleanupConfirmed, true);
  assert.equal(second.state.value?.exitCode, 124);
  assert.equal(shutdown.state.status, "rejected");
  assert.match(String(shutdown.state.error), cleanupError);
  assert.deepEqual([...harness.directories], [harness.created[0]]);
  assert.deepEqual(harness.scheduler.dispatched, [config.processTerminationGraceMs, config.processTerminationGraceMs]);
  assert.equal(harness.scheduler.pending.size, 0);
});

for (const platform of ["linux", "win32"] as const) {
  test(`${platform} ${platform === "win32" ? "supervisor cancel-write" : "termination"} failure without close retains ownership and blocks launches`, { timeout: 5_000 }, async () => {
    const harness = runnerHarness({ platform, terminationError: Object.assign(new Error("injected termination failure"), { code: "EPERM" }) });
    const operation = observe(harness.runner.runCli({ command: "validate", captureResultJson: true }));
    await checkpoint();
    assert.equal(harness.children.length, 1);
    if (platform === "win32") {
      const child = harness.children[0];
      assert.deepEqual(child.stdin.writes, [], "No worker launch before the versioned handshake");
      child.frame(supervisorHello);
      assert.deepEqual(JSON.parse(child.stdin.writes[0]), {
        protocolVersion: 1, arguments: ["validate", "--output", join(harness.created[0], "artifacts")],
        timeoutMs: config.defaultTimeoutMs,
      });
      child.stdin.writeError = new Error("injected supervisor cancel-write failure");
    }
    await harness.scheduler.advance(config.defaultTimeoutMs);
    if (platform === "win32") {
      assert.equal(operation.state.status, "pending", "Cancel failure cannot immediately prove cleanup");
      assert.deepEqual(harness.removed, []);
      await harness.scheduler.advance(15_000);
      assert.equal(harness.children[0].killCount, 0, "Windows supervision must never fall back to process signals");
    }
    await operation.done;
    assert.equal(operation.state.status, "fulfilled");
    assert.equal(operation.state.value?.exitCode, 70);
    assert.equal(operation.state.value?.processCleanupConfirmed, false);
    assert.equal(operation.state.value?.resultJson, undefined);
    assert.deepEqual([...harness.directories], harness.created);
    assert.deepEqual(harness.removed, []);
    assert.equal(harness.artifactReads(), 0);
    assert.equal(harness.children[0].unrefCount, 1);
    assert.equal(harness.children[0].stdout.destroyed, true);
    assert.equal(harness.children[0].stderr.destroyed, true);
    assert.equal(harness.children[0].exitCode, null);
    if (platform === "win32") {
      assert.deepEqual(harness.supervisorSpawns, [{ file: config.cliCommand, args: ["__mcpval-windows-supervisor"] }]);
      assert.deepEqual(harness.children[0].stdin.writes.slice(1).map(line => JSON.parse(line)), [{ kind: "cancel" }]);
      assert.equal(harness.children[0].stdin.writableEnded, true);
      assert.equal(harness.children[0].stdin.destroyed, true);
      assert.equal(harness.signals.length, 0);
      assert.equal(harness.probes.length, 0);
    } else {
      assert.deepEqual(harness.signals, [{ pid: -4000, signal: "SIGTERM" }]);
      assert.equal(harness.supervisorSpawns.length, 0);
    }
    const blocked = await harness.runner.runCli({ command: "discover" });
    assert.equal(blocked.exitCode, 70);
    assert.equal(blocked.processCleanupConfirmed, false);
    assert.equal((await harness.runner.getCliAvailability()).exitCode, 70);
    assert.equal(harness.children.length, 1);
    assert.equal(harness.created.length, 1);
    await assert.rejects(harness.runner.shutdownCliProcesses(), cleanupError);
    await assert.rejects(harness.runner.shutdownCliProcesses(), cleanupError);
    assert.deepEqual(harness.scheduler.dispatched, platform === "win32"
      ? [config.defaultTimeoutMs, 15_000] : [config.defaultTimeoutMs]);
    assert.equal(harness.scheduler.pending.size, 0);
  });
}

test("successful signals without close reach the cleanup deadline", { timeout: 5_000 }, async () => {
  const harness = runnerHarness();
  const operation = observe(harness.runner.runCli({ command: "validate", captureResultJson: true }));
  await checkpoint();
  assert.equal(harness.children.length, 1);
  const shutdown = observe(harness.runner.shutdownCliProcesses());
  harness.groups.delete(harness.children[0].pid);
  await harness.scheduler.advance(config.processTerminationGraceMs);
  assert.deepEqual(harness.signals, [{ pid: -4000, signal: "SIGTERM" }, { pid: -4000, signal: "SIGKILL" }]);
  assert.deepEqual(harness.probes, [-4000]);
  assert.equal(operation.state.status, "pending");
  assert.equal(shutdown.state.status, "pending");
  await harness.scheduler.advance(config.processCleanupTimeoutMs - config.processTerminationGraceMs - 1);
  assert.equal(operation.state.status, "pending");
  assert.equal(shutdown.state.status, "pending");
  await harness.scheduler.advance(1);
  await Promise.all([operation.done, shutdown.done]);
  assert.equal(operation.state.value?.exitCode, 70);
  assert.equal(operation.state.value?.processCleanupConfirmed, false);
  assert.equal(shutdown.state.status, "rejected");
  assert.match(String(shutdown.state.error), cleanupError);
  assert.deepEqual([...harness.directories], harness.created);
  assert.deepEqual(harness.removed, []);
  assert.equal(harness.artifactReads(), 0);
  assert.equal(harness.children[0].exitCode, null);
  assert.deepEqual(harness.scheduler.dispatched, [config.processTerminationGraceMs, config.processCleanupTimeoutMs]);
  assert.equal(harness.scheduler.pending.size, 0);
});

test("leader close and accepted signals with a surviving group settle unconfirmed at the deadline", { timeout: 5_000 }, async () => {
  const harness = runnerHarness();
  const operation = observe(harness.runner.runCli({ command: "validate", captureResultJson: true }));
  await checkpoint();
  const child = harness.children[0];
  child.exit();
  child.close();
  await harness.scheduler.advance(config.processTerminationGraceMs);
  assert.deepEqual(harness.signals, [{ pid: -child.pid, signal: "SIGTERM" }, { pid: -child.pid, signal: "SIGKILL" }]);
  assert.deepEqual(harness.probes, [-child.pid]);
  assert.equal(harness.groups.has(child.pid), true, "Accepted signals and leader close do not remove the group");
  await harness.scheduler.advance(config.processCleanupTimeoutMs - config.processTerminationGraceMs - 1);
  assert.equal(operation.state.status, "pending");
  assert.deepEqual(harness.removed, []);
  await harness.scheduler.advance(1);
  assert.equal(operation.state.status, "fulfilled", "The cleanup deadline must settle the invocation");
  await operation.done;
  assert.equal(operation.state.value?.exitCode, 70);
  assert.equal(operation.state.value?.processCleanupConfirmed, false);
  assert.equal(operation.state.value?.resultJson, undefined);
  assert.equal(harness.artifactReads(), 0);
  assert.deepEqual([...harness.directories], harness.created);
  assert.deepEqual(harness.removed, []);
  const blocked = await harness.runner.runCli({ command: "discover" });
  assert.equal(blocked.exitCode, 70);
  assert.equal(blocked.processCleanupConfirmed, false);
  assert.equal((await harness.runner.getCliAvailability()).exitCode, 70);
  assert.equal(harness.children.length, 1);
  assert.equal(harness.created.length, 1);
  await assert.rejects(harness.runner.shutdownCliProcesses(), cleanupError);
  await assert.rejects(harness.runner.shutdownCliProcesses(), cleanupError);
  const pendingAtDeadline = harness.scheduler.pending.size;
  const probesAtDeadline = harness.probes.length;
  await harness.scheduler.advance(config.processExitPollMs);
  assert.deepEqual({
    pendingAtDeadline,
    pendingAfterDeadline: harness.scheduler.pending.size,
    probesAfterDeadline: harness.probes.length - probesAtDeadline,
    unrefs: child.unrefCount,
    stdoutDestroys: child.stdout.destroyCount,
    stderrDestroys: child.stderr.destroyCount,
  }, {
    pendingAtDeadline: 0,
    pendingAfterDeadline: 0,
    probesAfterDeadline: 0,
    unrefs: 1,
    stdoutDestroys: 1,
    stderrDestroys: 1,
  }, "Deadline settlement must stop polling and run terminal cleanup exactly once");
});

test("observed group absence after SIGKILL plus child close confirms cleanup", { timeout: 5_000 }, async () => {
  const harness = runnerHarness();
  const operation = observe(harness.runner.runCli({ command: "validate", captureResultJson: true }));
  await checkpoint();
  const child = harness.children[0];
  child.exit();
  child.close();
  await harness.scheduler.advance(config.processTerminationGraceMs + config.processExitPollMs);
  assert.equal(operation.state.status, "pending", "Leader close and signal delivery are insufficient");
  assert.deepEqual(harness.signals, [{ pid: -child.pid, signal: "SIGTERM" }, { pid: -child.pid, signal: "SIGKILL" }]);
  assert.deepEqual(harness.probes, [-child.pid, -child.pid]);
  assert.deepEqual(harness.removed, []);
  assert.equal(harness.artifactReads(), 0);
  harness.groups.delete(child.pid);
  await harness.scheduler.advance(config.processExitPollMs);
  assert.equal(operation.state.status, "fulfilled");
  await operation.done;
  assert.equal(operation.state.value?.exitCode, 0);
  assert.equal(operation.state.value?.processCleanupConfirmed, true);
  assert.deepEqual(harness.probes, [-child.pid, -child.pid, -child.pid]);
  assert.deepEqual(harness.removed, harness.created);
  assert.equal(harness.directories.size, 0);
  assert.equal(harness.artifactReads(), 1);
  assert.equal(child.unrefCount, 0);
  assert.equal(harness.scheduler.pending.size, 0);
  await harness.runner.shutdownCliProcesses();
});

test("rm failure completed before shutdown retains artifact ownership and blocks launches", { timeout: 5_000 }, async () => {
  const harness = runnerHarness({ remove: async () => { throw new Error("injected rm failure"); } });
  const operation = observe(harness.runner.runCli({ command: "validate" }));
  await checkpoint();
  harness.children[0].close();
  harness.groups.delete(harness.children[0].pid);
  await harness.scheduler.advance(config.processTerminationGraceMs);
  assert.equal(operation.state.status, "rejected");
  await operation.done;
  assert.match(String(operation.state.error), artifactCleanupError);
  assert.deepEqual(harness.probes, [-4000]);
  assert.deepEqual(harness.removed, harness.created);
  assert.deepEqual([...harness.directories], harness.created);
  const blocked = await harness.runner.runCli({ command: "discover" });
  assert.equal(blocked.exitCode, 70);
  assert.equal(blocked.processCleanupConfirmed, false);
  assert.equal((await harness.runner.getCliAvailability()).exitCode, 70);
  assert.equal(harness.children.length, 1);
  assert.equal(harness.created.length, 1);
  await assert.rejects(harness.runner.shutdownCliProcesses(), cleanupError);
  await assert.rejects(harness.runner.shutdownCliProcesses(), cleanupError);
  assert.deepEqual([...harness.directories], harness.created);
  assert.deepEqual(harness.removed, harness.created);
  assert.equal(harness.scheduler.pending.size, 0);
});

for (const receipt of ["missing", "unconfirmed"] as const) {
  test(`Windows supervisor close with ${receipt} v1 terminal receipt retains ownership and blocks launches`, { timeout: 5_000 }, async () => {
    const harness = runnerHarness({ platform: "win32" });
    const operation = observe(harness.runner.runCli({ command: "validate", captureResultJson: true }));
    await checkpoint();
    const child = harness.children[0];
    child.frame(supervisorHello);
    assert.equal(JSON.parse(child.stdin.writes[0]).protocolVersion, 1);
    assert.equal(JSON.parse(child.stdin.writes[0]).arguments[0], "validate");
    if (receipt === "unconfirmed") child.frame({
      ...supervisorTerminal, cleanupConfirmed: false, activeProcesses: null, reason: "cleanup-unconfirmed",
    });
    child.exit();
    await checkpoint();
    assert.equal(operation.state.status, "pending", "Supervisor exit alone cannot settle cleanup");
    assert.deepEqual(harness.removed, []);
    child.close();
    await checkpoint();
    assert.equal(operation.state.status, "fulfilled");
    await operation.done;
    assert.equal(operation.state.value?.exitCode, 70);
    assert.equal(operation.state.value?.processCleanupConfirmed, false);
    assert.equal(operation.state.value?.resultJson, undefined);
    assert.equal(child.exitCode, 0);
    assert.equal(child.unrefCount, 0, "An already closed supervisor needs no unref");
    assert.equal(child.killCount, 0);
    assert.equal(child.stdin.destroyed, true);
    assert.equal(child.stdout.destroyed, true);
    assert.equal(child.stderr.destroyed, true);
    assert.deepEqual(harness.supervisorSpawns, [{ file: config.cliCommand, args: ["__mcpval-windows-supervisor"] }]);
    assert.deepEqual(harness.signals, []);
    assert.deepEqual(harness.probes, []);
    assert.equal(harness.artifactReads(), 0);
    assert.deepEqual(harness.removed, []);
    assert.deepEqual([...harness.directories], harness.created);
    const blocked = await harness.runner.runCli({ command: "discover" });
    assert.equal(blocked.exitCode, 70);
    assert.equal(blocked.processCleanupConfirmed, false);
    assert.equal((await harness.runner.getCliAvailability()).exitCode, 70);
    assert.equal(harness.children.length, 1);
    assert.equal(harness.created.length, 1);
    await assert.rejects(harness.runner.shutdownCliProcesses(), cleanupError);
    await assert.rejects(harness.runner.shutdownCliProcesses(), cleanupError);
    assert.equal(harness.scheduler.pending.size, 0);
  });
}

test("Windows v1 confirmed receipt plus close releases artifacts and permits later launches", { timeout: 5_000 }, async () => {
  const harness = runnerHarness({ platform: "win32" });
  const operation = observe(harness.runner.runCli({ command: "validate", captureResultJson: true }));
  await checkpoint();
  const child = harness.children[0];
  child.frame(supervisorHello);
  child.frame({ kind: "ready", protocolVersion: 1 });
  child.frame(supervisorTerminal);
  await checkpoint();
  assert.equal(operation.state.status, "pending", "A receipt without supervisor close is insufficient");
  assert.equal(harness.artifactReads(), 0);
  assert.deepEqual(harness.removed, []);
  child.close();
  await operation.done;
  assert.equal(operation.state.value?.exitCode, 0);
  assert.equal(operation.state.value?.processCleanupConfirmed, true);
  assert.equal(harness.artifactReads(), 1);
  assert.deepEqual(harness.removed, harness.created);
  assert.equal(harness.directories.size, 0);

  const availability = observe(harness.runner.getCliAvailability());
  assert.equal(harness.children.length, 2);
  const nextChild = harness.children[1];
  nextChild.frame(supervisorHello);
  assert.deepEqual(JSON.parse(nextChild.stdin.writes[0]), {
    protocolVersion: 1, arguments: ["--version"], timeoutMs: config.cliVersionTimeoutMs,
  });
  const shutdown = observe(harness.runner.shutdownCliProcesses());
  await checkpoint();
  assert.equal(shutdown.state.status, "pending");
  assert.equal(child.stdin.writes.length, 2, "Shutdown must not send cancellation to a completed supervisor");
  assert.deepEqual(nextChild.stdin.writes.slice(1).map(line => JSON.parse(line)), [{ kind: "cancel" }]);
  nextChild.frame({ ...supervisorTerminal, exitCode: 124, reason: "cancelled-or-parent-closed" });
  nextChild.close(124);
  await Promise.all([availability.done, shutdown.done]);
  assert.equal(availability.state.value?.exitCode, 124);
  assert.equal(availability.state.value?.processCleanupConfirmed, true);
  assert.equal(shutdown.state.status, "fulfilled");
  assert.deepEqual(harness.signals, []);
  assert.deepEqual(harness.probes, []);
  assert.ok(harness.children.every(owned => owned.killCount === 0 && owned.unrefCount === 0));
  assert.equal(harness.scheduler.pending.size, 0);
});

for (const cleanupFails of [false, true]) {
  test(`repeated wrapper signals await one cleanup (${cleanupFails ? "failure" : "success"})`, { timeout: 5_000 }, async () => {
    const cleanup = deferred<void>();
    const serverClose = deferred<void>();
    const wrapperProcess = Object.assign(new EventEmitter(), { exit: (code: number) => { exits.push(code); events.push(`exit:${code}`); } });
    const exits: number[] = [];
    const events: string[] = [];
    let connects = 0;
    let shutdownCalls = 0;
    let closes = 0;
    class Server {
      registerTool() {}
      registerResource() {}
      registerPrompt() {}
      async connect() { connects++; }
      async close() { closes++; events.push("close"); await serverClose.promise; }
    }
    loadActual("index", {
      "@modelcontextprotocol/sdk/server/mcp.js": { McpServer: Server },
      zod: { z },
      "./config.js": { config },
      "./tools.js": { ValidateInputSchema: { shape: {} }, HealthCheckInputSchema: { shape: {} }, DiscoverInputSchema: { shape: {} } },
      "./handlers.js": {},
      "./cli-runner.js": {
        getCliVersion: async () => "test-version",
        shutdownCliProcesses: () => { shutdownCalls++; events.push("cleanup"); return cleanup.promise; },
      },
      "./strict-stdio-server-transport.js": { StrictStdioServerTransport: class {} },
    }, { process: wrapperProcess });
    await checkpoint();
    assert.equal(connects, 1);
    assert.equal(wrapperProcess.listenerCount("SIGTERM"), 1);
    assert.equal(wrapperProcess.listenerCount("SIGINT"), 1);
    for (let dispatch = 0; dispatch < 3; dispatch++) {
      assert.equal(wrapperProcess.emit("SIGTERM"), true);
      await checkpoint();
      assert.equal(wrapperProcess.listenerCount("SIGTERM"), 1);
      assert.equal(shutdownCalls, 1);
      assert.equal(closes, 0);
      assert.deepEqual(exits, []);
    }
    assert.equal(wrapperProcess.emit("SIGINT"), true);
    assert.equal(shutdownCalls, 1);
    if (cleanupFails) cleanup.reject(new Error("injected shutdown failure"));
    else cleanup.resolve();
    await checkpoint();
    if (!cleanupFails) {
      assert.equal(closes, 1);
      assert.deepEqual(exits, []);
      assert.equal(wrapperProcess.emit("SIGTERM"), true);
      assert.equal(shutdownCalls, 1);
      serverClose.resolve();
      await checkpoint();
    }
    assert.equal(shutdownCalls, 1);
    assert.equal(closes, cleanupFails ? 0 : 1);
    assert.deepEqual(exits, [cleanupFails ? 70 : 0]);
    assert.deepEqual(events, cleanupFails ? ["cleanup", "exit:70"] : ["cleanup", "close", "exit:0"]);
  });
}

test("pre-aborted handler skips the availability probe and launch", { timeout: 5_000 }, async () => {
  let probes = 0;
  let launches = 0;
  const handlers = loadActual<typeof Handlers>("handlers", {
    "./config.js": { config },
    "./cli-runner.js": {
      getCliAvailability: async () => { probes++; return { exitCode: 0, stdout: "", stderr: "" }; },
      runCli: async () => { launches++; return { exitCode: 0, stdout: "", stderr: "" }; },
    },
  }, {});
  const cancellation = new AbortController();
  cancellation.abort();
  const result = await handlers.handleHealthCheck({ server: "https://example.test/mcp", access: "public", interactive: false }, cancellation.signal);
  assert.equal(result.isError, true);
  assert.equal(result.structuredContent.exitCode, 124);
  assert.equal((result.structuredContent.error as { code: string }).code, "CLI_CANCELLED");
  assert.equal(probes, 0);
  assert.equal(launches, 0);
});