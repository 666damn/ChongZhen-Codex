import { createRequire } from 'node:module';
import {
  existsSync,
  mkdirSync,
  mkdtempSync,
  readFileSync,
  renameSync,
  rmSync,
  statSync,
  writeFileSync,
} from 'node:fs';
import { tmpdir } from 'node:os';
import { dirname, join, resolve } from 'node:path';
import { pathToFileURL } from 'node:url';

import { computeAsarHeaderHash } from '../../scripts/asar-header-hash.mjs';
import { patchRegionRouting } from '../../scripts/patch-region.mjs';
import { patchRenderer } from '../../scripts/patch-renderer.mjs';
import { verifySidecar } from './verify-sidecar.mjs';

const requireFromPatcher = createRequire(new URL('../package.json', import.meta.url));
const asar = requireFromPatcher('@electron/asar');

function parseArguments(argv) {
  const values = {};
  for (let index = 0; index < argv.length; index += 2) {
    values[argv[index]?.replace(/^--/, '')] = argv[index + 1];
  }
  return values;
}

export async function buildSidecar({ input, output, token }) {
  if (!existsSync(input)) throw new Error(`Input ASAR does not exist: ${input}`);
  if (!/^czb_[a-f0-9]{64}$/i.test(token ?? '')) {
    throw new Error('Bridge token must be czb_ followed by 64 hexadecimal characters');
  }
  const source = resolve(input);
  const destination = resolve(output);
  if (source.toLowerCase() === destination.toLowerCase()) {
    throw new Error('Sidecar output must not replace the input ASAR');
  }

  const sourceLength = statSync(source).size;
  const sourceHeaderSha256 = computeAsarHeaderHash(source);
  const temporaryRoot = mkdtempSync(join(tmpdir(), 'chongzhen-sidecar-'));
  const extracted = join(temporaryRoot, 'app');
  const temporaryOutput = join(temporaryRoot, 'app.asar');
  const adjacentTemporary = `${destination}.writing-${process.pid}-${Date.now()}`;
  try {
    asar.extractAll(source, extracted);
    const rendererDirectory = join(extracted, 'out', 'renderer', 'js');
    const candidates = (await import('node:fs/promises')).readdir(rendererDirectory, { withFileTypes: true });
    const entries = (await candidates)
      .filter((entry) => entry.isFile() && /^index-.*\.js$/i.test(entry.name))
      .map((entry) => join(rendererDirectory, entry.name));
    const matches = entries.filter((entry) => {
      const sourceText = readFileSync(entry, 'utf8');
      return sourceText.includes('GLOBAL_COUNTRIES') && sourceText.includes('resolveByokRole');
    });
    if (matches.length !== 1) throw new Error(`Expected one renderer bundle, found ${matches.length}`);
    const rendererPath = matches[0];
    const renderer = readFileSync(rendererPath, 'utf8');
    writeFileSync(rendererPath, patchRenderer(patchRegionRouting(renderer), token), 'utf8');

    await asar.createPackageWithOptions(extracted, temporaryOutput, {
      unpackDir: 'node_modules/steamworks.js/dist',
    });
    rmSync(`${temporaryOutput}.unpacked`, { recursive: true, force: true });
    verifySidecar({ source, sidecar: temporaryOutput });
    if (statSync(source).size !== sourceLength || computeAsarHeaderHash(source) !== sourceHeaderSha256) {
      throw new Error('Input ASAR changed while the sidecar was being generated');
    }

    mkdirSync(dirname(destination), { recursive: true });
    rmSync(adjacentTemporary, { force: true });
    renameSync(temporaryOutput, adjacentTemporary);
    renameSync(adjacentTemporary, destination);
    return {
      sourceHeaderSha256,
      sourceLength,
      sidecarHeaderSha256: computeAsarHeaderHash(destination),
      sidecarLength: statSync(destination).size,
    };
  } finally {
    rmSync(adjacentTemporary, { force: true });
    rmSync(temporaryRoot, { recursive: true, force: true });
  }
}

if (process.argv[1] && import.meta.url === pathToFileURL(process.argv[1]).href) {
  const options = parseArguments(process.argv.slice(2));
  const token = process.env.CHONGZHEN_BRIDGE_TOKEN || options.token;
  if (!options.input || !options.output || !token) {
    throw new Error('Usage: set CHONGZHEN_BRIDGE_TOKEN and run node build-sidecar.mjs --input <official.asar> --output <sidecar.asar>');
  }
  process.stdout.write(`${JSON.stringify(await buildSidecar({ ...options, token }))}\n`);
}
