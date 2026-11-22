import { render, screen, waitFor, within } from '@testing-library/vue';
import userEvent from '@testing-library/user-event';
import { afterEach, beforeEach, describe, expect, test, vi } from 'vitest';
import { axe } from 'vitest-axe';
import type { EditorialChapterDto, EditorialChapterRequest, EditorialChapterUploadDto } from '@/api/generated';
import { i18n, languages, setLanguage, t } from '@/i18n';
import ChapterForm from './ChapterForm.vue';
import { FakeXhr, fileOfSize } from './fakeXhr';

// The chapter form's PDF and video branch (spec 2.4, 2.6): a file input per kind, checked on choosing; a video's
// duration read from its metadata or typed as minutes:seconds; on Save, an upload URL from the host
// (POST …/uploads {kind}), the File put straight to storage with a progress bar and a cancel button, and only then the
// chapter handed to onSave with the blob's name. A cancel saves nothing and keeps the form filled.

const notify = vi.hoisted(() => ({ success: vi.fn(), failure: vi.fn() }));
vi.mock('@/lib/notify', () => ({ notify }));

// The browser's reading of a video's metadata (readVideoDuration.test.ts); here it answers what each test says.
const duration = vi.hoisted(() => ({ read: vi.fn<(file: File) => Promise<number | undefined>>() }));
vi.mock('./readVideoDuration', () => ({ readVideoDuration: duration.read }));

const host = vi.fn<(input: RequestInfo | URL, init?: RequestInit) => Promise<Response>>();
const onSave = vi.fn<(request: EditorialChapterRequest) => Promise<boolean>>();

const versionId = '0198c1a0-0000-7000-8000-0000000000c1';
const size = 524_288_000;
const issued = {
  Video: {
    blobName: `course-versions/${versionId}/0198c1a0-0000-7000-8000-0000000000f9.mp4`,
    uploadUrl: `http://127.0.0.1:10000/devstoreaccount1/editorial/course-versions/${versionId}/0198c1a0-0000-7000-8000-0000000000f9.mp4?sp=c&sig=abc`,
    expiresAt: '2026-11-22T15:15:00+00:00',
    contentType: 'video/mp4',
    maxBytes: 2_147_483_648,
  },
  Pdf: {
    blobName: `course-versions/${versionId}/0198c1a0-0000-7000-8000-0000000000fa.pdf`,
    uploadUrl: `http://127.0.0.1:10000/devstoreaccount1/editorial/course-versions/${versionId}/0198c1a0-0000-7000-8000-0000000000fa.pdf?sp=c&sig=abc`,
    expiresAt: '2026-11-22T15:15:00+00:00',
    contentType: 'application/pdf',
    maxBytes: 52_428_800,
  },
} satisfies Record<'Video' | 'Pdf', EditorialChapterUploadDto>;

beforeEach(() => {
  FakeXhr.reset();
  vi.stubGlobal('XMLHttpRequest', FakeXhr);
  vi.stubGlobal('fetch', host);
  host.mockImplementation(async (input, init) => {
    const url = new URL(String(input), 'http://localhost');
    if (init?.method === 'POST' && url.pathname === `/api/editorial/course-versions/${versionId}/uploads`) {
      const { kind } = JSON.parse(String(init.body)) as { kind: 'Video' | 'Pdf' };
      return Response.json(issued[kind]);
    }
    return new Response('', { status: 404 });
  });
  onSave.mockReset();
  onSave.mockResolvedValue(true);
  duration.read.mockReset();
  duration.read.mockResolvedValue(725);
  notify.success.mockReset();
  notify.failure.mockReset();
});

afterEach(() => {
  host.mockReset();
  vi.unstubAllGlobals();
});

const handwashing: EditorialChapterDto = {
  id: '0198c1a0-0000-7000-8000-0000000000e3',
  position: 3,
  title: 'Händewaschen im Video',
  kind: 'Video',
  text: null,
  file: `course-versions/${versionId}/0198c1a0-0000-7000-8000-0000000000f3.mp4`,
  durationSeconds: 725,
};

