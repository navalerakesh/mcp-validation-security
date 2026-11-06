#!/usr/bin/env node

import { createHash } from 'node:crypto';
import { spawn } from 'node:child_process';
import { copyFile, mkdtemp, mkdir, readFile, readdir, rename, rm, stat, writeFile } from 'node:fs/promises';
import { tmpdir } from 'node:os';
import { basename, dirname, join, resolve } from 'node:path';
import { fileURLToPath, pathToFileURL } from 'node:url';

const MAX_EVIDENCE_BYTES = 16 * 1024 * 1024;
const MAX_SCENARIOS = 512;
const MAX_CHECKS_PER_SCENARIO = 8192;
const MAX_TEXT_LENGTH = 4096;
const DEFAULT_SCENARIO_TIMEOUT_MS = 30_000;
const DEFAULT_RUN_TIMEOUT_MS = 10 * 60_000;
const SCRIPT_DIR = dirname(fileURLToPath(import.meta.url));
const PIN_PATH = resolve(SCRIPT_DIR, '../Mcp.Benchmark.Core/Resources/official-conformance-pin.json');
const PACKAGE_PATH = resolve(SCRIPT_DIR, 'official-conformance-package.json');
const PACKAGE_LOCK_PATH = resolve(SCRIPT_DIR, 'official-conformance-package-lock.json');

function usage() {
  return `Usage: node scripts/run-official-conformance.mjs --url <https-url> --output <wrapper.json> [options]

Runs the pinned official MCP server conformance requirement set and emits a
mcpval provenance wrapper. The validator never invokes this companion itself.

Options:
  --url <url>                   MCP Streamable HTTP endpoint (required)
  --output <path>               Evidence wrapper output path (required)
  --results-dir <path>          Preserve raw official scenario results
  --scenario-timeout-ms <ms>    Per-scenario timeout (default: 30000)
  --run-timeout-ms <ms>         Whole-run timeout (default: 600000)
  --help                        Show this help
`;
}

function parseArguments(argv) {
  const options = {};
  for (let index = 0; index < argv.length; index += 1) {
    const argument = argv[index];
    if (argument === '--help' || argument === '-h') {
      options.help = true;
      continue;
    }
    if (!argument.startsWith('--')) {
      throw new Error(`Unexpected argument: ${argument}`);
    }
    const value = argv[index + 1];
    if (!value || value.startsWith('--')) {
      throw new Error(`Missing value for ${argument}.`);
    }
    options[argument.slice(2)] = value;
    index += 1;
  }
  return options;
}

function parsePositiveInteger(value, fallback, name) {
  if (value === undefined) {
    return fallback;
  }
  const parsed = Number(value);
  if (!Number.isSafeInteger(parsed) || parsed <= 0) {
    throw new Error(`${name} must be a positive integer.`);
  }
  return parsed;
}

function validateTarget(value) {
  let target;
  try {
    target = new URL(value);
  } catch {
    throw new Error('--url must be an absolute HTTP or HTTPS URL.');
  }
  if (!['http:', 'https:'].includes(target.protocol) || target.username || target.password || target.search || target.hash) {
    throw new Error('--url must be HTTP(S) and must not contain credentials, query parameters, or a fragment.');
  }
  return target.href;
}

export async function loadConformancePin(pinPath = PIN_PATH) {
  const pin = JSON.parse(await readFile(pinPath, 'utf8'));
  if (!pin.packageName || !pin.version || !pin.packageSha256 || !pin.packageIntegrity ||
      !pin.sourceCommit || !pin.requirementSet?.revision || !pin.requirementSet?.sha256 ||
      !Array.isArray(pin.requirementSet.requiredServerScenarios) ||
      !Array.isArray(pin.requirementSet.notScoredServerScenarios)) {
    throw new Error('The official conformance pin manifest is incomplete.');
  }
  return pin;
}

export async function verifyDependencyLock(pin, lockPath = PACKAGE_LOCK_PATH) {
  const lock = JSON.parse(await readFile(lockPath, 'utf8'));
  const rootVersion = lock.packages?.['']?.dependencies?.[pin.packageName];
  const runner = lock.packages?.[`node_modules/${pin.packageName}`];
  if (lock.lockfileVersion !== 3 || rootVersion !== pin.version ||
      runner?.version !== pin.version || runner?.integrity !== pin.packageIntegrity) {
    throw new Error('The official conformance dependency lock does not match the trusted runner pin.');
  }
  for (const [path, packageEntry] of Object.entries(lock.packages)) {
    if (!path || packageEntry.link) continue;
    if (!path.startsWith('node_modules/') || typeof packageEntry.integrity !== 'string' ||
        !packageEntry.resolved?.startsWith('https://registry.npmjs.org/')) {
      throw new Error(`The official conformance dependency lock contains an unpinned package: ${path}.`);
    }
  }
  return lock;
}

