import { readFile } from 'node:fs/promises';
import { type APIRequestContext, expect, type Page, test } from '@playwright/test';
import { memberOfOnePharmacy } from './onePharmacy';
import { signIn } from './signIn';

// The chapter player (#44) against the AppHost, with only the developer's pharmacy mocked (memberOfOnePharmacy).
// Through the host's API, the realm's local user editor publishes a course of a long text chapter, a video chapter of
// the clip in e2e/fixtures and a one-page PDF chapter; the developer then works through it on
// /learning/courses/{versionId}: the text is done once scrolled to its end, a seek past what was watched snaps back,
// a reload resumes the video at the reported position, the video played to its end is done, and the PDF's last page
// is done.
//
// The clip, e2e/fixtures/clip-20s.mp4 (73,824 bytes): AV1 (main profile, av01), 160×90, 10 fps, 20 s, no audio, MP4
// with major brand isom, made with ffmpeg 6.1.1 and SVT-AV1 1.7.0:
//   ffmpeg -f lavfi -i testsrc2=size=160x90:rate=10:duration=20 -c:v libsvtav1 -preset 12 -crf 50 -an \
//     -movflags +faststart e2e/fixtures/clip-20s.mp4
//
// The first step probes whether the browser plays it (canPlayType and a load of the clip). Owner's decision on #44: if
// Playwright's Chromium cannot, this spec runs in Google Chrome (channel: 'chrome') in CI. In CI (process.env.CI) a
// browser that cannot play the clip fails the spec, so the video's steps are never skipped unnoticed there; locally
// they are skipped with an annotation, and the unit tests cover the seek limit and resume.
// Keycloak's login is English.

const keycloak = process.env.E2E_KEYCLOAK_URL ?? 'http://localhost:8080';
const clipSeconds = 20;
const av1 = 'video/mp4; codecs="av01.0.00M.08"';

/** A course title of this run's own, so runs never share a version. */
const courseTitle = () => `E2E Player ${Date.now()}`;
const textTitle = 'Hygieneplan lesen';
const videoTitle = 'Händewaschen im Video';
const pdfTitle = 'Merkblatt';

/** A text long enough that its end is out of view on opening. */
const longText = [
  '# Hygieneplan',
  ...Array.from(
    { length: 60 },
    (_, index) => `Absatz ${index + 1}: Hände vor und nach jedem Kundenkontakt 30 Sekunden waschen.`,
  ),
  'Ende des Hygieneplans.',
].join('\n\n');

/** The access token Keycloak issues to a local user, whose password is their name (direct access grants are on). */
async function accessToken(request: APIRequestContext, user: string) {
  const response = await request.post(`${keycloak}/realms/pharma-training/protocol/openid-connect/token`, {
    form: { grant_type: 'password', client_id: 'pharma-training-web', username: user, password: user },
  });
  expect(response.ok(), await response.text()).toBe(true);
  const body = (await response.json()) as { access_token: string };
  return `Bearer ${body.access_token}`;
}

/** Calls the host's API; fails the test on anything outside 2xx, naming the call. */
async function call(
  request: APIRequestContext,
  authorization: string,
  method: 'POST' | 'PUT',
  path: string,
  data?: unknown,
) {
  const response = await request.fetch(`/api/${path}`, {
    method,
    headers: { Authorization: authorization },
    ...(data === undefined ? {} : { data }),
  });
  expect(response.ok(), `${method} ${path} answered ${response.status()}: ${await response.text()}`).toBe(true);
  return response;
}

