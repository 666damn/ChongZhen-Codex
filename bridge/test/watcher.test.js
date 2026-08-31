import test from 'node:test';
import assert from 'node:assert/strict';

import { GameWatcher } from '../src/watcher.js';

function fixture() {
  const state = { gameRunning: false, starts: 0, stops: 0, alive: false };
  const watcher = new GameWatcher({
    processProvider: async () => state.gameRunning,
    startBridge: async () => { state.starts += 1; state.alive = true; return { id: state.starts }; },
    stopBridge: async () => { state.stops += 1; state.alive = false; },
    isBridgeRunning: async () => state.alive,
    idleMs: 10_000,
  });
  return { state, watcher };
}

test('watcher remains idle when the game is not running', async () => {
  const { state, watcher } = fixture();

  await watcher.tick(1_000);

  assert.equal(state.starts, 0);
  assert.equal(state.stops, 0);
});

test('watcher starts the bridge exactly once while the game remains running', async () => {
  const { state, watcher } = fixture();
  state.gameRunning = true;

  await watcher.tick(1_000);
  await watcher.tick(2_000);
  await watcher.tick(3_000);

  assert.equal(state.starts, 1);
  assert.equal(state.stops, 0);
});

test('watcher restarts a crashed bridge while the game is still running', async () => {
  const { state, watcher } = fixture();
  state.gameRunning = true;
  await watcher.tick(1_000);
  state.alive = false;

  await watcher.tick(2_000);

  assert.equal(state.starts, 2);
});

test('watcher stops the bridge only after the game idle deadline', async () => {
  const { state, watcher } = fixture();
  state.gameRunning = true;
  await watcher.tick(1_000);
  state.gameRunning = false;

  await watcher.tick(10_999);
  assert.equal(state.stops, 0);
  await watcher.tick(11_000);
  assert.equal(state.stops, 1);
});
