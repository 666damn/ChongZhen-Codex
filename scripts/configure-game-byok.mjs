import { pathToFileURL } from 'node:url';

const STRATEGY_KEY = 'local_codex';
const ROLE_NAMES = [
  'chat_model',
  'court_model',
  'second_model',
  'simulate_model_1',
  'simulate_model_2',
];

function assertByokConfig(config) {
  if (!config?.providers || !config?.custom_llms) {
    throw new Error('The selected localStorage value is not a BYOK configuration');
  }
}

export function mergeLocalCodexStrategy(currentConfig, token) {
  assertByokConfig(currentConfig);
  if (typeof token !== 'string' || !token) throw new Error('A local bridge token is required');
  const roles = Object.fromEntries(
    ROLE_NAMES.map((role) => [role, { provider: STRATEGY_KEY, model: role }]),
  );
  return {
    ...currentConfig,
    providers: {
      ...currentConfig.providers,
      [STRATEGY_KEY]: {
        builtin: false,
        name: '本机 Codex',
        base_url: 'http://127.0.0.1:43129/v1',
        api_key: token,
        extra_headers: {},
        params_override: {},
      },
    },
    custom_llms: {
      ...currentConfig.custom_llms,
      [STRATEGY_KEY]: {
        name: '本机 Codex（跟随当前模型）',
        roles,
      },
    },
  };
}

export function removeLocalCodexStrategy(currentConfig) {
  assertByokConfig(currentConfig);
  const providers = { ...currentConfig.providers };
  const customLlms = { ...currentConfig.custom_llms };
  delete providers[STRATEGY_KEY];
  delete customLlms[STRATEGY_KEY];
  return { ...currentConfig, providers, custom_llms: customLlms };
}

export function findGameStorageTargets(entries) {
  const parsedConfigs = [];
  for (const [key, value] of Object.entries(entries)) {
    try {
      const parsed = JSON.parse(value);
      if (parsed && typeof parsed === 'object' && parsed.providers && parsed.custom_llms) {
        parsedConfigs.push({ key, parsed });
      }
    } catch {
    }
  }
  if (!parsedConfigs.length) throw new Error('No BYOK configuration value was found in game localStorage');

  const activeKeys = Object.keys(entries).filter((key) => key.startsWith('byok_active_strategy_key'));
  if (!activeKeys.length) throw new Error('No active BYOK strategy key was found in game localStorage');

  const config = parsedConfigs.find(({ key }) => key !== 'llm_config') ?? parsedConfigs[0];
  const activeStrategyKey = activeKeys.find((key) => key !== 'byok_active_strategy_key') ?? activeKeys[0];
  return { configKey: config.key, activeStrategyKey };
}

export function transformGameStorage(entries, { action, token } = {}) {
  if (action !== 'install' && action !== 'remove') throw new Error('Action must be install or remove');
  const { configKey, activeStrategyKey } = findGameStorageTargets(entries);
  const currentConfig = JSON.parse(entries[configKey]);
  const currentActive = JSON.parse(entries[activeStrategyKey] ?? 'null');

  if (action === 'install') {
    return {
      configKey,
      activeStrategyKey,
      configValue: JSON.stringify(mergeLocalCodexStrategy(currentConfig, token)),
      activeStrategyValue: JSON.stringify(STRATEGY_KEY),
    };
  }

  const config = removeLocalCodexStrategy(currentConfig);
  const active = currentActive === STRATEGY_KEY
    ? (Object.keys(config.custom_llms)[0] ?? null)
    : currentActive;
  return {
    configKey,
    activeStrategyKey,
    configValue: JSON.stringify(config),
    activeStrategyValue: JSON.stringify(active),
  };
}

class CdpClient {
  constructor(url) {
    this.url = url;
    this.nextId = 1;
    this.pending = new Map();
  }