/** A small, valid PDF of one empty page, made here; its xref offsets are counted from the text. */
function smallPdf(): Buffer {
  const objects = [
    '<< /Type /Catalog /Pages 2 0 R >>',
    '<< /Type /Pages /Kids [3 0 R] /Count 1 >>',
    '<< /Type /Page /Parent 2 0 R /MediaBox [0 0 595 842] >>',
  ];
  let body = '%PDF-1.4\n';
  const offsets: number[] = [];
  objects.forEach((object, index) => {
    offsets.push(Buffer.byteLength(body));
    body += `${index + 1} 0 obj\n${object}\nendobj\n`;
  });
  const xref = Buffer.byteLength(body);
  body += `xref\n0 ${objects.length + 1}\n0000000000 65535 f \n`;
  body += offsets.map((offset) => `${String(offset).padStart(10, '0')} 00000 n \n`).join('');
  body += `trailer\n<< /Size ${objects.length + 1} /Root 1 0 R >>\nstartxref\n${xref}\n%%EOF\n`;
  return Buffer.from(body, 'latin1');
}

/** Uploads a file as the editor, straight to the upload URL the host issues; resolves to the blob's name. */
async function upload(
  request: APIRequestContext,
  editor: string,
  version: string,
  kind: 'Video' | 'Pdf',
  bytes: Buffer,
) {
  const issued = (await (
    await call(request, editor, 'POST', `editorial/course-versions/${version}/uploads`, { kind })
  ).json()) as { blobName: string; uploadUrl: string };
  const stored = await request.put(issued.uploadUrl, {
    headers: { 'x-ms-blob-type': 'BlockBlob', 'content-type': kind === 'Video' ? 'video/mp4' : 'application/pdf' },
    data: bytes,
  });
  expect(stored.status(), await stored.text()).toBe(201);
  return issued.blobName;
}

interface PublishedCourse {
  version: string;
  text: string;
  video: string;
  pdf: string;
}

/** Publishes the course as editor: the text, the video and the PDF chapter, one question approved, then published. */
async function publishCourse(request: APIRequestContext, title: string, clip: Buffer): Promise<PublishedCourse> {
  const editor = await accessToken(request, 'editor');
  const version = (await (await call(request, editor, 'POST', 'editorial/courses', { title })).json()) as string;
  const versions = `editorial/course-versions/${version}`;
  const chapter = async (body: Record<string, unknown>) =>
    (await (await call(request, editor, 'POST', `${versions}/chapters`, body)).json()) as string;

  const text = await chapter({ title: textTitle, kind: 'Text', text: longText });
  const video = await chapter({
    title: videoTitle,
    kind: 'Video',
    file: await upload(request, editor, version, 'Video', clip),
    durationSeconds: clipSeconds,
  });
  const pdf = await chapter({
    title: pdfTitle,
    kind: 'Pdf',
    file: await upload(request, editor, version, 'Pdf', smallPdf()),
  });
  await call(request, editor, 'PUT', `${versions}/blueprint`, {
    questions: 1,
    passingScorePercent: 80,
    validityMonths: 12,
  });
  const question = (await (
    await call(request, editor, 'POST', `${versions}/questions`, {
      chapterId: text,
      text: 'Wie lange waschen Sie die Hände?',
      type: 'SingleChoice',
      options: [
        { text: '30 Sekunden', correct: true },
        { text: '5 Sekunden', correct: false },
      ],
    })
  ).json()) as string;
  await call(request, editor, 'POST', `${versions}/questions/${question}/approve`);
  await call(request, editor, 'POST', `${versions}/submit`);
  await call(request, editor, 'POST', `${versions}/publish`);
  return { version, text, video, pdf };
}

/**
 * Waits as the developer until Learning knows the version (the start answers 2xx: 404 until it does, and any other
 * refusal is no reason to go on) and the files are ready (their media URL answers 200, not 409).
 */
async function waitUntilLearnable(request: APIRequestContext, course: PublishedCourse) {
  const developer = await accessToken(request, 'developer');
  await expect
    .poll(
      async () =>
        (
          await request.post('/api/learning/sessions', {
            headers: { Authorization: developer },
            data: { courseVersionId: course.version },
          })
        ).ok(),
      { message: 'Learning knows the published version (the start answers 2xx)', timeout: 30_000 },
    )
    .toBe(true);
  for (const chapter of [course.video, course.pdf]) {
    await expect
      .poll(
        async () =>
          (await request.get(`/api/learning/media/${chapter}`, { headers: { Authorization: developer } })).status(),
        { message: `the media of chapter ${chapter} is ready`, timeout: 30_000 },
      )
      .toBe(200);
  }
}

