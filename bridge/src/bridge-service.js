import { createHash } from 'node:crypto';

import {
  compareHistory,
  deriveConversationKey,
  hashMessages,
} from './hash.js';
import { CODEX_OUTPUT_SCHEMA, parseCodexEnvelope } from './openai-format.js';
import { buildCodexPrompt } from './prompt-builder.js';

const SHARED_SCOPE = 'shared-local-codex';

function branchKey(baseKey, historyHashes) {
  const suffix = createHash('sha256').update(historyHashes.join('\n')).digest('hex').slice(0, 16);
  return `${baseKey}#branch-${suffix}`;
}

function assistantMessage(envelope) {
  const message = { role: 'assistant', content: envelope.content };
  if (envelope.tool_calls.length) message.tool_calls = envelope.tool_calls;
  return message;
}

export class BridgeService {
  constructor({ codex, stateStore, maxConcurrency = 2 }) {
    this.codex = codex;
    this.stateStore = stateStore;
    this.queues = new Map();
    this.maxConcurrency = maxConcurrency;
    this.active = 0;
    this.waiters = [];
  }

  async handleChatCompletion(body, signal) {
    const account = await this.codex.readAccount();
    if (!account) {
      throw Object.assign(new Error('请先在 Codex 中登录 ChatGPT 账号'), {
        status: 503,
        type: 'authentication_error',
      });
    }
    const baseKey = deriveConversationKey(body._chongzhen_context ?? { role: body.model }, body.messages ?? []);
    return this.#enqueue(`${SHARED_SCOPE}:${baseKey}`, () => this.#withSlot(
      () => this.#complete({ body, signal, baseKey }),
    ));
  }

  async #complete({ body, signal, baseKey }) {
    const messages = Array.isArray(body.messages) ? body.messages : [];
    const incomingHashes = hashMessages(messages);
    let conversationKey = baseKey;
    let record = this.stateStore.getConversation(SHARED_SCOPE, baseKey)
      ?? this.stateStore.getLatestConversationByKey(baseKey);
    let comparison = record ? compareHistory(record.historyHashes, incomingHashes) : null;

    if (record && comparison.kind === 'diverge') {
      conversationKey = branchKey(baseKey, incomingHashes);
      const existingBranch = this.stateStore.getConversation(SHARED_SCOPE, conversationKey)
        ?? this.stateStore.getLatestConversationByKey(conversationKey);
      if (existingBranch) {
        record = existingBranch;
        comparison = compareHistory(record.historyHashes, incomingHashes);
      } else {
        record = null;
        comparison = null;
      }
    }

    let threadId;
    let promptMessages;
    if (!record) {
      threadId = await this.codex.startThread();
      promptMessages = messages;
    } else {
      try {
        threadId = await this.codex.resumeThread(record.threadId);
      } catch {
        threadId = await this.codex.startThread();
        comparison = null;
      }
      promptMessages = comparison?.kind === 'continue'
        ? messages.slice(comparison.newStart)
        : messages;
    }

    const prompt = buildCodexPrompt(promptMessages, body.tools ?? [], body.tool_choice ?? 'auto');
    const result = await this.codex.runTurn(threadId, prompt, CODEX_OUTPUT_SCHEMA, signal);
    const envelope = parseCodexEnvelope(result.text);
    const storedHashes = hashMessages([...messages, assistantMessage(envelope)]);
    this.stateStore.upsertConversation({
      accountFingerprint: SHARED_SCOPE,
      conversationKey,
      threadId,
      historyHashes: storedHashes,
      parentConversationKey: conversationKey === baseKey ? null : baseKey,
    });
    return {
      ...envelope,
      _bridge: { accountFingerprint: SHARED_SCOPE, conversationKey, threadId, turnId: result.turnId },
    };
  }

  #enqueue(key, operation) {
    const previous = this.queues.get(key) ?? Promise.resolve();
    const next = previous.catch(() => {}).then(operation);
    this.queues.set(key, next);
    return next.finally(() => {
      if (this.queues.get(key) === next) this.queues.delete(key);
    });
  }

  async #withSlot(operation) {
    if (this.active >= this.maxConcurrency) {
      await new Promise((resolve) => this.waiters.push(resolve));
    }
    this.active += 1;
    try {
      return await operation();
    } finally {
      this.active -= 1;
      this.waiters.shift()?.();
    }
  }
}
