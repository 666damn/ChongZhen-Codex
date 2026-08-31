export class GameWatcher {
  constructor({ processProvider, startBridge, stopBridge, isBridgeRunning, idleMs = 60_000 }) {
    this.processProvider = processProvider;
    this.startBridge = startBridge;
    this.stopBridge = stopBridge;
    this.isBridgeRunning = isBridgeRunning;
    this.idleMs = idleMs;
    this.bridgeHandle = null;
    this.lastGameSeenAt = null;
  }

  async tick(now = Date.now()) {
    const gameRunning = await this.processProvider();
    const bridgeRunning = this.bridgeHandle ? await this.isBridgeRunning(this.bridgeHandle) : false;
    if (gameRunning) {
      this.lastGameSeenAt = now;
      if (!bridgeRunning) this.bridgeHandle = await this.startBridge();
      return;
    }
    if (
      bridgeRunning
      && this.lastGameSeenAt !== null
      && now - this.lastGameSeenAt >= this.idleMs
    ) {
      await this.stopBridge(this.bridgeHandle);
      this.bridgeHandle = null;
      this.lastGameSeenAt = null;
    }
  }

  async stop() {
    if (this.bridgeHandle && await this.isBridgeRunning(this.bridgeHandle)) {
      await this.stopBridge(this.bridgeHandle);
    }
    this.bridgeHandle = null;
  }
}
