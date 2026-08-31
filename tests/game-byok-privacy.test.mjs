import assert from 'node:assert/strict';
import { mkdtemp, readdir, rm } from 'node:fs/promises';
import { tmpdir } from 'node:os';
import { join } from 'node:path';
import test from 'node:test';

import { transformGameStorage } from '../scripts/configure-game-byok.mjs';

test('BYOK transformation never writes a full configuration backup to disk', async (t) => {
  const directory = await mkdtemp(join(tmpdir(), 'cz-byok-privacy-'));
  t.after(() => rm(directory, { recursive: true, force: true }));
  const previous = process.cwd();
  process.chdir(directory);
  try {
    const transformed = transformGameStorage({
      llm_settings_user: JSON.stringify({
        providers: { qwen: { api_key: 'qwen-secret-fixture' } },
        custom_llms: { qwen_choice: { name: 'Qwen' } },
      }),
      byok_active_strategy_key_user: JSON.stringify('qwen_choice'),
    }, { action: 'install', token: 'local-token' });
    assert.equal(JSON.parse(transformed.configValue).providers.qwen.api_key, 'qwen-secret-fixture');
    assert.deepEqual(await readdir(directory), []);
  } finally {
    process.chdir(previous);
  }
});
