import { type APIRequestContext, type Browser, expect, type Page, test } from '@playwright/test';
import { addMonths } from '@/lib/addMonths';
import { pdfText, qrLinkIn } from './certificatePdf';
import { issuedCertificate, type IssuedCertificate } from './certificationDatabase';
import { accessToken, courseTitle, daysAhead, germanDate, pharmacyName } from './compliance';
import { acceptOn, invitationLink, mailText, recordAcceptances, signInAs } from './invitations';

// MVP 1 end to end (#55, X1), against the AppHost with no route mocked, in three browsers of their own: the realm's
// local user owner (Olaf Owner, no realm role) registers a pharmacy and invites employee-c (Clara Employee), a user no other spec invites (an address is mailed at most every 10 minutes, #265), on its team
// page; she accepts from the mail in Mailpit. The realm's local user editor starts a course in the editorial inbox,
// gives it a text chapter and a test of one question the editor wrote and approved (no AI draft) through the API, as
// e2e/editorial-workflow.spec.ts does, then submits and publishes it on the version's page. The owner assigns it to her
// in the wizard; she opens it from "Meine Schulungen", works through the chapter in the player, fails the test once and
// passes it. The link her certificate's QR code encodes, decoded from the PDF Certification made, opens /verify/{code}
// signed out and says it is valid; the owner's team matrix shows her course as completed.
//
// What no page offers yet, and how the test reaches it instead:
// - "Meine Bestätigungen" (Z6, GET /certification/me/certificates) does not exist: nothing the learner reads names her
//   certificate. Its id is read from Certification's database (e2e/certificationDatabase.ts, as
//   e2e/certification-verify.spec.ts reads codes); the PDF is then fetched as she would (GET
//   /certification/certificates/{id}/pdf), its page drawn in a browser and its QR code decoded there (qrLinkIn).
// Every run registers a pharmacy and publishes a course of its own, so what earlier runs left changes nothing; owner
// and employee-c may already belong to several pharmacies. Keycloak's login is English.

const employee = 'Clara Employee';
const employeeAddress = 'employee-c@example.com';

/** Today in Germany, where the host dates certificates (DayInGermany()), as yyyy-mm-dd. */
function todayInGermany() {
  // en-CA writes a date as yyyy-mm-dd.
  return new Intl.DateTimeFormat('en-CA', {
    timeZone: 'Europe/Berlin',
    year: 'numeric',
    month: '2-digit',
    day: '2-digit',
  }).format(new Date());
}

/** A browser of the user's own, in German like the default context. */
async function browserOf(browser: Browser) {
  const context = await browser.newContext({ locale: 'de-DE' });
  return { context, page: await context.newPage() };
}

/** Signs in as a local user on the start page; resolves to the Authorization header the app then sends. */
async function signIn(page: Page, user: string) {
  const signedIn = page.waitForRequest(
    (request) => request.url().includes('/api/') && request.headers()['authorization'] !== undefined,
  );
  await page.goto('/');
  await signInAs(page, user);
  await expect(page.getByText(`Angemeldet als ${user}`)).toBeVisible();
  return (await signedIn).headers()['authorization'] ?? '';
}

/**
 * Makes the pharmacy current in the shell: chosen in the switcher when the user belongs to several and another is
 * current, or current by itself when it is their only one or registering or accepting made it current.
 */
async function choose(page: Page, pharmacy: string) {
  // Exactly: the start page has regions whose names hold the word too.
  const region = page.getByRole('region', { name: 'Apotheke', exact: true });
  const switcher = region.getByRole('button', { name: /^Apotheke( wählen$|: )/ });
  const name = region.getByText(pharmacy, { exact: true });
  const chosen = region.getByRole('button', { name: `Apotheke: ${pharmacy}` });
  const only = region.getByRole('paragraph').filter({ has: page.getByText(pharmacy, { exact: true }) });
  const another = switcher.filter({ hasNotText: pharmacy });
  // A settled state before deciding: the pharmacy is current (chosen on the switcher, or the only one), or the switcher
  // shows another (or none). While the list loads, or a list read before the pharmacy was added names only another,
  // neither shows, and the wait goes on; either one stays until the test chooses.
  await expect(chosen.or(only).or(another)).toBeVisible();
  if (await another.isVisible()) {
    await switcher.click();
    await page
      .getByRole('menu', { name: 'Apotheken' })
      .getByRole('menuitemradio', { name: new RegExp(`^${pharmacy}`) })
      .click();
    await expect(chosen).toBeVisible();
  }
  await expect(name.first()).toBeVisible();
}