function sha256(value) {
  return createHash('sha256').update(value).digest('hex');
}

function requireText(value, field) {
  if (typeof value !== 'string' || value.trim().length === 0 || value.length > MAX_TEXT_LENGTH) {
    throw new Error(`Official result contains an invalid ${field}.`);
  }
  return value;
}

function normalizeCheck(check) {
  if (!check || typeof check !== 'object' || Array.isArray(check)) {
    throw new Error('Official result contains an invalid check.');
  }
  const status = String(check.status ?? '').toLowerCase();
  if (!['success', 'failure', 'warning', 'skipped', 'info'].includes(status)) {
    throw new Error('Official result contains an unsupported check status.');
  }
  const timestamp = new Date(requireText(check.timestamp, 'check timestamp'));
  if (Number.isNaN(timestamp.valueOf())) {
    throw new Error('Official result contains an invalid check timestamp.');
  }
  const specReferences = (check.specReferences ?? []).map(reference => ({
    id: requireText(reference?.id, 'specification reference ID'),
    url: reference?.url == null ? null : requireText(reference.url, 'specification reference URL')
  }));
  return {
    id: requireText(check.id, 'check ID'),
    name: requireText(check.name, 'check name'),
    description: requireText(check.description, 'check description'),
    status,
    timestamp: timestamp.toISOString(),
    specReferences,
    details: check.details ?? null,
    metadata: check.metadata ?? null,
    errorMessage: check.errorMessage == null ? null : String(check.errorMessage)
  };
}

async function findCheckFiles(directory) {
  const files = [];
  async function walk(current) {
    for (const entry of await readdir(current, { withFileTypes: true })) {
      const path = join(current, entry.name);
      if (entry.isSymbolicLink()) {
        throw new Error('Official result directories must not contain symbolic links.');
      }
      if (entry.isDirectory()) {
        await walk(path);
      } else if (entry.isFile() && entry.name === 'checks.json') {
        files.push(path);
      }
    }
  }
  await walk(directory);
  return files.sort();
}

function scenarioIdFromPath(checkPath) {
  return basename(dirname(checkPath))
    .replace(/^server-/, '')
    .replace(/-\d{4}-\d{2}-\d{2}T[\d-]+Z$/, '');
}

export async function collectOfficialScenarios(resultsDirectory, pin) {
  const required = new Set(pin.requirementSet.requiredServerScenarios);
  const notScored = new Map(pin.requirementSet.notScoredServerScenarios.map(scenario => [scenario.scenarioId, scenario]));
  const files = await findCheckFiles(resultsDirectory);
  if (files.length > MAX_SCENARIOS) {
    throw new Error('Official result contains too many scenarios.');
  }
  let totalBytes = 0;
  const scenarioIds = new Set();
  const scenarios = [];
  for (const file of files) {
    const fileStat = await stat(file);
    totalBytes += fileStat.size;
    if (totalBytes > MAX_EVIDENCE_BYTES) {
      throw new Error('Official result exceeds the supported evidence size.');
    }
    const scenarioId = scenarioIdFromPath(file);
    if (scenarioIds.has(scenarioId)) {
      throw new Error(`Official result contains duplicate scenario output: ${scenarioId}.`);
    }
    scenarioIds.add(scenarioId);
    if (!required.has(scenarioId) && !notScored.has(scenarioId)) {
      throw new Error(`Official result contains a scenario outside the pinned requirement set: ${scenarioId}.`);
    }
    const checksJson = await readFile(file);
    const rawChecks = JSON.parse(checksJson.toString('utf8'));
    if (!Array.isArray(rawChecks) || rawChecks.length > MAX_CHECKS_PER_SCENARIO) {
      throw new Error(`Official result contains invalid checks for scenario: ${scenarioId}.`);
    }
    const checks = rawChecks.map(normalizeCheck);
    const checkIds = new Set();
    for (const check of checks) {
      if (!checkIds.add(check.id)) {
        throw new Error(`Official result contains duplicate check IDs for scenario: ${scenarioId}.`);
      }
    }
    const classification = notScored.get(scenarioId);
    scenarios.push({
      scenarioId,
      extensionId: classification?.extensionId ?? null,
      scoringDisposition: classification ? 'notScored' : 'required',
      notScoredReason: classification?.reason ?? null,
      checksSha256: sha256(checksJson),
      checksBase64: checksJson.toString('base64'),
      checkCount: checks.length
    });
  }
  return scenarios.sort((left, right) => left.scenarioId.localeCompare(right.scenarioId));
}

