import { existsSync, mkdirSync, readFileSync, rmSync, writeFileSync } from 'node:fs';
import { join } from 'node:path';

import { BridgeService } from './bridge-service.js';
import { CodexClient } from './codex-client.js';
import { createHttpServer } from './http-server.js';
import { buildReadyStatus } from './ready-status.js';
import { StateStore } from './state-store.js';

const runtimeRoot = process.env.CHONGZHEN_BRIDGE_HOME
  || join(process.env.LOCALAPPDATA || process.cwd(), 'ChongZhenCodexBridge');
const configPath = process.env.CHONGZHEN_BRIDGE_CONFIG || join(runtimeRoot, 'config.json');
const config = existsSync(configPath) ? JSON.parse(readFileSync(configPath, 'utf8')) : {};
const token = process.env.CHONGZHEN_BRIDGE_TOKEN || config.token;
const port = Number(process.env.CHONGZHEN_BRIDGE_PORT || config.port || 43129);

if (!token) throw new Error(`Local bridge token is missing: ${configPath}`);
mkdirSync(runtimeRoot, { recursive: true });
const codexCwd = join(runtimeRoot, 'codex-cwd');
mkdirSync(codexCwd, { recursive: true });

const stateStore = StateStore.open(join(runtimeRoot, 'state.db'));
const codex = new CodexClient({ cwd: codexCwd });
const bridgeService = new BridgeService({ codex, stateStore });
const server = createHttpServer({ bridgeService, token });
const readyPath = join(runtimeRoot, 'bridge-ready.json');

function shutdown(exitCode = 0) {
  rmSync(readyPath, { force: true });
  server.close(() => {
    codex.close();
    stateStore.close();
    process.exit(exitCode);
  });
  setTimeout(() => process.exit(exitCode), 3000).unref();
}

process.once('SIGINT', () => shutdown(0));
process.once('SIGTERM', () => shutdown(0));
process.once('uncaughtException', (error) => {
  process.stderr.write(`${error.stack || error.message}\n`);
  shutdown(1);
});
process.once('unhandledRejection', (error) => {
  process.stderr.write(`${error?.stack || error}\n`);
  shutdown(1);
});

server.listen(port, '127.0.0.1', async () => {
  try {
    await codex.start();
    if (!await codex.isLoggedIn()) throw new Error('Codex CLI is not logged in');
    writeFileSync(readyPath, JSON.stringify(buildReadyStatus({
      pid: process.pid,
      port,
      startedAt: new Date().toISOString(),
    }), null, 2));
    process.stdout.write(`ChongZhen Codex Bridge ready on 127.0.0.1:${port}\n`);
  } catch (error) {
    process.stderr.write(`Codex startup failed: ${error.message}\n`);
  }
});