const titleField = () => screen.getByRole('textbox', { name: t('editorial.chapterForm.title') });
const kinds = () => screen.getByRole('radiogroup', { name: t('editorial.chapterForm.kind') });
const kind = (name: 'Text' | 'Pdf' | 'Video') =>
  within(kinds()).getByRole('radio', { name: t(`glossary.chapterKind.${name}`) });
const fileInput = (name: 'Pdf' | 'Video') => screen.getByLabelText(t(`editorial.chapterForm.file.${name}`));
const durationField = () => screen.getByRole('textbox', { name: t('editorial.chapterForm.duration') });
const submit = () => screen.getByRole('button', { name: t('editorial.chapterForm.submit') });
const progressBar = () =>
  screen.getByRole('progressbar', { name: t('editorial.upload.progress', { name: 'kurs.mp4' }) });
const cancelUpload = () => screen.getByRole('button', { name: t('editorial.upload.cancel') });

const video = (bytes = size, type = 'video/mp4') => fileOfSize('kurs.mp4', type, bytes);
const handout = () => new File(['%PDF-1.4\n%%EOF\n'], 'handout.pdf', { type: 'application/pdf' });

function renderForm(chapter?: EditorialChapterDto) {
  return render(ChapterForm, { props: { versionId, chapter, questions: [], onSave, saving: false } });
}

/** The editor names the chapter, picks its kind and chooses the file; what the browser's file picker lets through. */
async function choose(kindName: 'Pdf' | 'Video', file: File, { title = 'Händewaschen' } = {}) {
  const user = userEvent.setup({ applyAccept: false });
  const rendered = renderForm();
  await user.click(titleField());
  await user.paste(title);
  await user.click(kind(kindName));
  await user.upload(fileInput(kindName), file);
  return { user, ...rendered };
}

/** The PUT to storage, once it went out. */
async function put(count = 1): Promise<FakeXhr> {
  await waitFor(() => expect(FakeXhr.instances.filter((xhr) => xhr.sent)).toHaveLength(count));
  const xhr = FakeXhr.instances[count - 1];
  if (xhr === undefined) {
    throw new Error('No PUT went out.');
  }
  return xhr;
}

/** The requests the form made to the host, by method and path. */
const asked = () =>
  host.mock.calls.map(
    ([input, init]) => `${init?.method ?? 'GET'} ${new URL(String(input), 'http://localhost').pathname}`,
  );

/** A video of 500 MB chosen, its duration read, and Save pressed: the upload has started. */
async function uploadVideo() {
  const chosen = await choose('Video', video());
  await waitFor(() => expect(durationField()).toHaveValue('12:05'));
  await chosen.user.click(submit());
  const xhr = await put();
  await screen.findByRole('progressbar', { name: t('editorial.upload.progress', { name: 'kurs.mp4' }) });
  return { ...chosen, xhr };
}

describe('the file input', () => {
  test('a PDF chapter takes a PDF file, a video chapter an MP4 video and its duration, a text chapter neither', async () => {
    const user = userEvent.setup();
    renderForm();

    await user.click(kind('Pdf'));

    expect(fileInput('Pdf')).toHaveAttribute('type', 'file');
    expect(fileInput('Pdf')).toHaveAttribute('accept', 'application/pdf');
    expect(screen.getByLabelText('PDF-Datei')).toBe(fileInput('Pdf'));
    expect(screen.queryByRole('textbox', { name: t('editorial.chapterForm.duration') })).not.toBeInTheDocument();

    await user.click(kind('Video'));

    expect(fileInput('Video')).toHaveAttribute('type', 'file');
    expect(fileInput('Video')).toHaveAttribute('accept', 'video/mp4');
    expect(screen.getByLabelText('Video (MP4)')).toBe(fileInput('Video'));
    expect(screen.getByRole('textbox', { name: 'Dauer (Minuten:Sekunden)' })).toBe(durationField());
    expect(screen.queryByLabelText(t('editorial.chapterForm.file.Pdf'))).not.toBeInTheDocument();

    await user.click(kind('Text'));

    expect(screen.queryByLabelText(t('editorial.chapterForm.file.Video'))).not.toBeInTheDocument();
    expect(screen.queryByRole('textbox', { name: t('editorial.chapterForm.duration') })).not.toBeInTheDocument();
    expect(screen.getByRole('textbox', { name: t('editorial.chapterForm.text') })).toBeInTheDocument();
  });

  test('choosing a video reads its duration from the file and says so', async () => {
    const file = video();
    await choose('Video', file);

    await waitFor(() => expect(durationField()).toHaveValue('12:05'));
    expect(duration.read).toHaveBeenCalledWith(file);
    expect(screen.getByText(t('editorial.chapterForm.durationDetected'))).toBeInTheDocument();
    expect(screen.getByText('Aus dem Video gelesen.')).toBeInTheDocument();
  });

  test('a video whose duration cannot be read leaves the field empty and asks for it', async () => {
    duration.read.mockResolvedValue(undefined);

    await choose('Video', video());

    expect(
      await screen.findByText('Die Dauer ließ sich nicht aus dem Video lesen. Bitte geben Sie sie ein.'),
    ).toBeInTheDocument();
    expect(durationField()).toHaveValue('');
  });
});

