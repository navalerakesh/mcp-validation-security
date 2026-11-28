import { createHash } from "node:crypto";
import { appendFile, lstat, mkdir, mkdtemp, open, opendir, realpath } from "node:fs/promises";
import { basename, join } from "node:path";

const maximumJsonBytes = 16 * 1024 * 1024;
const maximumArtifactBytes = 64 * 1024 * 1024;
const maximumBundleBytes = 256 * 1024 * 1024;
const maximumEntries = 64;
const maximumJsonDepth = 128;

class InvalidArtifacts extends Error {}
const requireValue = condition => { if (!condition) throw new InvalidArtifacts(); };
const record = value => {
  requireValue(value !== null && typeof value === "object" && !Array.isArray(value));
  return value;
};
const text = value => {
  requireValue(typeof value === "string" && value.length > 0 && value.length <= 128 && !/[\u0000-\u001f\u007f]/u.test(value));
  return value;
};
const version = (value, major) => typeof value === "string" && new RegExp(`^${major}\\.(0|[1-9][0-9]{0,8})\\.(0|[1-9][0-9]{0,8})$`).test(value);

async function entries(directory) {
  const found = [];
  for await (const entry of await opendir(directory)) {
    requireValue(found.length < maximumEntries && !entry.isSymbolicLink());
    found.push(entry);
  }
  return found;
}

async function regularFile(path, limit) {
  const info = await lstat(path);
  requireValue(info.isFile() && !info.isSymbolicLink() && info.size <= limit);
  const file = await open(path, "r");
  try {
    const opened = await file.stat();
    requireValue(opened.isFile() && opened.dev === info.dev && opened.ino === info.ino && opened.size === info.size);
    return file;
  } catch (error) {
    await file.close();
    throw error;
  }
}

async function json(path, signal) {
  const file = await regularFile(path, maximumJsonBytes);
  try {
    const buffer = Buffer.alloc(maximumJsonBytes + 1);
    let length = 0;
    while (length < buffer.length) {
      signal.throwIfAborted();
      const { bytesRead } = await file.read(buffer, length, buffer.length - length, null);
      if (bytesRead === 0) break;
      length += bytesRead;
    }
    requireValue(length <= maximumJsonBytes);
    const bytes = buffer.subarray(0, length);
    const source = new TextDecoder("utf-8", { fatal: true }).decode(bytes);
    let depth = 0, quoted = false, escaped = false;
    for (const character of source) {
      if (quoted) {
        if (escaped) escaped = false;
        else if (character === "\\") escaped = true;
        else if (character === '"') quoted = false;
      } else if (character === '"') quoted = true;
      else if (character === "{" || character === "[") requireValue(++depth <= maximumJsonDepth);
      else if (character === "}" || character === "]") depth--;
    }
    return { value: record(JSON.parse(source)),
      sha256: createHash("sha256").update(bytes).digest("hex") };
  } finally { await file.close(); }
}

