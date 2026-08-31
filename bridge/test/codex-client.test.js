import test from 'node:test';
import assert from 'node:assert/strict';
import { chmod, mkdtemp, mkdir, rm, writeFile } from 'node:fs/promises';
import { join } from 'node:path';
import { tmpdir } from 'node:os';
import { fileURLToPath } from 'node:url';

import { CodexClient } from '../src/codex-client.js';
import { discoverCodexExecutable } from '../src/codex-discovery.js';

const fixture = fileURLToPath(new URL('./fixtures/fake-app-server.js', import.meta.url));

test('Codex client initializes, reads the active account, and completes a turn', async (t) => {
  const client = new CodexClient({ executable: process.execPath, args: [fixture] });
  t.after(() => client.close());

  await client.start();
  assert.equal(client.model, undefined);
  assert.equal(client.defaultModel, 'gpt-5.6-sol');
  assert.equal(await client.readCurrentModel(), 'gpt-5.6-terra');
  assert.deepEqual(await client.readAccount(), {
    type: 'chatgpt', email: 'fixture@example.com', planType: 'pro',
  });
  assert.equal(await client.startThread(), 'thread-fixture');
  assert.equal(await client.resumeThread('thread-existing'), 'thread-existing');

  const result = await client.runTurn('thread-fixture', 'respond with JSON', {
    type: 'object', properties: { content: { type: 'string' } }, required: ['content'], additionalProperties: true,
  });
  assert.deepEqual(result, {
    turnId: 'turn-fixture',
    text: '{"content":"fixture answer","tool_calls":[],"finish_reason":"stop"}',
  });
});

test('Codex client reports a missing stored thread so the bridge can rebuild it', async (t) => {
  const client = new CodexClient({ executable: process.execPath, args: [fixture] });
  t.after(() => client.close());
  await client.start();

  await assert.rejects(client.resumeThread('missing'), /thread not found/);
});

test('Codex discovery selects the newest installed version directory', async (t) => {
  const root = await mkdtemp(join(tmpdir(), 'cz-codex-discovery-'));
  t.after(() => rm(root, { recursive: true, force: true }));
  const oldDirectory = join(root, 'OpenAI', 'Codex', 'bin', '001');
  const newDirectory = join(root, 'OpenAI', 'Codex', 'bin', '999');
  await mkdir(oldDirectory, { recursive: true });
  await mkdir(newDirectory, { recursive: true });
  const oldPath = join(oldDirectory, 'codex.exe');
  const newPath = join(newDirectory, 'codex.exe');
  await writeFile(oldPath, 'old');
  await writeFile(newPath, 'new');
  await chmod(oldPath, 0o755);
  await chmod(newPath, 0o755);

  assert.equal(discoverCodexExecutable({ LOCALAPPDATA: root, PATH: '' }, { bundledPath: join(root, 'missing-bundled.exe') }), newPath);
});
