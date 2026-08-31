import test from 'node:test';
import assert from 'node:assert/strict';

import {
  compareHistory,
  deriveConversationKey,
  hashMessages,
  normalizeMessages,
} from '../src/hash.js';

test('message normalization preserves tool-call meaning while removing undefined fields', () => {
  const normalized = normalizeMessages([
    {
      role: 'assistant',
      content: null,
      ignored: undefined,
      tool_calls: [{ id: 'call_1', type: 'function', function: { name: 'ping', arguments: { b: 2, a: 1 } } }],
    },
  ]);

  assert.deepEqual(normalized, [{
    role: 'assistant',
    content: null,
    tool_calls: [{ id: 'call_1', type: 'function', function: { name: 'ping', arguments: '{"a":1,"b":2}' } }],
  }]);
});

test('conversation key is stable but changes for save, role, or system prompt', () => {
  const messages = [{ role: 'system', content: '你是崇祯朝的大臣。' }, { role: 'user', content: '议事。' }];
  const base = { archive_id: 'save-1', scenario_id: 'official', role: 'chat_model', launch_id: 'launch-a' };
  const key = deriveConversationKey(base, messages);

  assert.equal(key, deriveConversationKey({ ...base }, messages));
  assert.notEqual(key, deriveConversationKey({ ...base, archive_id: 'save-2' }, messages));
  assert.notEqual(key, deriveConversationKey({ ...base, role: 'second_model' }, messages));
  assert.notEqual(key, deriveConversationKey(base, [{ role: 'system', content: '另一个人物。' }]));
});

test('history comparison identifies continuation, duplicate, and divergence', () => {
  const first = hashMessages([{ role: 'user', content: '一' }, { role: 'assistant', content: '二' }]);
  const continued = hashMessages([
    { role: 'user', content: '一' },
    { role: 'assistant', content: '二' },
    { role: 'user', content: '三' },
  ]);
  const diverged = hashMessages([{ role: 'user', content: '一' }, { role: 'assistant', content: '不同' }]);

  assert.deepEqual(compareHistory(first, first), { kind: 'duplicate', common: 2, newStart: 2 });
  assert.deepEqual(compareHistory(first, continued), { kind: 'continue', common: 2, newStart: 2 });
  assert.deepEqual(compareHistory(first, diverged), { kind: 'diverge', common: 1, newStart: 1 });
});
