#!/usr/bin/env node

const { startFixtureServer } = require("./mcp-fixture-runtime.cjs");
const { appendFileSync, closeSync } = require("node:fs");
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
const notificationArgument = process.argv.indexOf("--notifications-before-response");
const notificationCount = notificationArgument < 0 ? 0 : Number(process.argv[notificationArgument + 1]);
if (!Number.isInteger(notificationCount) || notificationCount < 0 || notificationCount > 65) throw new Error("Invalid fixture notification count.");
startFixtureServer("compliant", method => {
	if (method === "notifications/initialized" && process.argv.includes("--close-notification-stdout")) {
		closeSync(1);
	}
	if (method === "notifications/initialized" && process.argv.includes("--partial-notification-response")) {
		process.stdout.write('{"jsonrpc":"2.0"');
	}
	if (!auditFile) return;
	if (++recorded > 256 || method.length > 128) throw new Error("Fixture method audit budget exceeded.");
	appendFileSync(auditFile, `${JSON.stringify({ method })}\n`, { mode: 0o600 });
}, method => method !== heldMethod, response => {
	for (let index = 0; index < notificationCount; index++) {
		process.stdout.write(`${JSON.stringify({ jsonrpc: "2.0", method: "notifications/message", params: { level: "info", data: "owned fixture notification" } })}\n`);
	}
	return process.argv.includes("--mismatch-response-id") && Object.prototype.hasOwnProperty.call(response, "id")
		? { ...response, id: "unrelated-fixture-response" }
		: response;
});