  async connect() {
    this.socket = new WebSocket(this.url);
    this.socket.addEventListener('message', (event) => {
      const message = JSON.parse(String(event.data));
      if (!message.id || !this.pending.has(message.id)) return;
      const { resolve, reject } = this.pending.get(message.id);
      this.pending.delete(message.id);
      if (message.error) reject(new Error(message.error.message));
      else resolve(message.result);
    });
    await new Promise((resolve, reject) => {
      this.socket.addEventListener('open', resolve, { once: true });
      this.socket.addEventListener('error', () => reject(new Error(`Unable to connect to ${this.url}`)), { once: true });
    });
  }

  request(method, params = {}) {
    const id = this.nextId++;
    return new Promise((resolve, reject) => {
      this.pending.set(id, { resolve, reject });
      this.socket.send(JSON.stringify({ id, method, params }));
    });
  }

  close() {
    this.socket?.close();
  }
}

async function evaluate(client, expression) {
  const result = await client.request('Runtime.evaluate', {
    expression,
    returnByValue: true,
    awaitPromise: true,
  });
  if (result.exceptionDetails) {
    throw new Error(result.exceptionDetails.exception?.description || result.exceptionDetails.text || 'Renderer evaluation failed');
  }
  return result.result?.value;
}

function parseArguments(argv) {
  const options = {};
  for (let index = 0; index < argv.length; index += 1) {
    const key = argv[index];
    if (!key.startsWith('--')) continue;
    options[key.slice(2)] = argv[index + 1];
    index += 1;
  }
  return options;
}

export async function configure({ port, token, action }) {
  const targets = await fetch(`http://127.0.0.1:${port}/json/list`).then((response) => {
    if (!response.ok) throw new Error(`CDP target list returned HTTP ${response.status}`);
    return response.json();
  });
  const target = targets.find((candidate) => candidate.type === 'page' && candidate.webSocketDebuggerUrl);
  if (!target) throw new Error('No game renderer CDP target was found');

  const client = new CdpClient(target.webSocketDebuggerUrl);
  await client.connect();
  try {
    await client.request('Runtime.enable');
    const entries = await evaluate(client, 'Object.fromEntries(Object.keys(localStorage).map((key) => [key, localStorage.getItem(key)]))');
    const transformed = transformGameStorage(entries, { action, token });
    const { configKey, activeStrategyKey, configValue, activeStrategyValue } = transformed;
    const writeExpression = `(() => {
      localStorage.setItem(${JSON.stringify(configKey)}, ${JSON.stringify(configValue)});
      localStorage.setItem(${JSON.stringify(activeStrategyKey)}, ${JSON.stringify(activeStrategyValue)});
      return {
        config: localStorage.getItem(${JSON.stringify(configKey)}),
        active: localStorage.getItem(${JSON.stringify(activeStrategyKey)})
      };
    })()`;
    const written = await evaluate(client, writeExpression);
    const verifiedConfig = JSON.parse(written.config);
    const verifiedActive = JSON.parse(written.active);
    const expectedActive = JSON.parse(activeStrategyValue);
    const installed = Boolean(verifiedConfig.custom_llms?.[STRATEGY_KEY]);
    if (verifiedActive !== expectedActive) throw new Error('Active strategy verification failed');
    if ((action === 'install') !== installed) throw new Error('Local Codex strategy verification failed');
    return {
      result: 'pass',
      targetTitle: target.title,
      configKey,
      activeStrategyKey,
      action,
      activeStrategy: verifiedActive,
      localCodexInstalled: installed,
    };
  } finally {
    client.close();
  }
}

if (process.argv[1] && import.meta.url === pathToFileURL(process.argv[1]).href) {
  const options = parseArguments(process.argv.slice(2));
  if (!options.port || !['install', 'remove'].includes(options.action) || (options.action === 'install' && !options.token)) {
    throw new Error('Usage: node configure-game-byok.mjs --port <port> --action install|remove [--token <token>]');
  }
  const result = await configure({
    port: Number(options.port),
    token: options.token,
    action: options.action,
  });
  process.stdout.write(`${JSON.stringify(result, null, 2)}\n`);
}
