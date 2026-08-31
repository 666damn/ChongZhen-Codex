import assert from 'node:assert/strict';
import test from 'node:test';
import { fileURLToPath } from 'node:url';
import { dirname, join } from 'node:path';

import { verifyAsarContent } from '../scripts/verify-asar.mjs';

const root = dirname(dirname(fileURLToPath(import.meta.url)));

test('final ASAR contains AU region and authenticated Codex bridge patches together', () => {
  const result = verifyAsarContent(join(root, 'build', 'app.asar'));

  assert.match(result.rendererEntry, /out[\\/]renderer[\\/]js[\\/]index-.*\.js$/i);
  assert.equal(result.hasAuRegion, true);
  assert.equal(result.hasBridgeEndpoint, true);
  assert.equal(result.hasConversationContext, true);
  assert.equal(result.hasForcedByok, true);
  assert.equal(result.hasLocalBearerToken, true);
});
