import { expect, type Locator, type Page, test } from '@playwright/test';
import { acceptOn, invitationLink, mailText, recordAcceptances, signInAs, unique } from './invitations';

// The members and invitations page (#12) against the AppHost, with no route mocked. The developer registers a pharmacy
// of their own through the UI and works in it: invites an address, is refused inviting it twice and resending it at
// once (the cooldown of #158), revokes the invitation, is refused ending or demoting themself as its last manager, then
// invites employee-a, who accepts through the mail in Mailpit, and changes and ends her membership. Each run registers
// a pharmacy and invites an address of its own, so what earlier runs left (the developer may belong to many
// pharmacies) changes nothing.

/** The developer's and employee-a's names, as the realm's local users have them. */
const developer = 'Local Developer';
const employee = 'Anna Employee';
const employeeAddress = 'employee-a@example.com';

const lastManager = 'Der letzte Apothekenleiter kann nicht ausgetragen oder zum Mitarbeiter gemacht werden.';

/** Signs in as the developer on the start page. */
async function signIn(page: Page) {
  await page.goto('/');
  await page.getByLabel('Username or email').fill('developer');
  await page.getByLabel('Password', { exact: true }).fill('developer');
  await page.getByRole('button', { name: 'Sign In' }).click();
  await expect(page.getByText('Angemeldet als developer')).toBeVisible();
}

/** Registers a pharmacy with the name through the page, as its pharmacist; the app then shows its team page. */
async function register(page: Page, name: string) {
  await page.goto('/organisation/register');
  await page.getByRole('textbox', { name: 'Name der Apotheke' }).fill(name);
  await page.getByRole('combobox', { name: 'Ihre Tätigkeit' }).selectOption({ label: 'Apotheker' });
  await page.getByRole('button', { name: 'Registrieren', exact: true }).click();
  await expect(page.getByRole('status').filter({ hasText: `Die Apotheke ${name} wurde registriert.` })).toBeVisible();
  await expect(page).toHaveURL(/\/organisation$/);
}

/**
 * Makes the pharmacy current in the shell: chosen in the switcher when the developer belongs to several (earlier runs
 * add some) and another is current, or current by itself when it is their only one or registering made it current.
 */
async function choose(page: Page, pharmacy: string) {
  // Exactly: the start page has regions whose names hold the word too.
  const region = page.getByRole('region', { name: 'Apotheke', exact: true });
  const switcher = region.getByRole('button', { name: /^Apotheke( wählen$|: )/ });
  const name = region.getByText(pharmacy, { exact: true });
  const chosen = region.getByRole('button', { name: `Apotheke: ${pharmacy}` });
  // The first: a switcher that already names the pharmacy holds its name too.
  await expect(switcher.or(name).first()).toBeVisible();
  if ((await switcher.isVisible()) && !(await chosen.isVisible())) {
    await switcher.click();
    await page
      .getByRole('menu', { name: 'Apotheken' })
      .getByRole('menuitemradio', { name: new RegExp(`^${pharmacy}`) })
      .click();
    await expect(chosen).toBeVisible();
  }
  await expect(name).toBeVisible();
}

/** A toast that is a status message with the text. */
const toast = (page: Page, text: string) => page.getByRole('status').filter({ hasText: text });

/** A toast that is an alert with the text: how the page reports a refused action (notify.failure). */
const alertToast = (page: Page, text: string) => page.getByRole('alert').filter({ hasText: text });

/** The row of a section's table holding the text. */
const row = (section: Locator, text: string) => section.getByRole('row').filter({ hasText: text });

/** Invites the address as a PTA and employee through the page's dialog; answers the dialog, still open or closing. */
async function invite(page: Page, email: string) {
  await page.getByRole('button', { name: 'Einladen', exact: true }).click();
  const dialog = page.getByRole('dialog', { name: 'Mitglied einladen' });
  await dialog.getByLabel('E-Mail-Adresse').fill(email);
  await dialog
    .getByRole('combobox', { name: 'Tätigkeit' })
    .selectOption({ label: 'PTA (pharmazeutisch-technischer Assistent)' });
  await dialog.getByRole('combobox', { name: 'Rolle' }).selectOption({ label: 'Mitarbeiter' });
  await dialog.getByRole('button', { name: 'Einladung senden' }).click();
  return dialog;
}

