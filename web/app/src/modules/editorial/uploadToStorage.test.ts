import { afterEach, beforeEach, expect, test, vi } from 'vitest';
import { FakeXhr, fileOfSize } from './fakeXhr';
import { UploadCancelled, UploadFailed, uploadToStorage } from './uploadToStorage';

// A chapter's file goes straight to storage (spec 2.6, ADR 0002): one PUT of the File itself to the upload URL the host
// issued, a service SAS on one blob that may only be created. XMLHttpRequest, since fetch reports no upload progress.
// The browser streams the File from disk; nothing here may read it into memory, which a 500 MB video would not survive.

const uploadUrl =
  'http://127.0.0.1:10000/devstoreaccount1/editorial/course-versions/0198c1a0-0000-7000-8000-0000000000c1/0198c1a0-0000-7000-8000-0000000000f9.mp4?sv=2025-01-05&sr=b&sp=c&se=2026-11-22T15%3A15%3A00Z&sig=abc';

/** 500 MB of video, as the browser hands it over: a File of that size, holding nothing in the test. */
const size = 524_288_000;
const video = () => fileOfSize('kurs.mp4', 'video/mp4', size);

beforeEach(() => {
  FakeXhr.reset();
  vi.stubGlobal('XMLHttpRequest', FakeXhr);
});

afterEach(() => {
  vi.unstubAllGlobals();
  vi.restoreAllMocks();
});

function start(file: File, signal = new AbortController().signal) {
  const onProgress = vi.fn<(progress: { loaded: number; total: number }) => void>();
  const uploading = uploadToStorage({ url: uploadUrl, file, contentType: 'video/mp4', onProgress, signal });
  // Keeps a rejection the test has not awaited yet from being reported as unhandled.
  uploading.catch(() => undefined);
  return { uploading, onProgress };
}

test('the file is put to the upload URL with the blob type and its content type, and nothing else', () => {
  start(video());

  const xhr = FakeXhr.only();
  expect(xhr.method).toBe('PUT');
  expect(xhr.url).toBe(uploadUrl);
  expect(xhr.headers).toEqual({ 'x-ms-blob-type': 'BlockBlob', 'content-type': 'video/mp4' });
  expect(xhr.withCredentials).toBe(false);
  expect(xhr.sent).toBe(true);
});

// The SAS is the credential: the user's bearer token must never reach storage.
test('the request carries no Authorization header', () => {
  start(video());

  expect(Object.keys(FakeXhr.only().headers)).not.toContain('authorization');
});

test('the very File is sent, and nothing reads it into memory', () => {
  const reads = [
    vi.spyOn(Blob.prototype, 'arrayBuffer'),
    vi.spyOn(Blob.prototype, 'text'),
    vi.spyOn(Blob.prototype, 'bytes'),
    vi.spyOn(Blob.prototype, 'slice'),
    vi.spyOn(FileReader.prototype, 'readAsArrayBuffer'),
    vi.spyOn(FileReader.prototype, 'readAsBinaryString'),
    vi.spyOn(FileReader.prototype, 'readAsDataURL'),
    vi.spyOn(FileReader.prototype, 'readAsText'),
  ];
  const file = video();

  start(file);

  expect(FakeXhr.only().body).toBe(file);
  for (const read of reads) {
    expect(read).not.toHaveBeenCalled();
  }
});

test('progress is reported as the bytes go out', () => {
  const { onProgress } = start(video());
  const xhr = FakeXhr.only();

  xhr.upload.progress(0, size);
  xhr.upload.progress(262_144_000, size);
  xhr.upload.progress(size, size);

  expect(onProgress.mock.calls.map(([progress]) => progress)).toEqual([
    { loaded: 0, total: size },
    { loaded: 262_144_000, total: size },
    { loaded: size, total: size },
  ]);
});

test('201 from storage, the blob created, resolves', async () => {
  const { uploading } = start(video());

  FakeXhr.only().respond(201);

  await expect(uploading).resolves.toBeUndefined();
});

// 403: the SAS expired, or its signature does not match; 409: the blob exists already, since the URL may only create.
test.each([403, 409, 500])('%i from storage rejects with UploadFailed and the status', async (status) => {
  const { uploading } = start(video());

  FakeXhr.only().respond(status);

  const failure = await uploading.then(
    () => undefined,
    (error: unknown) => error,
  );
  expect(failure).toBeInstanceOf(UploadFailed);
  expect(failure).toMatchObject({ status });
});

test('a network error rejects with UploadFailed', async () => {
  const { uploading } = start(video());

  FakeXhr.only().failOnNetwork();

  await expect(uploading).rejects.toBeInstanceOf(UploadFailed);
});

test('a timeout rejects with UploadFailed', async () => {
  const { uploading } = start(video());

  FakeXhr.only().timeOut();

  await expect(uploading).rejects.toBeInstanceOf(UploadFailed);
});

test('aborting the signal aborts the request and rejects with UploadCancelled', async () => {
  const cancelling = new AbortController();
  const { uploading, onProgress } = start(video(), cancelling.signal);
  const xhr = FakeXhr.only();
  xhr.upload.progress(262_144_000, size);

  cancelling.abort();

  expect(xhr.aborted).toBe(true);
  await expect(uploading).rejects.toBeInstanceOf(UploadCancelled);
  expect(onProgress).toHaveBeenCalledOnce();
});

test('a signal aborted already sends nothing and rejects with UploadCancelled', async () => {
  const cancelling = new AbortController();
  cancelling.abort();

  const { uploading } = start(video(), cancelling.signal);

  await expect(uploading).rejects.toBeInstanceOf(UploadCancelled);
  expect(FakeXhr.instances.every((xhr) => !xhr.sent)).toBe(true);
});

test('UploadCancelled is no UploadFailed: a cancel is not reported as a failure', () => {
  expect(new UploadCancelled()).not.toBeInstanceOf(UploadFailed);
  expect(new UploadFailed(403)).toBeInstanceOf(Error);
  expect(new UploadFailed(403).status).toBe(403);
});