describe('checked on choosing, before anything is sent', () => {
  test.each([
    ['a video of 3 GiB', 'Video', () => video(3 * 1_073_741_824), 'Die Datei ist zu groß: höchstens 2.048 MB.'],
    [
      'a PDF of 60 MiB',
      'Pdf',
      () => fileOfSize('handout.pdf', 'application/pdf', 62_914_560),
      'Die Datei ist zu groß: höchstens 50 MB.',
    ],
    ['a QuickTime video', 'Video', () => video(size, 'video/quicktime'), 'Wählen Sie ein Video im Format MP4.'],
    [
      'a Word file as PDF',
      'Pdf',
      () => fileOfSize('handout.doc', 'application/msword', 1_000),
      'Wählen Sie eine PDF-Datei.',
    ],
    ['an empty video', 'Video', () => video(0), 'Die Datei ist leer.'],
    ['an empty PDF', 'Pdf', () => new File([], 'handout.pdf', { type: 'application/pdf' }), 'Die Datei ist leer.'],
  ] as const)('%s is refused at the file input, and Save sends nothing', async (_case, kindName, file, message) => {
    const { user } = await choose(kindName, file());

    const alert = await screen.findByRole('alert');
    expect(alert).toHaveTextContent(message);
    expect(fileInput(kindName)).toHaveAttribute('aria-invalid', 'true');
    expect(fileInput(kindName)).toHaveAccessibleDescription(expect.stringContaining(message));

    await user.click(submit());

    expect(await screen.findAllByRole('alert')).not.toHaveLength(0);
    expect(host).not.toHaveBeenCalled();
    expect(FakeXhr.instances).toEqual([]);
    expect(onSave).not.toHaveBeenCalled();
  });

  test('the size limit is written as the language writes it', async () => {
    await choose('Video', video(3 * 1_073_741_824));
    expect(await screen.findByRole('alert')).toHaveTextContent(
      t('editorial.chapterForm.fileTooLarge', { max: i18n.global.n(2048, 'megabytes') }),
    );

    setLanguage('en-GB', { remember: false });

    await waitFor(() =>
      expect(screen.getByRole('alert')).toHaveTextContent('The file is too large: at most 2,048 MB.'),
    );
  });

  test('a new video chapter without a file is refused at the file input', async () => {
    const user = userEvent.setup();
    renderForm();
    await user.click(titleField());
    await user.paste('Händewaschen');
    await user.click(kind('Video'));
    await user.click(durationField());
    await user.paste('1:30');

    await user.click(submit());

    expect(await screen.findByRole('alert')).toHaveTextContent('Wählen Sie eine Datei.');
    expect(fileInput('Video')).toHaveAccessibleDescription(expect.stringContaining('Wählen Sie eine Datei.'));
    expect(host).not.toHaveBeenCalled();
  });
});

