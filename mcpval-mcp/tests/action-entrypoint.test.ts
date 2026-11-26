import assert from "node:assert/strict";
import { spawnSync } from "node:child_process";
import { access, mkdtemp, mkdir, readFile, realpath, rm, writeFile } from "node:fs/promises";
import { tmpdir } from "node:os";
import { delimiter, dirname, join, relative } from "node:path";
import { fileURLToPath } from "node:url";
import test from "node:test";
import { parse } from "yaml";

const action = parse(await readFile(new URL("../../action.yml", import.meta.url), "utf8"));
const actionRoot = fileURLToPath(new URL("../../", import.meta.url));
const validation = action.runs.steps.find((step: { name: string }) => step.name === "Run MCP validation");
const bash = process.platform === "win32"
  ? join(process.env.ProgramFiles ?? "C:\\Program Files", "Git", "bin", "bash.exe")
  : "bash";

const cases: { name: string; exitCode: number; inputs?: Record<string, string>; rejected?: boolean; outputFailure?: "missing" | "directory"; comprehensive?: boolean; governance?: boolean; extensions?: boolean; artifacts?: "missing" | "corrupt"; defaultHuman?: boolean; stale?: boolean }[] = [
  ...[0, 1, 2, 64, 70, 75, 124].map(exitCode => ({ name: `collector exit ${exitCode}`, exitCode })),
  { name: "missing target", exitCode: 1, inputs: { server: "" }, rejected: true },
  { name: "empty output path", exitCode: 1, inputs: { "output-dir": "" }, rejected: true },
  { name: "newline output path", exitCode: 1, inputs: { "output-dir": "private-path\ninjected-output=value" }, rejected: true },
  { name: "invalid assurance", exitCode: 1, inputs: { assurance: "unknown" }, rejected: true },
  { name: "unconfirmed elevation", exitCode: 1, inputs: { mode: "elevated" }, rejected: true },
  { name: "regression without baseline", exitCode: 1, inputs: { "regression-only": "true" }, rejected: true },
  { name: "provider without authorization", exitCode: 1, inputs: { assurance: "provider" }, rejected: true },
  { name: "interactive CI is rejected", exitCode: 1, inputs: { interactive: "true" }, rejected: true },
  { name: "malformed interactive input", exitCode: 1, inputs: { interactive: "maybe" }, rejected: true },
  { name: "comprehensive without evidence", exitCode: 1, inputs: { assurance: "comprehensive", mode: "standard" }, rejected: true },
  { name: "comprehensive Safe execution", exitCode: 1,
    inputs: { assurance: "comprehensive", "identity-baseline": "identity.json", "validation-id": "reserved", "conformance-results": "official.json" }, rejected: true },
  { name: "comprehensive without reserved ID", exitCode: 1,
    inputs: { assurance: "comprehensive", mode: "standard", "identity-baseline": "identity.json", "conformance-results": "official.json" }, rejected: true },
  { name: "comprehensive typed evidence forwarding", exitCode: 2, comprehensive: true,
    inputs: { assurance: "comprehensive", mode: "standard", "identity-baseline": "identity with spaces.json",
      "validation-id": "reserved-fixture", "conformance-results": "official with spaces.json" } },
  { name: "typed governance forwarding", exitCode: 0, governance: true, inputs: {
    "max-requests": "16", timeout: "10", "protocol-era": "legacy", "persistence-mode": "explicit-output",
    "redact-level": "strict", trace: "redacted", "client-profiles": "vscode-copilot-agent, claude-code",
    "allowed-hosts": "example.test,auth.example.test", "allowed-origins": "https://example.test, https://auth.example.test",
    "allow-private-addresses": "true"
  } },
  ...["--confirm-elevated-risk", "example.test,", "example.test,,other.test", "example.test\n--confirm-elevated-risk"].map(value =>
    ({ name: `invalid host list ${JSON.stringify(value)}`, exitCode: 1, inputs: { "allowed-hosts": value }, rejected: true })),
  { name: "typed extension forwarding", exitCode: 0, extensions: true,
    inputs: { extensions: "io.modelcontextprotocol/skills, io.modelcontextprotocol/tasks" } },
  { name: "newline extension list", exitCode: 1, inputs: { extensions: "io.modelcontextprotocol/skills\nignored" }, rejected: true },
  { name: "trailing empty extension", exitCode: 1, inputs: { extensions: "io.modelcontextprotocol/skills," }, rejected: true },
  { name: "malformed extension identifier", exitCode: 1, inputs: { extensions: "io.modelcontextprotocol/skills, invalid id" }, rejected: true },
  { name: "missing output after success", exitCode: 0, outputFailure: "missing" },
  { name: "missing output after failure", exitCode: 75, outputFailure: "missing" },
  { name: "unwritable output after success", exitCode: 0, outputFailure: "directory" },
  { name: "unwritable output after failure", exitCode: 75, outputFailure: "directory" },
  { name: "default human output remains unchanged", exitCode: 0, defaultHuman: true },
  { name: "missing artifacts cannot turn exit zero into acceptance", exitCode: 0, artifacts: "missing" },
  { name: "stale artifacts never satisfy a new run", exitCode: 0, artifacts: "missing", stale: true },
  { name: "corrupt artifacts retain collector exit", exitCode: 0, artifacts: "corrupt" },
  { name: "dry-run has no collection credit", exitCode: 0, inputs: { "dry-run": "true" }, artifacts: "missing" }
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
      const mock = join(root, "mock-cli.mjs");
      await writeFile(mock, `
        import { writeFile } from "node:fs/promises";
        const args = process.argv.slice(2);
        await writeFile(process.env.MOCK_CAPTURE, JSON.stringify(args));
        if (process.env.MOCK_ARTIFACTS !== "missing") {
          const { writeActionBundle } = await import(${JSON.stringify(new URL("../../scripts/tests/fixtures/action-artifacts.mjs", import.meta.url).href)});
          const fixture = await writeActionBundle(args[args.indexOf("--output") + 1], Number(process.env.MOCK_EXIT),
            process.env.MOCK_EXIT === "124" ? "cancelled" : "succeeded");
          if (process.env.MOCK_ARTIFACTS === "corrupt") await writeFile(fixture.resultPath, "invalid");
        }
        if (!args.includes("--output-format")) console.log("Human-readable collector output.");
        process.exit(Number(process.env.MOCK_EXIT));
      `);
      await writeFile(join(bin, "mcpval"), '#!/usr/bin/env bash\nexec "$MOCK_NODE" "$MOCK_SCRIPT" "$@"\n', { mode: 0o755 });
      await writeFile(script, validation.run);
      const server = 'node "fixture with spaces.js" --label "$(printf unintended)"';
      const inputs: Record<string, string> = Object.fromEntries(Object.entries(action.inputs)
        .map(([name, value]) => [name, String((value as { default?: unknown }).default ?? "")]));
      inputs.server = server;
      inputs["output-dir"] = join(root, "reports with spaces");
      inputs["max-concurrency"] = "3";
      if (!scenario.defaultHuman) inputs["output-format"] = "json";
      Object.assign(inputs, scenario.inputs);
      if (scenario.stale) {
        const fixture = new URL("../../scripts/tests/fixtures/action-artifacts.mjs", import.meta.url).href;
        const prepared = spawnSync(process.execPath, ["--input-type=module", "-e",
          `await (await import(${JSON.stringify(fixture)})).writeActionBundle(process.argv[1]);`, inputs["output-dir"]], { encoding: "utf8" });
        assert.equal(prepared.status, 0, prepared.stderr);
      }
      const env: NodeJS.ProcessEnv = {
        PATH: `${bin}${delimiter}${process.env.PATH ?? ""}`,
        HOME: root, TMPDIR: root, TEMP: root, TMP: root,
        SystemRoot: process.env.SystemRoot, WINDIR: process.env.WINDIR,
        GITHUB_OUTPUT: output, MOCK_CAPTURE: capture, MOCK_EXIT: String(scenario.exitCode),
        GITHUB_ACTION_PATH: actionRoot,
        MOCK_NODE: process.execPath, MOCK_SCRIPT: mock, MOCK_ARTIFACTS: scenario.artifacts ?? ""
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
      const artifactFailure = scenario.artifacts && inputs["dry-run"] !== "true";
      assert.equal(execution.status, scenario.outputFailure || artifactFailure ? 70 : scenario.exitCode, execution.stderr);
      if (scenario.rejected) {
        await assert.rejects(access(capture), { code: "ENOENT" });
        await assert.rejects(access(output), { code: "ENOENT" });
        return;
      }
      const actualArguments = JSON.parse(await readFile(capture, "utf8")) as string[];
      const invocationDirectory = actualArguments[actualArguments.indexOf("--output") + 1];
      const outputRoot = await realpath(inputs["output-dir"]);
      assert.equal(dirname(invocationDirectory), outputRoot);
      assert.match(relative(outputRoot, invocationDirectory), /^mcpval-action-[A-Za-z0-9]+$/);
      assert.deepEqual(actualArguments, [
        "validate", "--policy", "balanced",
        "--assurance", scenario.comprehensive ? "comprehensive" : "inventory", "--server", server, "--access", "public",
        "--max-concurrency", "3",
        ...(scenario.governance ? ["--max-requests", "16", "--timeout", "10", "--protocol-era", "legacy",
          "--persistence-mode", "explicit-output", "--redact-level", "strict", "--trace", "redacted",
          "--client-profile", "vscode-copilot-agent", "--client-profile", "claude-code",
          "--allow-host", "example.test", "--allow-host", "auth.example.test",
          "--allow-origin", "https://example.test", "--allow-origin", "https://auth.example.test",
          "--allow-private-addresses"] : []),
        ...(scenario.comprehensive ? ["--conformance-results", inputs["conformance-results"],
          "--identity-baseline", inputs["identity-baseline"], "--validation-id", inputs["validation-id"]] : []),
        "--mode", scenario.comprehensive ? "standard" : "safe",
        ...(scenario.extensions ? ["--extension", "io.modelcontextprotocol/skills", "--extension", "io.modelcontextprotocol/tasks"] : []),
        ...(inputs["dry-run"] === "true" ? ["--dry-run"] : []),
        ...(scenario.defaultHuman ? [] : ["--output-format", "json"]), "--output", invocationDirectory
      ]);
      if (scenario.outputFailure) {
        assert.ok(execution.stdout.includes(`collector exit: ${scenario.exitCode}`));
        assert.equal(execution.stderr, "");
        assert.ok(!execution.stdout.includes(root));
        await assert.rejects(access(output), { code: "ENOENT" });
        return;
      }
      const outputs = Object.fromEntries((await readFile(output, "utf8")).trim().split("\n").map(line => {
        const separator = line.indexOf("=");
        return [line.slice(0, separator), line.slice(separator + 1)];
      }));
      assert.equal(outputs["exit-code"], String(scenario.exitCode));
      assert.equal(outputs["invocation-directory"], invocationDirectory);
      assert.equal(outputs["artifact-state"], inputs["dry-run"] === "true" ? "plan-only"
        : scenario.artifacts ?? (scenario.exitCode === 124 ? "partial" : "published"));
      if (scenario.defaultHuman) assert.ok(execution.stdout.includes("Human-readable collector output."));
      for (const name of Object.keys(outputs))
        assert.equal(action.outputs[name].value, `\${{ steps.${validation.id}.outputs.${name} }}`);
      assert.equal(action.outputs["exit-code"].value, `\${{ steps.${validation.id}.outputs.exit-code }}`);
      const upload = action.runs.steps.find((step: { name: string }) => step.name === "Upload validation artifacts");
      assert.match(upload.if, /always\(\)/);
      assert.equal(upload.with.path, `\${{ steps.${validation.id}.outputs.invocation-directory }}`);
      assert.equal(upload.with["if-no-files-found"], "error");
    } finally {
      await rm(root, { recursive: true, force: true });
    }
  });
}