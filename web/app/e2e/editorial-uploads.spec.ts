import { open, rm } from 'node:fs/promises';
import { tmpdir } from 'node:os';
import { join } from 'node:path';
import { expect, type Page, test } from '@playwright/test';

// PDF and video chapters, as the realm's local user `editor` (realm role editor) adds them against the AppHost: the file
// goes straight from the browser to the AppHost's Azurite with one PUT to the upload URL the host issued (spec 2.6), and
// the chapter then names the blob. Each test starts a course of its own, so runs never share a version. Keycloak's
// login is English.

/** Signs in as editor and starts a course from the inbox; resolves on its first version's page. */
async function startCourse(page: Page, title: string) {
  await page.goto('/');
  await page.getByLabel('Username or email').fill('editor');
  await page.getByLabel('Password', { exact: true }).fill('editor');
  await page.getByRole('button', { name: 'Sign In' }).click();
  await expect(page.getByText('Angemeldet als editor')).toBeVisible();
  await page.goto('/editorial');
  await page.getByRole('button', { name: 'Neue Schulung' }).first().click();
  const dialog = page.getByRole('dialog', { name: 'Neue Schulung anlegen' });
  await dialog.getByRole('textbox', { name: 'Titel' }).fill(title);
  await dialog.getByRole('button', { name: 'Anlegen', exact: true }).click();
  await expect(page).toHaveURL(/\/editorial\/versions\/[0-9a-f-]{36}$/);
  await expect(page.getByRole('heading', { name: title, level: 1 })).toBeVisible();
}

const chapters = (page: Page) => page.getByRole('list', { name: 'Kapitel' }).getByRole('listitem');

