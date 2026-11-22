import { render } from '@testing-library/vue';
import { afterEach, beforeEach, expect, test, vi } from 'vitest';
import { defineComponent } from 'vue';
import type { EditorialChapterUploadDto } from '@/api/generated';
import { t } from '@/i18n';
import { FakeXhr, fileOfSize } from './fakeXhr';
import { useChapterUpload } from './useChapterUpload';

// A PDF's or a video's way into storage (spec 2.6): ask the host for an upload URL (POST …/uploads {kind}), check the
// file against what it issued (maxBytes, contentType), put it there with progress, and hand back the blob's name for the
// chapter. Its state: idle → requesting → uploading(loaded, total) → attaching → done | cancelled | failed. A cancel is
// no failure; a failure tells the user, and a retry asks for a new URL, since the old one may only create and may have
// expired.

const notify = vi.hoisted(() => ({ success: vi.fn(), failure: vi.fn() }));
vi.mock('@/lib/notify', () => ({ notify }));

const host = vi.fn<(input: RequestInfo | URL, init?: RequestInit) => Promise<Response>>();

const versionId = '0198c1a0-0000-7000-8000-0000000000c1';
const size = 524_288_000;
const blobName = `course-versions/${versionId}/0198c1a0-0000-7000-8000-0000000000f9.mp4`;
const issued: EditorialChapterUploadDto = {
  blobName,
  uploadUrl: `http://127.0.0.1:10000/devstoreaccount1/editorial/${blobName}?sv=2025-01-05&sr=b&sp=c&sig=abc`,
  expiresAt: '2026-11-22T15:15:00+00:00',
  contentType: 'video/mp4',
  maxBytes: 2_147_483_648,
};
const pdfIssued: EditorialChapterUploadDto = {
  blobName: `course-versions/${versionId}/0198c1a0-0000-7000-8000-0000000000fa.pdf`,
  uploadUrl: `http://127.0.0.1:10000/devstoreaccount1/editorial/course-versions/${versionId}/0198c1a0-0000-7000-8000-0000000000fa.pdf?sp=c&sig=abc`,
  expiresAt: '2026-11-22T15:15:00+00:00',
  contentType: 'application/pdf',
  maxBytes: 52_428_800,
};

/** What the host issues for the next upload URL asked for. */
let issuing: () => Promise<Response>;

beforeEach(() => {
  FakeXhr.reset();
  vi.stubGlobal('XMLHttpRequest', FakeXhr);
  vi.stubGlobal('fetch', host);
  issuing = async () => Response.json(issued);
  host.mockImplementation(async (input, init) => {
    const url = new URL(String(input), 'http://localhost');
    if (init?.method === 'POST' && url.pathname === `/api/editorial/course-versions/${versionId}/uploads`) {
      return issuing();
    }
    return new Response('', { status: 404 });
  });
  notify.success.mockReset();
  notify.failure.mockReset();
});

afterEach(() => {
  host.mockReset();
  vi.unstubAllGlobals();
});

function renderUpload() {
  const used: { upload?: ReturnType<typeof useChapterUpload> } = {};
  const Upload = defineComponent({
    setup() {
      used.upload = useChapterUpload();
      return () => null;
    },
  });
  const { unmount } = render(Upload);
  if (used.upload === undefined) {
    throw new Error('The component did not set up.');
  }
  return { upload: used.upload, unmount };
}

const video = (bytes = size, type = 'video/mp4') => fileOfSize('kurs.mp4', type, bytes);

/** What was asked of the host, in order. */
const asked = () =>
  host.mock.calls.map(([input, init]) => ({
    method: init?.method ?? 'GET',
    path: new URL(String(input), 'http://localhost').pathname,
    body: init?.body === undefined ? undefined : (JSON.parse(String(init.body)) as unknown),
  }));

/** The PUT to storage, once it went out. */
async function put(): Promise<FakeXhr> {
  await vi.waitFor(() => expect(FakeXhr.instances.filter((xhr) => xhr.sent)).toHaveLength(1));
  return FakeXhr.only();
}

test('it starts idle', () => {
  const { upload } = renderUpload();

  expect(upload.state.value).toEqual({ status: 'idle' });
});

