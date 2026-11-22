import { expect, type Locator, type Page, test } from '@playwright/test';

// The test page of a course version (#25), as the realm's local user `editor` (realm role editor) works on it against
// the AppHost. Each test starts a course of its own, so runs never share a version. Keycloak's login is English.
// The AppHost runs without the model's settings unless a developer set them: the AI draft then answers 503
// model-not-configured, and the draft's 200 path is proven by the page's component tests instead.

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

/** Adds a text chapter on the version's page. */
async function addTextChapter(page: Page, title: string, text: string) {
  await page.getByRole('button', { name: 'Kapitel hinzufügen' }).first().click();
  const dialog = page.getByRole('dialog', { name: 'Neues Kapitel' });
  await dialog.getByRole('textbox', { name: 'Titel' }).fill(title);
  await dialog.getByRole('textbox', { name: 'Inhalt (Markdown)' }).fill(text);
  await dialog.getByRole('button', { name: 'Speichern' }).click();
  await expect(dialog).toBeHidden();
  await expect(
    page.getByRole('status').filter({ hasText: new RegExp(`Das Kapitel „${title}. wurde gespeichert\\.`) }),
  ).toBeVisible();
}

/** Opens the version's test page from its link. */
async function openTest(page: Page, title: string) {
  await page.getByRole('link', { name: 'Test: Vorgaben und Fragen' }).click();
  await expect(page).toHaveURL(/\/editorial\/versions\/[0-9a-f-]{36}\/test$/);
  await expect(page.getByRole('heading', { name: `Test zu „${title}“`, level: 1 })).toBeVisible();
}

/** The blueprint's section, a region named by its heading. */
const blueprint = (page: Page) => page.getByRole('region', { name: 'Testvorgaben' });

/** Changes the blueprint in its form and waits for it to be saved. */
async function setBlueprint(page: Page, questions: string, percent: string, months: string) {
  await page.getByRole('button', { name: 'Vorgaben ändern' }).click();
  await page.getByLabel('Anzahl Fragen').fill(questions);
  await page.getByLabel('Bestehensgrenze (%)').fill(percent);
  await page.getByLabel('Gültigkeit (Monate)').fill(months);
  await page.getByRole('button', { name: 'Vorgaben speichern' }).click();
  await expect(page.getByRole('status').filter({ hasText: 'Die Testvorgaben wurden gespeichert.' })).toBeVisible();
  await expect(page.getByRole('button', { name: 'Vorgaben ändern' })).toBeVisible();
}

/** The question form's dialog: a single-choice question about the first chapter, its first option correct. */
async function addSingleChoiceQuestion(page: Page, text: string, options: [string, string]) {
  await page.getByRole('button', { name: 'Frage hinzufügen' }).first().click();
  const dialog = page.getByRole('dialog', { name: 'Neue Frage' });
  await dialog.getByRole('combobox', { name: 'Kapitel' }).selectOption({ index: 1 });
  await dialog.getByRole('textbox', { name: 'Frage', exact: true }).fill(text);
  await dialog.getByRole('textbox', { name: 'Option 1', exact: true }).fill(options[0]);
  await dialog.getByRole('textbox', { name: 'Option 2', exact: true }).fill(options[1]);
  const correct: Locator = dialog
    .getByRole('radiogroup', { name: 'Richtige Option' })
    .or(dialog.getByRole('group', { name: 'Richtige Option' }));
  await correct.getByRole('radio').first().check();
  await dialog.getByRole('button', { name: 'Speichern' }).click();
  await expect(dialog).toBeHidden();
  await expect(page.getByRole('status').filter({ hasText: 'Die Frage wurde gespeichert.' })).toBeVisible();
}

/** The question cards, the list's own items (each card holds a list of its options too). */
const questions = (page: Page) => page.getByRole('list', { name: 'Fragen', exact: true }).locator(':scope > li');