/** Opens "Neues Kapitel", names the chapter and picks its kind; resolves to the dialog. */
async function newChapter(page: Page, title: string, kind: 'PDF' | 'Video') {
  await page.getByRole('button', { name: 'Kapitel hinzufügen' }).first().click();
  const dialog = page.getByRole('dialog', { name: 'Neues Kapitel' });
  await dialog.getByRole('textbox', { name: 'Titel' }).fill(title);
  await dialog.getByRole('radiogroup', { name: 'Art' }).getByRole('radio', { name: kind, exact: true }).check();
  return dialog;
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

test('an editor adds a PDF chapter; the file goes to storage and the chapter shows after a reload', async ({
  page,
}) => {
  await startCourse(page, `E2E PDF ${Date.now()}`);
  const dialog = await newChapter(page, 'Handout', 'PDF');

  await dialog.getByLabel('PDF-Datei').setInputFiles({
    name: 'handout.pdf',
    mimeType: 'application/pdf',
    buffer: smallPdf(),
  });
  const stored = page.waitForResponse((response) => response.request().method() === 'PUT');
  await dialog.getByRole('button', { name: 'Speichern' }).click();

  expect((await stored).status()).toBe(201);
  await expect(dialog).toBeHidden();
  await expect(page.getByRole('status').filter({ hasText: /Das Kapitel „Handout. wurde gespeichert\./ })).toBeVisible();
  await page.reload();
  await expect(chapters(page)).toHaveCount(1);
  await expect(chapters(page).first()).toContainText('Handout');
  await expect(chapters(page).first()).toContainText('PDF');
  await expect(chapters(page).first()).toContainText('Datei hochgeladen');
});

test('an editor adds a video chapter with its duration typed; one PUT stores it, and a reload shows it', async ({
  page,
}) => {
  await startCourse(page, `E2E Video ${Date.now()}`);
  const dialog = await newChapter(page, 'Clip', 'Video');
  const puts: string[] = [];
  page.on('request', (request) => {
    if (request.method() === 'PUT') {
      puts.push(request.url());
    }
  });

  // 2 MB of zeros: no codec in Playwright's Chromium reads a duration from it, so the editor types it in.
  await dialog.getByLabel('Video (MP4)').setInputFiles({
    name: 'clip.mp4',
    mimeType: 'video/mp4',
    buffer: Buffer.alloc(2 * 1024 * 1024),
  });
  await expect(
    dialog.getByText('Die Dauer ließ sich nicht aus dem Video lesen. Bitte geben Sie sie ein.'),
  ).toBeVisible();
  await dialog.getByRole('textbox', { name: 'Dauer (Minuten:Sekunden)' }).fill('1:30');
  const putting = page.waitForRequest((request) => request.method() === 'PUT');
  const stored = page.waitForResponse((response) => response.request().method() === 'PUT');
  await dialog.getByRole('button', { name: 'Speichern' }).click();

  const request = await putting;
  const headers = await request.allHeaders();
  expect(headers['x-ms-blob-type']).toBe('BlockBlob');
  expect(headers['content-type']).toBe('video/mp4');
  expect(headers.authorization).toBeUndefined();
  expect(new URL(request.url()).searchParams.get('sp')).toBe('c');
  expect((await stored).status()).toBe(201);
  await expect(dialog).toBeHidden();
  expect(puts).toHaveLength(1);
  await page.reload();
  await expect(chapters(page)).toHaveCount(1);
  await expect(chapters(page).first()).toContainText('Clip');
  await expect(chapters(page).first()).toContainText('Video');
  await expect(chapters(page).first()).toContainText('Dauer 1:30');
});

test('a 500 MB video uploads with progress and can be cancelled, leaving no chapter', async ({ page }) => {
  // A sparse file: 500 MB long, no disk used. The browser reads it from the path; the test never holds it in memory.
  const path = join(tmpdir(), `kurs-${Date.now()}.mp4`);
  const handle = await open(path, 'w');
  await handle.truncate(524_288_000);
  await handle.close();
  try {
    await startCourse(page, `E2E Abbruch ${Date.now()}`);
    const dialog = await newChapter(page, 'Kurs', 'Video');
    const chapterRequests: string[] = [];
    page.on('request', (request) => {
      if (/\/editorial\/course-versions\/[^/]+\/chapters/.test(request.url()) && request.method() !== 'GET') {
        chapterRequests.push(`${request.method()} ${request.url()}`);
      }
    });

    await dialog.getByLabel('Video (MP4)').setInputFiles(path);
    await expect(
      dialog.getByText('Die Dauer ließ sich nicht aus dem Video lesen. Bitte geben Sie sie ein.'),
    ).toBeVisible();
    await dialog.getByRole('textbox', { name: 'Dauer (Minuten:Sekunden)' }).fill('10:00');
    // About 5 MB/s up: the upload takes some 100 s, long enough to see it move and cancel it.
    const network = await page.context().newCDPSession(page);
    await network.send('Network.enable');
    await network.send('Network.emulateNetworkConditions', {
      offline: false,
      latency: 0,
      downloadThroughput: -1,
      uploadThroughput: 5 * 1024 * 1024,
    });
    await dialog.getByRole('button', { name: 'Speichern' }).click();

    const progress = dialog.getByRole('progressbar', { name: /^Upload von kurs-\d+\.mp4$/ });
    await expect(progress).toBeVisible();
    await expect
      .poll(async () => Number(await progress.getAttribute('aria-valuenow')), { timeout: 60_000 })
      .toBeGreaterThan(0);
    await expect(dialog.getByText(/^\d+\s?% hochgeladen$/)).toBeVisible();
    await expect(dialog.getByRole('button', { name: 'Speichern' })).toBeDisabled();
    await dialog.getByRole('button', { name: 'Upload abbrechen' }).click();

    await expect(dialog.getByText('Der Upload wurde abgebrochen; das Kapitel wurde nicht gespeichert.')).toBeVisible();
    await expect(progress).toBeHidden();
    await expect(dialog.getByRole('textbox', { name: 'Titel' })).toHaveValue('Kurs');
    await network.send('Network.emulateNetworkConditions', {
      offline: false,
      latency: 0,
      downloadThroughput: -1,
      uploadThroughput: -1,
    });
    await dialog.getByRole('button', { name: 'Abbrechen', exact: true }).click();
    await expect(dialog).toBeHidden();
    expect(chapterRequests).toEqual([]);
    await page.reload();
    await expect(page.getByText('Diese Version hat noch keine Kapitel.')).toBeVisible();
  } finally {
    await rm(path, { force: true });
  }
});
