import assert from "node:assert/strict";
import { existsSync } from "node:fs";
import { chmod, mkdtemp, writeFile } from "node:fs/promises";
import { tmpdir } from "node:os";
import { dirname, join } from "node:path";
import test from "node:test";
import { config } from "../src/config.js";
import { runCli } from "../src/cli-runner.js";

const runnerScript = `#!/usr/bin/env node
import { mkdir, readFile, writeFile } from "node:fs/promises";
import { join } from "node:path";

const args = process.argv.slice(2);
const payload = { args };
const configIndex = args.indexOf("--config");
if (configIndex >= 0) {
  const configPath = args[configIndex + 1];
  payload.configPath = configPath;
  payload.configJson = JSON.parse(await readFile(configPath, "utf-8"));
}

const outputIndex = args.indexOf("--output");
if (outputIndex >= 0) {
  const outputDir = args[outputIndex + 1];
  payload.outputDir = outputDir;
  await mkdir(outputDir, { recursive: true });
  await writeFile(join(outputDir, "validation-result.json"), JSON.stringify({ overallStatus: "NeedsReview", complianceScore: 62 }));
  await writeFile(join(outputDir, "validation-report.md"), "# mock report\\n");
}

process.stdout.write(JSON.stringify(payload));
const exitCode = Number(process.env.MOCK_EXIT_CODE ?? "0");
process.exit(exitCode);
`;

async function createMockCli(): Promise<string> {
  const dir = await mkdtemp(join(tmpdir(), "mcpval-localmcp-test-"));
  const scriptPath = join(dir, "mock-cli.mjs");
  await writeFile(scriptPath, runnerScript, "utf-8");
  await chmod(scriptPath, 0o755);
  return scriptPath;
}

test("runCli passes secrets via config and not argv", async () => {
  const mockCliPath = await createMockCli();
  const previousCliPath = process.env[config.cliPathEnvVar];
  process.env[config.cliPathEnvVar] = mockCliPath;

  try {
    const result = await runCli({
      command: "validate",
      args: ["--policy", "strict"],
      configJson: {
        server: {
          endpoint: "https://example.test/mcp",
          authentication: {
            type: "bearer",
            token: "super-secret-token",
          },
        },
      },
      captureResultJson: true,
    });

    const invocation = JSON.parse(result.stdout) as {
      args: string[];
      configPath: string;
      configJson: { server: { authentication: { token: string } } };
    };

    assert.equal(result.exitCode, 0);
    assert.deepEqual(result.resultJson, { overallStatus: "NeedsReview", complianceScore: 62 });
    assert.ok(invocation.args.includes("--config"));
    assert.ok(invocation.args.includes("--output"));
    assert.ok(!invocation.args.includes("super-secret-token"));
    assert.equal(invocation.configJson.server.authentication.token, "super-secret-token");
    assert.equal(existsSync(invocation.configPath), false);
    assert.equal(existsSync(dirname(invocation.configPath)), false);
  } finally {
    if (previousCliPath) {
      process.env[config.cliPathEnvVar] = previousCliPath;
    } else {
      delete process.env[config.cliPathEnvVar];
    }
  }
});

test("runCli does not append output for discover", async () => {
  const mockCliPath = await createMockCli();
  const previousCliPath = process.env[config.cliPathEnvVar];
  process.env[config.cliPathEnvVar] = mockCliPath;

  try {
    const result = await runCli({
      command: "discover",
      args: ["--format", "json"],
      configJson: {
        server: {
          endpoint: "https://example.test/mcp",
        },
      },
    });

    const invocation = JSON.parse(result.stdout) as { args: string[] };

    assert.equal(result.exitCode, 0);
    assert.ok(!invocation.args.includes("--output"));
  } finally {
    if (previousCliPath) {
      process.env[config.cliPathEnvVar] = previousCliPath;
    } else {
      delete process.env[config.cliPathEnvVar];
    }
  }
});

test("runCli preserves result artifacts on non-zero validate exit", async () => {
  const mockCliPath = await createMockCli();
  const previousCliPath = process.env[config.cliPathEnvVar];
  process.env[config.cliPathEnvVar] = mockCliPath;

  try {
    const result = await runCli(
      {
        command: "validate",
        configJson: {
          server: {
            endpoint: "https://example.test/mcp",
          },
        },
        captureResultJson: true,
      },
      { MOCK_EXIT_CODE: "3" },
    );

    assert.equal(result.exitCode, 3);
    assert.deepEqual(result.resultJson, { overallStatus: "NeedsReview", complianceScore: 62 });
  } finally {
    if (previousCliPath) {
      process.env[config.cliPathEnvVar] = previousCliPath;
    } else {
      delete process.env[config.cliPathEnvVar];
    }
  }
});