test('it asks the host for an upload URL of its kind, and is requesting until the URL arrives', async () => {
  let answer: (response: Response) => void = () => undefined;
  issuing = () =>
    new Promise<Response>((resolve) => {
      answer = resolve;
    });
  const { upload } = renderUpload();

  const uploading = upload.start(versionId, 'Video', video());

  await vi.waitFor(() => expect(host).toHaveBeenCalledOnce());
  expect(upload.state.value).toEqual({ status: 'requesting' });
  expect(asked()).toEqual([
    { method: 'POST', path: `/api/editorial/course-versions/${versionId}/uploads`, body: { kind: 'Video' } },
  ]);
  answer(Response.json(issued));
  const xhr = await put();
  xhr.respond(201);
  await expect(uploading).resolves.toBe(blobName);
});

test('the file goes to the URL issued, with the content type issued, and its progress is the state', async () => {
  const { upload } = renderUpload();
  const file = video();

  void upload.start(versionId, 'Video', file);
  const xhr = await put();

  expect(xhr.method).toBe('PUT');
  expect(xhr.url).toBe(issued.uploadUrl);
  expect(xhr.headers).toEqual({ 'x-ms-blob-type': 'BlockBlob', 'content-type': 'video/mp4' });
  expect(xhr.body).toBe(file);
  expect(upload.state.value).toMatchObject({ status: 'uploading', total: size });

  xhr.upload.progress(262_144_000, size);

  await vi.waitFor(() => expect(upload.state.value).toEqual({ status: 'uploading', loaded: 262_144_000, total: size }));
});

test('once stored, it answers the blob name and is attaching; the chapter saved, it is done', async () => {
  const { upload } = renderUpload();

  const uploading = upload.start(versionId, 'Video', video());
  (await put()).respond(201);

  await expect(uploading).resolves.toBe(blobName);
  expect(upload.state.value).toEqual({ status: 'attaching', blobName });

  upload.attached();

  expect(upload.state.value).toEqual({ status: 'done' });
  expect(notify.failure).not.toHaveBeenCalled();
});

test('a PDF asks for a PDF URL and goes as application/pdf', async () => {
  issuing = async () => Response.json(pdfIssued);
  const { upload } = renderUpload();
  const handout = new File(['%PDF-1.4\n%%EOF\n'], 'handout.pdf', { type: 'application/pdf' });

  const uploading = upload.start(versionId, 'Pdf', handout);
  const xhr = await put();
  xhr.respond(201);

  await expect(uploading).resolves.toBe(pdfIssued.blobName);
  expect(asked()[0]?.body).toEqual({ kind: 'Pdf' });
  expect(xhr.url).toBe(pdfIssued.uploadUrl);
  expect(xhr.headers['content-type']).toBe('application/pdf');
  expect(xhr.body).toBe(handout);
});

test('cancelling while uploading aborts the PUT, answers no blob, says nothing and sends nothing more', async () => {
  const { upload } = renderUpload();

  const uploading = upload.start(versionId, 'Video', video());
  const xhr = await put();
  xhr.upload.progress(262_144_000, size);
  upload.cancel();

  expect(xhr.aborted).toBe(true);
  await expect(uploading).resolves.toBeUndefined();
  expect(upload.state.value).toEqual({ status: 'cancelled' });
  expect(notify.failure).not.toHaveBeenCalled();
  expect(asked()).toHaveLength(1);
});

test('cancelling while the URL is on its way puts nothing to storage', async () => {
  let answer: (response: Response) => void = () => undefined;
  issuing = () =>
    new Promise<Response>((resolve) => {
      answer = resolve;
    });
  const { upload } = renderUpload();

  const uploading = upload.start(versionId, 'Video', video());
  await vi.waitFor(() => expect(host).toHaveBeenCalledOnce());
  upload.cancel();
  answer(Response.json(issued));

  await expect(uploading).resolves.toBeUndefined();
  expect(upload.state.value).toEqual({ status: 'cancelled' });
  expect(FakeXhr.instances.filter((xhr) => xhr.sent)).toEqual([]);
});

