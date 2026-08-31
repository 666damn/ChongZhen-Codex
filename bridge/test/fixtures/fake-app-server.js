import { createInterface } from 'node:readline';

const lines = createInterface({ input: process.stdin, crlfDelay: Infinity });

function send(message, delay = 0) {
  setTimeout(() => process.stdout.write(`${JSON.stringify(message)}\n`), delay);
}

lines.on('line', (line) => {
  const message = JSON.parse(line);
  if (message.method === 'initialized') return;
  if (message.method === 'echo') {
    send({ id: message.id, result: { value: message.params.value } }, message.params.delay);
    return;
  }
  if (message.method === 'emit/error-notification') {
    send({ id: message.id, result: {} });
    send({ method: 'error', params: { error: { message: 'fixture upstream error' }, willRetry: false } }, 2);
    return;
  }
  if (message.method === 'initialize') {
    send({ id: message.id, result: { userAgent: 'fake-app-server/1.0' } });
    return;
  }
  if (message.method === 'account/read') {
    send({ id: message.id, result: { account: { type: 'chatgpt', email: 'fixture@example.com', planType: 'pro' }, requiresOpenaiAuth: true } });
    return;
  }
  if (message.method === 'model/list') {
    send({ id: message.id, result: { data: [
      { id: 'gpt-5.6-sol', displayName: 'GPT-5.6 Sol', isDefault: true, hidden: false },
      { id: 'gpt-5.5', displayName: 'GPT-5.5', isDefault: false, hidden: false },
    ], nextCursor: null } });
    return;
  }
  if (message.method === 'config/read') {
    send({ id: message.id, result: { config: { model: 'gpt-5.6-terra' }, origins: {} } });
    return;
  }
  if (message.method === 'thread/start') {
    if (message.params.sandbox !== 'read-only') {
      send({ id: message.id, error: { code: -32600, message: `invalid sandbox ${message.params.sandbox}` } });
      return;
    }
    if (message.params.model !== 'gpt-5.6-terra') {
      send({ id: message.id, error: { code: -32600, message: `invalid model ${message.params.model}` } });
      return;
    }
    send({ id: message.id, result: { thread: { id: 'thread-fixture', ephemeral: false } } });
    return;
  }
  if (message.method === 'thread/resume') {
    if (message.params.sandbox !== 'read-only') {
      send({ id: message.id, error: { code: -32600, message: `invalid sandbox ${message.params.sandbox}` } });
      return;
    }
    if (message.params.model !== 'gpt-5.6-terra') {
      send({ id: message.id, error: { code: -32600, message: `invalid model ${message.params.model}` } });
    } else if (message.params.threadId === 'missing') {
      send({ id: message.id, error: { code: -32001, message: 'thread not found' } });
    } else {
      send({ id: message.id, result: { thread: { id: message.params.threadId, ephemeral: false } } });
    }
    return;
  }
  if (message.method === 'turn/start') {
    if (message.params.model !== 'gpt-5.6-terra') {
      send({ id: message.id, error: { code: -32600, message: `invalid model ${message.params.model}` } });
      return;
    }
    send({ id: message.id, result: { turn: { id: 'turn-fixture', status: 'inProgress', items: [], error: null } } });
    send({ method: 'item/completed', params: { threadId: message.params.threadId, turnId: 'turn-fixture', item: { id: 'item-1', type: 'agentMessage', text: '{"content":"fixture answer","tool_calls":[],"finish_reason":"stop"}', phase: 'final_answer' } } }, 4);
    send({ method: 'turn/completed', params: { threadId: message.params.threadId, turn: { id: 'turn-fixture', status: 'completed', items: [], error: null } } }, 8);
    return;
  }
  if (message.method === 'turn/interrupt') {
    send({ id: message.id, result: {} });
    return;
  }
  send({ id: message.id, error: { code: -32601, message: `unknown ${message.method}` } });
});
