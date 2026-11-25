import { type APIRequestContext, expect, type Page } from '@playwright/test';

// The running application's identity provider and mail catcher, at the ports the AppHost gives them in run mode; on
// staging their public addresses, and Mailpit's inbox behind its basic auth, user:password (#130).
const keycloak = process.env.E2E_KEYCLOAK_URL ?? 'http://localhost:8080';
const mailpit = process.env.E2E_MAILPIT_URL ?? 'http://localhost:8025';
const mailpitAuth = process.env.E2E_MAILPIT_AUTH ?? '';

/** Mailpit's basic auth, when it has one: staging's has, run mode's has not. */
const mailpitHeaders: Record<string, string> =
  mailpitAuth === '' ? {} : { Authorization: `Basic ${Buffer.from(mailpitAuth).toString('base64')}` };

/** An address and a pharmacy name no earlier run used, so a test finds its own mail and its own pharmacy. */
export function unique(prefix: string) {
  return `${prefix}-${Date.now()}-${Math.floor(Math.random() * 1e6)}`;
}

/** The access token Keycloak issues to a local user, whose password is their name (direct access grants are on). */
async function accessToken(request: APIRequestContext, user: string) {
  const response = await request.post(`${keycloak}/realms/pharma-training/protocol/openid-connect/token`, {
    form: { grant_type: 'password', client_id: 'pharma-training-web', username: user, password: user },
  });
  expect(response.ok(), await response.text()).toBe(true);
  const body = (await response.json()) as { access_token: string };
  return body.access_token;
}

/**
 * Registers a pharmacy with the given name as the local user developer, who manages it, and invites the address to it
 * as an employee, through the host's API; answers the pharmacy's id.
 */
export async function inviteToNewPharmacy(request: APIRequestContext, name: string, email: string) {
  const headers = { Authorization: `Bearer ${await accessToken(request, 'developer')}` };
  const registered = await request.post('/api/organisation/pharmacies', {
    headers,
    data: { name, jobFunction: 'Pharmacist' },
  });
  expect(registered.status(), await registered.text()).toBe(201);
  const pharmacy = (await registered.json()) as string;

  const invited = await request.post(`/api/organisation/pharmacies/${pharmacy}/invitations`, {
    headers,
    data: { email, jobFunction: 'Pta', role: 'Employee' },
  });
  expect(invited.status(), await invited.text()).toBe(201);
  return pharmacy;
}

type Summary = { ID: string; Subject: string };

/**
 * The text of the one mail to the address whose subject contains the marker, from Mailpit's API; waits for it, since
 * the application sends mail from a consumer, after the request that asked for it has been answered. Behind basic auth
 * (staging) it asks through Node's fetch, which no Playwright trace records, so the auth stays out of one; any request
 * context of Playwright's is traced once tracing is on.
 */
export async function mailText(request: APIRequestContext, to: string, marker: string) {
  const get = async (path: string, query?: Record<string, string>) => {
    const url = `${mailpit}${path}${query === undefined ? '' : `?${new URLSearchParams(query).toString()}`}`;
    if (mailpitAuth === '') {
      const response = await request.get(url);
      return { ok: response.ok(), status: response.status(), json: () => response.json() as Promise<unknown> };
    }
    const response = await fetch(url, { headers: mailpitHeaders });
    return { ok: response.ok, status: response.status, json: () => response.json() as Promise<unknown> };
  };

  let id: string | undefined;
  await expect
    .poll(
      async () => {
        const response = await get('/api/v1/search', { query: `to:"${to}"` });
        expect(response.ok, `Mailpit's search answered ${response.status}`).toBe(true);
        const found = ((await response.json()) as { messages: Summary[] }).messages.filter((message) =>
          message.Subject.includes(marker),
        );
        id = found[0]?.ID;
        return found.length;
      },
      { message: `a mail to ${to} about "${marker}"`, timeout: 30_000 },
    )
    .toBe(1);
  const message = await get(`/api/v1/message/${id ?? ''}`);
  return ((await message.json()) as { Text: string }).Text;
}

/** The invitation link in an invitation mail. */
export function invitationLink(text: string) {
  const link = /https?:\/\/\S+\/invitations\/accept#token=[A-Za-z0-9_-]{43}/.exec(text)?.[0];
  expect(link, `no invitation link in: ${text}`).toBeDefined();
  return link ?? '';
}

/**
 * Signs in on Keycloak's login page, wherever the app sent the browser to it from. A local user's password is their
 * name; a user a test created has one of its own.
 */
export async function signInAs(page: Page, user: string, password = user) {
  await page.getByLabel('Username or email').fill(user);
  await page.getByLabel('Password', { exact: true }).fill(password);
  await page.getByRole('button', { name: 'Sign In' }).click();
}

/** Every POST that accepts an invitation, from the moment the test starts recording. */
export function recordAcceptances(page: Page) {
  const accepted: string[] = [];
  page.on('request', (request) => {
    if (request.method() === 'POST' && request.url().endsWith('/organisation/invitations/accept')) {
      accepted.push(request.url());
    }
  });
  return accepted;
}

/**
 * The page is ready to accept: "Einladung annehmen", naming no pharmacy, with the token out of the address and storage
 * and nothing posted yet; pressing its button accepts the invitation (the owner's decision on #13, 2026-11-23).
 */
export async function acceptOn(page: Page, pharmacy: string, token: string, accepted: string[]) {
  const button = page.getByRole('button', { name: 'Einladung annehmen' });
  // From the link through Keycloak's sign-in back to the page: longer than one page after a cold start.
  await expect(button).toBeVisible({ timeout: 15_000 });
  await expect(page.getByRole('heading', { name: 'Einladung annehmen', level: 1 })).toBeVisible();
  await expect(page.getByRole('main')).not.toContainText(pharmacy);
  await expect(page).toHaveURL(/\/invitations\/accept$/);
  expect(page.url()).not.toContain(token);
  expect(await page.evaluate(() => sessionStorage.getItem('invitation-token'))).toBeNull();
  expect(accepted).toEqual([]);
  await button.click();
  await expect.poll(() => accepted.length).toBe(1);
}