/** A toast that is a status message with the text. */
const toast = (page: Page, text: string) => page.getByRole('status').filter({ hasText: text });

/** Calls the editorial API as the editor; fails the test on anything outside 2xx, naming the call. */
async function editorial(
  request: APIRequestContext,
  authorization: string,
  method: 'POST' | 'PUT',
  path: string,
  data?: unknown,
) {
  const response = await request.fetch(`/api/editorial/course-versions/${path}`, {
    method,
    headers: { Authorization: authorization },
    ...(data === undefined ? {} : { data }),
  });
  expect(response.ok(), `${method} ${path} answered ${response.status()}: ${await response.text()}`).toBe(true);
  return response;
}

/**
 * The course's content, as e2e/editorial-workflow.spec.ts builds it: a text chapter, a blueprint of one question at a
 * pass mark of 80 % valid for 12 months, and one single-choice question the editor wrote and approved.
 */
async function writeContent(request: APIRequestContext, authorization: string, version: string) {
  const chapter = (await (
    await editorial(request, authorization, 'POST', `${version}/chapters`, {
      title: 'Einleitung',
      kind: 'Text',
      text: 'Waschen Sie die Hände 30 Sekunden lang.',
    })
  ).json()) as string;
  await editorial(request, authorization, 'PUT', `${version}/blueprint`, {
    questions: 1,
    passingScorePercent: 80,
    validityMonths: 12,
  });
  const question = (await (
    await editorial(request, authorization, 'POST', `${version}/questions`, {
      chapterId: chapter,
      text: 'Wie lange waschen Sie die Hände?',
      type: 'SingleChoice',
      options: [
        { text: '30 Sekunden', correct: true },
        { text: '5 Sekunden', correct: false },
      ],
    })
  ).json()) as string;
  await editorial(request, authorization, 'POST', `${version}/questions/${question}/approve`);
}

/** The test's one question. */
const question = (page: Page) => page.getByRole('group', { name: /Frage 1 von 1/ });

/** Starts the attempt shown as "Versuch n von 3" and chooses the option, which is saved at once. */
async function startAndChoose(page: Page, attempt: number, option: string) {
  await expect(page.getByText(`Versuch ${attempt} von 3.`, { exact: false })).toBeVisible();
  await page.getByRole('button', { name: 'Test beginnen' }).click();
  await expect(page.getByRole('heading', { level: 2, name: 'Fragen' })).toBeFocused();
  await question(page).getByRole('radio', { name: option }).check();
  await expect(question(page).getByText('Gespeichert', { exact: true })).toBeVisible();
}

/** Submits through the confirmation and lands on the result. */
async function submit(page: Page) {
  await page.getByRole('button', { name: 'Test abgeben' }).click();
  const dialog = page.getByRole('dialog', { name: 'Test abgeben?' });
  await dialog.getByRole('button', { name: 'Abgeben', exact: true }).click();
  await expect(page).toHaveURL(/\/assessment\/sessions\/[^/]+\/result$/);
}

