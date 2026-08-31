import { pathToFileURL } from 'node:url';

import { computeAsarHeaderHash } from '../../scripts/asar-header-hash.mjs';
import { verifyAsarContent } from '../../scripts/verify-asar.mjs';
import { verifyAsarDelta } from '../../scripts/verify-asar-delta.mjs';

export function verifySidecar({ source, sidecar }) {
  return {
    sourceHeaderSha256: computeAsarHeaderHash(source),
    sidecarHeaderSha256: computeAsarHeaderHash(sidecar),
    content: verifyAsarContent(sidecar),
    delta: verifyAsarDelta(source, sidecar),
  };
}

if (process.argv[1] && import.meta.url === pathToFileURL(process.argv[1]).href) {
  if (!process.argv[2] || !process.argv[3]) {
    throw new Error('Usage: node verify-sidecar.mjs <official.asar> <sidecar.asar>');
  }
  process.stdout.write(`${JSON.stringify(verifySidecar({ source: process.argv[2], sidecar: process.argv[3] }))}\n`);
}
