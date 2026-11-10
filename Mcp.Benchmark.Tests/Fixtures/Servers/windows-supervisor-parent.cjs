"use strict";

const fs = require("node:fs");
const path = require("node:path");
const { spawn } = require("node:child_process");
const { createInterface } = require("node:readline");

const [executable, root, mode] = process.argv.slice(2);
if (process.platform !== "win32" || !path.isAbsolute(executable) || !path.isAbsolute(root) ||
    (mode !== undefined && mode !== "nested")) {
  throw new Error("Expected a Windows CLI apphost and absolute fixture workspace");
}
function record(role, pid, parentPid) {
  const receipt = path.join(root, role + ".json");
  fs.writeFileSync(receipt + ".tmp", JSON.stringify({ pid, parentPid }));
  fs.renameSync(receipt + ".tmp", receipt);
}
if (mode === "nested") record("leader", process.pid, process.ppid);
const supervisor = spawn(executable, ["__mcpval-windows-supervisor"], {
  stdio: ["overlapped", "overlapped", "overlapped"], shell: false, windowsHide: true,
});
let poll;
let stopping = false;
const deadline = setTimeout(() => {
  stopping = true;
  clearInterval(poll);
  if (!supervisor.stdin.writableEnded) supervisor.stdin.end('{"kind":"cancel"}\n');
  process.stdin.destroy();
  process.exitCode = 90;
}, 90000);
supervisor.once("spawn", () => {
  record(mode === "nested" ? "host" : "supervisor", supervisor.pid, process.pid);
});
supervisor.on("error", error => { throw error; });
supervisor.stdin.on("error", error => {
  if (error.code !== "EPIPE") throw error;
});
if (mode === "nested") {
  let launched = false;
  let readyReceived = false;
  let launchStarted;
  const frames = createInterface({ input: supervisor.stdout, crlfDelay: Infinity });
  frames.on("line", line => {
    if (line.length > 16384) throw new Error("Oversized supervisor frame");
    const frame = JSON.parse(line);
    if (frame.kind !== "ready") return;
    if (!launched || readyReceived || Object.keys(frame).sort().join(",") !== "kind,protocolVersion" || frame.protocolVersion !== 1)
      throw new Error("Invalid or duplicate ready frame");
    readyReceived = true;
    if (stopping || supervisor.stdin.writableEnded) return;
    if (performance.now() - launchStarted >= 20000) {
      stopping = true;
      supervisor.stdin.end('{"kind":"cancel"}\n');
      return;
    }
    supervisor.stdin.write('{"kind":"resume"}\n');
  });
  poll = setInterval(() => {
    if (!fs.existsSync(path.join(root, "release"))) return;
    clearInterval(poll);
    launched = true;
    launchStarted = performance.now();
    supervisor.stdin.write(JSON.stringify({ protocolVersion: 1, arguments: ["--version"], timeoutMs: 20000 }) + "\n");
  }, 20);
} else {
  process.stdin.pipe(supervisor.stdin);
}
supervisor.stdout.pipe(process.stdout);
supervisor.stderr.pipe(process.stderr);
supervisor.once("close", code => {
  clearTimeout(deadline);
  clearInterval(poll);
  process.stdin.destroy();
  process.exitCode = process.exitCode ?? code ?? 91;
});