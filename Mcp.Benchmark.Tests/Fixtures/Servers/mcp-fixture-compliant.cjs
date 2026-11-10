#!/usr/bin/env node

const { startFixtureServer } = require("./mcp-fixture-runtime.cjs");
const { appendFileSync } = require("node:fs");
const { isAbsolute } = require("node:path");

const auditArgument = process.argv.indexOf("--audit-methods");
const auditFile = auditArgument < 0 ? null : process.argv[auditArgument + 1];
if (auditArgument >= 0 && (!auditFile || !isAbsolute(auditFile))) throw new Error("Fixture audit requires an absolute output path.");
const processArgument = process.argv.indexOf("--audit-processes");
const processFile = processArgument < 0 ? null : process.argv[processArgument + 1];
if (processArgument >= 0 && (!processFile || !isAbsolute(processFile))) throw new Error("Fixture process audit requires an absolute output path.");
if (processFile) appendFileSync(processFile, `${JSON.stringify({ pid: process.pid, parentPid: process.ppid })}\n`, { mode: 0o600 });
const holdArgument = process.argv.indexOf("--hold-method");
const heldMethod = holdArgument < 0 ? null : process.argv[holdArgument + 1];
if (holdArgument >= 0 && heldMethod !== "tools/list") throw new Error("Only tools/list can be held by this fixture.");
let recorded = 0;
startFixtureServer("compliant", method => {
	if (!auditFile) return;
	if (++recorded > 256 || method.length > 128) throw new Error("Fixture method audit budget exceeded.");
	appendFileSync(auditFile, `${JSON.stringify({ method })}\n`, { mode: 0o600 });
}, method => method !== heldMethod);