describe("a video's duration", () => {
  test.each([
    ['', 'Geben Sie die Dauer an, z. B. 12:30.'],
    ['0:00', 'Die Dauer liegt zwischen 0:01 und 240:00.'],
    ['240:01', 'Die Dauer liegt zwischen 0:01 und 240:00.'],
    ['12:60', 'Die Dauer liegt zwischen 0:01 und 240:00.'],
    ['12 Minuten', 'Die Dauer liegt zwischen 0:01 und 240:00.'],
  ])('"%s" is refused with "%s", and nothing is sent', async (typed, message) => {
    duration.read.mockResolvedValue(undefined);
    const { user } = await choose('Video', video());
    await screen.findByText(t('editorial.chapterForm.durationUnknown'));
    if (typed !== '') {
      await user.click(durationField());
      await user.paste(typed);
    }

    await user.click(submit());

    const alerts = await screen.findAllByRole('alert');
    expect(alerts.map((alert) => alert.textContent.trim())).toContain(message);
    expect(durationField()).toHaveAttribute('aria-invalid', 'true');
    expect(durationField()).toHaveAccessibleDescription(expect.stringContaining(message));
    expect(host).not.toHaveBeenCalled();
    expect(FakeXhr.instances).toEqual([]);
  });

  test.each([
    ['0:01', 1],
    ['1:30', 90],
    ['240:00', 14_400],
  ])('"%s" typed by hand is saved as %i seconds', async (typed, seconds) => {
    duration.read.mockResolvedValue(undefined);
    const { user } = await choose('Video', video());
    await screen.findByText(t('editorial.chapterForm.durationUnknown'));
    await user.click(durationField());
    await user.paste(typed);

    await user.click(submit());
    (await put()).respond(201);

    await waitFor(() => expect(onSave).toHaveBeenCalledOnce());
    expect(onSave.mock.calls[0]?.[0].durationSeconds).toBe(seconds);
  });
});

