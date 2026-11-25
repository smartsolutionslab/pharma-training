import { type APIRequestContext, type Browser, expect, type Page, test } from '@playwright/test';
import { addMonths } from '@/lib/addMonths';
import { pdfText, qrLinkIn } from './certificatePdf';
import { courseTitle, daysAhead, germanDate, germanLongDate, pharmacyName } from './compliance';
import { acceptOn, invitationLink, mailText, recordAcceptances, signInAs } from './invitations';
import { fullName, x1Cast, type X1User } from './x1Users';

// MVP 1 end to end (#55, X1), with no route mocked, in three browsers of their own, locally against the AppHost and on
// staging after each deploy (#130, x1-staging.yml). The owner (Olaf Owner, no realm role) registers a pharmacy and invites
// the employee (Clara Employee) on its team page; she accepts from the mail in Mailpit. The editor starts a course in the
// editorial inbox, gives it a text chapter and a test of one question the editor wrote and approved (no AI draft) through
// the API, as e2e/editorial-workflow.spec.ts does, then submits and publishes it on the version's page. The owner assigns
// it to her in the wizard; she opens it from "Meine Schulungen", works through the chapter in the player, fails the test
// once and passes it. Her certificate is the one "Meine Bestätigungen" lists her for the course (GET
// /certification/me/certificates, as she reads it); its PDF is fetched as she would, its page drawn in a browser and its
// QR code decoded there (qrLinkIn). The link it encodes opens /verify/{code} signed out and says it is valid; the owner's
// team matrix shows her course as completed.
//
// Who they are (e2e/x1Users.ts): locally the realm's local users owner, editor and employee-c, a user no other spec
// invites (an address is mailed at most every 10 minutes, #265); on staging three users the run creates through
// Keycloak's admin API and deletes afterwards. Every run registers a pharmacy and publishes a course of its own, so what
// earlier runs left changes nothing; the local owner and employee-c may already belong to several pharmacies. Keycloak's
// login is English.

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

/** The next call the page makes to the host's API with a token: its Authorization header. */
function nextAuthorization(page: Page) {
  return page
    .waitForRequest((request) => request.url().includes('/api/') && request.headers()['authorization'] !== undefined)
    .then((request) => request.headers()['authorization'] ?? '');
}

