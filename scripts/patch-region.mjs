import { readFileSync, writeFileSync } from 'node:fs';
import { pathToFileURL } from 'node:url';

const ORIGINAL = `function getIpRegionByCountry(ipCountry) {
  if (!ipCountry) return 1;
  const upper = ipCountry.toUpperCase();
  if (upper === "CN") return 1;
  if (GLOBAL_COUNTRIES.includes(upper)) return 2;
  return 0;
}`;

const PATCHED = ORIGINAL.replace('  return 0;', '  return 2;');

export function patchRegionRouting(source) {
  const first = source.indexOf(ORIGINAL);
  if (first < 0) throw new Error('Missing region routing patch anchor');
  if (source.indexOf(ORIGINAL, first + ORIGINAL.length) >= 0) {
    throw new Error('Expected exactly once region routing patch anchor');
  }
  return source.slice(0, first) + PATCHED + source.slice(first + ORIGINAL.length);
}

if (process.argv[1] && import.meta.url === pathToFileURL(process.argv[1]).href) {
  const values = Object.fromEntries(
    process.argv.slice(2).reduce((pairs, value, index, all) => {
      if (value.startsWith('--')) pairs.push([value.slice(2), all[index + 1]]);
      return pairs;
    }, []),
  );
  if (!values.input || !values.output) {
    throw new Error('Usage: node patch-region.mjs --input <file> --output <file>');
  }
  writeFileSync(values.output, patchRegionRouting(readFileSync(values.input, 'utf8')), 'utf8');
}