describe('uploading', () => {
  test('Save asks for a video URL and puts the very file there', async () => {
    const file = video();
    const { user } = await choose('Video', file);
    await waitFor(() => expect(durationField()).toHaveValue('12:05'));

    await user.click(submit());
    const xhr = await put();

    expect(asked()).toEqual([`POST /api/editorial/course-versions/${versionId}/uploads`]);
    expect(JSON.parse(String(host.mock.calls[0]?.[1]?.body))).toEqual({ kind: 'Video' });
    expect(xhr.method).toBe('PUT');
    expect(xhr.url).toBe(issued.Video.uploadUrl);
    expect(xhr.headers).toEqual({ 'x-ms-blob-type': 'BlockBlob', 'content-type': 'video/mp4' });
    expect(xhr.body).toBe(file);
    expect(onSave).not.toHaveBeenCalled();
  });

  test('a progress bar named after the file shows how much went out', async () => {
    const { xhr } = await uploadVideo();

    expect(progressBar()).toHaveAttribute('aria-valuemin', '0');
    expect(progressBar()).toHaveAttribute('aria-valuemax', '100');
    expect(screen.getByRole('progressbar', { name: 'Upload von kurs.mp4' })).toBe(progressBar());

    xhr.upload.progress(262_144_000, size);

    await waitFor(() => expect(progressBar()).toHaveAttribute('aria-valuenow', '50'));
    expect(
      // Testing Library collapses the page's whitespace, the no-break space before % included, but not the matcher's.
      screen.getByText(
        t('editorial.upload.progressValue', { percent: i18n.global.n(0.5, 'percent') }).replace(/\s+/g, ' '),
      ),
    ).toBeInTheDocument();
    expect(screen.getByText('50 % hochgeladen')).toBeInTheDocument();

    xhr.upload.progress(524_288_000, size);

    await waitFor(() => expect(progressBar()).toHaveAttribute('aria-valuenow', '100'));
    expect(screen.getByText('100 % hochgeladen')).toBeInTheDocument();
  });

  test('while uploading, everything but the cancel button is disabled', async () => {
    await uploadVideo();

    expect(cancelUpload()).toBeEnabled();
    expect(screen.getByRole('button', { name: 'Upload abbrechen' })).toBe(cancelUpload());
    expect(submit()).toBeDisabled();
    expect(titleField()).toBeDisabled();
    expect(kind('Text')).toBeDisabled();
    expect(kind('Pdf')).toBeDisabled();
    expect(kind('Video')).toBeDisabled();
    expect(fileInput('Video')).toBeDisabled();
    expect(durationField()).toBeDisabled();
  });

  test('once stored (201), the chapter is saved with the blob name and the duration, and no text', async () => {
    const { xhr } = await uploadVideo();

    xhr.respond(201);

    await waitFor(() => expect(onSave).toHaveBeenCalledOnce());
    expect(onSave).toHaveBeenCalledWith({
      title: 'Händewaschen',
      kind: 'Video',
      text: null,
      file: issued.Video.blobName,
      durationSeconds: 725,
    });
  });

  test('a PDF chapter is saved with its blob name, no text and no duration', async () => {
    const file = handout();
    const { user } = await choose('Pdf', file, { title: 'Handout' });

    await user.click(submit());
    const xhr = await put();
    xhr.respond(201);

    expect(JSON.parse(String(host.mock.calls[0]?.[1]?.body))).toEqual({ kind: 'Pdf' });
    expect(xhr.headers['content-type']).toBe('application/pdf');
    expect(xhr.body).toBe(file);
    await waitFor(() => expect(onSave).toHaveBeenCalledOnce());
    expect(onSave).toHaveBeenCalledWith({
      title: 'Handout',
      kind: 'Pdf',
      text: null,
      file: issued.Pdf.blobName,
      durationSeconds: null,
    });
  });

  test('cancelling aborts the upload, saves nothing, says so and keeps the form filled', async () => {
    const { user, xhr } = await uploadVideo();
    xhr.upload.progress(262_144_000, size);

    await user.click(cancelUpload());

    expect(xhr.aborted).toBe(true);
    expect(await screen.findByText(t('editorial.upload.cancelled'))).toBeInTheDocument();
    expect(screen.getByText('Der Upload wurde abgebrochen; das Kapitel wurde nicht gespeichert.')).toBeInTheDocument();
    expect(screen.queryByRole('progressbar')).not.toBeInTheDocument();
    expect(onSave).not.toHaveBeenCalled();
    expect(asked()).toEqual([`POST /api/editorial/course-versions/${versionId}/uploads`]);
    expect(notify.failure).not.toHaveBeenCalled();
    expect(titleField()).toHaveValue('Händewaschen');
    expect(titleField()).toBeEnabled();
    expect(durationField()).toHaveValue('12:05');
    expect(submit()).toBeEnabled();
  });

  test('Save after a cancel starts over with a new URL', async () => {
    const { user, xhr } = await uploadVideo();
    await user.click(cancelUpload());
    await screen.findByText(t('editorial.upload.cancelled'));

    await user.click(submit());
    const again = await put(2);
    again.respond(201);

    expect(xhr.aborted).toBe(true);
    expect(asked()).toEqual([
      `POST /api/editorial/course-versions/${versionId}/uploads`,
      `POST /api/editorial/course-versions/${versionId}/uploads`,
    ]);
    await waitFor(() => expect(onSave).toHaveBeenCalledOnce());
    expect(onSave.mock.calls[0]?.[0].file).toBe(issued.Video.blobName);
  });

  test.each([
    ['403 from storage, the URL expired', (xhr: FakeXhr) => xhr.respond(403)],
    ['a network error', (xhr: FakeXhr) => xhr.failOnNetwork()],
  ])('%s is reported, saves nothing and lets the editor try again', async (_case, fail) => {
    const { xhr } = await uploadVideo();

    fail(xhr);

    await waitFor(() =>
      expect(notify.failure).toHaveBeenCalledWith('Der Upload ist fehlgeschlagen. Versuchen Sie es erneut.'),
    );
    expect(onSave).not.toHaveBeenCalled();
    await waitFor(() => expect(submit()).toBeEnabled());
    expect(screen.queryByRole('progressbar')).not.toBeInTheDocument();
    expect(titleField()).toHaveValue('Händewaschen');
  });

  test('closing the form while uploading aborts the upload', async () => {
    const { xhr, unmount } = await uploadVideo();

    unmount();

    expect(xhr.aborted).toBe(true);
    expect(onSave).not.toHaveBeenCalled();
  });
});

