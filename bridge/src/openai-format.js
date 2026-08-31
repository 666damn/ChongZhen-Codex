function stripFence(text) {
  const trimmed = String(text ?? '').trim();
  const match = trimmed.match(/^```(?:json)?\s*([\s\S]*?)\s*```$/i);
  return match ? match[1] : trimmed;
}

export function parseCodexEnvelope(text) {
  const parsed = JSON.parse(stripFence(text));
  const toolCalls = Array.isArray(parsed.tool_calls) ? parsed.tool_calls.map((toolCall, index) => ({
    id: String(toolCall?.id || `call_${index + 1}`),
    type: 'function',
    function: {
      name: String(toolCall?.function?.name ?? ''),
      arguments: typeof toolCall?.function?.arguments === 'string'
        ? toolCall.function.arguments
        : JSON.stringify(toolCall?.function?.arguments ?? {}),
    },
  })) : [];
  return {
    content: parsed.content == null ? null : String(parsed.content),
    tool_calls: toolCalls,
    finish_reason: toolCalls.length ? 'tool_calls' : (parsed.finish_reason === 'length' ? 'length' : 'stop'),
  };
}

export function createChatCompletion(envelope, model, requestId) {
  const message = { role: 'assistant', content: envelope.content };
  if (envelope.tool_calls?.length) message.tool_calls = envelope.tool_calls;
  return {
    id: `chatcmpl-${requestId}`,
    object: 'chat.completion',
    created: Math.floor(Date.now() / 1000),
    model,
    choices: [{ index: 0, message, finish_reason: envelope.finish_reason }],
    usage: { prompt_tokens: 0, completion_tokens: 0, total_tokens: 0 },
  };
}

export function createSseChunks(envelope, model, requestId) {
  const id = `chatcmpl-${requestId}`;
  const base = { id, object: 'chat.completion.chunk', created: Math.floor(Date.now() / 1000), model };
  const chunks = [];
  chunks.push(`data: ${JSON.stringify({ ...base, choices: [{ index: 0, delta: { role: 'assistant' }, finish_reason: null }] })}\n\n`);
  if (envelope.content) {
    chunks.push(`data: ${JSON.stringify({ ...base, choices: [{ index: 0, delta: { content: envelope.content }, finish_reason: null }] })}\n\n`);
  }
  if (envelope.tool_calls?.length) {
    chunks.push(`data: ${JSON.stringify({ ...base, choices: [{ index: 0, delta: { tool_calls: envelope.tool_calls.map((toolCall, index) => ({ index, ...toolCall })) }, finish_reason: null }] })}\n\n`);
  }
  chunks.push(`data: ${JSON.stringify({ ...base, choices: [{ index: 0, delta: {}, finish_reason: envelope.finish_reason }] })}\n\n`);
  chunks.push('data: [DONE]\n\n');
  return chunks;
}

export const CODEX_OUTPUT_SCHEMA = {
  type: 'object',
  properties: {
    content: { type: ['string', 'null'] },
    tool_calls: {
      type: 'array',
      items: {
        type: 'object',
        properties: {
          id: { type: 'string' },
          type: { type: 'string', enum: ['function'] },
          function: {
            type: 'object',
            properties: {
              name: { type: 'string' },
              arguments: { type: 'string' },
            },
            required: ['name', 'arguments'],
            additionalProperties: false,
          },
        },
        required: ['id', 'type', 'function'],
        additionalProperties: false,
      },
    },
    finish_reason: { type: 'string', enum: ['stop', 'tool_calls', 'length'] },
  },
  required: ['content', 'tool_calls', 'finish_reason'],
  additionalProperties: false,
};
