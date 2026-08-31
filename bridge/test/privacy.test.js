import test from 'node:test';
import assert from 'node:assert/strict';

test('ready status persists operational state without account or model data', async () => {
  let module;
  try {
    module = await import('../src/ready-status.js');
  } catch {
    module = null;
  }
  assert.ok(module, 'ready status builder should exist');

  const status = module.buildReadyStatus({
    pid: 42,
    port: 43129,
    startedAt: '2026-08-31T00:00:00.000Z',
    account: { type: 'chatgpt', email: 'private@example.test', planType: 'pro' },
    configuredModel: 'private-model-name',
    defaultModel: 'private-default-model',
  });

  assert.deepEqual(status, {
    pid: 42,
    port: 43129,
    modelMode: 'follow-local-codex',
    startedAt: '2026-08-31T00:00:00.000Z',
  });
  assert.doesNotMatch(JSON.stringify(status), /private|chatgpt|pro/i);
});
