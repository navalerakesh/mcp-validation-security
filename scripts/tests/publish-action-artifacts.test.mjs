import assert from "node:assert/strict";
import { createHash } from "node:crypto";
import { mkdtemp, mkdir, open, rm, stat, symlink, writeFile } from "node:fs/promises";
import { tmpdir } from "node:os";
import { join } from "node:path";
import test from "node:test";
import { inspectActionArtifacts } from "../publish-action-artifacts.mjs";
import { writeActionBundle } from "./fixtures/action-artifacts.mjs";

for (const [variant, expected, code] of [
  ["positive", "published", 0], ["policy-failure", "published", 1],
  ["incomplete", "published", 2], ["cancelled", "partial", 124],
  ["plan-only", "plan-only", 0], ["missing", "missing", 0], ["usage", "missing", 64],
  ["max-json-depth", "published", 0], ["literal-delimiters", "published", 0],
  ["aggregate-boundary", "published", 0], ["aggregate-budget", "corrupt", 0],
  ...["invalid-json", "invalid-utf8", "digest", "wrong-run", "wrong-target", "wrong-profile", "wrong-assessment", "wrong-exit", "wrong-clock", "duplicate-bundle",
    "path-escape", "duplicate-subject", "missing-subject", "oversized-json", "oversized-subject", "over-json-depth", "symlink"].map(value => [value, "corrupt", 0])
]) {
  test(`Action artifact receipt: ${variant}`, { timeout: 15_000 }, async () => {
    const root = await mkdtemp(join(tmpdir(), "mcpval-action-receipt-"));
    try {
      if (!["plan-only", "missing", "usage"].includes(variant)) {
        const fixture = await writeActionBundle(root, code, variant === "cancelled" ? "cancelled" : "succeeded");
        if (["max-json-depth", "over-json-depth", "literal-delimiters"].includes(variant)) {
          let opaque = String.raw`"quoted \" literal" ${"[".repeat(130)} ${"}".repeat(130)}`;
          if (variant !== "literal-delimiters") {
            for (let index = 0; index < (variant === "max-json-depth" ? 127 : 128); index++) opaque = { nested: opaque };
          }
          fixture.result.uninterpretedPayload = opaque;
          const bytes = JSON.stringify(fixture.result);
          await writeFile(fixture.resultPath, bytes);
          fixture.audit.digests.artifactsSha256["fixture-result.json"] = createHash("sha256").update(bytes).digest("hex");
        }
        if (variant === "invalid-json") await writeFile(fixture.resultPath, "private-malformed-canary");
        if (variant === "invalid-utf8") await writeFile(fixture.resultPath, Buffer.from([0xc3, 0x28]));
        if (variant === "digest") fixture.audit.digests.artifactsSha256["fixture-result.json"] = "0".repeat(64);
        if (variant === "wrong-run") fixture.audit.validationId = "other-run";
        if (variant === "wrong-target") fixture.audit.digests.targetIdentitySha256 = "b".repeat(64);
        if (variant === "wrong-profile") fixture.audit.assuranceProfile = "provider";
        if (variant === "wrong-assessment") fixture.audit.coreConformance = "conformant";
        if (variant === "wrong-exit") fixture.audit.recommendedExitCode = 1;
        if (variant === "wrong-clock") fixture.audit.runTerminationOutcome = "cancelled";
        if (variant === "duplicate-bundle") await mkdir(join(root, "mcp-validation-other"));
        if (variant === "path-escape") fixture.audit.artifactPaths = ["../fixture-result.json"];
        if (variant === "duplicate-subject") fixture.audit.artifactPaths.push("fixture-result.json");
        if (variant === "missing-subject") await rm(fixture.resultPath);
        if (variant === "oversized-json") {
          const file = await open(fixture.resultPath, "w");
          try { await file.truncate(16 * 1024 * 1024 + 1); } finally { await file.close(); }
        }
        if (variant === "oversized-subject") {
          const file = await open(join(fixture.bundle, "oversized.html"), "w");
          try { await file.truncate(64 * 1024 * 1024 + 1); } finally { await file.close(); }
          fixture.audit.artifactPaths.push("oversized.html");
          fixture.audit.digests.artifactsSha256["oversized.html"] = "0".repeat(64);
        }
        if (variant.startsWith("aggregate-")) {
          const maximum = 64 * 1024 * 1024;
          const resultBytes = (await stat(fixture.resultPath)).size;
          const hashes = new Map();
          const zeros = Buffer.alloc(64 * 1024);
          for (let index = 0; index < 4; index++) {
            const name = `subject-${index}.html`;
            const length = index === 3 ? maximum - resultBytes + (variant === "aggregate-budget" ? 1 : 0) : maximum;
            const file = await open(join(fixture.bundle, name), "w");
            try { await file.truncate(length); } finally { await file.close(); }
            if (!hashes.has(length)) {
              const digest = createHash("sha256");
              for (let remaining = length; remaining > 0; remaining -= zeros.length)
                digest.update(zeros.subarray(0, Math.min(remaining, zeros.length)));
              hashes.set(length, digest.digest("hex"));
            }
            fixture.audit.artifactPaths.push(name);
            fixture.audit.digests.artifactsSha256[name] = hashes.get(length);
          }
        }
        if (variant === "symlink") {
          await rm(fixture.resultPath);
          await symlink(fixture.auditPath, fixture.resultPath);
        }
        await writeFile(fixture.auditPath, JSON.stringify(fixture.audit));
      }
      const actual = await inspectActionArtifacts(root, code, variant === "plan-only");
      assert.equal(actual.outputs["artifact-state"], expected);
      assert.equal(actual.exitCode, expected === "corrupt" || (expected === "missing" && code === 0) ? 70 : 0);
      if (["published", "partial"].includes(expected)) {
        assert.equal(actual.outputs["validation-id"], "fixture-run");
        assert.equal(actual.outputs["policy-passed"], String(code === 0));
        assert.equal(actual.outputs["core-conformance"], "incomplete");
        assert.equal(JSON.parse(actual.outputs["artifact-paths"]).length, variant === "aggregate-boundary" ? 6 : 2);
      } else {
        assert.equal(actual.outputs["validation-id"], "");
        assert.equal(actual.outputs["result-path"], "");
      }
      assert.ok(!JSON.stringify(actual).includes("private-malformed-canary"));
    } finally { await rm(root, { recursive: true, force: true }); }
  });
}