// The limits the host issues with the upload URL are the ones that count (spec 2.4): a file they refuse is refused at
// the file input as one refused on choosing is, with the same message, its {max} the issued maxBytes in megabytes.
describe('refused by the limits issued with the upload URL', () => {
  /** The host issues the URL with the limits given, for a video. */
  function issuing(limits: Partial<EditorialChapterUploadDto>) {
    host.mockImplementation(async (input, init) => {
      const url = new URL(String(input), 'http://localhost');
      if (init?.method === 'POST' && url.pathname === `/api/editorial/course-versions/${versionId}/uploads`) {
        return Response.json({ ...issued.Video, ...limits });
      }
      return new Response('', { status: 404 });
    });
  }

  test('a video larger than the issued maxBytes is refused at the file input, with the issued limit, and not put', async () => {
    issuing({ maxBytes: '104857600' });
    const { user } = await choose('Video', video(209_715_200));
    await waitFor(() => expect(durationField()).toHaveValue('12:05'));
    expect(screen.queryByRole('alert')).not.toBeInTheDocument();

    await user.click(submit());

    const message = t('editorial.chapterForm.fileTooLarge', { max: i18n.global.n(100, 'megabytes') });
    const alert = await screen.findByRole('alert');
    expect(alert).toHaveTextContent(message);
    expect(alert).toHaveTextContent('Die Datei ist zu groß: höchstens 100 MB.');
    expect(fileInput('Video')).toHaveAttribute('aria-invalid', 'true');
    expect(fileInput('Video')).toHaveAccessibleDescription(expect.stringContaining(message));
    expect(asked()).toEqual([`POST /api/editorial/course-versions/${versionId}/uploads`]);
    expect(FakeXhr.instances.filter((xhr) => xhr.sent)).toEqual([]);
    expect(onSave).not.toHaveBeenCalled();
  });

  test('a video of another content type than the issued one is refused at the file input, and not put', async () => {
    issuing({ contentType: 'video/quicktime' });
    const { user } = await choose('Video', video());
    await waitFor(() => expect(durationField()).toHaveValue('12:05'));

    await user.click(submit());

    const alert = await screen.findByRole('alert');
    expect(alert).toHaveTextContent(t('editorial.chapterForm.fileWrongType.Video'));
    expect(alert).toHaveTextContent('Wählen Sie ein Video im Format MP4.');
    expect(fileInput('Video')).toHaveAttribute('aria-invalid', 'true');
    expect(fileInput('Video')).toHaveAccessibleDescription(
      expect.stringContaining(t('editorial.chapterForm.fileWrongType.Video')),
    );
    expect(FakeXhr.instances.filter((xhr) => xhr.sent)).toEqual([]);
    expect(onSave).not.toHaveBeenCalled();
  });
});

// A failed upload is said in the form too, not only in a toast that goes away: the editor reads it where they retry.
describe('a failed upload stays visible in the form', () => {
  test.each([
    ['403 from storage, the URL expired', (xhr: FakeXhr) => xhr.respond(403)],
    ['500 from storage', (xhr: FakeXhr) => xhr.respond(500)],
    ['a network error', (xhr: FakeXhr) => xhr.failOnNetwork()],
  ])('%s', async (_case, fail) => {
    const { xhr } = await uploadVideo();

    fail(xhr);

    const alert = await screen.findByRole('alert');
    expect(alert).toHaveTextContent(t('editorial.upload.failed'));
    expect(alert).toHaveTextContent('Der Upload ist fehlgeschlagen. Versuchen Sie es erneut.');
    expect(notify.failure).toHaveBeenCalledWith(t('editorial.upload.failed'));
    await waitFor(() => expect(submit()).toBeEnabled());
    expect(screen.getByRole('alert')).toHaveTextContent(t('editorial.upload.failed'));
    expect(onSave).not.toHaveBeenCalled();
  });

  test('an upload URL the host does not issue', async () => {
    host.mockResolvedValue(new Response('', { status: 500 }));
    const { user } = await choose('Video', video());
    await waitFor(() => expect(durationField()).toHaveValue('12:05'));

    await user.click(submit());

    expect(await screen.findByRole('alert')).toHaveTextContent(t('editorial.upload.failed'));
    expect(FakeXhr.instances).toEqual([]);
    expect(onSave).not.toHaveBeenCalled();
  });
});

