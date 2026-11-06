import assert from 'node:assert/strict';
import { mkdtemp, mkdir, rm, writeFile } from 'node:fs/promises';
import { tmpdir } from 'node:os';
import { join } from 'node:path';
import test from 'node:test';
import {
  buildEvidenceDocument,
  collectOfficialScenarios,
  loadConformancePin,
  verifyDependencyLock
} from '../run-official-conformance.mjs';

const timestamp = '2026-11-05T12-00-00-000Z';

function check(id) {
  return {
    id,
    name: id,
    description: 'Pinned scenario check',
    status: 'SUCCESS',
    timestamp: '2026-11-05T12:00:00.000Z',
    specReferences: [{ id: 'MCP-1', url: 'https://modelcontextprotocol.io/specification/2026-07-28' }]
  };
}

async function writeScenario(root, scenarioId, suffix = '') {
  const directory = join(root, suffix, `server-${scenarioId}-${timestamp}`);
  await mkdir(directory, { recursive: true });
  await writeFile(join(directory, 'checks.json'), `${JSON.stringify([check(`${scenarioId}-pass`)])}\n`);
}

test('collectOfficialScenarios wraps the complete frozen server set', async () => {
  const pin = await loadConformancePin();
  const root = await mkdtemp(join(tmpdir(), 'mcpval-conformance-test-'));
  try {
    for (const scenarioId of pin.requirementSet.requiredServerScenarios) {
      await writeScenario(root, scenarioId);
    }
    for (const scenario of pin.requirementSet.notScoredServerScenarios) {
      await writeScenario(root, scenario.scenarioId);
    }

    const scenarios = await collectOfficialScenarios(root, pin);
    assert.equal(scenarios.length, 50);
    assert.equal(scenarios.filter(scenario => scenario.scoringDisposition === 'required').length, 37);
    assert.equal(scenarios.filter(scenario => scenario.scoringDisposition === 'notScored').length, 13);
    assert.ok(scenarios.every(scenario => /^[a-f0-9]{64}$/.test(scenario.checksSha256)));
    assert.ok(scenarios.filter(scenario => scenario.scenarioId.startsWith('tasks-'))
      .every(scenario => scenario.extensionId === 'io.modelcontextprotocol/tasks'));

    const document = buildEvidenceDocument({
      pin,
      target: 'https://provider.example.test/mcp',
      startedAt: new Date('2026-11-05T12:00:00Z'),
      endedAt: new Date('2026-11-05T12:01:00Z'),
      exitCode: 0,
      state: 'completed',
      scenarios,
      scenarioTimeoutMs: 30_000
    });
    assert.equal(document.documentType, 'mcpval.official-conformance-evidence');
    assert.equal(document.runner.version, pin.version);
    assert.equal(document.requirementSet.sha256, pin.requirementSet.sha256);
    assert.match(document.target.identitySha256, /^[a-f0-9]{64}$/);
    assert.ok(!JSON.stringify(document.invocation).includes('provider.example.test'));
    assert.ok(document.scenarios.every(scenario => scenario.checkCount === undefined));
    assert.ok(document.scenarios.every(scenario => typeof scenario.checksBase64 === 'string'));
  } finally {
    await rm(root, { recursive: true, force: true });
  }
});

test('collectOfficialScenarios rejects duplicate scenario outputs', async () => {
  const pin = await loadConformancePin();
  const root = await mkdtemp(join(tmpdir(), 'mcpval-conformance-test-'));
  try {
    const scenarioId = pin.requirementSet.requiredServerScenarios[0];
    await writeScenario(root, scenarioId);
    await writeScenario(root, scenarioId, 'copy');

    await assert.rejects(
      collectOfficialScenarios(root, pin),
      /duplicate scenario output/
    );
  } finally {
    await rm(root, { recursive: true, force: true });
  }
});

test('verifyDependencyLock admits only the exact pinned runner', async () => {
  const pin = await loadConformancePin();

  await verifyDependencyLock(pin);
  await assert.rejects(
    verifyDependencyLock({ ...pin, version: 'future-version' }),
    /does not match the trusted runner pin/
  );
});
