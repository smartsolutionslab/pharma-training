import { QueryClient, VueQueryPlugin } from '@tanstack/vue-query';
import { render, screen, waitFor, within } from '@testing-library/vue';
import userEvent from '@testing-library/user-event';
import { afterEach, beforeEach, expect, test, vi } from 'vitest';
import { createMemoryHistory } from 'vue-router';
import type { CertificateVerificationDto } from '@/api/generated';
import { setLanguage } from '@/i18n';
import { createAppRouter } from '@/router';
import VerifyCertificatePage from './VerifyCertificatePage.vue';

// The verification page (#54, Z2), /verify/:code, which a certificate's QR code opens, for anyone, signed in or not. It
// asks the host GET /certification/verify/{code} once and shows whether the certificate is valid or expired, then its
// holder, course and dates, and nothing else. A malformed code is not sent; an unknown one reads as the malformed one
// does. Every request spends the per-address limit that all verifiers share (ADR 0026): no automatic retry, no refetch
// on focus, one request per press of Retry.
//
// The page renders signed out, as a visitor sees it: no sign-in plugin, no token. Its layout (the public header) is
// the shell's, tested in src/shell/AppShell.public.test.ts.

type Fetch = (input: RequestInfo | URL, init?: RequestInit) => Promise<Response>;

const code = 'AbCdEfGhIjKlMnOpQrStUvWxYz012-_9';
const verifyPath = (verified = code) => `/api/certification/verify/${verified}`;

const valid: CertificateVerificationDto = {
  holderName: 'Anna Schmidt',
  courseTitle: 'Hygiene in der Apotheke',
  issuedOn: '2026-11-24',
  validUntil: '2027-11-24',
  validity: 'Valid',
};

const expired: CertificateVerificationDto = {
  holderName: 'Berta Bauer',
  courseTitle: 'Datenschutz',
  issuedOn: '2024-11-24',
  validUntil: '2025-11-24',
  validity: 'Expired',
};

const notVerifiableText =
  'Zu diesem Link gibt es keine Teilnahmebestätigung. Prüfen Sie, ob Sie den vollständigen Link aus dem QR-Code geöffnet haben.';
const limitText = 'Gerade werden zu viele Prüfungen angefragt. Bitte versuchen Sie es in einer Minute erneut.';
const loadFailedText = 'Die Bestätigung konnte nicht geprüft werden.';

/** Problem details as the host sends a refusal. */
function problem(status: number, code?: string, detail = 'Words of the host, not for the user.', headers = {}) {
  return new Response(JSON.stringify({ title: 'Refused', status, detail, code }), {
    status,
    headers: { 'Content-Type': 'application/problem+json', ...headers },
  });
}

const tooMany = () => problem(429, 'certification.verification-limit-reached', 'Rate limit.', { 'Retry-After': '60' });

// The host: the verification answers with whatever the test sets; anything else waits for an answer that never comes.
const fetch = vi.fn<Fetch>();
vi.stubGlobal('fetch', fetch);
let answer: () => Response;

beforeEach(() => {
  answer = () => Response.json(valid);
  fetch.mockImplementation((input) => {
    const url = new URL(String(input), 'http://localhost');
    if (url.pathname.startsWith('/api/certification/verify/')) {
      return Promise.resolve(answer());
    }
    return new Promise<Response>(() => undefined);
  });
});
afterEach(() => {
  fetch.mockReset();
});

/** The paths of the verifications the host received, in order. */
const verifications = () =>
  fetch.mock.calls
    .map(([input]) => new URL(String(input), 'http://localhost').pathname)
    .filter((path) => path.startsWith('/api/certification/verify'));

/**
 * The page at a verification link, as a QR code opens it. The query client is the library's default: whatever the page
 * does not retry or refetch, it has said so itself.
 */
async function renderPage(address = `/verify/${code}`) {
  const router = createAppRouter(createMemoryHistory());
  await router.push(address);
  const queryClient = new QueryClient();
  render(VerifyCertificatePage, { global: { plugins: [router, [VueQueryPlugin, { queryClient }]] } });
  return { router, queryClient };
}

const title = () => screen.getByRole('heading', { level: 1, name: 'Bestätigung prüfen' });

