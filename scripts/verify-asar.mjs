import { createRequire } from 'node:module';
import { pathToFileURL } from 'node:url';

const requireFromPatcher = createRequire(new URL('../patcher/package.json', import.meta.url));
const asar = requireFromPatcher('@electron/asar');

export function verifyAsarContent(asarPath) {
  const entries = asar.listPackage(asarPath);
  const candidates = entries.filter((entry) => /out[\\/]renderer[\\/]js[\\/]index-[^\\/]+\.js$/i.test(entry));
  const bundles = candidates.map((entry) => ({
    entry,
    source: asar.extractFile(asarPath, entry.replace(/^[\\/]/, '')).toString('utf8'),
  }));
  const mainBundle = bundles.find(({ source }) => source.includes('GLOBAL_COUNTRIES') && source.includes('resolveByokRole'));
  if (!mainBundle) throw new Error('Main renderer bundle was not found in ASAR');

  const { entry: rendererEntry, source: renderer } = mainBundle;
  return {
    rendererEntry,
    rendererBytes: Buffer.byteLength(renderer),
    hasAuRegion: /const GLOBAL_COUNTRIES\s*=\s*\["HK",\s*"MO",\s*"AU"\]/.test(renderer),
    hasGlobalRegion: /if \(GLOBAL_COUNTRIES\.includes\(upper\)\) return 2;\s*return 2;/.test(renderer),
    hasBridgeEndpoint: renderer.includes('http://127.0.0.1:43129/v1/chat/completions'),
    hasConversationContext: renderer.includes('_chongzhen_context'),
    hasForcedByok: renderer.includes('codexByokEnabledBackingAtom'),
    hasLocalBearerToken: renderer.includes('czb_'),
  };
}

if (process.argv[1] && import.meta.url === pathToFileURL(process.argv[1]).href) {
  const asarPath = process.argv[2];
  if (!asarPath) throw new Error('Usage: node verify-asar.mjs <app.asar>');
  const result = verifyAsarContent(asarPath);
  const failed = Object.entries(result)
    .filter(([key, value]) => key.startsWith('has') && value !== true)
    .map(([key]) => key);
  if (failed.length) throw new Error(`ASAR verification failed: ${failed.join(', ')}`);
  process.stdout.write(`${JSON.stringify(result, null, 2)}\n`);
}
