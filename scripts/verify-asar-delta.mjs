import { createHash } from 'node:crypto';
import { createRequire } from 'node:module';
import { pathToFileURL } from 'node:url';

const requireFromPatcher = createRequire(new URL('../patcher/package.json', import.meta.url));
const asar = requireFromPatcher('@electron/asar');

function digest(buffer) {
  return createHash('sha256').update(buffer).digest('hex');
}

export function verifyAsarDelta(baselinePath, patchedPath) {
  const baselineEntries = asar.listPackage(baselinePath);
  const patchedEntries = asar.listPackage(patchedPath);
  if (JSON.stringify(baselineEntries) !== JSON.stringify(patchedEntries)) {
    throw new Error('Patched ASAR entry list differs from baseline');
  }

  const changed = [];
  for (const entry of baselineEntries) {
    const path = entry.replace(/^[\\/]/, '');
    const baselineStat = asar.statFile(baselinePath, path);
    if (baselineStat.files || baselineStat.unpacked || baselineStat.link) continue;
    const baselineDigest = digest(asar.extractFile(baselinePath, path));
    const patchedDigest = digest(asar.extractFile(patchedPath, path));
    if (baselineDigest !== patchedDigest) changed.push(entry);
  }
  if (changed.length !== 1 || !/out[\\/]renderer[\\/]js[\\/]index-.*\.js$/i.test(changed[0])) {
    throw new Error(`Expected only the renderer bundle to change; changed: ${changed.join(', ')}`);
  }
  return { changedFiles: changed };
}

if (process.argv[1] && import.meta.url === pathToFileURL(process.argv[1]).href) {
  if (!process.argv[2] || !process.argv[3]) {
    throw new Error('Usage: node verify-asar-delta.mjs <baseline.asar> <patched.asar>');
  }
  process.stdout.write(`${JSON.stringify(verifyAsarDelta(process.argv[2], process.argv[3]), null, 2)}\n`);
}
