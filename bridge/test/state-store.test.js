import test from 'node:test';
import assert from 'node:assert/strict';
import { mkdtemp, rm } from 'node:fs/promises';
import { join } from 'node:path';
import { tmpdir } from 'node:os';

import { StateStore } from '../src/state-store.js';

test('state store persists metadata and conversation mappings across reopen', async (t) => {
  const directory = await mkdtemp(join(tmpdir(), 'cz-bridge-state-'));
  t.after(() => rm(directory, { recursive: true, force: true }));
  const databasePath = join(directory, 'state.db');

  const first = StateStore.open(databasePath);
  first.setMeta('account_salt', 'salt-value');
  first.upsertConversation({
    accountFingerprint: 'account-a',
    conversationKey: 'save-1:chat',
    threadId: 'thread-123',
    historyHashes: ['hash-a', 'hash-b'],
    parentConversationKey: null,
  });
  first.close();

  const second = StateStore.open(databasePath);
  assert.equal(second.getMeta('account_salt'), 'salt-value');
  assert.deepEqual(second.getConversation('account-a', 'save-1:chat'), {
    accountFingerprint: 'account-a',
    conversationKey: 'save-1:chat',
    threadId: 'thread-123',
    historyHashes: ['hash-a', 'hash-b'],
    parentConversationKey: null,
  });
  second.close();
});

test('state store isolates identical game conversations by Codex account', async (t) => {
  const directory = await mkdtemp(join(tmpdir(), 'cz-bridge-accounts-'));
  t.after(() => rm(directory, { recursive: true, force: true }));
  const store = StateStore.open(join(directory, 'state.db'));

  store.upsertConversation({
    accountFingerprint: 'account-a', conversationKey: 'save-1:chat', threadId: 'thread-a', historyHashes: [], parentConversationKey: null,
  });
  store.upsertConversation({
    accountFingerprint: 'account-b', conversationKey: 'save-1:chat', threadId: 'thread-b', historyHashes: [], parentConversationKey: null,
  });

  assert.equal(store.getConversation('account-a', 'save-1:chat').threadId, 'thread-a');
  assert.equal(store.getConversation('account-b', 'save-1:chat').threadId, 'thread-b');
  store.close();
});

test('state store finds a legacy mapping for shared-session migration', async (t) => {
  const directory = await mkdtemp(join(tmpdir(), 'cz-bridge-shared-migration-'));
  t.after(() => rm(directory, { recursive: true, force: true }));
  const store = StateStore.open(join(directory, 'state.db'));
  store.upsertConversation({
    accountFingerprint: 'legacy-account', conversationKey: 'save-1:chat', threadId: 'thread-legacy', historyHashes: ['one'], parentConversationKey: null,
  });

  assert.equal(store.getLatestConversationByKey('save-1:chat').threadId, 'thread-legacy');
  store.close();
});

test('forked conversation records its parent without overwriting the original', async (t) => {
  const directory = await mkdtemp(join(tmpdir(), 'cz-bridge-fork-'));
  t.after(() => rm(directory, { recursive: true, force: true }));
  const store = StateStore.open(join(directory, 'state.db'));
  store.upsertConversation({
    accountFingerprint: 'account-a', conversationKey: 'root', threadId: 'thread-root', historyHashes: ['one'], parentConversationKey: null,
  });

  const forkKey = store.forkConversation({
    accountFingerprint: 'account-a',
    conversationKey: 'root',
    threadId: 'thread-fork',
    historyHashes: ['one', 'different'],
  });

  assert.equal(forkKey, 'root#fork-1');
  assert.equal(store.getConversation('account-a', 'root').threadId, 'thread-root');
  assert.equal(store.getConversation('account-a', forkKey).parentConversationKey, 'root');
  store.close();
});
