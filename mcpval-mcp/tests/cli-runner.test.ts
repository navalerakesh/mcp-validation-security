import assert from "node:assert/strict";
import { existsSync } from "node:fs";
import { chmod, mkdtemp, readdir, rm, writeFile } from "node:fs/promises";
import { tmpdir } from "node:os";
import { dirname, join } from "node:path";
import test, { after } from "node:test";
import fc from "fast-check";
import { config } from "../src/config.js";
import { normalizeCliExitCode, runCli } from "../src/cli-runner.js";

const temporaryRoots: string[] = [];
after(async () => Promise.all(temporaryRoots.map((root) => rm(root, { recursive: true, force: true }))));

const canonicalResult = {
  documentType: "mcpval.assurance-result",
  documentSchemaVersion: "3.1.0",
  assurance: { profile: { profileId: "inventory" }, certificateEligibility: { status: "ineligible" } },
};

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
  const layoutIndex = args.indexOf("--mock-layout");
  const layout = layoutIndex < 0 ? "flat" : args[layoutIndex + 1];
  const roots = layout === "bundle" ? [join(outputDir, "mcp-validation-fixture")]
    : layout === "staging" ? [join(outputDir, ".pending-fixture")]
    : layout === "multiple" ? [join(outputDir, "mcp-validation-first"), join(outputDir, "mcp-validation-second")]
    : layout === "missing" ? [] : [outputDir];
  for (const root of roots) {
    await mkdir(root, { recursive: true });
    const canonical = ${JSON.stringify(canonicalResult)};
    const body = JSON.stringify(layout === "empty-object" ? {}
      : layout === "unrelated" ? { ...canonical, documentType: "unrelated.document" }
      : layout === "future-major" ? { ...canonical, documentSchemaVersion: "4.0.0" }
      : layout === "legacy-2" ? { ...canonical, documentSchemaVersion: "2.3.0" }
      : layout === "invalid-version" ? { ...canonical, documentSchemaVersion: "2.01.0" }
      : layout === "legacy-v1" ? { overallStatus: "NeedsReview", complianceScore: 62 } : canonical);
    const padding = layout === "oversized" ? " ".repeat(${config.maximumCliOutputBytes} + 1)
      : layout === "exact-limit" ? " ".repeat(${config.maximumCliOutputBytes} - Buffer.byteLength(body)) : "";
    await writeFile(join(root, layout === "hidden" ? ".pending-result.json" : "validation-result.json"), body + padding);
    await writeFile(join(root, "validation-report.md"), "# mock report\\n");
  }
}

