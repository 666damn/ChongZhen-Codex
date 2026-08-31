import { execFile } from 'node:child_process';
import { openSync, mkdirSync } from 'node:fs';
import { join } from 'node:path';
import { promisify } from 'node:util';
import { fileURLToPath } from 'node:url';
import { spawn } from 'node:child_process';

import { GameWatcher } from './watcher.js';

const execFileAsync = promisify(execFile);
const runtimeRoot = process.env.CHONGZHEN_BRIDGE_HOME
  || join(process.env.LOCALAPPDATA || process.cwd(), 'ChongZhenCodexBridge');
const logDirectory = join(runtimeRoot, 'logs');
mkdirSync(logDirectory, { recursive: true });
const bridgeMain = fileURLToPath(new URL('./main.js', import.meta.url));

async function gameIsRunning() {
  try {
    const { stdout } = await execFileAsync('tasklist.exe', [
      '/FI', 'IMAGENAME eq ChongZhenSimulator.exe', '/FO', 'CSV', '/NH',
    ], { windowsHide: true, encoding: 'utf8' });
    return stdout.toLowerCase().includes('chongzhensimulator.exe');
  } catch {
    return false;
  }
}

function startBridge() {
  const stdout = openSync(join(logDirectory, 'bridge-stdout.log'), 'a');
  const stderr = openSync(join(logDirectory, 'bridge-stderr.log'), 'a');
  return spawn(process.execPath, ['--disable-warning=ExperimentalWarning', bridgeMain], {
    env: process.env,
    windowsHide: true,
    stdio: ['ignore', stdout, stderr],
  });
}

const watcher = new GameWatcher({
  processProvider: gameIsRunning,
  startBridge: async () => startBridge(),
  stopBridge: async (child) => child.kill(),
  isBridgeRunning: async (child) => child.exitCode === null && !child.killed,
  idleMs: Number(process.env.CHONGZHEN_BRIDGE_IDLE_MS || 60_000),
});

let ticking = false;
const interval = setInterval(async () => {
  if (ticking) return;
  ticking = true;
  try {
    await watcher.tick();
  } catch (error) {
    process.stderr.write(`${new Date().toISOString()} watcher error: ${error.stack || error.message}\n`);
  } finally {
    ticking = false;
  }
}, Number(process.env.CHONGZHEN_WATCH_INTERVAL_MS || 2_000));
interval.unref?.();

async function shutdown() {
  clearInterval(interval);
  await watcher.stop();
  process.exit(0);
}

process.once('SIGINT', shutdown);
process.once('SIGTERM', shutdown);
await watcher.tick();
setInterval(() => {}, 2 ** 30);
