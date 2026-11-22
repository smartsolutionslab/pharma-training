import { expect, type Page, test } from '@playwright/test';
import { invitationLink, inviteToNewPharmacy, mailText, signInAs, unique } from './invitations';

// No route is mocked: the developer registers a pharmacy and invites an address through the host's API, the invitation
// mail is read from Mailpit, and the invitee opens its link in a browser. Each run registers a pharmacy of its own, so
// what earlier runs left (employee-a may already belong to several pharmacies) changes nothing.

/** Every address the page requested and every Referer it sent, to show the token reaches no server. */
function recordRequests(page: Page) {
  const sent: string[] = [];
  page.on('request', (request) => {
    sent.push(request.url(), request.headers()['referer'] ?? '');
  });
  return sent;
}

/** The token of an invitation link: what follows `#token=`. */
const tokenOf = (link: string) => link.slice(link.indexOf('#token=') + '#token='.length);

/** The app has accepted the invitation: "Meine Schulungen", in the pharmacy, with nothing of the link kept. */
async function expectMemberOn(page: Page, pharmacy: string, token: string) {
  await expect(
    page.getByRole('status').filter({ hasText: `Sie sind jetzt Mitglied der Apotheke ${pharmacy}.` }),
  ).toBeVisible();
  await expect(page).toHaveURL(/\/learning$/);
  await expect(page.getByRole('heading', { name: 'Meine Schulungen', level: 1 })).toBeVisible();
  const region = page.getByRole('region', { name: 'Apotheke', exact: true });
  await expect(region.getByText(pharmacy, { exact: true }).first()).toBeVisible();
  expect(page.url()).not.toContain(token);
  expect(await page.evaluate(() => sessionStorage.getItem('invitation-token'))).toBeNull();
}

test('an invited employee signs in from the link and lands on Meine Schulungen in the pharmacy', async ({
  page,
  request,
}) => {
  const pharmacy = `E2E Einladung ${unique('a')}`;
  await inviteToNewPharmacy(request, pharmacy, 'employee-a@example.com');
  const link = invitationLink(await mailText(request, 'employee-a@example.com', pharmacy));
  const token = tokenOf(link);
  const sent = recordRequests(page);

  await page.goto(link);
  await expect(page.getByLabel('Username or email')).toBeVisible();
  expect(page.url()).not.toContain(token);
  await signInAs(page, 'employee-a');

  await expectMemberOn(page, pharmacy, token);
  expect(sent.filter((value) => value.includes(token))).toEqual([]);
});

test('a forwarded link signed in as someone else says the invitation is for another address', async ({
  page,
  request,
}) => {
  const pharmacy = `E2E Weitergeleitet ${unique('b')}`;
  const invited = `${unique('forwarded')}@example.com`;
  await inviteToNewPharmacy(request, pharmacy, invited);
  const link = invitationLink(await mailText(request, invited, pharmacy));

  await page.goto(link);
  await signInAs(page, 'employee-b');

  await expect(page.getByRole('heading', { name: 'Einladung für eine andere Adresse', level: 1 })).toBeVisible();
  await expect(page).toHaveURL(/\/invitations\/accept$/);
});

test('an invited person without an account registers, verifies the address and lands on Meine Schulungen', async ({
  page,
  request,
}) => {
  const pharmacy = `E2E Neues Konto ${unique('c')}`;
  const username = unique('invitee');
  const invited = `${username}@example.com`;
  await inviteToNewPharmacy(request, pharmacy, invited);
  const link = invitationLink(await mailText(request, invited, pharmacy));
  const token = tokenOf(link);

  await page.goto(link);
  await page.getByRole('link', { name: 'Register' }).click();
  await page.getByLabel('Username', { exact: true }).fill(username);
  await page.getByLabel('Email', { exact: true }).fill(invited);
  await page.getByLabel('First name').fill('Ina');
  await page.getByLabel('Last name').fill('Invitee');
  await page.getByLabel('Password', { exact: true }).fill(username);
  await page.getByLabel('Confirm password').fill(username);
  await page.getByRole('button', { name: 'Register' }).click();

  // Keycloak asks the new account to verify its address before it signs in, and mails the link to Mailpit.
  await expect(page.getByText('You need to verify your email address to activate your account.')).toBeVisible();
  const verification = /https?:\/\/\S+\/login-actions\/action-token\?\S+/.exec(
    await mailText(request, invited, 'Verify email'),
  )?.[0];
  expect(verification).toBeDefined();
  await page.goto(verification ?? '');
  // Keycloak may first ask to confirm the address with a link of its own, when it no longer knows the session.
  const proceed = page.getByRole('link', { name: /proceed/i });
  await expect(proceed.or(page.getByRole('heading', { name: 'Meine Schulungen', level: 1 }))).toBeVisible();
  if (await proceed.isVisible()) {
    await proceed.click();
  }

  await expectMemberOn(page, pharmacy, token);
});
