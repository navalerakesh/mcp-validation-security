#!/usr/bin/env node

const fs = require("node:fs");
const path = require("node:path");
const { startFixtureServer } = require("./mcp-fixture-runtime.cjs");

function fail() {
  process.stderr.write("Controlled fixture rejected its configuration, files, or call budget.\n");
  process.exit(1);
}

function main() {
  const options = new Map();
  const cliArguments = process.argv.slice(2);
  if (cliArguments.length !== 4) fail();
  for (let index = 0; index < cliArguments.length; index += 2) {
    const option = cliArguments[index];
    if (!["--root", "--mode"].includes(option) || options.has(option)) fail();
    options.set(option, cliArguments[index + 1]);
  }

  const suppliedRoot = options.get("--root");
  const mode = options.get("--mode");
  if (!suppliedRoot || !path.isAbsolute(suppliedRoot) ||
      !["write", "drop", "noop", "reject", "partial-error"].includes(mode)) fail();

  const rootPath = path.resolve(suppliedRoot);
  const rootStat = fs.lstatSync(rootPath);
  const ownerId = typeof process.getuid === "function" ? process.getuid() : null;
  if (rootStat.isSymbolicLink() || !rootStat.isDirectory() ||
      (ownerId !== null && rootStat.uid !== ownerId)) fail();
  const root = fs.realpathSync(rootPath);
  const maximumBytes = 4096;
  const noFollow = typeof fs.constants.O_NOFOLLOW === "number" ? fs.constants.O_NOFOLLOW : 0;

  function verifyRoot() {
    const current = fs.lstatSync(rootPath);
    if (current.isSymbolicLink() || !current.isDirectory() ||
        current.dev !== rootStat.dev || current.ino !== rootStat.ino ||
        fs.realpathSync(rootPath) !== root) fail();
  }

  function openFile(name, create = false) {
    verifyRoot();
    const filePath = path.join(root, name);
    const previous = fs.lstatSync(filePath, { throwIfNoEntry: false });
    if (previous && (previous.isSymbolicLink() || !previous.isFile())) fail();
    if (!previous && !create) fail();
    const descriptor = fs.openSync(filePath,
      fs.constants.O_RDWR | noFollow | fs.constants.O_NONBLOCK |
      (!previous && create ? fs.constants.O_CREAT | fs.constants.O_EXCL : 0), 0o600);
    try {
      const metadata = fs.fstatSync(descriptor);
      if (!metadata.isFile() || metadata.nlink !== 1 || metadata.size > maximumBytes ||
          (previous && (previous.dev !== metadata.dev || previous.ino !== metadata.ino)) ||
          (ownerId !== null && metadata.uid !== ownerId)) fail();
      return descriptor;
    } catch (error) {
      fs.closeSync(descriptor);
      throw error;
    }
  }

  function readState(descriptor) {
    const buffer = Buffer.alloc(maximumBytes + 1);
    let length = 0;
    while (length < buffer.length) {
      const count = fs.readSync(descriptor, buffer, length, buffer.length - length, length);
      if (count === 0) break;
      length += count;
    }
    if (length > maximumBytes) fail();
    const state = JSON.parse(buffer.toString("utf8", 0, length));
    if (!state || typeof state !== "object" || Array.isArray(state) ||
        (state.value !== 0 && state.value !== 1)) fail();
    return state;
  }

  function writeJson(descriptor, value) {
    const buffer = Buffer.from(`${JSON.stringify(value)}\n`);
    if (buffer.length > maximumBytes) fail();
    let offset = 0;
    while (offset < buffer.length) {
      const count = fs.writeSync(descriptor, buffer, offset, buffer.length - offset, offset);
      if (count === 0) fail();
      offset += count;
    }
    fs.ftruncateSync(descriptor, buffer.length);
  }

  function writeAudit(applicationRejected) {
    const descriptor = openFile("audit.json", true);
    try {
      fs.fchmodSync(descriptor, 0o600);
      writeJson(descriptor, { attempted: true, applicationRejected });
    } finally {
      fs.closeSync(descriptor);
    }
  }

  const initialDescriptor = openFile("state.json");
  let initialStat;
  try {
    initialStat = fs.fstatSync(initialDescriptor);
    readState(initialDescriptor);
  } finally {
    fs.closeSync(initialDescriptor);
  }

  let attempted = false;
  startFixtureServer("compliant", method => {
    if (method !== "tools/call") return;
    if (attempted) fail();
    attempted = true;
    try {
      writeAudit(true);
    } catch {
      fail();
    }
  }, undefined, undefined, profile => {
    profile.toolsPages = [[{
      name: "fixture_write",
      description: "Sets the owned fixture state value to one.",
      inputSchema: {
        type: "object",
        properties: { value: { type: "integer", const: 1 } },
        required: ["value"],
        additionalProperties: false
      },
      annotations: {
        readOnlyHint: false,
        destructiveHint: false,
        openWorldHint: false,
        idempotentHint: true
      }
    }]];
    profile.toolCall = request => {
      try {
        const argumentsValue = request.params.arguments;
        const validArguments = argumentsValue.value === 1 && Object.keys(argumentsValue).length === 1;
        const applicationRejected = !validArguments || mode === "reject" || mode === "partial-error";
        if (validArguments && (mode === "write" || mode === "partial-error")) {
          const descriptor = openFile("state.json");
          try {
            const metadata = fs.fstatSync(descriptor);
            if (metadata.dev !== initialStat.dev || metadata.ino !== initialStat.ino) fail();
            const state = readState(descriptor);
            state.value = 1;
            writeJson(descriptor, state);
          } finally {
            fs.closeSync(descriptor);
          }
        }
        writeAudit(applicationRejected);
        return {
          content: [{ type: "text", text: applicationRejected ? "rejected" : "accepted" }],
          isError: applicationRejected
        };
      } catch {
        fail();
      }
    };
  });
}

try {
  main();
} catch {
  fail();
}