test('MVP 1: an owner registers and invites, an editor publishes, the employee passes, the certificate verifies and the matrix shows it completed', async ({
  page,
  browser,
  baseURL,
}) => {
  test.setTimeout(360_000);
  const pharmacy = pharmacyName('MVP 1');
  const title = courseTitle('MVP 1');
  const editor = await browserOf(browser);
  const learner = await browserOf(browser);
  const visitor = await browserOf(browser);
  // The day she passes, in Germany, worked out once: the certificate is dated then, and valid until 12 months on.
  let passedOn = '';

  try {
    await test.step('the owner registers a pharmacy', async () => {
      await signIn(page, 'owner');
      await page.goto('/organisation/register');
      await expect(page.getByRole('heading', { name: 'Apotheke registrieren', level: 1 })).toBeVisible();
      await page.getByRole('textbox', { name: 'Name der Apotheke' }).fill(pharmacy);
      await page.getByRole('combobox', { name: 'Ihre Tätigkeit' }).selectOption({ label: 'Apotheker' });
      await page.getByRole('button', { name: 'Registrieren', exact: true }).click();
      await expect(toast(page, `Die Apotheke ${pharmacy} wurde registriert.`)).toBeVisible();
      await expect(page).toHaveURL(/\/organisation$/);
      await choose(page, pharmacy);
      await expect(page.getByRole('heading', { name: 'Mitglieder', level: 1 })).toBeVisible();
    });

    await test.step('the owner invites the employee on the team page', async () => {
      await page.getByRole('button', { name: 'Einladen', exact: true }).click();
      const dialog = page.getByRole('dialog', { name: 'Mitglied einladen' });
      await dialog.getByLabel('E-Mail-Adresse').fill(employeeAddress);
      await dialog
        .getByRole('combobox', { name: 'Tätigkeit' })
        .selectOption({ label: 'PTA (pharmazeutisch-technischer Assistent)' });
      await dialog.getByRole('combobox', { name: 'Rolle' }).selectOption({ label: 'Mitarbeiter' });
      await dialog.getByRole('button', { name: 'Einladung senden' }).click();
      await expect(toast(page, `Die Einladung an ${employeeAddress} wurde gesendet.`)).toBeVisible();
      await expect(
        page.getByRole('region', { name: 'Einladungen' }).getByRole('row').filter({ hasText: employeeAddress }),
      ).toContainText('Offen');
    });

    await test.step('the employee accepts the invitation from the mail', async () => {
      const link = invitationLink(await mailText(page.request, employeeAddress, pharmacy));
      const accepted = recordAcceptances(learner.page);
      await learner.page.goto(link);
      await signInAs(learner.page, 'employee-c');
      await acceptOn(learner.page, pharmacy, link.slice(link.indexOf('#token=') + '#token='.length), accepted);
      await expect(toast(learner.page, `Sie sind jetzt Mitglied der Apotheke ${pharmacy}.`)).toBeVisible({
        timeout: 15_000,
      });
      await expect(learner.page.getByRole('heading', { name: 'Meine Schulungen', level: 1 })).toBeVisible();

      await page.reload();
      await expect(
        page.getByRole('region', { name: 'Aktive Mitglieder' }).getByRole('row').filter({ hasText: employee }),
      ).toContainText('PTA');
    });

    await test.step('an editor publishes a course with a test the editor wrote, without the AI draft', async () => {
      const authorization = await signIn(editor.page, 'editor');
      await editor.page.goto('/editorial');
      await editor.page.getByRole('button', { name: 'Neue Schulung' }).first().click();
      const dialog = editor.page.getByRole('dialog', { name: 'Neue Schulung anlegen' });
      await dialog.getByRole('textbox', { name: 'Titel' }).fill(title);
      await dialog.getByRole('button', { name: 'Anlegen', exact: true }).click();
      await expect(editor.page).toHaveURL(/\/editorial\/versions\/[0-9a-f-]{36}$/);
      const version = new URL(editor.page.url()).pathname.split('/').at(-1) ?? '';

      await writeContent(editor.page.request, authorization, version);
      await editor.page.reload();

      const workflow = editor.page.getByRole('region', { name: 'Prüfung und Veröffentlichung' });
      await workflow.getByRole('button', { name: 'Zur Prüfung einreichen' }).click();
      await expect(toast(editor.page, 'Version 1 ist zur Prüfung eingereicht.')).toBeVisible();
      await workflow.getByRole('button', { name: 'Veröffentlichen' }).click();
      const publishing = editor.page.getByRole('dialog', { name: 'Version veröffentlichen?' });
      await publishing.getByRole('button', { name: 'Veröffentlichen' }).click();
      await expect(toast(editor.page, 'Version 1 ist veröffentlicht.')).toBeVisible();
      await expect(editor.page.getByText('Veröffentlicht', { exact: true })).toBeVisible();
    });

    await test.step('the owner assigns the course to the employee in the wizard', async () => {
      const next = page.getByRole('button', { name: 'Weiter', exact: true });
      const learnerBox = page
        .getByRole('region', { name: 'Teilnehmende wählen' })
        .getByRole('checkbox', { name: `${employee} auswählen` });
      // Compliance learns of the course and of her membership from events: the wizard is opened again until it lists her.
      await expect(async () => {
        await page.goto('/compliance/assign');
        await expect(page.getByRole('heading', { name: 'Schulung zuweisen', level: 1 })).toBeVisible();
        const course = page
          .getByRole('region', { name: 'Schulung wählen' })
          .getByRole('button', { name: `${title} wählen` });
        await course.click();
        await expect(course).toHaveAttribute('aria-pressed', 'true');
        await next.click();
        await expect(learnerBox).toBeVisible({ timeout: 3_000 });
      }).toPass({ timeout: 30_000 });
      await learnerBox.check();
      await next.click();

      const dueDate = daysAhead(30);
      const due = page.getByRole('region', { name: 'Fälligkeit festlegen' });
      await due.getByLabel('Fällig am').fill(dueDate);
      await next.click();

      const review = page.getByRole('region', { name: 'Zuweisung prüfen' });
      await expect(review.getByRole('definition')).toHaveText([title, employee, germanDate(dueDate), 'Keine']);
      await review.getByRole('button', { name: 'Zuweisen', exact: true }).click();
      await expect(
        page
          .getByRole('region', { name: 'Ergebnis' })
          .getByText('Die Schulung wurde einer Person zugewiesen.', { exact: true }),
      ).toBeVisible();
    });

    await test.step('the employee opens the course from "Meine Schulungen" and works through it in the player', async () => {
      await learner.page.goto('/learning');
      await choose(learner.page, pharmacy);
      await expect(learner.page.getByRole('heading', { name: 'Meine Schulungen', level: 1 })).toBeVisible();
      const course = learner.page
        .getByRole('region', { name: 'Zugewiesene Schulungen' })
        .getByRole('listitem')
        .filter({ has: learner.page.getByRole('heading', { name: title, level: 3 }) });
      await expect(course.getByText('Offen', { exact: true })).toBeVisible();
      await course.getByRole('link', { name: `Starten: ${title}` }).click();
      await expect(learner.page).toHaveURL(/\/learning\/courses\/[0-9a-f-]{36}$/);
      await expect(learner.page.getByRole('heading', { level: 1, name: title })).toBeVisible();
      const toTest = learner.page.getByRole('link', { name: 'Zum Test' });
      await expect(toTest).toBeVisible({ timeout: 30_000 });
      await toTest.click();
      await expect(learner.page).toHaveURL(/\/assessment\/sessions\/[^/]+$/);
      // The examination opens a few seconds after the content is completed: the page asks again until it is.
      await expect(learner.page.getByRole('button', { name: 'Test beginnen' })).toBeVisible({ timeout: 30_000 });
    });

    await test.step('the employee fails the test once', async () => {
      await startAndChoose(learner.page, 1, '5 Sekunden');
      await submit(learner.page);
      await expect(learner.page.getByRole('heading', { level: 1, name: `Ergebnis: ${title}` })).toBeVisible();
      await expect(learner.page.getByText('Versuch 1', { exact: true })).toBeVisible();
      await expect(learner.page.getByText('Nicht bestanden', { exact: true })).toBeVisible();
      await expect(learner.page.getByText('Noch 2 Versuche', { exact: true })).toBeVisible();
    });

    await test.step('the employee passes the test on the second attempt', async () => {
      await learner.page.getByRole('link', { name: 'Nächsten Versuch beginnen' }).click();
      await startAndChoose(learner.page, 2, '30 Sekunden');
      await submit(learner.page);
      await expect(learner.page.getByText('Versuch 2', { exact: true })).toBeVisible();
      await expect(learner.page.getByText('Bestanden', { exact: true })).toBeVisible();
      passedOn = todayInGermany();
      await expect(learner.page.getByText('Ihre Teilnahmebestätigung wird ausgestellt.')).toBeVisible();
    });

    await test.step('the certificate verifies by the link its QR code encodes, signed out', async () => {
      // Z6: no page or API the learner reads names her certificate yet ("Meine Bestätigungen",
      // GET /certification/me/certificates), so its id comes from Certification's database.
      let certificate: IssuedCertificate | undefined;
      await expect
        .poll(
          () => {
            certificate = issuedCertificate(employee, title);
            return certificate?.id ?? '';
          },
          { message: `Certification issues ${employee} a certificate for ${title}`, timeout: 30_000 },
        )
        .not.toBe('');
      const { id, verificationCode } = certificate ?? { id: '', verificationCode: '' };

      // The PDF, as its holder reads it; Certification renders it after issuing, so it may not be ready at once.
      const holder = await accessToken(page.request, 'employee-c');
      let documentUrl = '';
      await expect
        .poll(
          async () => {
            const response = await page.request.get(`/api/certification/certificates/${id}/pdf`, {
              headers: { Authorization: holder },
            });
            if (!response.ok()) {
              return `${response.status()}: ${await response.text()}`;
            }
            documentUrl = ((await response.json()) as { url: string }).url;
            return 'ready';
          },
          { message: `the PDF of certificate ${id}`, timeout: 30_000 },
        )
        .toBe('ready');
      const pdf = await page.request.get(documentUrl);
      expect(pdf.ok(), `the PDF at ${documentUrl} answered ${pdf.status()}`).toBe(true);
      const bytes = await pdf.body();
      const link = await qrLinkIn(visitor.page, bytes);
      expect(link, `no QR code found on the PDF's first page`).not.toBe('');
      expect(new URL(link).origin).toBe(new URL(baseURL ?? '').origin);
      expect(new URL(link).pathname).toBe(`/verify/${verificationCode}`);
      // The link printed below the code is the one it encodes.
      const text = await pdfText(bytes);
      expect(/Echtheit prüfen:\s*(\S+)/.exec(text)?.[1], `the PDF's text: ${text}`).toBe(link);

      await visitor.page.goto(link);
      const main = visitor.page.getByRole('main');
      await expect(visitor.page.getByRole('heading', { level: 1, name: 'Bestätigung prüfen' })).toBeVisible();
      await expect(main.getByRole('heading', { level: 2, name: 'Gültig' })).toBeVisible();
      await expect(main.getByText(employee, { exact: true })).toBeVisible();
      await expect(main.getByText(title, { exact: true })).toBeVisible();
      await expect(main.getByText(germanDate(passedOn), { exact: true })).toBeVisible();
      await expect(main.getByText(germanDate(addMonths(passedOn, 12)), { exact: true })).toBeVisible();
    });

    await test.step('the owner sees the course as completed in the team matrix', async () => {
      await page.getByRole('navigation', { name: 'Bereiche' }).getByRole('link', { name: 'Team-Matrix' }).click();
      await expect(page).toHaveURL(/\/compliance$/);
      await expect(page.getByRole('heading', { name: 'Team-Matrix', level: 1 })).toBeVisible();
      const matrix = page.getByRole('region', { name: 'Team-Matrix: Mitglieder nach Schulungen' });
      await expect(matrix.getByRole('columnheader').filter({ hasText: title })).toBeVisible();
      const cell = matrix
        .getByRole('row')
        .filter({ has: page.getByRole('rowheader', { name: new RegExp(`^${employee}`) }) })
        .getByRole('cell');
      // Compliance completes the assignment when it learns of the certificate, from an event: read the page again
      // until it has.
      await expect(async () => {
        await page.reload();
        await expect(cell).toContainText('Abgeschlossen', { timeout: 3_000 });
      }).toPass({ timeout: 30_000 });
      await expect(cell).toContainText(`gültig bis ${germanDate(addMonths(passedOn, 12))}`);
      const rate = page.getByRole('region', { name: 'Compliance-Quote' });
      await expect(rate.getByText(/^100\s%$/)).toBeVisible();
      await expect(rate.getByText('Fristgerecht: 1 von 1', { exact: true })).toBeVisible();
    });
  } finally {
    await Promise.all([editor.context.close(), learner.context.close(), visitor.context.close()]);
  }
});