/** Signs in on the start page; resolves to the Authorization header the app then sends. */
async function signIn(page: Page, user: X1User) {
  const signedIn = nextAuthorization(page);
  await page.goto('/');
  await signInAs(page, user.username, user.password);
  await expect(page.getByText(`Angemeldet als ${user.username}`)).toBeVisible();
  return signedIn;
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
  const chosen = region.getByRole('button', { name: `Apotheke: ${pharmacy}`, exact: true });
  const only = region.getByRole('paragraph').filter({ has: page.getByText(pharmacy, { exact: true }) });
  // Any switcher but this pharmacy's, by its whole name: a name that holds this one ("… Nord") is another.
  const escaped = pharmacy.replace(/[.*+?^${}()|[\]\\]/g, '\\$&');
  const another = region.getByRole('button', { name: new RegExp(`^Apotheke( wählen|: (?!${escaped}$).+)$`) });
  // A settled state before deciding: the pharmacy is current (chosen on the switcher, or the only one), or the switcher
  // shows another (or none). While the list loads, neither shows and the wait goes on; either one stays until the test
  // chooses. A list read before the pharmacy was added shows another, and its menu lacks this pharmacy: the callers
  // open a page that reads the memberships afresh first.
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
  const cast = await x1Cast();
  const employee = fullName(cast.employee);
  const employeeAddress = cast.employee.email;
  const pharmacy = pharmacyName('MVP 1');
  const title = courseTitle('MVP 1');
  const editor = await browserOf(browser);
  const learner = await browserOf(browser);
  const visitor = await browserOf(browser);
  // The day she passes, in Germany, worked out once: the certificate is dated then, and valid until 12 months on.
  // The day the host counts as the day of passing lies between these two, read on the test's clock around the submit;
  // the dates the app shows are then checked against the issue date it shows, as the host takes both from passedAt.
  let submittedOn = '';
  let passedOn = '';
  let issuedOn = '';

  try {
    await test.step('the owner registers a pharmacy', async () => {
      await signIn(page, cast.owner);
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
      await signInAs(learner.page, cast.employee.username, cast.employee.password);
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
      const authorization = await signIn(editor.page, cast.editor);
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
      await expect(review.getByRole('definition')).toHaveText([title, employee, germanLongDate(dueDate), 'Keine']);
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
      submittedOn = todayInGermany();
      await submit(learner.page);
      await expect(learner.page.getByText('Versuch 2', { exact: true })).toBeVisible();
      await expect(learner.page.getByText('Bestanden', { exact: true })).toBeVisible();
      passedOn = todayInGermany();
      await expect(learner.page.getByText('Ihre Teilnahmebestätigung wird ausgestellt.')).toBeVisible();
    });

    await test.step('the certificate verifies by the link its QR code encodes, signed out', async () => {
      // Her certificate, as "Meine Bestätigungen" lists it for her: the one for this course. Certification issues it
      // from an event after the pass, so the list is asked again until it has. Her token comes from her own page.
      const ready = nextAuthorization(learner.page);
      await learner.page.goto('/learning');
      const holder = await ready;
      let id = '';
      await expect
        .poll(
          async () => {
            const response = await page.request.get('/api/certification/me/certificates', {
              headers: { Authorization: holder },
            });
            if (!response.ok()) {
              return `${response.status()}: ${await response.text()}`;
            }
            const held = (await response.json()) as { items: { certificateId: string; courseTitle: string }[] };
            const mine = held.items.filter((each) => each.courseTitle === title);
            id = mine[0]?.certificateId ?? '';
            return mine.length;
          },
          { message: `Certification issues ${employee} one certificate for ${title}`, timeout: 30_000 },
        )
        .toBe(1);

      // The PDF, as its holder reads it; Certification renders it after issuing, so it may not be ready at once.
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
      expect(new URL(link).pathname).toMatch(/^\/verify\/[^/]+$/);
      // The link printed below the code is the one it encodes.
      const text = await pdfText(bytes);
      expect(/Echtheit prüfen:\s*(\S+)/.exec(text)?.[1], `the PDF's text: ${text}`).toBe(link);

      await visitor.page.goto(link);
      const main = visitor.page.getByRole('main');
      await expect(visitor.page.getByRole('heading', { level: 1, name: 'Bestätigung prüfen' })).toBeVisible();
      await expect(main.getByRole('heading', { level: 2, name: 'Gültig' })).toBeVisible();
      await expect(main.getByText(employee, { exact: true })).toBeVisible();
      await expect(main.getByText(title, { exact: true })).toBeVisible();
      // The issue date as the page shows it: the day the host counted, before or after a midnight in Germany during the
      // submit. Every valid-until is twelve months from it.
      const shownIssueDate = (
        await main.locator('dt', { hasText: 'Ausgestellt am' }).locator('+ dd').textContent()
      )?.trim();
      expect([germanDate(submittedOn), germanDate(passedOn)]).toContain(shownIssueDate);
      issuedOn = shownIssueDate === germanDate(passedOn) ? passedOn : submittedOn;
      await expect(main.getByText(germanDate(addMonths(issuedOn, 12)), { exact: true })).toBeVisible();
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
      await expect(cell).toContainText(`gültig bis ${germanDate(addMonths(issuedOn, 12))}`);
      const rate = page.getByRole('region', { name: 'Compliance-Quote' });
      await expect(rate.getByText(/^100\s%$/)).toBeVisible();
      await expect(rate.getByText('Fristgerecht: 1 von 1', { exact: true })).toBeVisible();
    });
  } finally {
    await Promise.all([editor.context.close(), learner.context.close(), visitor.context.close()]);
    // The run's users go, however it ended (on staging; the local users stay).
    await cast.dispose();
  }
});
