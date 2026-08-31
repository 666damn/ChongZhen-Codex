import { EventEmitter } from 'node:events';
import { createInterface } from 'node:readline';

export class JsonlRpcClient extends EventEmitter {
  constructor(child) {
    super();
    this.child = child;
    this.nextId = 1;
    this.pending = new Map();
    this.closed = false;
    this.lines = createInterface({ input: child.stdout, crlfDelay: Infinity });
    this.lines.on('line', (line) => this.#handleLine(line));
    child.once('exit', (code, signal) => this.#handleExit(code, signal));
    child.once('error', (error) => this.#failAll(error));
  }

  request(method, params = {}) {
    if (this.closed) return Promise.reject(new Error('JSONL RPC client is closed'));
    const id = this.nextId++;
    return new Promise((resolve, reject) => {
      this.pending.set(id, { resolve, reject });
      this.#write({ method, id, params });
    });
  }

  notify(method, params = undefined) {
    const message = params === undefined ? { method } : { method, params };
    this.#write(message);
  }

  respond(id, result) {
    this.#write({ id, result });
  }

  close() {
    if (this.closed) return;
    this.closed = true;
    this.lines.close();
    this.child.stdin.end();
    if (!this.child.killed) this.child.kill();
    this.#failAll(new Error('JSONL RPC client closed'));
  }

  #write(message) {
    if (this.closed) throw new Error('JSONL RPC client is closed');
    this.child.stdin.write(`${JSON.stringify(message)}\n`);
  }

  #handleLine(line) {
    if (!line.trim()) return;
    let message;
    try {
      message = JSON.parse(line);
    } catch (error) {
      this.emit('protocolError', new Error(`Invalid JSONL from app-server: ${error.message}`));
      return;
    }
    if (message.id !== undefined && !message.method) {
      const pending = this.pending.get(message.id);
      if (!pending) return;
      this.pending.delete(message.id);
      if (message.error) {
        const error = new Error(message.error.message || 'JSON-RPC request failed');
        Object.assign(error, message.error);
        pending.reject(error);
      } else {
        pending.resolve(message.result);
      }
      return;
    }
    if (message.method && message.id !== undefined) {
      this.emit('serverRequest', message);
      return;
    }
    if (message.method) {
      this.emit('notification', message);
      this.emit(message.method === 'error' ? 'rpcError' : message.method, message.params);
    }
  }

  #handleExit(code, signal) {
    if (this.closed) return;
    this.closed = true;
    this.#failAll(new Error(`Codex app-server exited (code=${code}, signal=${signal})`));
    this.emit('exit', { code, signal });
  }

  #failAll(error) {
    for (const pending of this.pending.values()) pending.reject(error);
    this.pending.clear();
  }
}
