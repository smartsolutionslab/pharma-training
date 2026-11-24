import { inflateRawSync } from 'node:zlib';

// Reads a ZIP's entries, in the central directory's order, with nothing but node:zlib: the web workspace has no zip
// library, and an e2e helper is not worth one. The sizes come from the central directory, not the local headers, since
// a streamed archive (the certificate export, ADR 0032) writes them in a data descriptor after each entry's data. Only
// what such an archive holds is read: stored or deflated entries, no ZIP64, no encryption, no archive comment beyond
// what the end record says.

const endOfCentralDirectory = 0x06054b50;
const centralDirectoryHeader = 0x02014b50;
const localFileHeader = 0x04034b50;

/** The archive's entries by name, in order, each with its uncompressed bytes; fails, saying why, on anything else. */
export function zipEntries(archive: Buffer): Map<string, Buffer> {
  // The end record is 22 bytes, followed by a comment of at most 65535: search backwards for its signature.
  let end = archive.length - 22;
  while (end >= 0 && archive.readUInt32LE(end) !== endOfCentralDirectory) {
    end -= 1;
  }
  if (end < 0) {
    throw new Error('The archive has no end-of-central-directory record.');
  }
  const count = archive.readUInt16LE(end + 10);
  let offset = archive.readUInt32LE(end + 16);

  const entries = new Map<string, Buffer>();
  for (let index = 0; index < count; index += 1) {
    if (archive.readUInt32LE(offset) !== centralDirectoryHeader) {
      throw new Error(`Central directory entry ${index} has no signature at offset ${offset}.`);
    }
    const method = archive.readUInt16LE(offset + 10);
    const compressedSize = archive.readUInt32LE(offset + 20);
    const size = archive.readUInt32LE(offset + 24);
    const nameLength = archive.readUInt16LE(offset + 28);
    const extraLength = archive.readUInt16LE(offset + 30);
    const commentLength = archive.readUInt16LE(offset + 32);
    const local = archive.readUInt32LE(offset + 42);
    const name = archive.toString('utf8', offset + 46, offset + 46 + nameLength);
    offset += 46 + nameLength + extraLength + commentLength;

    if (archive.readUInt32LE(local) !== localFileHeader) {
      throw new Error(`The local header of ${name} has no signature at offset ${local}.`);
    }
    const start = local + 30 + archive.readUInt16LE(local + 26) + archive.readUInt16LE(local + 28);
    const data = archive.subarray(start, start + compressedSize);
    if (method !== 0 && method !== 8) {
      throw new Error(`${name} is compressed with method ${method}; only stored (0) and deflated (8) are read.`);
    }
    const bytes = method === 0 ? Buffer.from(data) : inflateRawSync(data);
    if (bytes.length !== size) {
      throw new Error(`${name} holds ${bytes.length} bytes, but the central directory says ${size}.`);
    }
    entries.set(name, bytes);
  }
  return entries;
}
