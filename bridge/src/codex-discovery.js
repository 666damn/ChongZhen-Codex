import { existsSync, readdirSync } from 'node:fs';
import { delimiter, join } from 'node:path';
import { fileURLToPath } from 'node:url';

const DEFAULT_BUNDLED_PATH = fileURLToPath(new URL(
  '../node_modules/@openai/codex-win32-x64/vendor/x86_64-pc-windows-msvc/bin/codex.exe',
  import.meta.url,
));

export function discoverCodexExecutable(env = process.env, { bundledPath = DEFAULT_BUNDLED_PATH } = {}) {
  if (env.CHONGZHEN_CODEX_PATH && existsSync(env.CHONGZHEN_CODEX_PATH)) return env.CHONGZHEN_CODEX_PATH;
  if (existsSync(bundledPath)) return bundledPath;
  for (const directory of String(env.PATH ?? '').split(delimiter).filter(Boolean)) {
    const candidate = join(directory, 'codex.exe');
    if (existsSync(candidate)) return candidate;
  }

  const binRoot = join(env.LOCALAPPDATA ?? '', 'OpenAI', 'Codex', 'bin');
  if (existsSync(binRoot)) {
    const versions = readdirSync(binRoot, { withFileTypes: true })
      .filter((entry) => entry.isDirectory())
      .map((entry) => entry.name)
      .sort((left, right) => right.localeCompare(left));
    for (const version of versions) {
      const candidate = join(binRoot, version, 'codex.exe');
      if (existsSync(candidate)) return candidate;
    }
  }
  throw new Error('Codex executable was not found. Install or sign in to the Codex app first.');
}
