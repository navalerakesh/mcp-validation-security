import assert from "node:assert/strict";
import { chmod, mkdtemp, writeFile } from "node:fs/promises";
import { tmpdir } from "node:os";
import { join } from "node:path";
import test from "node:test";
import { config } from "../src/config.js";
import { handleDiscover, handleHealthCheck, handleValidate } from "../src/handlers.js";

const runnerScript = `#!/usr/bin/env node
import { mkdir, readFile, writeFile } from "node:fs/promises";
import { join } from "node:path";

const args = process.argv.slice(2);
if (args.includes("--version")) {
  process.stdout.write("mcpval test harness\\n");
  process.exit(0);
}

const outputIndex = args.indexOf("--output");
if (outputIndex >= 0) {
  const outputDir = args[outputIndex + 1];
  await mkdir(outputDir, { recursive: true });
  if (process.env.MOCK_WRITE_RESULT_JSON === "1") {
    await writeFile(join(outputDir, "validation-result.json"), JSON.stringify({ overallStatus: "NeedsReview", complianceScore: 62 }));
  }
}

if (process.env.MOCK_STDOUT) process.stdout.write(process.env.MOCK_STDOUT);
if (process.env.MOCK_STDERR) process.stderr.write(process.env.MOCK_STDERR);
process.exit(Number(process.env.MOCK_EXIT_CODE ?? "0"));
`;

async function createMockCli(): Promise<string> {
  const dir = await mkdtemp(join(tmpdir(), "mcpval-localmcp-handlers-test-"));
  const scriptPath = join(dir, "mock-cli.mjs");
  await writeFile(scriptPath, runnerScript, "utf-8");
  await chmod(scriptPath, 0o755);
  return scriptPath;
}

test("handleValidate treats captured result json as a successful tool result", async () => {
  const mockCliPath = await createMockCli();
  const previousCliPath = process.env[config.cliPathEnvVar];
  const previousWriteResult = process.env.MOCK_WRITE_RESULT_JSON;
  const previousExitCode = process.env.MOCK_EXIT_CODE;

  process.env[config.cliPathEnvVar] = mockCliPath;
  process.env.MOCK_WRITE_RESULT_JSON = "1";
  process.env.MOCK_EXIT_CODE = "3";

  try {
    const result = await handleValidate({
      server: "https://example.test/mcp",
      access: "public",
      interactive: false,
      verbose: false,
    });

    assert.equal(result.isError, false);
    assert.match(result.text, /Compliance Score: 62%/);
  } finally {
    if (previousCliPath) process.env[config.cliPathEnvVar] = previousCliPath;
    else delete process.env[config.cliPathEnvVar];

    if (previousWriteResult) process.env.MOCK_WRITE_RESULT_JSON = previousWriteResult;
    else delete process.env.MOCK_WRITE_RESULT_JSON;

    if (previousExitCode) process.env.MOCK_EXIT_CODE = previousExitCode;
    else delete process.env.MOCK_EXIT_CODE;
  }
});

test("handleHealthCheck marks non-zero CLI exits as tool errors", async () => {
  const mockCliPath = await createMockCli();
  const previousCliPath = process.env[config.cliPathEnvVar];
  const previousStderr = process.env.MOCK_STDERR;
  const previousExitCode = process.env.MOCK_EXIT_CODE;

  process.env[config.cliPathEnvVar] = mockCliPath;
  process.env.MOCK_STDERR = "health-check failed";
  process.env.MOCK_EXIT_CODE = "2";

  try {
    const result = await handleHealthCheck({
      server: "https://example.test/mcp",
      access: "public",
      interactive: false,
    });

    assert.equal(result.isError, true);
    assert.equal(result.text, "health-check failed");
  } finally {
    if (previousCliPath) process.env[config.cliPathEnvVar] = previousCliPath;
    else delete process.env[config.cliPathEnvVar];

    if (previousStderr) process.env.MOCK_STDERR = previousStderr;
    else delete process.env.MOCK_STDERR;

    if (previousExitCode) process.env.MOCK_EXIT_CODE = previousExitCode;
    else delete process.env.MOCK_EXIT_CODE;
  }
});

test("handleDiscover includes explicit false isError on success", async () => {
  const mockCliPath = await createMockCli();
  const previousCliPath = process.env[config.cliPathEnvVar];
  const previousStdout = process.env.MOCK_STDOUT;
  const previousExitCode = process.env.MOCK_EXIT_CODE;

  process.env[config.cliPathEnvVar] = mockCliPath;
  process.env.MOCK_STDOUT = "discovery ok";
  process.env.MOCK_EXIT_CODE = "0";

  try {
    const result = await handleDiscover({
      server: "https://example.test/mcp",
      access: "public",
      interactive: false,
      format: "json",
    });

    assert.equal(result.isError, false);
    assert.equal(result.text, "discovery ok");
  } finally {
    if (previousCliPath) process.env[config.cliPathEnvVar] = previousCliPath;
    else delete process.env[config.cliPathEnvVar];

    if (previousStdout) process.env.MOCK_STDOUT = previousStdout;
    else delete process.env.MOCK_STDOUT;

    if (previousExitCode) process.env.MOCK_EXIT_CODE = previousExitCode;
    else delete process.env.MOCK_EXIT_CODE;
  }
});