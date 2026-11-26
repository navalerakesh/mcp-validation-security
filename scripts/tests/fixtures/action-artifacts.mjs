import { createHash } from "node:crypto";
import { mkdir, writeFile } from "node:fs/promises";
import { join } from "node:path";

export async function writeActionBundle(directory, exitCode = 0, termination = "succeeded") {
  const bundle = join(directory, "mcp-validation-fixture");
  await mkdir(bundle, { recursive: true });
  const result = {
    documentType: "mcpval.assurance-result", documentSchemaVersion: "3.8.0",
    run: { validationId: "fixture-run", terminationOutcome: termination, targetIdentitySha256: "a".repeat(64) },
    policyDecision: { passed: exitCode === 0, recommendedExitCode: exitCode === 124 ? 2 : exitCode },
    assurance: { profile: { profileId: "inventory" }, assessments: {
      coreConformance: { status: "incomplete" }, securityAssurance: { status: "incomplete" },
      operationalReadiness: { status: "notAssessed" }
    } }
  };
  const resultBytes = JSON.stringify(result);
  const audit = {
    documentType: "mcpval.audit-manifest", documentSchemaVersion: "2.4.0", dryRun: false,
    validationId: "fixture-run", runTerminationOutcome: termination, recommendedExitCode: exitCode,
    assuranceProfile: "inventory", coreConformance: "incomplete", securityAssurance: "incomplete", operationalReadiness: "notAssessed",
    artifactPaths: ["fixture-result.json"], digests: {
      targetIdentitySha256: "a".repeat(64),
      artifactsSha256: { "fixture-result.json": createHash("sha256").update(resultBytes).digest("hex") }
    }
  };
  const resultPath = join(bundle, "fixture-result.json");
  const auditPath = join(bundle, "fixture-audit.json");
  await writeFile(resultPath, resultBytes);
  await writeFile(auditPath, JSON.stringify(audit));
  return { bundle, result, audit, resultPath, auditPath };
}
