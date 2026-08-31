import test from 'node:test';
import assert from 'node:assert/strict';
import { spawn } from 'node:child_process';
import { fileURLToPath } from 'node:url';

import { JsonlRpcClient } from '../src/jsonl-rpc.js';

const fixture = fileURLToPath(new URL('./fixtures/fake-app-server.js', import.meta.url));

test('JSONL RPC correlates out-of-order responses to their requests', async (t) => {
  const child = spawn(process.execPath, [fixture], { stdio: ['pipe', 'pipe', 'pipe'] });
  t.after(() => child.kill());
  const rpc = new JsonlRpcClient(child);

  const slow = rpc.request('echo', { value: 'slow', delay: 20 });
  const fast = rpc.request('echo', { value: 'fast', delay: 1 });

  assert.deepEqual(await fast, { value: 'fast' });
  assert.deepEqual(await slow, { value: 'slow' });
  rpc.close();
});

test('JSONL RPC surfaces protocol errors with code and message', async (t) => {
  const child = spawn(process.execPath, [fixture], { stdio: ['pipe', 'pipe', 'pipe'] });
  t.after(() => child.kill());
  const rpc = new JsonlRpcClient(child);

  await assert.rejects(
    rpc.request('unknown/method', {}),
    (error) => error.code === -32601 && error.message === 'unknown unknown/method',
  );
  rpc.close();
});

test('JSONL RPC maps an app-server error notification without triggering Node unhandled error semantics', async (t) => {
  const child = spawn(process.execPath, [fixture], { stdio: ['pipe', 'pipe', 'pipe'] });
  t.after(() => child.kill());
  const rpc = new JsonlRpcClient(child);
  const received = new Promise((resolve) => rpc.once('rpcError', resolve));

  await rpc.request('emit/error-notification', {});
  const notification = await received;

  assert.equal(notification.error.message, 'fixture upstream error');
  rpc.close();
});
