import { afterEach, beforeEach, expect, test, vi } from 'vitest';
import { fileOfSize } from './fakeXhr';
import { readVideoDuration } from './readVideoDuration';

// A video chapter needs its duration (VideoDuration: 1 to 14,400 whole seconds). The browser reads it from the file's
// metadata alone (spec 2.4): a <video preload="metadata"> on an object URL of the File, never the File's bytes in
// memory. It rounds up to whole seconds, gives up after 10 s, and revokes the object URL whatever came of it. Without a
// duration the editor types it in; that is the form's.

const objectUrl = 'blob:http://localhost/0198c1a0-0000-7000-8000-0000000000b1';
const video = () => fileOfSize('kurs.mp4', 'video/mp4', 524_288_000);

let created: HTMLVideoElement[];

beforeEach(() => {
  created = [];
  const createElement = document.createElement.bind(document);
  vi.spyOn(document, 'createElement').mockImplementation((tagName: string, options?: ElementCreationOptions) => {
    const element = createElement(tagName, options);
    if (element instanceof HTMLVideoElement) {
      created.push(element);
    }
    return element;
  });
  vi.spyOn(URL, 'createObjectURL').mockReturnValue(objectUrl);
  vi.spyOn(URL, 'revokeObjectURL').mockReturnValue(undefined);
});

afterEach(() => {
  vi.useRealTimers();
  vi.restoreAllMocks();
});

/** The <video> it loads the metadata with, once it made one. */
async function player(): Promise<HTMLVideoElement> {
  await vi.waitFor(() => expect(created).toHaveLength(1));
  const [element] = created;
  if (element === undefined) {
    throw new Error('No <video> was created.');
  }
  return element;
}

/** The metadata arrived: the browser knows the duration (NaN when the file had none it could read). */
function metadataLoaded(element: HTMLVideoElement, duration: number) {
  Object.defineProperty(element, 'duration', { value: duration, configurable: true });
  element.dispatchEvent(new Event('loadedmetadata'));
}

test('the metadata of the very file is loaded through an object URL, and nothing more of it', async () => {
  const reads = [
    vi.spyOn(Blob.prototype, 'arrayBuffer'),
    vi.spyOn(Blob.prototype, 'slice'),
    vi.spyOn(FileReader.prototype, 'readAsArrayBuffer'),
    vi.spyOn(FileReader.prototype, 'readAsDataURL'),
  ];
  const file = video();

  const reading = readVideoDuration(file);
  const element = await player();

  expect(URL.createObjectURL).toHaveBeenCalledWith(file);
  expect(element.preload).toBe('metadata');
  expect(element.src).toBe(objectUrl);
  metadataLoaded(element, 725);
  await expect(reading).resolves.toBe(725);
  for (const read of reads) {
    expect(read).not.toHaveBeenCalled();
  }
});

test('a duration of 724.2 seconds is rounded up to 725, and the object URL is revoked', async () => {
  const reading = readVideoDuration(video());

  metadataLoaded(await player(), 724.2);

  await expect(reading).resolves.toBe(725);
  expect(URL.revokeObjectURL).toHaveBeenCalledWith(objectUrl);
});

test.each([
  ['NaN', Number.NaN],
  ['infinite', Number.POSITIVE_INFINITY],
])('a duration the browser reads as %s is none, and the object URL is revoked', async (_case, duration) => {
  const reading = readVideoDuration(video());

  metadataLoaded(await player(), duration);

  await expect(reading).resolves.toBeUndefined();
  expect(URL.revokeObjectURL).toHaveBeenCalledWith(objectUrl);
});

// A codec the browser lacks, or a file that is no MP4 at all.
test('a file the browser cannot read has no duration, and the object URL is revoked', async () => {
  const reading = readVideoDuration(video());

  (await player()).dispatchEvent(new Event('error'));

  await expect(reading).resolves.toBeUndefined();
  expect(URL.revokeObjectURL).toHaveBeenCalledWith(objectUrl);
});

test('it gives up after 10 seconds without metadata, and the object URL is revoked', async () => {
  vi.useFakeTimers();
  let answer: number | undefined | 'pending' = 'pending';
  const reading = readVideoDuration(video()).then((duration: number | undefined) => {
    answer = duration;
    return duration;
  });
  await player();

  await vi.advanceTimersByTimeAsync(9_999);
  expect(answer).toBe('pending');
  expect(URL.revokeObjectURL).not.toHaveBeenCalled();

  await vi.advanceTimersByTimeAsync(1);
  await expect(reading).resolves.toBeUndefined();
  expect(URL.revokeObjectURL).toHaveBeenCalledWith(objectUrl);
});
