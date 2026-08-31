import { randomUUID } from 'node:crypto';
import { createServer } from 'node:http';

import { createChatCompletion, createSseChunks } from './openai-format.js';

function sendJson(response, status, body) {
  const payload = JSON.stringify(body);
  response.writeHead(status, { 'content-type': 'application/json; charset=utf-8', 'content-length': Buffer.byteLength(payload) });
  response.end(payload);
}

function errorBody(error) {
  return { error: { message: error.message || 'Bridge request failed', type: error.type || 'bridge_error', code: error.code || null } };
}

async function readJson(request) {
  const chunks = [];
  let size = 0;
  for await (const chunk of request) {
    size += chunk.length;
    if (size > 5 * 1024 * 1024) throw Object.assign(new Error('Request body too large'), { status: 413 });
    chunks.push(chunk);
  }
  return JSON.parse(Buffer.concat(chunks).toString('utf8'));
}

function isLoopbackHost(host = '') {
  const name = host.replace(/^\[/, '').replace(/\](:\d+)?$/, '').replace(/:\d+$/, '').toLowerCase();
  return name === '127.0.0.1' || name === 'localhost' || name === '::1';
}

export function createHttpServer({ bridgeService, token }) {
  return createServer(async (request, response) => {
    try {
      if (!isLoopbackHost(request.headers.host)) {
        sendJson(response, 403, errorBody(Object.assign(new Error('Loopback Host required'), { type: 'access_error' })));
        return;
      }
      if (request.method === 'GET' && request.url === '/health') {
        sendJson(response, 200, { status: 'ok' });
        return;
      }
      if (request.headers.authorization !== `Bearer ${token}`) {
        sendJson(response, 401, errorBody(Object.assign(new Error('Invalid local bridge token'), { type: 'authentication_error' })));
        return;
      }
      if (request.method === 'GET' && request.url === '/v1/models') {
        sendJson(response, 200, { object: 'list', data: [{ id: 'codex-current', object: 'model', owned_by: 'local-codex' }] });
        return;
      }
      if (request.method !== 'POST' || request.url !== '/v1/chat/completions') {
        sendJson(response, 404, errorBody(Object.assign(new Error('Not found'), { type: 'not_found_error' })));
        return;
      }

      const body = await readJson(request);
      const abortController = new AbortController();
      request.once('aborted', () => abortController.abort());
      const envelope = await bridgeService.handleChatCompletion(body, abortController.signal);
      const requestId = randomUUID().replaceAll('-', '');
      const model = body.model || 'codex-current';
      if (body.stream) {
        response.writeHead(200, { 'content-type': 'text/event-stream; charset=utf-8', 'cache-control': 'no-cache', connection: 'keep-alive' });
        for (const chunk of createSseChunks(envelope, model, requestId)) response.write(chunk);
        response.end();
      } else {
        sendJson(response, 200, createChatCompletion(envelope, model, requestId));
      }
    } catch (error) {
      if (!response.headersSent) sendJson(response, error.status || 500, errorBody(error));
      else response.end();
    }
  });
}