test('leaving while uploading aborts the PUT', async () => {
  const { upload, unmount } = renderUpload();

  void upload.start(versionId, 'Video', video());
  const xhr = await put();
  unmount();

  expect(xhr.aborted).toBe(true);
});

// 403 from storage: the SAS expired (15 minutes at most) or does not hold.
test.each([
  ['403 from storage', (xhr: FakeXhr) => xhr.respond(403), 'expired'],
  ['a network error', (xhr: FakeXhr) => xhr.failOnNetwork(), 'network'],
  ['a timeout', (xhr: FakeXhr) => xhr.timeOut(), 'network'],
  ['500 from storage', (xhr: FakeXhr) => xhr.respond(500), 'status'],
] as const)('%s fails as %s, answers no blob and tells the user', async (_case, fail, reason) => {
  const { upload } = renderUpload();

  const uploading = upload.start(versionId, 'Video', video());
  fail(await put());

  await expect(uploading).resolves.toBeUndefined();
  expect(upload.state.value).toEqual({ status: 'failed', reason });
  expect(notify.failure).toHaveBeenCalledExactlyOnceWith(t('editorial.upload.failed'));
  expect(notify.failure).toHaveBeenCalledWith('Der Upload ist fehlgeschlagen. Versuchen Sie es erneut.');
});

test('a retry after a failure asks for a new URL', async () => {
  const { upload } = renderUpload();
  void upload.start(versionId, 'Video', video());
  (await put()).respond(403);
  await vi.waitFor(() => expect(upload.state.value).toMatchObject({ status: 'failed' }));

  const retrying = upload.start(versionId, 'Video', video());

  await vi.waitFor(() => expect(host).toHaveBeenCalledTimes(2));
  await vi.waitFor(() => expect(FakeXhr.instances.filter((xhr) => xhr.sent)).toHaveLength(2));
  FakeXhr.instances[1]?.respond(201);
  await expect(retrying).resolves.toBe(blobName);
});

// Checked before anything is sent (spec 2.4): PDF at most 50 MiB as application/pdf, video at most 2 GiB as video/mp4,
// and not empty.
test.each([
  ['a video of 3 GiB', 'Video', () => video(3 * 1_073_741_824), 'tooLarge'],
  ['a video of 2 GiB and a byte', 'Video', () => video(2_147_483_649), 'tooLarge'],
  ['a QuickTime video', 'Video', () => video(size, 'video/quicktime'), 'wrongType'],
  ['an empty video', 'Video', () => video(0), 'empty'],
  ['a PDF of 50 MiB and a byte', 'Pdf', () => fileOfSize('handout.pdf', 'application/pdf', 52_428_801), 'tooLarge'],
  ['a Word file as PDF', 'Pdf', () => fileOfSize('handout.docx', 'application/msword', 1_000), 'wrongType'],
] as const)('%s is refused before anything is asked or sent', async (_case, kind, file, reason) => {
  const { upload } = renderUpload();

  await expect(upload.start(versionId, kind, file())).resolves.toBeUndefined();

  expect(upload.state.value).toEqual({ status: 'failed', reason });
  expect(host).not.toHaveBeenCalled();
  expect(FakeXhr.instances).toEqual([]);
});

test('a video of exactly 2 GiB is not refused', async () => {
  const { upload } = renderUpload();

  void upload.start(versionId, 'Video', video(2_147_483_648));

  await put();
});

// Once issued, the host's maxBytes and contentType are what counts.
test.each([
  ['a smaller maxBytes than the file', { ...issued, maxBytes: '104857600' }, 'tooLarge'],
  ['another content type', { ...issued, contentType: 'video/webm' }, 'wrongType'],
] as const)('a URL issued with %s puts nothing to storage', async (_case, dto, reason) => {
  issuing = async () => Response.json(dto);
  const { upload } = renderUpload();

  await expect(upload.start(versionId, 'Video', video())).resolves.toBeUndefined();

  expect(upload.state.value).toEqual({ status: 'failed', reason });
  expect(FakeXhr.instances.filter((xhr) => xhr.sent)).toEqual([]);
});