/** Whether this browser plays the clip: canPlayType for AV1 in MP4, and the clip's metadata loaded from its bytes. */
async function probeClip(page: Page, clip: Buffer) {
  return page.evaluate(
    async ({ bytes, type }) => {
      const video = document.createElement('video');
      const canPlay = video.canPlayType(type);
      if (canPlay === '') {
        return { playable: false, why: `canPlayType('${type}') is empty` };
      }
      const url = URL.createObjectURL(new Blob([new Uint8Array(bytes)], { type: 'video/mp4' }));
      const loaded = await new Promise<string>((resolve) => {
        video.addEventListener('loadedmetadata', () => resolve('loaded'));
        video.addEventListener('error', () => resolve(`error ${video.error?.code ?? ''}`));
        video.muted = true;
        video.preload = 'metadata';
        video.src = url;
      });
      URL.revokeObjectURL(url);
      return loaded === 'loaded'
        ? { playable: true, why: `canPlayType: ${canPlay}` }
        : { playable: false, why: `the clip did not load: ${loaded}` };
    },
    { bytes: [...clip], type: av1 },
  );
}

const overviewItem = (page: Page, title: string) =>
  page.getByRole('navigation', { name: 'Kapitel' }).getByRole('listitem').filter({ hasText: title });
/** The "Erledigt" badge beside the chapter's h2. */
const doneBadge = (page: Page, title: string) =>
  page.getByRole('heading', { level: 2, name: title }).locator('xpath=..').getByText('Erledigt', { exact: true });

/** The chapter's progress in the host's answer to a report. */
async function storedPercent(answer: { json: () => Promise<unknown> }, chapterId: string) {
  const session = (await answer.json()) as { chapters: { chapterId: string; progressPercent: number | string }[] };
  return Number(session.chapters.find((chapter) => chapter.chapterId === chapterId)?.progressPercent ?? 0);
}