// A chapter the page could not save after its file was stored (onSave answers false) is saved again with the file
// already stored: the URL was create-only, and the blob is there.
test('a save refused after the upload is retried with the stored blob, without uploading again', async () => {
  onSave.mockResolvedValueOnce(false);
  const { user, xhr } = await uploadVideo();
  xhr.respond(201);
  await waitFor(() => expect(onSave).toHaveBeenCalledOnce());
  await waitFor(() => expect(submit()).toBeEnabled());

  await user.click(submit());

  await waitFor(() => expect(onSave).toHaveBeenCalledTimes(2));
  expect(onSave.mock.calls[1]?.[0].file).toBe(issued.Video.blobName);
  expect(FakeXhr.instances.filter((each) => each.sent)).toHaveLength(1);
  expect(asked()).toEqual([`POST /api/editorial/course-versions/${versionId}/uploads`]);
});

describe('editing a file chapter', () => {
  test('without a new file, it keeps its file and asks for no upload URL', async () => {
    const user = userEvent.setup();
    renderForm(handwashing);
    expect(kind('Video')).toBeChecked();
    expect(durationField()).toHaveValue('12:05');
    expect(screen.getByText(t('editorial.chapterForm.fileKept'))).toBeInTheDocument();

    await user.clear(durationField());
    await user.click(durationField());
    await user.paste('12:30');
    await user.click(submit());

    await waitFor(() => expect(onSave).toHaveBeenCalledOnce());
    expect(onSave).toHaveBeenCalledWith({
      title: 'Händewaschen im Video',
      kind: 'Video',
      text: null,
      file: handwashing.file,
      durationSeconds: 750,
    });
    expect(host).not.toHaveBeenCalled();
    expect(FakeXhr.instances).toEqual([]);
  });

  test('with a new file, it uploads that one and saves its blob name', async () => {
    const user = userEvent.setup();
    renderForm(handwashing);
    await user.upload(fileInput('Video'), video());
    await waitFor(() => expect(durationField()).toHaveValue('12:05'));

    await user.click(submit());
    (await put()).respond(201);

    await waitFor(() => expect(onSave).toHaveBeenCalledOnce());
    expect(onSave.mock.calls[0]?.[0]).toMatchObject({
      kind: 'Video',
      file: issued.Video.blobName,
      durationSeconds: 725,
    });
  });
});

test('switched to English while uploading, the upload speaks English', async () => {
  const { xhr } = await uploadVideo();
  xhr.upload.progress(262_144_000, size);
  await waitFor(() => expect(progressBar()).toHaveAttribute('aria-valuenow', '50'));

  setLanguage('en-GB', { remember: false });

  expect(await screen.findByRole('progressbar', { name: 'Uploading kurs.mp4' })).toHaveAttribute('aria-valuenow', '50');
  expect(screen.getByText('50% uploaded')).toBeInTheDocument();
  expect(screen.getByRole('button', { name: 'Cancel upload' })).toBeEnabled();
  expect(screen.getByLabelText('Video (MP4)')).toBeDisabled();
  expect(screen.getByRole('textbox', { name: 'Duration (minutes:seconds)' })).toHaveValue('12:05');
  expect(screen.getByText('Read from the video.')).toBeInTheDocument();
});

// The form is part of the course version page, whose axe check (CourseVersionPage.axe.test.ts) sees it closed; here
// axe sees the video branch as the editor fills it in, and while it uploads.
describe.each(languages)('in %s', (language) => {
  beforeEach(() => {
    setLanguage(language, { remember: false });
  });

  test('the video form with a file chosen has no accessibility violations axe can find', async () => {
    const { container } = await choose('Video', video());
    await waitFor(() => expect(durationField()).toHaveValue('12:05'));

    expect(await axe(container)).toHaveNoViolations();
  });

  test('the form while uploading has no accessibility violations axe can find', async () => {
    const { container, xhr } = await uploadVideo();
    xhr.upload.progress(262_144_000, size);
    await waitFor(() => expect(progressBar()).toHaveAttribute('aria-valuenow', '50'));

    expect(await axe(container)).toHaveNoViolations();
  });

  test('the form after a cancel has no accessibility violations axe can find', async () => {
    const { container, user } = await uploadVideo();
    await user.click(cancelUpload());
    await screen.findByText(t('editorial.upload.cancelled'));

    expect(await axe(container)).toHaveNoViolations();
  });
});
