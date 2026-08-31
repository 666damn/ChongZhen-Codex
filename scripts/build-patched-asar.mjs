import { createRequire } from 'node:module';
import { existsSync, mkdirSync, mkdtempSync, readFileSync, rmSync, statSync, writeFileSync } from 'node:fs';
import { tmpdir } from 'node:os';
import { dirname, join } from 'node:path';
import { pathToFileURL } from 'node:url';

import { computeAsarHeaderHash } from './asar-header-hash.mjs';
import { patchRegionRouting } from './patch-region.mjs';
import { patchRenderer } from './patch-renderer.mjs';
import { verifyAsarContent } from './verify-asar.mjs';

const requireFromPatcher = createRequire(new URL('../patcher/package.json', import.meta.url));
const asar = requireFromPatcher('@electron/asar');

function parseArguments(argv) {
  const options = {};
  for (let index = 0; index < argv.length; index += 2) {
    options[argv[index]?.replace(/^--/, '')] = argv[index + 1];
  }
  return options;
}

export async function buildPatchedAsar({ input, output, token }) {
  if (!existsSync(input)) throw new Error(`Input ASAR does not exist: ${input}`);
  if (!/^czb_[a-f0-9]{64}$/i.test(token ?? '')) {
    throw new Error('Release bridge identifier must be czb_ followed by 64 hexadecimal characters');
  }
  const temporaryRoot = mkdtempSync(join(tmpdir(), 'chongzhen-asar-'));
  const extracted = join(temporaryRoot, 'app');
  try {
    asar.extractAll(input, extracted);
    const rendererDirectory = join(extracted, 'out', 'renderer', 'js');
    const candidates = (await import('node:fs/promises')).readdir(rendererDirectory, { withFileTypes: true });
    const entries = (await candidates)
      .filter((entry) => entry.isFile() && /^index-.*\.js$/i.test(entry.name))
      .map((entry) => join(rendererDirectory, entry.name));
    const matches = entries.filter((entry) => {
      const source = readFileSync(entry, 'utf8');
      return source.includes('GLOBAL_COUNTRIES') && source.includes('resolveByokRole');
    });
    if (matches.length !== 1) throw new Error(`Expected one renderer bundle, found ${matches.length}`);
    const rendererPath = matches[0];
    const source = readFileSync(rendererPath, 'utf8');
    writeFileSync(rendererPath, patchRenderer(patchRegionRouting(source), token), 'utf8');

    mkdirSync(dirname(output), { recursive: true });
    rmSync(output, { force: true });
    rmSync(`${output}.unpacked`, { recursive: true, force: true });
    await asar.createPackageWithOptions(extracted, output, {
      unpackDir: 'node_modules/steamworks.js/dist',
    });
    rmSync(`${output}.unpacked`, { recursive: true, force: true });

    const verification = verifyAsarContent(output);
    return {
      output,
      size: statSync(output).size,
      headerHash: computeAsarHeaderHash(output),
      ...verification,
    };
  } finally {
    rmSync(temporaryRoot, { recursive: true, force: true });
  }
}

if (process.argv[1] && import.meta.url === pathToFileURL(process.argv[1]).href) {
  const options = parseArguments(process.argv.slice(2));
  if (!options.input || !options.output || !options.token) {
    throw new Error('Usage: node build-patched-asar.mjs --input <app.asar> --output <app.asar> --token <release-id>');
  }
  const result = await buildPatchedAsar(options);
  process.stdout.write(`${JSON.stringify(result, null, 2)}\n`);
}