test('a learner works through a text, a video and a PDF chapter; the video keeps to what was watched and resumes', async ({
  page,
}) => {
  test.setTimeout(240_000);
  const clip = await readFile(new URL('./fixtures/clip-20s.mp4', import.meta.url));

  const probe = await probeClip(page, clip);
  if (process.env.CI) {
    expect(
      probe.playable,
      `${test.info().project.name} does not play the AV1/MP4 clip (${probe.why}); in CI this spec runs in a browser that does (#44).`,
    ).toBe(true);
  }
  if (!probe.playable) {
    test.info().annotations.push({
      type: 'skipped: video steps',
      description:
        `${test.info().project.name} does not play the AV1/MP4 clip (${probe.why}). Owner's decision on #44: ` +
        "run this spec in Google Chrome (channel: 'chrome') in CI; here the unit tests cover the seek limit and resume.",
    });
  }

  const course = await publishCourse(page.request, courseTitle(), clip);
  await waitUntilLearnable(page.request, course);
  await memberOfOnePharmacy(page);
  await signIn(page);

  // The text chapter: open until its end has been in view.
  await page.goto(`/learning/courses/${course.version}?chapter=${course.text}`);
  await expect(page.getByRole('heading', { level: 2, name: textTitle })).toBeVisible();
  await expect(overviewItem(page, textTitle)).toHaveAttribute('aria-current', 'step');
  await expect(overviewItem(page, textTitle)).toContainText('Offen');
  await page.getByText('Ende des Hygieneplans.').scrollIntoViewIfNeeded();
  await expect(doneBadge(page, textTitle)).toBeVisible();
  await expect(overviewItem(page, textTitle)).toContainText('Erledigt');

  // On to the video with Next chapter; its heading takes the focus.
  await page.getByRole('button', { name: 'Nächstes Kapitel' }).click();
  const videoHeading = page.getByRole('heading', { level: 2, name: videoTitle });
  await expect(videoHeading).toBeFocused();
  await expect(page).toHaveURL(new RegExp(`chapter=${course.video}`));
  const video = page.getByLabel(`Video: ${videoTitle}`);
  await expect(video).toBeVisible();

  if (probe.playable) {
    // Played muted for about 3 s, then paused: the pause reports what was watched.
    await expect.poll(() => video.evaluate((element: HTMLVideoElement) => element.readyState)).toBeGreaterThan(0);
    await video.evaluate((element: HTMLVideoElement) => {
      element.muted = true;
      return element.play();
    });
    await expect
      .poll(() => video.evaluate((element: HTMLVideoElement) => element.currentTime), { timeout: 15_000 })
      .toBeGreaterThan(3);
    // The pause's own report: the video chapter's, not one the 30 s cadence or the text chapter sent.
    const reported = page.waitForResponse(
      (response) =>
        response.request().method() === 'POST' &&
        response.url().endsWith('/progress') &&
        (response.request().postDataJSON() as { chapterId?: string } | null)?.chapterId === course.video,
    );
    await video.evaluate((element: HTMLVideoElement) => element.pause());
    const answer = await reported;
    const sent = answer.request().postDataJSON() as { chapterId: string; percent: number };
    expect(sent.chapterId).toBe(course.video);
    expect(sent.percent).toBeGreaterThan(0);
    const watched = await video.evaluate((element: HTMLVideoElement) => element.currentTime);

    // A seek past what was watched snaps back and is said in the polite status region.
    await video.evaluate((element: HTMLVideoElement) => {
      element.currentTime = 18;
    });
    await expect
      .poll(() => video.evaluate((element: HTMLVideoElement) => element.currentTime))
      .toBeLessThanOrEqual(watched + 0.5);
    await expect(page.getByRole('status').filter({ hasText: /Vorspulen ist nur bis 0:0\d möglich\./ })).toHaveCount(1);

    // After a reload the video resumes at the reported position: percent/100 × 20 s.
    const percent = await storedPercent(answer, course.video);
    expect(percent).toBeGreaterThan(0);
    await page.reload();
    await expect(videoHeading).toBeVisible();
    await expect.poll(() => video.evaluate((element: HTMLVideoElement) => element.readyState)).toBeGreaterThan(0);
    await expect
      .poll(async () =>
        Math.abs((await video.evaluate((element: HTMLVideoElement) => element.currentTime)) - (percent / 100) * 20),
      )
      .toBeLessThanOrEqual(0.3);
    await expect(page.getByRole('status').filter({ hasText: /Fortgesetzt bei 0:0\d\./ })).toHaveCount(1);

    // Played to its end, the video is done.
    await video.evaluate((element: HTMLVideoElement) => {
      element.muted = true;
      return element.play();
    });
    await expect(doneBadge(page, videoTitle)).toBeVisible({ timeout: 40_000 });
    await expect(overviewItem(page, videoTitle)).toContainText('Erledigt');
  }

  // The PDF, opened from the overview: its only page shown, it is done.
  await overviewItem(page, pdfTitle).getByRole('link').click();
  await expect(page.getByRole('heading', { level: 2, name: pdfTitle })).toBeFocused();
  await expect(page.getByLabel(`PDF: ${pdfTitle}`)).toBeVisible();
  await expect(page.getByText('Seite 1 von 1')).toBeVisible();
  await expect(doneBadge(page, pdfTitle)).toBeVisible();

  if (probe.playable) {
    // Said once in the polite status region, and shown in a lasting panel that is no second status region.
    const contentCompleted = 'Sie haben alle Kapitel bearbeitet. Der Test ist jetzt freigeschaltet.';
    await expect(page.getByRole('status').filter({ hasText: contentCompleted })).toHaveCount(1);
    await expect(page.locator('section').filter({ hasText: contentCompleted })).toBeVisible();
  }
});