export async function inspectActionArtifacts(directory, collectorExit, dryRun) {
  requireValue(Number.isInteger(collectorExit) && collectorExit >= 0 && collectorExit <= 255 && typeof dryRun === "boolean");
  const base = { "artifact-state": "missing", "artifact-directory": directory, "result-path": "", "audit-path": "",
    "artifact-paths": "[]", "validation-id": "", "run-termination": "", "policy-passed": "",
    "target-identity-sha256": "", "assurance-profile": "", "core-conformance": "", "security-assurance": "", "operational-readiness": "" };
  try {
    const rootInfo = await lstat(directory);
    requireValue(rootInfo.isDirectory() && !rootInfo.isSymbolicLink());
    const root = await realpath(directory);
    const children = await entries(root);
    const bundles = children.filter(entry => entry.isDirectory() && entry.name.startsWith("mcp-validation-"));
    if (dryRun && collectorExit === 0) {
      requireValue(bundles.length === 0);
      return { outputs: { ...base, "artifact-state": "plan-only" }, exitCode: 0 };
    }
    if (bundles.length === 0) return { outputs: base, exitCode: collectorExit === 0 ? 70 : 0 };
    requireValue(bundles.length === 1 && !dryRun);
    const bundle = join(root, bundles[0].name);
    const files = await entries(bundle);
    requireValue(files.every(entry => entry.isFile()));
    const results = files.filter(entry => entry.name.endsWith("-result.json"));
    const audits = files.filter(entry => entry.name.endsWith("-audit.json"));
    requireValue(results.length === 1 && audits.length === 1);
    const signal = AbortSignal.timeout(10_000);
    const resultPath = join(bundle, results[0].name);
    const auditPath = join(bundle, audits[0].name);
    const capturedResult = await json(resultPath, signal);
    const result = capturedResult.value;
    const audit = (await json(auditPath, signal)).value;
    requireValue(result.documentType === "mcpval.assurance-result" && (version(result.documentSchemaVersion, 2) || version(result.documentSchemaVersion, 3)));
    requireValue(audit.documentType === "mcpval.audit-manifest" && version(audit.documentSchemaVersion, 2) && audit.dryRun === false);
    const run = record(result.run);
    const decision = record(result.policyDecision);
    const assurance = record(result.assurance);
    const assessments = record(assurance.assessments);
    const validationId = text(run.validationId);
    const profile = text(record(assurance.profile).profileId);
    const core = text(record(assessments.coreConformance).status);
    const security = text(record(assessments.securityAssurance).status);
    const operations = text(record(assessments.operationalReadiness).status);
    requireValue(audit.validationId === validationId && typeof decision.passed === "boolean");
    requireValue(audit.assuranceProfile === profile && audit.coreConformance === core &&
      audit.securityAssurance === security && audit.operationalReadiness === operations);
    requireValue(audit.recommendedExitCode === collectorExit);
    requireValue(audit.runTerminationOutcome === run.terminationOutcome);
    const paths = audit.artifactPaths;
    const digests = record(audit.digests);
    requireValue(typeof run.targetIdentitySha256 === "string" && /^[a-f0-9]{64}$/.test(run.targetIdentitySha256) &&
      run.targetIdentitySha256 === digests.targetIdentitySha256);
    const hashes = record(digests.artifactsSha256);
    requireValue(Array.isArray(paths) && paths.length > 0 && paths.length <= maximumEntries &&
      paths.every(path => typeof path === "string" && /^[A-Za-z0-9][A-Za-z0-9._-]{0,255}$/.test(path) && basename(path) === path));
    requireValue(new Set(paths.map(path => path.toLowerCase())).size === paths.length &&
      paths.includes(results[0].name) && Object.keys(hashes).length === paths.length);
    requireValue(hashes[results[0].name] === capturedResult.sha256);
    let totalBytes = 0;
    for (const name of paths) {
      requireValue(typeof hashes[name] === "string" && /^[a-f0-9]{64}$/.test(hashes[name]));
      const file = await regularFile(join(bundle, name), maximumArtifactBytes);
      try {
        const digest = createHash("sha256");
        let length = 0;
        for await (const chunk of file.createReadStream({ autoClose: false, signal })) {
          length += chunk.length;
          totalBytes += chunk.length;
          requireValue(length <= maximumArtifactBytes && totalBytes <= maximumBundleBytes);
          digest.update(chunk);
        }
        requireValue(digest.digest("hex") === hashes[name]);
      } finally { await file.close(); }
    }
    const termination = text(run.terminationOutcome);
    return { exitCode: 0, outputs: {
      ...base, "artifact-state": termination === "succeeded" ? "published" : "partial",
      "artifact-directory": bundle, "result-path": resultPath, "audit-path": auditPath,
      "artifact-paths": JSON.stringify([...paths.map(name => join(bundle, name)), auditPath]),
      "validation-id": validationId, "run-termination": termination, "policy-passed": String(decision.passed),
      "target-identity-sha256": run.targetIdentitySha256,
      "assurance-profile": profile, "core-conformance": core, "security-assurance": security, "operational-readiness": operations
    }};
  } catch (error) {
    if (error instanceof InvalidArtifacts || error instanceof SyntaxError ||
        error?.name === "AbortError" || ["ENOENT", "ELOOP", "ENOTDIR", "ERR_ENCODING_INVALID_ENCODED_DATA"].includes(error?.code))
      return { outputs: { ...base, "artifact-state": "corrupt" }, exitCode: 70 };
    throw error;
  }
}

if (import.meta.main) {
  try {
    const [operation, directory, exit, dryRun] = process.argv.slice(2);
    requireValue(typeof directory === "string" && directory.length > 0 && !/[\r\n\u0000]/u.test(directory));
    if (operation === "prepare") {
      await mkdir(directory, { recursive: true });
      console.log(await mkdtemp(join(await realpath(directory), "mcpval-action-")));
    } else {
      requireValue(operation === "publish" && /^(0|[1-9][0-9]{0,2})$/.test(exit ?? "") && ["true", "false"].includes(dryRun));
      const result = await inspectActionArtifacts(directory, Number(exit), dryRun === "true");
      requireValue(typeof process.env.GITHUB_OUTPUT === "string" && process.env.GITHUB_OUTPUT.length > 0);
      await appendFile(process.env.GITHUB_OUTPUT, Object.entries(result.outputs).map(([key, value]) => `${key}=${value}\n`).join(""));
      if (result.exitCode !== 0) console.error("::error::Validation artifacts are missing, corrupt, or inconsistent; collector status is retained separately.");
      process.exitCode = result.exitCode;
    }
  } catch {
    console.error("::error::Unable to prepare or publish typed artifact outputs; no target or file contents are disclosed.");
    process.exitCode = 70;
  }
}