test('the blueprint is saved and shown', async ({ page }) => {
  const title = `E2E Testvorgaben ${Date.now()}`;
  await startCourse(page, title);
  await openTest(page, title);
  await expect(blueprint(page)).toContainText('10');
  await expect(blueprint(page)).toContainText(/80\s%/);
  await expect(blueprint(page)).toContainText('12 Monate');

  await setBlueprint(page, '2', '75', '24');

  await page.reload();
  await expect(page.getByRole('heading', { name: 'Testvorgaben', level: 2 })).toBeVisible();
  await expect(blueprint(page)).toContainText(/75\s%/);
  await expect(blueprint(page)).toContainText('24 Monate');
  await expect(blueprint(page).locator('dd').first()).toHaveText('2');
});

test('a hand-written question is added, marked unreviewed, approved and counted', async ({ page }) => {
  const title = `E2E Fragen ${Date.now()}`;
  await startCourse(page, title);
  await addTextChapter(page, 'Einleitung', '# Willkommen\n\nHände werden **30 Sekunden** gewaschen.');
  await openTest(page, title);
  await setBlueprint(page, '2', '80', '12');

  await addSingleChoiceQuestion(page, 'Wie lange waschen Sie die Hände?', ['30 Sekunden', '5 Sekunden']);

  await expect(questions(page)).toHaveCount(1);
  await expect(questions(page).first()).toContainText('Wie lange waschen Sie die Hände?');
  await expect(questions(page).first()).toContainText('Ungeprüft');
  await expect(questions(page).first()).toContainText('Von Hand geschrieben, noch nicht geprüft');
  await expect(page.getByText('0 von 2 Fragen geprüft')).toBeVisible();

  await page.getByRole('button', { name: 'Frage 1 freigeben' }).click();

  await expect(page.getByRole('status').filter({ hasText: 'Frage 1 ist freigegeben.' })).toBeVisible();
  await expect(questions(page).first()).toContainText('Geprüft');
  await expect(questions(page).first()).not.toContainText('Ungeprüft');
  await expect(page.getByText('1 von 2 Fragen geprüft')).toBeVisible();
  await expect(page.getByRole('button', { name: 'Frage 1 freigeben' })).toHaveCount(0);
  // Who approved it, and when (Von Hand geschrieben, geprüft von … am …), is T8's, pending a decision.
  await page.reload();
  await expect(questions(page).first()).toContainText('Geprüft');
  await expect(page.getByText('1 von 2 Fragen geprüft')).toBeVisible();
});

test('an AI draft asks first, warns about the unreviewed question and without a model changes nothing', async ({
  page,
}) => {
  const title = `E2E KI-Entwurf ${Date.now()}`;
  await startCourse(page, title);
  await addTextChapter(page, 'Einleitung', '# Willkommen\n\nHände werden **30 Sekunden** gewaschen.');
  await openTest(page, title);
  await setBlueprint(page, '1', '80', '12');
  await addSingleChoiceQuestion(page, 'Wie lange waschen Sie die Hände?', ['30 Sekunden', '5 Sekunden']);
  await expect(questions(page)).toHaveCount(1);

  await page.getByRole('button', { name: 'KI-Entwurf erstellen', exact: true }).click();
  const confirming = page
    .getByRole('alertdialog', { name: 'KI-Entwurf erstellen?' })
    .or(page.getByRole('dialog', { name: 'KI-Entwurf erstellen?' }));
  await expect(confirming.getByText('Die KI entwirft eine Frage. Geprüfte Fragen bleiben erhalten.')).toBeVisible();
  await expect(
    confirming.getByText('Eine ungeprüfte Frage wird ersetzt, auch wenn sie von Hand geschrieben ist.'),
  ).toBeVisible();
  await confirming.getByRole('button', { name: 'Entwurf erstellen' }).click();

  await expect(
    page.getByRole('alert').filter({
      hasText: 'Für KI-Entwürfe ist kein Sprachmodell eingerichtet. Wenden Sie sich an den Betrieb der Plattform.',
    }),
  ).toBeVisible();
  await expect(questions(page)).toHaveCount(1);
  await expect(questions(page).first()).toContainText('Wie lange waschen Sie die Hände?');
  await page.reload();
  await expect(questions(page)).toHaveCount(1);
  await expect(questions(page).first()).toContainText('Wie lange waschen Sie die Hände?');
  await expect(questions(page).first()).toContainText('Ungeprüft');
});
