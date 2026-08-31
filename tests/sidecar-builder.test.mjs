import assert from 'node:assert/strict';
import { createHash } from 'node:crypto';
import { createRequire } from 'node:module';
import { mkdtemp, mkdir, readFile, rm, writeFile } from 'node:fs/promises';
import { tmpdir } from 'node:os';
import { dirname, join } from 'node:path';
import test from 'node:test';

import { buildSidecar } from '../patcher/src/build-sidecar.mjs';
import { verifyAsarContent } from '../scripts/verify-asar.mjs';
import { verifyAsarDelta } from '../scripts/verify-asar-delta.mjs';

const requireFromBridge = createRequire(new URL('../bridge/package.json', import.meta.url));
const asar = requireFromBridge('@electron/asar');
const fixturePath = join(import.meta.dirname, 'fixtures', 'sidecar-renderer.js');

async function sha256(path) {
  return createHash('sha256').update(await readFile(path)).digest('hex');
}

async function createFixture(root, { compatible = true } = {}) {
  const sourceRoot = join(root, 'source');
  const renderer = join(sourceRoot, 'out', 'renderer', 'js', 'index-fixture.js');
  await mkdir(dirname(renderer), { recursive: true });
  const fixture = await readFile(fixturePath, 'utf8');
  await writeFile(renderer, compatible ? fixture : fixture.replace('  return 0;', '  return 2;'));
  await writeFile(join(sourceRoot, 'unrelated.bin'), Buffer.from([1, 2, 3, 4]));
  const archive = join(root, 'official.asar');
  await asar.createPackage(sourceRoot, archive);
  return archive;
}

test('builds a verified sidecar without changing the official ASAR', async (context) => {
  const root = await mkdtemp(join(tmpdir(), 'cz-sidecar-test-'));
  context.after(() => rm(root, { recursive: true, force: true }));
  const source = await createFixture(root);
  const output = join(root, 'generated', 'app.asar');
  const before = await sha256(source);

  const result = await buildSidecar({
    input: source,
    output,
    token: `czb_${'a'.repeat(64)}`,
  });

  assert.equal(await sha256(source), before);
  assert.match(result.sourceHeaderSha256, /^[a-f0-9]{64}$/);
  assert.match(result.sidecarHeaderSha256, /^[a-f0-9]{64}$/);
  assert.equal(result.sourceLength, (await readFile(source)).length);
  assert.equal(result.sidecarLength, (await readFile(output)).length);
  assert.equal(verifyAsarContent(output).hasGlobalRegion, true);
  assert.deepEqual(verifyAsarDelta(source, output).changedFiles, ['\\out\\renderer\\js\\index-fixture.js']);
});

test('rejects an incompatible ASAR without leaving a sidecar', async (context) => {
  const root = await mkdtemp(join(tmpdir(), 'cz-sidecar-bad-'));
  context.after(() => rm(root, { recursive: true, force: true }));
  const source = await createFixture(root, { compatible: false });
  const output = join(root, 'generated', 'app.asar');

  await assert.rejects(
    buildSidecar({ input: source, output, token: `czb_${'b'.repeat(64)}` }),
    /region routing patch anchor/i,
  );
  await assert.rejects(readFile(output), /ENOENT/);
});
