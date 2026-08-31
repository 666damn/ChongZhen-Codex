import assert from 'node:assert/strict';
import test from 'node:test';

import {
  findGameStorageTargets,
  mergeLocalCodexStrategy,
  removeLocalCodexStrategy,
  transformGameStorage,
} from '../scripts/configure-game-byok.mjs';

test('finds user-scoped game config and active strategy keys by their values', () => {
  const entries = {
    unrelated: 'value',
    llm_settings_user_42: JSON.stringify({ providers: { qwen: {} }, custom_llms: { qwen_choice: {} } }),
    byok_active_strategy_key_user_42: JSON.stringify('qwen_choice'),
  };

  assert.deepEqual(findGameStorageTargets(entries), {
    configKey: 'llm_settings_user_42',
    activeStrategyKey: 'byok_active_strategy_key_user_42',
  });
});

test('adds and selects local Codex without overwriting existing BYOK settings', () => {
  const original = {
    providers: {
      qwen: { name: 'Qwen', base_url: 'https://qwen.invalid/v1', api_key: 'qwen-key' },
    },
    custom_llms: {
      qwen_choice: { name: 'Qwen3.5 9B', roles: { chat_model: { provider: 'qwen', model: 'qwen3.5-9b' } } },
    },
  };

  const merged = mergeLocalCodexStrategy(original, 'local-token');

  assert.equal(merged.providers.qwen.api_key, 'qwen-key');
  assert.equal(merged.custom_llms.qwen_choice.name, 'Qwen3.5 9B');
  assert.deepEqual(merged.providers.local_codex, {
    builtin: false,
    name: '\u672c\u673a Codex',
    base_url: 'http://127.0.0.1:43129/v1',
    api_key: 'local-token',
    extra_headers: {},
    params_override: {},
  });
  assert.equal(merged.custom_llms.local_codex.name, '\u672c\u673a Codex\uff08\u8ddf\u968f\u5f53\u524d\u6a21\u578b\uff09');
  assert.deepEqual(Object.keys(merged.custom_llms.local_codex.roles).sort(), [
    'chat_model', 'court_model', 'second_model', 'simulate_model_1', 'simulate_model_2',
  ]);
  for (const [roleName, role] of Object.entries(merged.custom_llms.local_codex.roles)) {
    assert.deepEqual(role, { provider: 'local_codex', model: roleName });
  }
});

test('removes only local Codex and selects a safe existing fallback', () => {
  const current = mergeLocalCodexStrategy({
    providers: { qwen: { api_key: 'preserved' } },
    custom_llms: { qwen_choice: { name: 'Qwen' } },
  }, 'local-token');

  const removed = removeLocalCodexStrategy(current);
  assert.deepEqual(removed, {
    providers: { qwen: { api_key: 'preserved' } },
    custom_llms: { qwen_choice: { name: 'Qwen' } },
  });

  const transformed = transformGameStorage({
    llm_settings_user: JSON.stringify(current),
    byok_active_strategy_key_user: JSON.stringify('local_codex'),
  }, { action: 'remove' });
  assert.equal(JSON.parse(transformed.activeStrategyValue), 'qwen_choice');
  assert.deepEqual(JSON.parse(transformed.configValue), removed);
});