function hasCompleteScenarioSet(scenarios, pin) {
  const expected = new Set([
    ...pin.requirementSet.requiredServerScenarios,
    ...pin.requirementSet.notScoredServerScenarios.map(scenario => scenario.scenarioId)
  ]);
  return scenarios.length === expected.size &&
    scenarios.every(scenario => expected.has(scenario.scenarioId) && scenario.checkCount > 0);
}

export function buildEvidenceDocument({ pin, target, startedAt, endedAt, exitCode, state, scenarios, scenarioTimeoutMs }) {
  return {
    documentType: 'mcpval.official-conformance-evidence',
    documentSchemaVersion: '1.0.0',
    runner: {
      packageName: pin.packageName,
      version: pin.version,
      sourceCommit: pin.sourceCommit,
      packageSha256: pin.packageSha256,
      packageIntegrity: pin.packageIntegrity
    },
    requirementSet: {
      revision: pin.requirementSet.revision,
      sha256: pin.requirementSet.sha256
    },
    target: {
      identitySha256: sha256(Buffer.from(target, 'utf8')),
      transport: 'http',
      protocolVersion: pin.requirementSet.revision
    },
    invocation: {
      mode: 'server',
      sanitizedArguments: [
        'server', '--url', '<target>', '--requirements', pin.requirementSet.revision,
        '--output-dir', '<results>', '--timeout', String(scenarioTimeoutMs), '--verbose'
      ]
    },
    run: {
      startedAtUtc: startedAt.toISOString(),
      endedAtUtc: endedAt.toISOString(),
      exitCode,
      state
    },
    scenarios: scenarios.map(({ checkCount: _, ...scenario }) => scenario)
  };
}

function isolatedEnvironment(homeDirectory) {
  const allowed = ['PATH', 'SystemRoot', 'TMPDIR', 'TMP', 'TEMP', 'NODE_EXTRA_CA_CERTS', 'SSL_CERT_FILE', 'SSL_CERT_DIR'];
  const environment = Object.fromEntries(allowed.filter(key => process.env[key]).map(key => [key, process.env[key]]));
  return {
    ...environment,
    HOME: homeDirectory,
    npm_config_cache: join(homeDirectory, '.npm-cache'),
    npm_config_userconfig: join(homeDirectory, '.npmrc-empty'),
    npm_config_ignore_scripts: 'true',
    npm_config_audit: 'false',
    npm_config_fund: 'false',
    NO_COLOR: '1'
  };
}

function spawnProcess(command, arguments_, options = {}) {
  return new Promise((resolvePromise, rejectPromise) => {
    const child = spawn(command, arguments_, options);
    let stdout = '';
    let stderr = '';
    if (options.stdio === 'pipe') {
      child.stdout.on('data', chunk => { stdout += chunk; });
      child.stderr.on('data', chunk => { stderr += chunk; });
    }
    child.once('error', rejectPromise);
    child.once('close', (code, signal) => resolvePromise({ code: code ?? 1, signal, stdout, stderr }));
  });
}

async function runWithTimeout(command, arguments_, options, timeoutMs) {
  return new Promise((resolvePromise, rejectPromise) => {
    const child = spawn(command, arguments_, options);
    let timedOut = false;
    const timer = setTimeout(() => {
      timedOut = true;
      child.kill('SIGTERM');
      setTimeout(() => child.kill('SIGKILL'), 5_000).unref();
    }, timeoutMs);
    timer.unref();
    child.once('error', error => {
      clearTimeout(timer);
      rejectPromise(error);
    });
    child.once('close', (code, signal) => {
      clearTimeout(timer);
      resolvePromise({ code: code ?? 1, signal, timedOut });
    });
  });
}

async function writeAtomic(path, value) {
  await mkdir(dirname(path), { recursive: true });
  const temporaryPath = `${path}.${process.pid}.tmp`;
  const json = `${JSON.stringify(value, null, 2)}\n`;
  if (Buffer.byteLength(json) > MAX_EVIDENCE_BYTES) {
    throw new Error('Generated official conformance wrapper exceeds the supported evidence size.');
  }
  await writeFile(temporaryPath, json, { encoding: 'utf8', mode: 0o600 });
  await rename(temporaryPath, path);
}

