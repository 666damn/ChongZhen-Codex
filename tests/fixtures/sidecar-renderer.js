const atom = (...args) => ({ args });
const currentConfig = null;
const byokEnabledAtom = atom(false);
const BYOK_ROLE_ALIAS = { npc_roleplay: "chat_model" };
const normalizeBaseUrl = (baseUrl) => baseUrl.replace(/\/+$/, "");
const resolveByokRole = (role) => {
  if (!currentConfig) return null;
  const roleConfig = currentConfig.roles[role] || currentConfig.roles[BYOK_ROLE_ALIAS[role]] || currentConfig.roles.default;
  if (!roleConfig) return null;
  const provider = currentConfig.providers[roleConfig.provider];
  if (!provider) return null;
  return {
    url: `${normalizeBaseUrl(provider.base_url)}/chat/completions`,
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
const GLOBAL_COUNTRIES = ["HK", "MO", "AU"];
function getIpRegionByCountry(ipCountry) {
  if (!ipCountry) return 1;
  const upper = ipCountry.toUpperCase();
  if (upper === "CN") return 1;
  if (GLOBAL_COUNTRIES.includes(upper)) return 2;
  return 0;
}
