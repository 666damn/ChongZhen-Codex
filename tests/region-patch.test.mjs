import assert from 'node:assert/strict';
import { readFileSync } from 'node:fs';
import { join } from 'node:path';
import test from 'node:test';
import vm from 'node:vm';

import { patchRegionRouting } from '../scripts/patch-region.mjs';

const fixturePath = join(import.meta.dirname, 'fixtures', 'region-function.js');
const fixture = readFileSync(fixturePath, 'utf8');

function evaluate(source) {
  const context = {};
  vm.runInNewContext(`${source}\nglobalThis.route = getIpRegionByCountry;`, context);
  return context.route;
}

test('keeps default/CN domestic and sends every other non-empty country international', () => {
  const route = evaluate(patchRegionRouting(fixture));
  const cases = new Map([
    [undefined, 1], ['', 1], ['CN', 1], ['cn', 1],
    ['HK', 2], ['MO', 2], ['AU', 2], ['US', 2], ['DE', 2], ['ZZ', 2],
  ]);
  for (const [country, expected] of cases) assert.equal(route(country), expected, String(country));
});

test('refuses ambiguous or already-patched region code', () => {
  assert.throws(() => patchRegionRouting(fixture.replace('return 0;', 'return 2;')), /anchor/i);
  assert.throws(() => patchRegionRouting(`${fixture}\n${fixture}`), /exactly once/i);
});