test('a manager invites, resends and revokes, cannot leave as the last manager, and changes and ends a member', async ({
  page,
  request,
  browser,
}) => {
  const pharmacy = `E2E Team ${unique('t')}`;
  const invited = `${unique('e2e')}@example.com`;

  await signIn(page);
  await register(page, pharmacy);
  await choose(page, pharmacy);
  await expect(page.getByRole('heading', { name: 'Mitglieder', level: 1 })).toBeVisible();
  const members = page.getByRole('region', { name: 'Aktive Mitglieder' });
  const invitations = page.getByRole('region', { name: 'Einladungen' });

  // The developer is listed as the pharmacy's manager, as themself.
  await expect(row(members, `${developer} (Sie)`)).toContainText('Apothekenleiter');

  // An invitation is listed as open, with the day it expires.
  const dialog = await invite(page, invited);
  await expect(toast(page, `Die Einladung an ${invited} wurde gesendet.`)).toBeVisible();
  await expect(dialog).toBeHidden();
  await expect(page.getByRole('button', { name: 'Einladen', exact: true })).toBeFocused();
  const invitation = row(invitations, invited);
  await expect(invitation).toContainText('Offen');
  await expect(invitation).toContainText(/\d{2}\.\d{2}\.\d{4}/);

  // The same address again is refused in the dialog, which stays open.
  const again = await invite(page, invited);
  await expect(again.getByRole('alert')).toHaveText('Diese Adresse ist schon Mitglied oder eingeladen.');
  await expect(again).toBeVisible();
  await again.getByRole('button', { name: 'Abbrechen' }).click();
  await expect(again).toBeHidden();

  // Resending right after inviting is refused by the cooldown (#158: 10 minutes after the invitation's latest mail),
  // and the invitation stays listed as open. A renewal that succeeds needs the cooldown to have passed, which only the
  // backend's integration tests can reach with time travel (OrganisationInvitationLimitsTests).
  await invitations.getByRole('button', { name: `Einladung an ${invited} neu senden` }).click();
  await expect(
    alertToast(page, 'Diese Einladung wurde gerade erst gesendet. Sie können sie in einigen Minuten erneut senden.'),
  ).toBeVisible();
  await expect(invitation).toContainText('Offen');

  await invitations.getByRole('button', { name: `Einladung an ${invited} zurückziehen` }).click();
  const revoke = page.getByRole('dialog', { name: `Einladung an ${invited} zurückziehen?` });
  await revoke.getByRole('button', { name: 'Zurückziehen' }).click();
  await expect(toast(page, `Die Einladung an ${invited} wurde zurückgezogen.`)).toBeVisible();
  await expect(invitation).toHaveCount(0);
  await expect(invitations.getByRole('heading', { name: 'Einladungen', level: 2 })).toBeFocused();

  // The last manager can neither end their membership nor become an employee.
  await members.getByRole('button', { name: `${developer} austragen` }).click();
  const end = page.getByRole('dialog', { name: `${developer} austragen?` });
  await end.getByRole('button', { name: 'Austragen' }).click();
  await expect(end).toBeHidden();
  await expect(members.getByRole('alert')).toHaveText(lastManager);
  await expect(members.getByRole('heading', { name: 'Aktive Mitglieder', level: 2 })).toBeFocused();
  await expect(row(members, `${developer} (Sie)`)).toBeVisible();

  await members.getByRole('button', { name: `Tätigkeit und Rolle von ${developer} ändern` }).click();
  const demote = page.getByRole('dialog', { name: 'Tätigkeit und Rolle ändern' });
  await demote.getByRole('combobox', { name: 'Rolle' }).selectOption({ label: 'Mitarbeiter' });
  await demote.getByRole('button', { name: 'Speichern' }).click();
  await expect(demote.getByRole('alert')).toHaveText(lastManager);
  await expect(demote).toBeVisible();
  await demote.getByRole('button', { name: 'Abbrechen' }).click();
  await expect(demote).toBeHidden();
  await expect(members.getByRole('button', { name: `Tätigkeit und Rolle von ${developer} ändern` })).toBeFocused();
  await expect(row(members, `${developer} (Sie)`)).toContainText('Apothekenleiter');

  // employee-a accepts an invitation through its mail, in a browser of her own.
  await invite(page, employeeAddress);
  await expect(toast(page, `Die Einladung an ${employeeAddress} wurde gesendet.`)).toBeVisible();
  await expect(row(invitations, employeeAddress)).toContainText('Offen');
  const link = invitationLink(await mailText(request, employeeAddress, pharmacy));
  const context = await browser.newContext({ locale: 'de-DE' });
  try {
    const invitee = await context.newPage();
    const accepted = recordAcceptances(invitee);
    await invitee.goto(link);
    await signInAs(invitee, 'employee-a');
    await acceptOn(invitee, pharmacy, link.slice(link.indexOf('#token=') + '#token='.length), accepted);
    await expect(toast(invitee, `Sie sind jetzt Mitglied der Apotheke ${pharmacy}.`)).toBeVisible({
      timeout: 15_000,
    });
  } finally {
    await context.close();
  }

  // Back as the developer: she is a member and her invitation is gone; her job function changes, then she leaves.
  await page.reload();
  const member = row(members, employee);
  await expect(member).toContainText('PTA');
  await expect(row(invitations, employeeAddress)).toHaveCount(0);

  await members.getByRole('button', { name: `Tätigkeit und Rolle von ${employee} ändern` }).click();
  const change = page.getByRole('dialog', { name: 'Tätigkeit und Rolle ändern' });
  await change
    .getByRole('combobox', { name: 'Tätigkeit' })
    .selectOption({ label: 'PKA (pharmazeutisch-kaufmännischer Angestellter)' });
  await change.getByRole('button', { name: 'Speichern' }).click();
  await expect(toast(page, `Tätigkeit und Rolle von ${employee} wurden geändert.`)).toBeVisible();
  await expect(change).toBeHidden();
  await expect(member).toContainText('PKA');

  await members.getByRole('button', { name: `${employee} austragen` }).click();
  await page
    .getByRole('dialog', { name: `${employee} austragen?` })
    .getByRole('button', { name: 'Austragen' })
    .click();
  await expect(toast(page, `${employee} wurde ausgetragen.`)).toBeVisible();
  await expect(member).toHaveCount(0);
  await expect(row(members, `${developer} (Sie)`)).toBeVisible();
});