process.stdout.write(JSON.stringify(payload));
const exitCode = Number(process.env.MOCK_EXIT_CODE ?? "0");
process.exit(exitCode);
`;

async function createMockCli(): Promise<string> {
  const dir = await mkdtemp(join(tmpdir(), "mcpval-localmcp-test-"));
  temporaryRoots.push(dir);
  const scriptPath = join(dir, "mock-cli.mjs");
  await writeFile(scriptPath, runnerScript, "utf-8");
  await chmod(scriptPath, 0o755);
  return scriptPath;
}

for (const layout of ["flat", "bundle", "staging", "multiple", "missing", "exact-limit", "oversized", "hidden", "empty-object", "unrelated", "future-major", "invalid-version", "legacy-v1", "legacy-2"]) {
  test(`runCli admits only one completed result: ${layout}`, async () => {
    const mockCliPath = await createMockCli();
    const previousCliPath = process.env[config.cliPathEnvVar];
    process.env[config.cliPathEnvVar] = mockCliPath;
    try {
      const result = await runCli({ command: "validate", args: ["--mock-layout", layout], captureResultJson: true });
      const accepted = layout === "flat" || layout === "bundle" || layout === "exact-limit" || layout === "legacy-2";
      assert.equal(result.exitCode, accepted ? 0 : 70);
      assert.deepEqual(result.resultJson, accepted
        ? layout === "legacy-2" ? { ...canonicalResult, documentSchemaVersion: "2.3.0" } : canonicalResult : undefined);
      const invocation = JSON.parse(result.stdout) as { outputDir: string };
      assert.equal(existsSync(dirname(invocation.outputDir)), false);
    } finally {
      if (previousCliPath) process.env[config.cliPathEnvVar] = previousCliPath;
      else delete process.env[config.cliPathEnvVar];
    }
  });
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
    assert.deepEqual(result.resultJson, canonicalResult);
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

test("runCli keeps generated bearer tokens out of argv", async () => {
  const mockCliPath = await createMockCli();
  const previousCliPath = process.env[config.cliPathEnvVar];
  process.env[config.cliPathEnvVar] = mockCliPath;

  const tokenArbitrary = fc
    .array(fc.constantFrom(..."ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz0123456789._~-"), { minLength: 1, maxLength: 64 })
    .map((chars) => chars.join(""));

  try {
    await fc.assert(
      fc.asyncProperty(tokenArbitrary, async (token) => {
        const result = await runCli({
          command: "validate",
          args: ["--policy", "balanced"],
          configJson: {
            server: {
              endpoint: "https://example.test/mcp",
              authentication: {
                type: "bearer",
                token,
              },
            },
          },
        });

        const invocation = JSON.parse(result.stdout) as {
          args: string[];
          configPath: string;
          configJson: { server: { authentication: { token: string } } };
        };

        assert.equal(result.exitCode, 0);
        assert.equal(invocation.configJson.server.authentication.token, token);
        assert.ok(!invocation.args.includes(token));
        assert.equal(existsSync(invocation.configPath), false);
        assert.equal(existsSync(dirname(invocation.configPath)), false);
      }),
      { numRuns: 25, seed: 5063504 },
    );
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
      { MOCK_EXIT_CODE: "2" },
    );

    assert.equal(result.exitCode, 2);
    assert.deepEqual(result.resultJson, canonicalResult);
  } finally {
    if (previousCliPath) {
      process.env[config.cliPathEnvVar] = previousCliPath;
    } else {
      delete process.env[config.cliPathEnvVar];
    }
  }
});

test("pre-launch cancellation and configuration failure leave no temporary artifacts", async () => {
  const mockCliPath = await createMockCli();
  const root = await mkdtemp(join(tmpdir(), "mcpval-runner-isolation-"));
  temporaryRoots.push(root);
  const keys = [config.cliPathEnvVar, "TMPDIR", "TMP", "TEMP"];
  const previous = new Map(keys.map(key => [key, process.env[key]]));
  process.env[config.cliPathEnvVar] = mockCliPath;
  for (const key of ["TMPDIR", "TMP", "TEMP"]) process.env[key] = root;
  try {
    const cancellation = new AbortController();
    cancellation.abort();
    const cancelled = await runCli({ command: "validate", signal: cancellation.signal });
    assert.equal(cancelled.exitCode, 124);
    assert.equal(cancelled.stdout, "");
    assert.deepEqual(await readdir(root), []);
    const invalid = await runCli({ command: "validate", configJson: { toJSON() { throw new Error("synthetic-config-canary"); } } });
    assert.equal(invalid.exitCode, 70);
    assert.ok(!invalid.stderr.includes("synthetic-config-canary"));
    assert.deepEqual(await readdir(root), []);
  } finally {
    for (const [key, value] of previous) {
      if (value === undefined) delete process.env[key]; else process.env[key] = value;
    }
  }
});

test("CLI output budget terminates the owned process and retains bounded output", { timeout: 15_000 }, async () => {
  const root = await mkdtemp(join(tmpdir(), "mcpval-output-bound-"));
  temporaryRoots.push(root);
  const script = join(root, "flood.mjs");
  await writeFile(script, `#!/usr/bin/env node\nprocess.stdout.write("x".repeat(${config.maximumCliOutputBytes + 65536})); setInterval(() => {}, 1000);\n`);
  await chmod(script, 0o755);
  const previous = process.env[config.cliPathEnvVar];
  process.env[config.cliPathEnvVar] = script;
  try {
    const result = await runCli({ command: "validate" });
    assert.equal(result.exitCode, 70);
    assert.ok(Buffer.byteLength(result.stdout) <= config.maximumCliOutputBytes);
    assert.match(result.stderr, /output exceeded/);
  } finally {
    if (previous === undefined) delete process.env[config.cliPathEnvVar]; else process.env[config.cliPathEnvVar] = previous;
  }
});

test("normalizeCliExitCode preserves stable numeric exits and maps wrapper failures", () => {
  assert.equal(normalizeCliExitCode({ code: 75 }), 75);
  assert.equal(normalizeCliExitCode({ killed: true }), 124);
  assert.equal(normalizeCliExitCode({ code: "ENOENT" }), 70);
  assert.equal(normalizeCliExitCode({}), 70);
});