import { readFileSync, writeFileSync } from 'node:fs';
import { pathToFileURL } from 'node:url';

function replaceExactlyOnce(source, anchor, replacement, name) {
  const first = source.indexOf(anchor);
  if (first < 0) throw new Error(`Missing renderer patch anchor: ${name}`);
  if (source.indexOf(anchor, first + anchor.length) >= 0) {
    throw new Error(`Expected exactly once renderer patch anchor: ${name}`);
  }
  return source.slice(0, first) + replacement + source.slice(first + anchor.length);
}

const RESOLVER_ANCHOR = `const resolveByokRole = (role) => {
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
};`;

const COMPLETE_ANCHOR = `const isByokConfigComplete = () => {
  if (!currentConfig) return false;
  return STRATEGY_REQUIRED_ROLES.every((role) => !!resolveByokRole(role));
};`;

export function patchRenderer(source, token) {
  if (!token || typeof token !== 'string') throw new Error('A non-empty local bridge token is required');
  let patched = replaceExactlyOnce(
    source,
    'const byokEnabledAtom = atom(false);',
    'const codexByokEnabledBackingAtom = atom(true);\nconst byokEnabledAtom = atom((get) => get(codexByokEnabledBackingAtom), (_get, set) => set(codexByokEnabledBackingAtom, true));',
    'byokEnabledAtom',
  );
  patched = replaceExactlyOnce(
    patched,
    RESOLVER_ANCHOR,
    `const resolveByokRole = (role) => ({
  url: "http://127.0.0.1:43129/v1/chat/completions",
  apiKey: ${JSON.stringify(token)},
  model: role || "chat_model",
  paramsOverride: {},
  extraHeaders: {}
});`,
    'resolveByokRole',
  );
  patched = replaceExactlyOnce(
    patched,
    COMPLETE_ANCHOR,
    'const isByokConfigComplete = () => true;',
    'isByokConfigComplete',
  );
  patched = replaceExactlyOnce(
    patched,
    '  const body = { ...filtered, model: resolved.model, stream };',
    `  let codexLaunchId = "launch-unknown";
  try {
    const codexLaunchKey = "chongzhen-codex-launch-id";
    codexLaunchId = sessionStorage.getItem(codexLaunchKey);
    if (!codexLaunchId) {
      codexLaunchId = crypto.randomUUID();
      sessionStorage.setItem(codexLaunchKey, codexLaunchId);
    }
  } catch {
  }
  const body = {
    ...filtered,
    model: resolved.model,
    stream,
    _chongzhen_context: {
      archive_id: useArchiveStore.getState().activeArchiveId,
      scenario_id: getScenarioHeaderValue(),
      role: llmRequest.role || resolved.model,
      operation_session_id: llmRequest.session_id,
      launch_id: codexLaunchId
    }
  };`,
    'buildByokRequestBody body',
  );
  return patched;
}

function parseArguments(argv) {
  const values = {};
  for (let index = 0; index < argv.length; index += 2) values[argv[index]] = argv[index + 1];
  return { input: values['--input'], output: values['--output'], token: values['--token'] };
}

if (process.argv[1] && import.meta.url === pathToFileURL(process.argv[1]).href) {
  const { input, output, token } = parseArguments(process.argv.slice(2));
  if (!input || !output || !token) {
    throw new Error('Usage: node patch-renderer.mjs --input <file> --output <file> --token <token>');
  }
  writeFileSync(output, patchRenderer(readFileSync(input, 'utf8'), token), 'utf8');
}
