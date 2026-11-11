import assert from "node:assert/strict";
import { spawnSync } from "node:child_process";
import { access, mkdtemp, mkdir, readFile, rm, writeFile } from "node:fs/promises";
import { tmpdir } from "node:os";
import { delimiter, join } from "node:path";
import test from "node:test";
import { parse } from "yaml";

const action = parse(await readFile(new URL("../../action.yml", import.meta.url), "utf8"));
const validation = action.runs.steps.find((step: { name: string }) => step.name === "Run MCP validation");
const bash = process.platform === "win32"
  ? join(process.env.ProgramFiles ?? "C:\\Program Files", "Git", "bin", "bash.exe")
  : "bash";

const cases: { name: string; exitCode: number; inputs?: Record<string, string>; rejected?: boolean; outputFailure?: "missing" | "directory" }[] = [
  ...[0, 1, 2, 64, 70, 75, 124].map(exitCode => ({ name: `collector exit ${exitCode}`, exitCode })),
  { name: "missing target", exitCode: 1, inputs: { server: "" }, rejected: true },
  { name: "invalid assurance", exitCode: 1, inputs: { assurance: "unknown" }, rejected: true },
  { name: "unconfirmed elevation", exitCode: 1, inputs: { mode: "elevated" }, rejected: true },
  { name: "regression without baseline", exitCode: 1, inputs: { "regression-only": "true" }, rejected: true },
  { name: "provider without authorization", exitCode: 1, inputs: { assurance: "provider" }, rejected: true },
  { name: "missing output after success", exitCode: 0, outputFailure: "missing" },
  { name: "missing output after failure", exitCode: 75, outputFailure: "missing" },
  { name: "unwritable output after success", exitCode: 0, outputFailure: "directory" },
  { name: "unwritable output after failure", exitCode: 75, outputFailure: "directory" }
];

for (const scenario of cases) {
  test(`Action entrypoint: ${scenario.name}`, async () => {
    const root = await mkdtemp(join(tmpdir(), "mcpval-action-"));
    try {
      const bin = join(root, "bin");
      await mkdir(bin);
      const capture = join(root, "arguments.json");
      const output = join(root, "github-output");
      const script = join(root, "validation.sh");
      const mock = join(root, "mock-cli.cjs");
      await writeFile(mock, `require("node:fs").writeFileSync(process.env.MOCK_CAPTURE, JSON.stringify(process.argv.slice(2))); process.exit(Number(process.env.MOCK_EXIT));\n`);
      await writeFile(join(bin, "mcpval"), '#!/usr/bin/env bash\nexec "$MOCK_NODE" "$MOCK_SCRIPT" "$@"\n', { mode: 0o755 });
      await writeFile(script, validation.run);
      const server = 'node "fixture with spaces.js" --label "$(printf unintended)"';
      const inputs: Record<string, string> = Object.fromEntries(Object.entries(action.inputs)
        .map(([name, value]) => [name, String((value as { default?: unknown }).default ?? "")]));
      inputs.server = server;
      inputs["output-dir"] = join(root, "reports with spaces");
      inputs["max-concurrency"] = "3";
      inputs["output-format"] = "json";
      Object.assign(inputs, scenario.inputs);
      const env: NodeJS.ProcessEnv = {
        PATH: `${bin}${delimiter}${process.env.PATH ?? ""}`,
        HOME: root, TMPDIR: root, TEMP: root, TMP: root,
        SystemRoot: process.env.SystemRoot, WINDIR: process.env.WINDIR,
        GITHUB_OUTPUT: output, MOCK_CAPTURE: capture, MOCK_EXIT: String(scenario.exitCode),
        MOCK_NODE: process.execPath, MOCK_SCRIPT: mock
      };
      for (const [name, expression] of Object.entries(validation.env)) {
        const match = /^\$\{\{\s*inputs\.([a-z-]+)\s*\}\}$/.exec(String(expression));
        assert.ok(match, `Unsupported Action environment binding: ${name}`);
        env[name] = inputs[match[1]];
      }
      if (scenario.outputFailure === "missing") delete env.GITHUB_OUTPUT;
      if (scenario.outputFailure === "directory") env.GITHUB_OUTPUT = bin;

      const execution = spawnSync(bash, [script.replaceAll("\\", "/")], {
        cwd: root, env, encoding: "utf8", timeout: 10_000, maxBuffer: 128 * 1024
      });

      assert.equal(execution.error, undefined);
      assert.equal(execution.signal, null);
      assert.equal(execution.status, scenario.outputFailure ? 70 : scenario.exitCode, execution.stderr);
      if (scenario.rejected) {
        await assert.rejects(access(capture), { code: "ENOENT" });
        await assert.rejects(access(output), { code: "ENOENT" });
        return;
      }
      assert.deepEqual(JSON.parse(await readFile(capture, "utf8")), [
        "validate", "--output", inputs["output-dir"], "--policy", "balanced",
        "--assurance", "inventory", "--server", server, "--access", "public",
        "--max-concurrency", "3", "--mode", "safe", "--output-format", "json"
      ]);
      if (scenario.outputFailure) {
        assert.ok(execution.stdout.includes(`collector exit: ${scenario.exitCode}`));
        assert.equal(execution.stderr, "");
        assert.ok(!execution.stdout.includes(root));
        await assert.rejects(access(output), { code: "ENOENT" });
        return;
      }
      assert.equal(await readFile(output, "utf8"), `exit-code=${scenario.exitCode}\n`);
      assert.equal(action.outputs["exit-code"].value, `\${{ steps.${validation.id}.outputs.exit-code }}`);
      const upload = action.runs.steps.find((step: { name: string }) => step.name === "Upload validation artifacts");
      assert.match(upload.if, /always\(\)/);
    } finally {
      await rm(root, { recursive: true, force: true });
    }
  });
}