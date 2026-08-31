import assert from 'node:assert/strict';
import { join } from 'node:path';
import test from 'node:test';

import { verifyAsarDelta } from '../scripts/verify-asar-delta.mjs';

test('release ASAR changes only the renderer bundle from the validated baseline', () => {
  const baseline = join(import.meta.dirname, '..', 'build', 'baseline.asar');
  const patched = join(import.meta.dirname, '..', 'build', 'app.asar');
  const result = verifyAsarDelta(baseline, patched);
  assert.equal(result.changedFiles.length, 1);
  assert.match(result.changedFiles[0], /out[\\/]renderer[\\/]js[\\/]index-.*\.js$/i);
});
