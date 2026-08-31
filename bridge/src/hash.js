import { createHash } from 'node:crypto';

function stableValue(value) {
  if (Array.isArray(value)) return value.map(stableValue);
  if (value && typeof value === 'object') {
    return Object.fromEntries(
      Object.entries(value)
        .filter(([, item]) => item !== undefined)
        .sort(([left], [right]) => left.localeCompare(right))
        .map(([key, item]) => [key, stableValue(item)]),
    );
  }
  return value;
}

function stableJson(value) {
  return JSON.stringify(stableValue(value));
}

function sha256(value) {
  return createHash('sha256').update(value, 'utf8').digest('hex');
}

export function normalizeMessages(messages = []) {
  return messages.map((message) => {
    const normalized = {};
    for (const key of ['role', 'name', 'content', 'tool_call_id']) {
      if (message?.[key] !== undefined) normalized[key] = stableValue(message[key]);
    }
    if (Array.isArray(message?.tool_calls)) {
      normalized.tool_calls = message.tool_calls.map((toolCall) => {
        const fn = toolCall?.function ?? {};
        const args = typeof fn.arguments === 'string' ? fn.arguments : stableJson(fn.arguments ?? {});
        return stableValue({
          id: toolCall?.id,
          type: toolCall?.type ?? 'function',
          function: { name: fn.name, arguments: args },
        });
      });
    }
    return normalized;
  });
}

export function hashMessages(messages = []) {
  return normalizeMessages(messages).map((message) => sha256(stableJson(message)));
}

export function compareHistory(previous = [], incoming = []) {
  let common = 0;
  while (common < previous.length && common < incoming.length && previous[common] === incoming[common]) {
    common += 1;
  }
  if (common === previous.length && common === incoming.length) {
    return { kind: 'duplicate', common, newStart: common };
  }
  if (common === previous.length) {
    return { kind: 'continue', common, newStart: common };
  }
  return { kind: 'diverge', common, newStart: common };
}

export function deriveConversationKey(context = {}, messages = []) {
  const systemMessages = normalizeMessages(messages).filter((message) => message.role === 'system');
  const identity = {
    root: String(context.archive_id || context.archiveId || context.launch_id || context.launchId || 'unsaved'),
    scenario: String(context.scenario_id || context.scenarioId || 'official-chongzhen-v1'),
    role: String(context.role || 'chat_model'),
    operation: String(context.operation_session_id || context.operationSessionId || ''),
    system: sha256(stableJson(systemMessages)),
  };
  return sha256(stableJson(identity));
}

export { stableJson };
