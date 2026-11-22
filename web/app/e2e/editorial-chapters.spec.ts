import { expect, type Locator, type Page, test } from '@playwright/test';

// Text chapters of a draft, as the realm's local user `editor` (realm role editor) writes them against the AppHost.
// Each test starts a course of its own, so runs never share a version. Keycloak's login is English.

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

/** The chapter form's dialog, by its name: "Neues Kapitel" or "Kapitel bearbeiten". */
async function fillChapter(dialog: Locator, { title, text }: { title: string; text?: string }) {
  await dialog.getByRole('textbox', { name: 'Titel' }).fill(title);
  if (text !== undefined) {
    await dialog.getByRole('textbox', { name: 'Inhalt (Markdown)' }).fill(text);
  }
  await dialog.getByRole('button', { name: 'Speichern' }).click();
  await expect(dialog).toBeHidden();
}

/** Adds a text chapter from the section's (or the empty state's) "Kapitel hinzufügen". */
async function addTextChapter(page: Page, title: string, text: string) {
  await page.getByRole('button', { name: 'Kapitel hinzufügen' }).first().click();
  await fillChapter(page.getByRole('dialog', { name: 'Neues Kapitel' }), { title, text });
  await expect(
    page.getByRole('status').filter({ hasText: new RegExp(`Das Kapitel „${title}. wurde gespeichert\\.`) }),
  ).toBeVisible();
}

test('an editor adds a text chapter, edits its title and deletes it; a reload shows each step', async ({ page }) => {
  await startCourse(page, `E2E Kapitel ${Date.now()}`);
  await expect(page.getByText('Diese Version hat noch keine Kapitel.')).toBeVisible();

  await addTextChapter(page, 'Einleitung', '# Willkommen\n\nDas ist **wichtig**.');

  await expect(chapters(page)).toHaveCount(1);
  await expect(chapters(page).first()).toContainText('Kapitel 1');
  await expect(chapters(page).first()).toContainText('Einleitung');
  await expect(chapters(page).first()).toContainText('Text');
  await page.reload();
  await expect(chapters(page)).toHaveCount(1);
  await expect(chapters(page).first()).toContainText('Einleitung');

  await page.getByRole('button', { name: /^„Einleitung. bearbeiten$/ }).click();
  const editing = page.getByRole('dialog', { name: 'Kapitel bearbeiten' });
  await expect(editing.getByRole('textbox', { name: 'Inhalt (Markdown)' })).toHaveValue(
    '# Willkommen\n\nDas ist **wichtig**.',
  );
  await fillChapter(editing, { title: 'Einführung' });

  await expect(chapters(page).first()).toContainText('Einführung');
  await page.reload();
  await expect(chapters(page).first()).toContainText('Einführung');
  await expect(page.getByText('Einleitung', { exact: true })).toHaveCount(0);

  await page.getByRole('button', { name: /^„Einführung. löschen$/ }).click();
  const confirming = page
    .getByRole('alertdialog', { name: 'Kapitel löschen?' })
    .or(page.getByRole('dialog', { name: 'Kapitel löschen?' }));
  await expect(confirming.getByText(/^„Einführung. wird gelöscht\.$/)).toBeVisible();
  await confirming.getByRole('button', { name: 'Löschen', exact: true }).click();

  await expect(page.getByRole('status').filter({ hasText: /Das Kapitel „Einführung. wurde gelöscht\./ })).toBeVisible();
  await expect(page.getByText('Diese Version hat noch keine Kapitel.')).toBeVisible();
  await page.reload();
  await expect(page.getByText('Diese Version hat noch keine Kapitel.')).toBeVisible();
});

test("the preview shows an editor's Markdown rendered, and a script in it as text", async ({ page }) => {
  await startCourse(page, `E2E Vorschau ${Date.now()}`);
  const ran: string[] = [];
  page.on('dialog', async (dialog) => {
    ran.push(dialog.message());
    await dialog.dismiss();
  });

  await page.getByRole('button', { name: 'Kapitel hinzufügen' }).first().click();
  const dialog = page.getByRole('dialog', { name: 'Neues Kapitel' });
  await dialog.getByRole('textbox', { name: 'Titel' }).fill('Sicherheit');
  await dialog
    .getByRole('textbox', { name: 'Inhalt (Markdown)' })
    .fill('# Willkommen\n\n<script>alert(1)</script>\n\n<img src=x onerror=alert(2)>\n\n[Klick](javascript:alert(3))');
  await dialog.getByRole('button', { name: 'Vorschau' }).click();

  await expect(dialog.getByRole('button', { name: 'Vorschau' })).toHaveAttribute('aria-pressed', 'true');
  await expect(dialog.getByRole('heading', { name: 'Willkommen', level: 3 })).toBeVisible();
  await expect(dialog.getByText('<script>alert(1)</script>')).toBeVisible();
  await expect(dialog.locator('.markdown img')).toHaveCount(0);
  await expect(dialog.locator('.markdown a[href^="javascript:"]')).toHaveCount(0);
  expect(ran).toEqual([]);
});

/** The chapters' titles in the order the list shows them. */
const titlesShown = async (page: Page, titles: string[]) =>
  expect(chapters(page)).toHaveText(titles.map((title) => new RegExp(title)));

test('an editor moves the third chapter to the top with its button; a reload keeps the order', async ({ page }) => {
  await startCourse(page, `E2E Reihenfolge ${Date.now()}`);
  for (const title of ['Erstes', 'Zweites', 'Drittes']) {
    await addTextChapter(page, title, `# ${title}`);
  }
  await titlesShown(page, ['Erstes', 'Zweites', 'Drittes']);

  const up = page.getByRole('button', { name: /^„Drittes. nach oben verschieben$/ });
  await up.click();
  await expect(page.getByText(/^„Drittes. steht jetzt an Position 2 von 3\.$/)).toBeAttached();
  await titlesShown(page, ['Erstes', 'Drittes', 'Zweites']);
  // The button keeps the focus, so a keyboard user presses it again.
  await expect(up).toBeFocused();
  await page.keyboard.press('Enter');
  await expect(page.getByText(/^„Drittes. steht jetzt an Position 1 von 3\.$/)).toBeAttached();
  await titlesShown(page, ['Drittes', 'Erstes', 'Zweites']);
  await expect(up).toBeDisabled();

  await page.reload();
  await titlesShown(page, ['Drittes', 'Erstes', 'Zweites']);
  await expect(chapters(page).first()).toContainText('Kapitel 1');
});

test('an editor drags the first chapter by its handle onto the last; a reload keeps the order', async ({ page }) => {
  await startCourse(page, `E2E Ziehen ${Date.now()}`);
  for (const title of ['Erstes', 'Zweites', 'Drittes']) {
    await addTextChapter(page, title, `# ${title}`);
  }

  await chapters(page)
    .filter({ hasText: 'Erstes' })
    .getByTitle('Zum Verschieben ziehen')
    .dragTo(chapters(page).filter({ hasText: 'Drittes' }));

  await titlesShown(page, ['Zweites', 'Drittes', 'Erstes']);
  await page.reload();
  await titlesShown(page, ['Zweites', 'Drittes', 'Erstes']);
});
