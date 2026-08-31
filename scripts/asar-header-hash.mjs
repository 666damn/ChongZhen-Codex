import { createHash } from 'node:crypto';
import { closeSync, fstatSync, openSync, readSync } from 'node:fs';
import { pathToFileURL } from 'node:url';

export function computeAsarHeaderHash(asarPath) {
  const handle = openSync(asarPath, 'r');
  try {
    const archiveSize = fstatSync(handle).size;
    const prefix = Buffer.alloc(8);
    if (readSync(handle, prefix, 0, prefix.length, 0) !== prefix.length) {
      throw new Error('Unable to read ASAR header prefix');
    }
    const headerPickleSize = prefix.readUInt32LE(4);
    if (headerPickleSize <= 8 || headerPickleSize > archiveSize - 8) {
      throw new Error(`Invalid ASAR header size: ${headerPickleSize}`);
    }
    const headerPickle = Buffer.alloc(headerPickleSize);
    if (readSync(handle, headerPickle, 0, headerPickle.length, 8) !== headerPickle.length) {
      throw new Error('Unable to read ASAR header');
    }
    const headerJsonSize = headerPickle.readUInt32LE(4);
    if (headerJsonSize <= 0 || headerJsonSize > headerPickle.length - 8) {
      throw new Error(`Invalid ASAR JSON header size: ${headerJsonSize}`);
    }
    return createHash('sha256')
      .update(headerPickle.subarray(8, 8 + headerJsonSize))
      .digest('hex');
  } finally {
    closeSync(handle);
  }
}

if (process.argv[1] && import.meta.url === pathToFileURL(process.argv[1]).href) {
  if (!process.argv[2]) throw new Error('Usage: node asar-header-hash.mjs <app.asar>');
  process.stdout.write(`${computeAsarHeaderHash(process.argv[2])}\n`);
}
