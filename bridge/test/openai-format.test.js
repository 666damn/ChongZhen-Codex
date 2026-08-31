import test from 'node:test';
import assert from 'node:assert/strict';

import {
  createChatCompletion,
  createSseChunks,
  parseCodexEnvelope,
} from '../src/openai-format.js';

test('Codex envelope parser accepts fenced structured output and normalizes tool arguments', () => {
  const result = parseCodexEnvelope('```json\n{"content":null,"tool_calls":[{"id":"call_1","type":"function","function":{"name":"ping","arguments":{"value":1}}}],"finish_reason":"tool_calls"}\n```');

  assert.deepEqual(result, {
    content: null,
    tool_calls: [{ id: 'call_1', type: 'function', function: { name: 'ping', arguments: '{"value":1}' } }],
    finish_reason: 'tool_calls',
  });
});

test('chat completion uses the standard OpenAI response shape', () => {
  const completion = createChatCompletion({ content: '回答', tool_calls: [], finish_reason: 'stop' }, 'chat_model', 'req-1');

  assert.equal(completion.object, 'chat.completion');
  assert.equal(completion.id, 'chatcmpl-req-1');
  assert.deepEqual(completion.choices[0].message, { role: 'assistant', content: '回答' });
  assert.equal(completion.choices[0].finish_reason, 'stop');
});

test('SSE chunks carry tool calls and terminate with DONE', () => {
  const chunks = createSseChunks({
    content: null,
    tool_calls: [{ id: 'call_1', type: 'function', function: { name: 'ping', arguments: '{}' } }],
    finish_reason: 'tool_calls',
  }, 'chat_model', 'req-2');

  assert.match(chunks.join(''), /"tool_calls"/);
  assert.match(chunks.join(''), /"finish_reason":"tool_calls"/);
  assert.equal(chunks.at(-1), 'data: [DONE]\n\n');
});
