import { spawn } from 'node:child_process';

import { JsonlRpcClient } from './jsonl-rpc.js';
import { discoverCodexExecutable } from './codex-discovery.js';

export class CodexClient {
  constructor({ executable, args, cwd, env = process.env } = {}) {
    this.executable = executable;
    this.args = args;
    this.cwd = cwd;
    this.env = env;
    this.defaultModel = null;
    this.child = null;
    this.rpc = null;
  }

  async start() {
    if (this.rpc && !this.rpc.closed) return;
    const executable = this.executable ?? discoverCodexExecutable(this.env);
    const args = this.args ?? ['app-server', '--listen', 'stdio://'];
    this.child = spawn(executable, args, {
      cwd: this.cwd,
      env: this.env,
      windowsHide: true,
      stdio: ['pipe', 'pipe', 'pipe'],
    });
    this.rpc = new JsonlRpcClient(this.child);
    this.rpc.on('serverRequest', (request) => {
      this.rpc.respond(request.id, { decision: 'decline' });
    });
    await this.rpc.request('initialize', {
      clientInfo: {
        name: 'chongzhen_codex_bridge',
        title: 'ChongZhen Codex Bridge',
        version: '1.0.0',
      },
    });
    this.rpc.notify('initialized');
    const models = await this.rpc.request('model/list', { includeHidden: true, limit: 100 });
    this.defaultModel = models.data?.find((candidate) => candidate.isDefault)?.id ?? null;
  }

  async isLoggedIn() {
    await this.start();
    const result = await this.rpc.request('account/read', { refreshToken: false });
    return Boolean(result.account);
  }

  async readCurrentModel() {
    await this.start();
    try {
      const result = await this.rpc.request('config/read', { includeLayers: false });
      return typeof result.config?.model === 'string' && result.config.model.trim()
        ? result.config.model.trim()
        : null;
    } catch {
      return null;
    }
  }

  async startThread() {
    await this.start();
    const model = await this.readCurrentModel();
    const result = await this.rpc.request('thread/start', {
      cwd: this.cwd,
      ...(model ? { model } : {}),
      approvalPolicy: 'never',
      sandbox: 'read-only',
      serviceName: 'chongzhen_codex_bridge',
    });
    return result.thread.id;
  }

  async resumeThread(threadId) {
    await this.start();
    const model = await this.readCurrentModel();
    const result = await this.rpc.request('thread/resume', {
      threadId,
      cwd: this.cwd,
      ...(model ? { model } : {}),
      approvalPolicy: 'never',
      sandbox: 'read-only',
    });
    return result.thread.id;
  }

  async runTurn(threadId, prompt, outputSchema, signal) {
    await this.start();
    const model = await this.readCurrentModel();
    let turnId;
    let finalText = '';
    let abortHandler;

    const completion = new Promise((resolve, reject) => {
      const onItem = (params) => {
        if (params?.threadId !== threadId || params?.turnId !== turnId) return;
        if (params.item?.type === 'agentMessage' && (!params.item.phase || params.item.phase === 'final_answer')) {
          finalText = params.item.text ?? finalText;
        }
      };
      const onTurn = (params) => {
        if (params?.threadId !== threadId || params?.turn?.id !== turnId) return;
        cleanup();
        if (params.turn.status === 'completed') resolve({ turnId, text: finalText });
        else reject(new Error(params.turn.error?.message || `Codex turn ${params.turn.status}`));
      };
      const cleanup = () => {
        this.rpc.off('item/completed', onItem);
        this.rpc.off('turn/completed', onTurn);
        if (signal && abortHandler) signal.removeEventListener('abort', abortHandler);
      };
      this.rpc.on('item/completed', onItem);
      this.rpc.on('turn/completed', onTurn);
      abortHandler = () => {
        if (turnId) this.rpc.request('turn/interrupt', { threadId, turnId }).catch(() => {});
      };
      if (signal) signal.addEventListener('abort', abortHandler, { once: true });
    });

    const result = await this.rpc.request('turn/start', {
      threadId,
      input: [{ type: 'text', text: prompt }],
      cwd: this.cwd,
      ...(model ? { model } : {}),
      approvalPolicy: 'never',
      sandboxPolicy: { type: 'readOnly' },
      outputSchema,
    });
    turnId = result.turn.id;
    if (signal?.aborted) abortHandler?.();
    return completion;
  }

  close() {
    const child = this.child;
    this.rpc?.close();
    child?.stdin?.destroy();
    child?.stdout?.destroy();
    child?.stderr?.destroy();
    if (child && !child.killed) child.kill();
    child?.unref();
    this.rpc = null;
    this.child = null;
  }
}
