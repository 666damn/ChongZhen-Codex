import test from 'node:test';
import assert from 'node:assert/strict';
import { mkdtemp, rm } from 'node:fs/promises';
import { join } from 'node:path';
import { tmpdir } from 'node:os';

import { BridgeService } from '../src/bridge-service.js';
import { deriveConversationKey, hashMessages } from '../src/hash.js';
import { StateStore } from '../src/state-store.js';

class FixtureCodex {
  constructor() {
    this.account = { type: 'chatgpt', email: 'one@example.com', planType: 'pro' };
    this.nextThread = 1;
    this.turns = [];
    this.resumed = [];
  }

  async isLoggedIn() { return Boolean(this.account); }
  async startThread() { return `thread-${this.nextThread++}`; }
  async resumeThread(threadId) { this.resumed.push(threadId); return threadId; }
  async runTurn(threadId, prompt) {
    this.turns.push({ threadId, prompt });
    const answer = `answer-${this.turns.length}`;
    return { turnId: `turn-${this.turns.length}`, text: JSON.stringify({ content: answer, tool_calls: [], finish_reason: 'stop' }) };
  }
}

async function fixture(t) {
  const directory = await mkdtemp(join(tmpdir(), 'cz-bridge-service-'));
  const stateStore = StateStore.open(join(directory, 'state.db'));
  t.after(async () => {
    stateStore.close();
    await rm(directory, { recursive: true, force: true });
  });
  const codex = new FixtureCodex();
  return { codex, stateStore, service: new BridgeService({ codex, stateStore, salt: '00112233445566778899aabbccddeeff' }) };
}

const context = { archive_id: 'save-1', scenario_id: 'official', role: 'chat_model', launch_id: 'launch-1' };

test('same game history continues the persisted Codex thread', async (t) => {
  const { codex, service } = await fixture(t);
  const first = await service.handleChatCompletion({
    model: 'chat_model', messages: [{ role: 'system', content: 'identity' }, { role: 'user', content: 'first question' }], _chongzhen_context: context,
  });
  const second = await service.handleChatCompletion({
    model: 'chat_model', messages: [
      { role: 'system', content: 'identity' },
      { role: 'user', content: 'first question' },
      { role: 'assistant', content: 'answer-1' },
      { role: 'user', content: 'second question' },
    ], _chongzhen_context: context,
  });

  assert.equal(first._bridge.threadId, 'thread-1');
  assert.equal(second._bridge.threadId, 'thread-1');
  assert.deepEqual(codex.resumed, ['thread-1']);
  assert.doesNotMatch(codex.turns[1].prompt, /first question/);
  assert.match(codex.turns[1].prompt, /second question/);
});

test('switching Codex accounts continues the same shared game thread', async (t) => {
  const { codex, service } = await fixture(t);
  const first = await service.handleChatCompletion({
    model: 'chat_model', messages: [{ role: 'user', content: 'shared question' }], _chongzhen_context: context,
  });
  codex.account = { type: 'chatgpt', email: 'two@example.com', planType: 'pro' };
  const second = await service.handleChatCompletion({
    model: 'chat_model', messages: [
      { role: 'user', content: 'shared question' },
      { role: 'assistant', content: 'answer-1' },
      { role: 'user', content: 'continue after account switch' },
    ], _chongzhen_context: context,
  });
  codex.account = { type: 'chatgpt', email: 'one@example.com', planType: 'pro' };
  const returned = await service.handleChatCompletion({
    model: 'chat_model', messages: [
      { role: 'user', content: 'shared question' },
      { role: 'assistant', content: 'answer-1' },
      { role: 'user', content: 'continue after account switch' },
      { role: 'assistant', content: 'answer-2' },
      { role: 'user', content: 'continue after switching back' },
    ], _chongzhen_context: context,
  });

  assert.equal(first._bridge.threadId, 'thread-1');
  assert.equal(second._bridge.threadId, 'thread-1');
  assert.equal(returned._bridge.threadId, 'thread-1');
  assert.deepEqual(codex.resumed, ['thread-1', 'thread-1']);
});

test('legacy account-scoped mapping migrates into the shared session', async (t) => {
  const { codex, stateStore, service } = await fixture(t);
  const legacyMessages = [
    { role: 'user', content: 'legacy question' },
    { role: 'assistant', content: 'legacy answer' },
  ];
  const key = deriveConversationKey(context, legacyMessages);
  stateStore.upsertConversation({
    accountFingerprint: 'legacy-account',
    conversationKey: key,
    threadId: 'thread-legacy',
    historyHashes: hashMessages(legacyMessages),
    parentConversationKey: null,
  });

  const result = await service.handleChatCompletion({
    model: 'chat_model',
    messages: [...legacyMessages, { role: 'user', content: 'continue legacy session' }],
    _chongzhen_context: context,
  });

  assert.equal(result._bridge.threadId, 'thread-legacy');
  assert.equal(stateStore.getConversation('shared-local-codex', key).threadId, 'thread-legacy');
  assert.deepEqual(codex.resumed, ['thread-legacy']);
});

test('divergent loaded history gets a deterministic branch and reuses it', async (t) => {
  const { service } = await fixture(t);
  await service.handleChatCompletion({ model: 'chat_model', messages: [{ role: 'user', content: 'original timeline' }], _chongzhen_context: context });
  const branchRequest = { model: 'chat_model', messages: [{ role: 'user', content: 'alternate timeline' }], _chongzhen_context: context };
  const firstBranch = await service.handleChatCompletion(branchRequest);
  const sameBranch = await service.handleChatCompletion(branchRequest);

  assert.equal(firstBranch._bridge.threadId, 'thread-2');
  assert.equal(sameBranch._bridge.threadId, 'thread-2');
  assert.match(firstBranch._bridge.conversationKey, /#branch-/);
});
