import { existsSync, readdirSync } from 'node:fs';
import { delimiter, join } from 'node:path';

export function discoverCodexExecutable(env = process.env) {
  if (env.CHONGZHEN_CODEX_PATH && existsSync(env.CHONGZHEN_CODEX_PATH)) return env.CHONGZHEN_CODEX_PATH;
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
