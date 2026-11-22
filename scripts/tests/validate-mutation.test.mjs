import assert from 'node:assert/strict';
import { spawnSync } from 'node:child_process';
import { chmodSync, copyFileSync, mkdirSync, mkdtempSync, readFileSync, rmSync, writeFileSync } from 'node:fs';
import { tmpdir } from 'node:os';
import { delimiter, join } from 'node:path';
import test from 'node:test';

for (const fixture of [
  { name: 'ordinary completion', output: 'The final mutation score is 95 %', exit: 0, expected: 0 },
  { name: 'whole-method instrumentation rollback', output: "Safe Mode! Stryker will remove all mutations in Evaluate and mark them as 'compile error'.", exit: 0, expected: 1 },
  { name: 'Stryker failure', output: 'The final mutation score is 80 %', exit: 2, expected: 2 },
  { name: 'stderr rollback', output: 'Safe Mode! Stryker removed a method.', stderr: true, exit: 0, expected: 1 },
  { name: 'log inspection error', output: 'The final mutation score is 95 %', inspectionExit: 2, exit: 0, expected: 1 },
  { name: 'log inspector unavailable', output: 'The final mutation score is 95 %', inspectionExit: 127, exit: 0, expected: 1 },
]) {
  test(`mutation gate rejects false readiness: ${fixture.name}`, () => {
    const root = mkdtempSync(join(tmpdir(), 'mcpval-mutation-gate-'));
    try {
      mkdirSync(join(root, 'scripts'));
      mkdirSync(join(root, 'bin'));
      const script = join(root, 'scripts', 'validate-mutation.sh');
      copyFileSync(new URL('../validate-mutation.sh', import.meta.url), script);
      const dotnet = join(root, 'bin', 'dotnet');
      writeFileSync(dotnet, `#!/usr/bin/env node
const { writeFileSync } = require('node:fs');
if (process.argv[2] === 'tool') process.exit(0);
writeFileSync(process.env.MUTATION_ARGUMENTS, JSON.stringify(process.argv.slice(2)));
if (process.env.MUTATION_FIXTURE_STDERR === 'true') console.error(process.env.MUTATION_FIXTURE_OUTPUT);
else console.log(process.env.MUTATION_FIXTURE_OUTPUT);
process.exit(Number(process.env.MUTATION_FIXTURE_EXIT));
`);
      chmodSync(dotnet, 0o700);
      if (fixture.inspectionExit !== undefined) {
        const grep = join(root, 'bin', 'grep');
        writeFileSync(grep, `#!/usr/bin/env node\nprocess.exit(${fixture.inspectionExit});\n`);
        chmodSync(grep, 0o700);
      }
      const output = join(root, 'output');
      const argsFile = join(root, 'arguments.json');
      const result = spawnSync('bash', [script.replaceAll('\\', '/')], {
        encoding: 'utf8', timeout: 10_000,
        env: { ...process.env, PATH: `${join(root, 'bin')}${delimiter}${process.env.PATH}`, MUTATION_OUTPUT_DIR: output.replaceAll('\\', '/'),
          MUTATION_ARGUMENTS: argsFile, MUTATION_FIXTURE_OUTPUT: fixture.output, MUTATION_FIXTURE_EXIT: String(fixture.exit),
          MUTATION_FIXTURE_STDERR: String(fixture.stderr ?? false) },
      });
      assert.equal(result.error, undefined);
      assert.equal(result.signal, null);
      assert.equal(result.status, fixture.expected, result.stderr);
      assert.ok(readFileSync(join(output, 'stryker.log'), 'utf8').includes(fixture.output));
      const args = JSON.parse(readFileSync(argsFile, 'utf8'));
      assert.equal(args[args.indexOf('--break-at') + 1], '90');
      assert.equal(args[args.indexOf('--threshold-low') + 1], '90');
      assert.deepEqual(args.filter((value, index) => args[index - 1] === '--mutate'), [
        '**/AssuranceAssessmentEngine.cs', '**/ProviderHarnessEvaluator.cs', '**/RuleCalibrationEvaluator.cs',
        '**/ControlledStateEvaluator.cs', '**/ControlledIdentityEvaluator.cs', '**/ControlledEvidenceAdmission.cs',
      ]);
    } finally {
      rmSync(root, { recursive: true, force: true });
    }
  });
}