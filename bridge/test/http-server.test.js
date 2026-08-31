import test from 'node:test';
import assert from 'node:assert/strict';

import { createHttpServer } from '../src/http-server.js';

async function listen(t, bridgeService) {
  const server = createHttpServer({ bridgeService, token: 'fixture-token' });
  await new Promise((resolve) => server.listen(0, '127.0.0.1', resolve));
  t.after(() => new Promise((resolve) => server.close(resolve)));
  const { port } = server.address();
  return `http://127.0.0.1:${port}`;
}

test('HTTP server rejects a wrong bearer token', async (t) => {
  const base = await listen(t, { handleChatCompletion() { throw new Error('must not run'); } });
  const response = await fetch(`${base}/v1/chat/completions`, {
    method: 'POST', headers: { 'content-type': 'application/json', authorization: 'Bearer wrong' }, body: '{"messages":[]}',
  });

  assert.equal(response.status, 401);
  assert.equal((await response.json()).error.type, 'authentication_error');
});

test('HTTP server returns normal and SSE OpenAI responses', async (t) => {
  const bridgeService = {
    async handleChatCompletion() {
      return { content: '本机 Codex 回复', tool_calls: [], finish_reason: 'stop', _bridge: { threadId: 'thread-1' } };
    },
  };
  const base = await listen(t, bridgeService);
  const headers = { 'content-type': 'application/json', authorization: 'Bearer fixture-token' };
  const normal = await fetch(`${base}/v1/chat/completions`, { method: 'POST', headers, body: JSON.stringify({ model: 'chat_model', messages: [] }) });
  const streamed = await fetch(`${base}/v1/chat/completions`, { method: 'POST', headers, body: JSON.stringify({ model: 'chat_model', messages: [], stream: true }) });

  assert.equal((await normal.json()).choices[0].message.content, '本机 Codex 回复');
  const sse = await streamed.text();
  assert.match(sse, /本机 Codex 回复/);
  assert.match(sse, /data: \[DONE\]/);
});