async function ensureEmptyDirectory(path) {
  await mkdir(path, { recursive: true });
  if ((await readdir(path)).length !== 0) {
    throw new Error('--results-dir must be empty to prevent stale scenario evidence.');
  }
}

async function main() {
  const options = parseArguments(process.argv.slice(2));
  if (options.help) {
    process.stdout.write(usage());
    return;
  }
  if (!options.url || !options.output) {
    throw new Error('--url and --output are required.');
  }
  const target = validateTarget(options.url);
  const outputPath = resolve(options.output);
  const scenarioTimeoutMs = parsePositiveInteger(options['scenario-timeout-ms'], DEFAULT_SCENARIO_TIMEOUT_MS, '--scenario-timeout-ms');
  const runTimeoutMs = parsePositiveInteger(options['run-timeout-ms'], DEFAULT_RUN_TIMEOUT_MS, '--run-timeout-ms');
  const pin = await loadConformancePin();
  await verifyDependencyLock(pin);
  const temporaryRoot = await mkdtemp(join(tmpdir(), 'mcpval-conformance-'));
  const resultsDirectory = options['results-dir'] ? resolve(options['results-dir']) : join(temporaryRoot, 'results');
  await ensureEmptyDirectory(resultsDirectory);
  const environment = isolatedEnvironment(join(temporaryRoot, 'home'));
  const npm = process.platform === 'win32' ? 'npm.cmd' : 'npm';

  try {
    const pack = await spawnProcess(npm, ['pack', '--json', '--pack-destination', temporaryRoot, `${pin.packageName}@${pin.version}`], {
      cwd: temporaryRoot,
      env: environment,
      stdio: 'pipe'
    });
    if (pack.code !== 0) {
      throw new Error('Unable to download the pinned official conformance package.');
    }
    const packResult = JSON.parse(pack.stdout)[0];
    if (packResult.integrity !== pin.packageIntegrity) {
      throw new Error('Official conformance package integrity does not match the trusted pin.');
    }
    const tarballPath = join(temporaryRoot, packResult.filename);
    if (sha256(await readFile(tarballPath)) !== pin.packageSha256) {
      throw new Error('Official conformance package digest does not match the trusted pin.');
    }
    await copyFile(PACKAGE_PATH, join(temporaryRoot, 'package.json'));
    await copyFile(PACKAGE_LOCK_PATH, join(temporaryRoot, 'package-lock.json'));
    const install = await spawnProcess(npm, ['ci', '--ignore-scripts'], {
      cwd: temporaryRoot,
      env: environment,
      stdio: 'pipe'
    });
    if (install.code !== 0) {
      throw new Error('Unable to install the verified official conformance package.');
    }
    const packageRoot = join(temporaryRoot, 'node_modules', ...pin.packageName.split('/'));
    const requirementPath = join(packageRoot, 'requirements', `${pin.requirementSet.revision}.yaml`);
    if (sha256(await readFile(requirementPath)) !== pin.requirementSet.sha256) {
      throw new Error('Official conformance requirement set does not match the trusted pin.');
    }

    const startedAt = new Date();
    const run = await runWithTimeout(process.execPath, [
      join(packageRoot, 'dist', 'index.js'), 'server', '--url', target,
      '--requirements', pin.requirementSet.revision,
      '--output-dir', resultsDirectory,
      '--timeout', String(scenarioTimeoutMs), '--verbose'
    ], { cwd: temporaryRoot, env: environment, stdio: 'inherit' }, runTimeoutMs);
    const endedAt = new Date();
    const scenarios = await collectOfficialScenarios(resultsDirectory, pin);
    const state = run.timedOut
      ? 'timedOut'
      : run.signal || !hasCompleteScenarioSet(scenarios, pin)
        ? 'failed'
        : 'completed';
    const document = buildEvidenceDocument({
      pin,
      target,
      startedAt,
      endedAt,
      exitCode: run.code,
      state,
      scenarios,
      scenarioTimeoutMs
    });
    await writeAtomic(outputPath, document);
    process.stdout.write(`Official conformance evidence written to ${outputPath}\n`);
    if (state !== 'completed' || run.code !== 0) {
      process.exitCode = 1;
    }
  } finally {
    await rm(temporaryRoot, { recursive: true, force: true });
  }
}

const entryPath = process.argv[1] ? pathToFileURL(resolve(process.argv[1])).href : '';
if (import.meta.url === entryPath) {
  main().catch(error => {
    process.stderr.write(`Official conformance companion failed: ${error.message}\n`);
    process.exitCode = 70;
  });
}
