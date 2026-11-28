"use strict";

const fs = require("node:fs");
const path = require("node:path");
const { spawn } = require("node:child_process");

const [mode, root, ...extra] = process.argv.slice(2);
if (!["echo", "stdin-eof", "tree", "branch", "leaf", "mcp", "nested"].includes(mode) || !path.isAbsolute(root)) {
  throw new Error("Invalid fixture arguments");
}
function ready(role, pid = process.pid) {
  const receipt = path.join(root, role + ".json");
  fs.writeFileSync(receipt + ".tmp", JSON.stringify({ pid, parentPid: process.ppid }));
  fs.renameSync(receipt + ".tmp", receipt);
}
function child(childMode, role) {
  const worker = spawn(process.execPath, [__filename, childMode, root, role], {
    stdio: "ignore", detached: true, windowsHide: true,
  });
  worker.on("error", error => { throw error; });
  worker.unref();
}
if (mode === "tree" || mode === "mcp") {
  child("branch", "child-a");
  child("leaf", "child-b");
} else if (mode === "branch") {
  child("leaf", "grandchild");
}
if (mode === "branch" || mode === "leaf") {
  fs.closeSync(0);
  fs.closeSync(1);
  fs.closeSync(2);
}
ready(mode === "branch" || mode === "leaf" ? extra[0] : "leader");
const deadline = setTimeout(() => process.exit(90), 90000);
let nestedHost;
if (mode === "nested") {
  if (!path.isAbsolute(extra[0])) throw new Error("Expected absolute CLI apphost");
  nestedHost = spawn(extra[0], ["__mcpval-windows-supervisor"], { stdio: ["overlapped", "overlapped", "overlapped"] });
  nestedHost.on("error", error => { throw error; });
  nestedHost.on("spawn", () => ready("host", nestedHost.pid));
  nestedHost.stdout.pipe(process.stdout);
  nestedHost.stderr.pipe(process.stderr);
  nestedHost.on("close", code => {
    clearTimeout(deadline);
    clearInterval(poll);
    process.exitCode = code ?? 91;
  });
}
const poll = setInterval(() => {
  if (mode === "tree" && fs.existsSync(path.join(root, "leader-exit"))) process.exit(0);
  if (!fs.existsSync(path.join(root, "release"))) return;
  if (mode === "branch" || mode === "leaf" || mode === "tree") return;
  clearInterval(poll);
  if (mode === "echo") {
    clearTimeout(deadline);
    process.stderr.write("fixture-stderr\n");
    process.stdout.write(JSON.stringify(extra) + "\n");
  } else if (mode === "stdin-eof") {
    process.stdin.once("end", () => {
      clearTimeout(deadline);
      process.stderr.write("fixture-stderr\n");
      process.stdout.write("stdin-eof\n");
    });
    process.stdin.resume();
  } else if (mode === "mcp") {
    process.argv = [process.execPath, __filename, ...extra];
    require("./mcp-fixture-compliant.cjs");
  } else if (mode === "nested") {
    nestedHost.stdin.write(JSON.stringify({ protocolVersion: 1, arguments: ["--version"], timeoutMs: 20000 }) + "\n");
  }
}, 20);