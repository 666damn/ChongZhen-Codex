import { DatabaseSync } from 'node:sqlite';

export class StateStore {
  static open(path) {
    return new StateStore(new DatabaseSync(path));
  }

  constructor(database) {
    this.database = database;
    this.database.exec(`
      PRAGMA journal_mode = WAL;
      PRAGMA foreign_keys = ON;
      CREATE TABLE IF NOT EXISTS meta (
        key TEXT PRIMARY KEY,
        value TEXT NOT NULL
      );
      CREATE TABLE IF NOT EXISTS conversations (
        account_fingerprint TEXT NOT NULL,
        conversation_key TEXT NOT NULL,
        thread_id TEXT NOT NULL,
        history_hashes TEXT NOT NULL,
        parent_conversation_key TEXT,
        updated_at INTEGER NOT NULL,
        PRIMARY KEY (account_fingerprint, conversation_key)
      );
    `);
    this.getMetaStatement = this.database.prepare('SELECT value FROM meta WHERE key = ?');
    this.setMetaStatement = this.database.prepare(`
      INSERT INTO meta(key, value) VALUES (?, ?)
      ON CONFLICT(key) DO UPDATE SET value = excluded.value
    `);
    this.getConversationStatement = this.database.prepare(`
      SELECT account_fingerprint, conversation_key, thread_id, history_hashes, parent_conversation_key
      FROM conversations WHERE account_fingerprint = ? AND conversation_key = ?
    `);
    this.getLatestConversationByKeyStatement = this.database.prepare(`
      SELECT account_fingerprint, conversation_key, thread_id, history_hashes, parent_conversation_key
      FROM conversations WHERE conversation_key = ?
      ORDER BY updated_at DESC, rowid DESC LIMIT 1
    `);
    this.upsertConversationStatement = this.database.prepare(`
      INSERT INTO conversations(
        account_fingerprint, conversation_key, thread_id, history_hashes, parent_conversation_key, updated_at
      ) VALUES (?, ?, ?, ?, ?, ?)
      ON CONFLICT(account_fingerprint, conversation_key) DO UPDATE SET
        thread_id = excluded.thread_id,
        history_hashes = excluded.history_hashes,
        parent_conversation_key = excluded.parent_conversation_key,
        updated_at = excluded.updated_at
    `);
  }

  getMeta(key) {
    return this.getMetaStatement.get(key)?.value ?? null;
  }

  setMeta(key, value) {
    this.setMetaStatement.run(key, String(value));
  }

  getConversation(accountFingerprint, conversationKey) {
    const row = this.getConversationStatement.get(accountFingerprint, conversationKey);
    if (!row) return null;
    return {
      accountFingerprint: row.account_fingerprint,
      conversationKey: row.conversation_key,
      threadId: row.thread_id,
      historyHashes: JSON.parse(row.history_hashes),
      parentConversationKey: row.parent_conversation_key,
    };
  }

  getLatestConversationByKey(conversationKey) {
    const row = this.getLatestConversationByKeyStatement.get(conversationKey);
    if (!row) return null;
    return {
      accountFingerprint: row.account_fingerprint,
      conversationKey: row.conversation_key,
      threadId: row.thread_id,
      historyHashes: JSON.parse(row.history_hashes),
      parentConversationKey: row.parent_conversation_key,
    };
  }

  upsertConversation(record) {
    this.upsertConversationStatement.run(
      record.accountFingerprint,
      record.conversationKey,
      record.threadId,
      JSON.stringify(record.historyHashes ?? []),
      record.parentConversationKey ?? null,
      Date.now(),
    );
  }

  forkConversation({ accountFingerprint, conversationKey, threadId, historyHashes }) {
    let index = 1;
    let forkKey = `${conversationKey}#fork-${index}`;
    while (this.getConversation(accountFingerprint, forkKey)) {
      index += 1;
      forkKey = `${conversationKey}#fork-${index}`;
    }
    this.upsertConversation({
      accountFingerprint,
      conversationKey: forkKey,
      threadId,
      historyHashes,
      parentConversationKey: conversationKey,
    });
    return forkKey;
  }

  close() {
    this.database.close();
  }
}
