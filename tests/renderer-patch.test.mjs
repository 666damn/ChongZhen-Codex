import test from 'node:test';
import assert from 'node:assert/strict';
import vm from 'node:vm';

import { patchRenderer } from '../scripts/patch-renderer.mjs';

const fixture = `
const atom = (...args) => ({ args });
const currentConfig = null;
const byokEnabledAtom = atom(false);
const BYOK_ROLE_ALIAS = { npc_roleplay: "chat_model" };
const normalizeBaseUrl = (baseUrl) => baseUrl.replace(/\\/+$/, "");
const resolveByokRole = (role) => {
  if (!currentConfig) return null;
  const roleConfig = currentConfig.roles[role] || currentConfig.roles[BYOK_ROLE_ALIAS[role]] || currentConfig.roles.default;
  if (!roleConfig) return null;
  const provider = currentConfig.providers[roleConfig.provider];
  if (!provider) return null;
  return {
    url: \`\${normalizeBaseUrl(provider.base_url)}/chat/completions\`,
    apiKey: provider.api_key,
    model: roleConfig.model,
    paramsOverride: provider.params_override,
    extraHeaders: provider.extra_headers
  };
};
const STRATEGY_REQUIRED_ROLES = ["chat_model", "simulate_model_1", "second_model"];
const isByokConfigComplete = () => {
  if (!currentConfig) return false;
  return STRATEGY_REQUIRED_ROLES.every((role) => !!resolveByokRole(role));
};
const PROTECTED_OVERRIDE_KEYS = new Set(["messages", "tools", "stream", "model"]);
const BACKEND_INTERNAL_KEYS = new Set(["role", "multi_round_fc"]);
const buildByokRequestBody = (llmRequest, resolved, stream) => {
  const filtered = {};
  for (const [k2, v2] of Object.entries(llmRequest)) {
    if (!BACKEND_INTERNAL_KEYS.has(k2)) filtered[k2] = v2;
  }
  const body = { ...filtered, model: resolved.model, stream };
  return body;
};
const useArchiveStore = { getState: () => ({ activeArchiveId: "save-fixture" }) };
const getScenarioHeaderValue = () => "scenario-fixture";
`;

function evaluate(source) {
  const storage = new Map();
  const context = {
    sessionStorage: {
      getItem: (key) => storage.get(key) ?? null,
      setItem: (key, value) => storage.set(key, String(value)),
    },
    crypto: { randomUUID: () => 'launch-fixture' },
  };
  vm.runInNewContext(`${source}\n;globalThis.bridgeApi={resolveByokRole,isByokConfigComplete,buildByokRequestBody};`, context);
  return context.bridgeApi;
}

test('renderer patch forces every model role through the authenticated local Codex bridge', () => {
  const patched = patchRenderer(fixture, 'token-fixture');
  const api = evaluate(patched);

  assert.deepEqual(JSON.parse(JSON.stringify(api.resolveByokRole('court_model'))), {
    url: 'http://127.0.0.1:43129/v1/chat/completions',
    apiKey: 'token-fixture',
    model: 'court_model',
    paramsOverride: {},
    extraHeaders: {},
  });
  assert.equal(api.isByokConfigComplete(), true);
});

test('renderer patch adds stable save, scenario, role, operation, and launch context to requests', () => {
  const api = evaluate(patchRenderer(fixture, 'token-fixture'));
  const resolved = api.resolveByokRole('chat_model');
  const body = api.buildByokRequestBody({
    role: 'chat_model',
    session_id: 'operation-9',
    messages: [{ role: 'user', content: 'test' }],
  }, resolved, true);

  assert.deepEqual({ ...body._chongzhen_context }, {
    archive_id: 'save-fixture',
    scenario_id: 'scenario-fixture',
    role: 'chat_model',
    operation_session_id: 'operation-9',
    launch_id: 'launch-fixture',
  });
  assert.equal(body.role, undefined);
});

test('renderer patch refuses ambiguous or already-modified source anchors', () => {
  assert.throws(() => patchRenderer(fixture.replace('const byokEnabledAtom = atom(false);', ''), 'token'), /anchor.*byokEnabledAtom/i);
  assert.throws(() => patchRenderer(`${fixture}\nconst byokEnabledAtom = atom(false);`, 'token'), /exactly once.*byokEnabledAtom/i);
});