/** The data the page lists, term by term. */
function listed(): [string, string][] {
  const terms = screen.getAllByRole('term').map((term) => term.textContent?.trim() ?? '');
  const definitions = screen.getAllByRole('definition').map((definition) => definition.textContent?.trim() ?? '');
  expect(definitions).toHaveLength(terms.length);
  return terms.map((term, index) => [term, definitions[index] ?? '']);
}

test('the page asks the host about the code in its address, once', async () => {
  await renderPage();

  await screen.findByRole('heading', { level: 2, name: 'Gültig' });
  expect(verifications()).toEqual([verifyPath()]);
});

test('a valid certificate reads Gültig first, then its holder, course and German dates', async () => {
  await renderPage();

  const result = await screen.findByRole('region', { name: 'Gültig' });
  expect(title()).toHaveClass('text-title');
  expect(within(result).getByRole('heading', { level: 2, name: 'Gültig' })).toBeInTheDocument();
  expect(
    within(result).getByText('Diese Teilnahmebestätigung ist echt und gültig bis 24.11.2027.'),
  ).toBeInTheDocument();
  expect(listed()).toEqual([
    ['Name', 'Anna Schmidt'],
    ['Schulung', 'Hygiene in der Apotheke'],
    ['Ausgestellt am', '24.11.2026'],
    ['Gültig bis', '24.11.2027'],
  ]);
  expect(screen.queryByRole('alert')).not.toBeInTheDocument();
});

// The word is the answer, not its colour: a screen reader hears it, and it reads the same printed in black and white.
test('an expired certificate reads Abgelaufen, in words, with the day it was valid until', async () => {
  answer = () => Response.json(expired);
  await renderPage();

  const result = await screen.findByRole('region', { name: 'Abgelaufen' });
  expect(within(result).getByRole('heading', { level: 2 })).toHaveTextContent('Abgelaufen');
  expect(
    within(result).getByText('Diese Teilnahmebestätigung ist echt, war aber nur bis 24.11.2025 gültig.'),
  ).toBeInTheDocument();
  expect(listed()).toEqual([
    ['Name', 'Berta Bauer'],
    ['Schulung', 'Datenschutz'],
    ['Ausgestellt am', '24.11.2024'],
    ['Gültig bis', '24.11.2025'],
  ]);
  expect(screen.queryByRole('heading', { name: 'Gültig' })).not.toBeInTheDocument();
});

// Z2: the certificate's data and nothing else. Whatever else an answer carries stays off the page, and so does the code.
test('the page shows neither the code nor any other field of the answer', async () => {
  answer = () =>
    Response.json({ ...valid, scorePercent: 87, pharmacyName: 'Apotheke am Markt', verificationCode: 'Geheim' });
  await renderPage();

  await screen.findByRole('heading', { level: 2, name: 'Gültig' });
  const shown = document.body.textContent ?? '';
  expect(shown).not.toContain(code);
  expect(shown).not.toContain('87');
  expect(shown).not.toContain('Apotheke am Markt');
  expect(shown).not.toContain('Geheim');
});

// The answer is untrusted input: a name that looks like markup is shown as the text it is.
test('a holder’s name and a course title are shown as text, never as markup', async () => {
  answer = () =>
    Response.json({ ...valid, holderName: '<b>Anna</b> Schmidt', courseTitle: '<img src=x onerror="alert(1)">' });
  await renderPage();

  await screen.findByRole('heading', { level: 2, name: 'Gültig' });
  expect(listed()).toEqual([
    ['Name', '<b>Anna</b> Schmidt'],
    ['Schulung', '<img src=x onerror="alert(1)">'],
    ['Ausgestellt am', '24.11.2026'],
    ['Gültig bis', '24.11.2027'],
  ]);
  expect(document.querySelector('main b, main img')).toBeNull();
});

test('a malformed code is not sent, and the page says it cannot be verified', async () => {
  await renderPage('/verify/abc');

  expect(await screen.findByRole('heading', { level: 2, name: 'Nicht prüfbar' })).toBeInTheDocument();
  expect(screen.getByText(notVerifiableText)).toBeInTheDocument();
  expect(screen.queryByRole('alert')).not.toBeInTheDocument();
  expect(verifications()).toEqual([]);
});

