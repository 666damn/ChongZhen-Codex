import test from 'node:test';
import assert from 'node:assert/strict';
import { once } from 'node:events';
import { chmod, copyFile, mkdtemp, mkdir, rm, writeFile } from 'node:fs/promises';
import { join } from 'node:path';
import { tmpdir } from 'node:os';
import { fileURLToPath } from 'node:url';

import { CodexClient } from '../src/codex-client.js';
import { discoverCodexExecutable } from '../src/codex-discovery.js';

const fixture = fileURLToPath(new URL('./fixtures/fake-app-server.js', import.meta.url));

test('Codex client initializes, checks login without exposing account data, and completes a turn', async (t) => {
  const client = new CodexClient({ executable: process.execPath, args: [fixture] });
  t.after(() => client.close());

  await client.start();
  assert.equal(client.model, undefined);
  assert.equal(client.defaultModel, 'gpt-5.6-sol');
  assert.equal(await client.readCurrentModel(), 'gpt-5.6-terra');
  assert.equal(await client.isLoggedIn(), true);
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

test('closing the client terminates and releases the app-server child', async () => {
  const client = new CodexClient({ executable: process.execPath, args: [fixture] });
  await client.start();
  const child = client.child;
  client.close();
  let timeout;
  try {
    await Promise.race([
      once(child, 'exit'),
      new Promise((_, reject) => { timeout = setTimeout(() => reject(new Error('app-server child did not exit')), 3000); }),
    ]);
  } finally {
    clearTimeout(timeout);
  }
  assert.notEqual(child.exitCode ?? child.signalCode, null);
});

test('Codex stderr is drained without logging or blocking RPC', async (t) => {
  const client = new CodexClient({
    executable: process.execPath,
    args: [fixture],
    env: { ...process.env, FIXTURE_STDERR_FLOOD: '1' },
  });
  t.after(() => client.close());
  await client.start();
  assert.equal(await client.isLoggedIn(), true);
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

test('Codex discovery prefers the target computer PATH over a packaged executable', async (t) => {
  const root = await mkdtemp(join(tmpdir(), 'cz-codex-path-first-'));
  t.after(() => rm(root, { recursive: true, force: true }));
  const pathDirectory = join(root, 'path-bin');
  await mkdir(pathDirectory, { recursive: true });
  const pathCodex = join(pathDirectory, 'codex.exe');
  const packagedCodex = join(root, 'packaged', 'codex.exe');
  await mkdir(join(root, 'packaged'), { recursive: true });
  await writeFile(pathCodex, 'path');
  await writeFile(packagedCodex, 'packaged');

  assert.equal(
    discoverCodexExecutable({ LOCALAPPDATA: root, PATH: pathDirectory }, { bundledPath: packagedCodex }),
    pathCodex,
  );
});

test('Codex discovery accepts the npm codex.cmd launcher on PATH', async (t) => {
  const root = await mkdtemp(join(tmpdir(), 'cz-codex-cmd-discovery-'));
  t.after(() => rm(root, { recursive: true, force: true }));
  const pathDirectory = join(root, 'npm-bin');
  await mkdir(pathDirectory, { recursive: true });
  const pathCodex = join(pathDirectory, 'codex.cmd');
  await writeFile(pathCodex, '@echo off\r\n');

  assert.equal(discoverCodexExecutable({ LOCALAPPDATA: root, PATH: pathDirectory }), pathCodex);
});

test('Windows npm codex.cmd launchers can run app-server RPC', { skip: process.platform !== 'win32' }, async (t) => {
  const root = await mkdtemp(join(tmpdir(), 'cz-codex-cmd-'));
  t.after(() => rm(root, { recursive: true, force: true }));
  const launcher = join(root, 'codex.cmd');
  const npmEntry = join(root, 'node_modules', '@openai', 'codex', 'bin', 'codex.js');
  await mkdir(join(root, 'node_modules', '@openai', 'codex', 'bin'), { recursive: true });
  await writeFile(launcher, '@echo off\r\n');
  await copyFile(fixture, npmEntry);
  const client = new CodexClient({ executable: launcher });
  t.after(() => client.close());

  await client.start();
  assert.equal(await client.isLoggedIn(), true);
});