// No enumeration (#51): an unknown code reads exactly as a malformed one, as content, not as an alert.
test('a code the host does not know reads as a malformed one: Nicht prüfbar, without an alert', async () => {
  answer = () => problem(404, 'certification.certificate-not-verifiable', 'Not verifiable.');
  await renderPage();

  expect(await screen.findByRole('heading', { level: 2, name: 'Nicht prüfbar' })).toBeInTheDocument();
  expect(screen.getByText(notVerifiableText)).toBeInTheDocument();
  expect(screen.queryByRole('alert')).not.toBeInTheDocument();
  expect(screen.queryByText('Not verifiable.')).not.toBeInTheDocument();
  expect(screen.queryByRole('button', { name: 'Erneut versuchen' })).not.toBeInTheDocument();
  expect(verifications()).toEqual([verifyPath()]);
});

// The limit every verifier shares (ADR 0026): the page says why, without blaming the reader, sends nothing by itself,
// and offers one more request per press.
test('over the limit the page says so in an alert, sends nothing more by itself, and tries again on Retry', async () => {
  const user = userEvent.setup();
  answer = tooMany;
  await renderPage();

  const alert = await screen.findByRole('alert');
  expect(alert).toHaveTextContent(limitText);
  expect(verifications()).toEqual([verifyPath()]);

  answer = () => Response.json(valid);
  await user.click(screen.getByRole('button', { name: 'Erneut versuchen' }));

  expect(await screen.findByRole('heading', { level: 2, name: 'Gültig' })).toBeInTheDocument();
  expect(verifications()).toEqual([verifyPath(), verifyPath()]);
  expect(screen.queryByRole('alert')).not.toBeInTheDocument();
  await waitFor(() => expect(title()).toHaveFocus());
});

test('each press of Retry sends one request', async () => {
  const user = userEvent.setup();
  answer = tooMany;
  await renderPage();
  await screen.findByRole('alert');

  await user.click(screen.getByRole('button', { name: 'Erneut versuchen' }));
  await waitFor(() => expect(verifications()).toHaveLength(2));
  expect(await screen.findByRole('alert')).toHaveTextContent(limitText);
  await user.click(screen.getByRole('button', { name: 'Erneut versuchen' }));
  await waitFor(() => expect(verifications()).toHaveLength(3));

  expect(await screen.findByRole('alert')).toHaveTextContent(limitText);
  expect(verifications()).toEqual([verifyPath(), verifyPath(), verifyPath()]);
});

test('a failure of the host says the certificate could not be verified, with Retry', async () => {
  answer = () => new Response('', { status: 500 });
  await renderPage();

  expect(await screen.findByRole('alert')).toHaveTextContent(loadFailedText);
  expect(screen.getByRole('button', { name: 'Erneut versuchen' })).toBeInTheDocument();
  expect(screen.queryByRole('heading', { level: 2, name: 'Nicht prüfbar' })).not.toBeInTheDocument();
  expect(verifications()).toEqual([verifyPath()]);
});

test('another of the module’s own refusals is shown in the host’s words', async () => {
  answer = () => problem(409, 'certification.something-new', 'Etwas Neues.');
  await renderPage();

  expect(await screen.findByRole('alert')).toHaveTextContent('Etwas Neues.');
});

// TanStack refetches a stale query when the window regains focus; the query cache's onFocus is what that focus runs.
test('the page does not ask again when the window regains focus', async () => {
  const { queryClient } = await renderPage();
  await screen.findByRole('heading', { level: 2, name: 'Gültig' });

  queryClient.getQueryCache().onFocus();

  expect(verifications()).toEqual([verifyPath()]);
  expect(queryClient.isFetching()).toBe(0);
});

test('in English the page reads Verify a certificate, Valid, and British dates', async () => {
  setLanguage('en-GB', { remember: false });
  await renderPage();

  const result = await screen.findByRole('region', { name: 'Valid' });
  expect(screen.getByRole('heading', { level: 1, name: 'Verify a certificate' })).toBeInTheDocument();
  expect(within(result).getByText('This certificate is genuine and valid until 24/11/2027.')).toBeInTheDocument();
  expect(listed()).toEqual([
    ['Name', 'Anna Schmidt'],
    ['Course', 'Hygiene in der Apotheke'],
    ['Issued on', '24/11/2026'],
    ['Valid until', '24/11/2027'],
  ]);
});
