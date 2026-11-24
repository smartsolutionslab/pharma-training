import { onlineManager, QueryClient, VueQueryPlugin } from '@tanstack/vue-query';
import { cleanup, render, screen, waitFor, within } from '@testing-library/vue';
import userEvent from '@testing-library/user-event';
import { afterEach, beforeEach, describe, expect, test, vi } from 'vitest';
import { axe } from 'vitest-axe';
import { createMemoryHistory } from 'vue-router';
import type { CertificateVerificationDto } from '@/api/generated';
import { languages, setLanguage } from '@/i18n';
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
let answer: () => Response | Promise<Response>;

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
  onlineManager.setOnline(true);
});

/** An answer the host holds until the test releases it, as a request still on its way. */
function held(response: () => Response) {
  let release = () => {};
  const answered = new Promise<Response>((resolve) => {
    release = () => resolve(response());
  });
  return { answered, release: () => release() };
}

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
  const { container } = render(VerifyCertificatePage, {
    global: { plugins: [router, [VueQueryPlugin, { queryClient }]] },
  });
  return { router, queryClient, container };
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

// Review of #54. A refusal leaves the query without data, and TanStack counts a query without data as stale whatever
// its staleTime: when the network comes back it would ask again by itself, spending the limit every verifier shares.
describe.each([
  { refusal: 'the limit', refused: tooMany },
  { refusal: 'a failure of the host', refused: () => new Response('', { status: 500 }) },
])('after $refusal', ({ refused }) => {
  test('the page does not ask again when the network comes back', async () => {
    answer = refused;
    const { queryClient } = await renderPage();
    await screen.findByRole('alert');

    onlineManager.setOnline(false);
    onlineManager.setOnline(true);

    await waitFor(() => expect(queryClient.isFetching()).toBe(0));
    expect(screen.getByRole('alert')).toBeInTheDocument();
    expect(verifications()).toEqual([verifyPath()]);
  });
});

// The alert and its Retry go whatever the answer: the focus goes to the h1, not lost on the body.
test('Retry over the limit answered by a code the host does not know moves the focus to the h1', async () => {
  const user = userEvent.setup();
  answer = tooMany;
  await renderPage();
  await screen.findByRole('alert');

  answer = () => problem(404, 'certification.certificate-not-verifiable', 'Not verifiable.');
  await user.click(screen.getByRole('button', { name: 'Erneut versuchen' }));

  expect(await screen.findByRole('heading', { level: 2, name: 'Nicht prüfbar' })).toBeInTheDocument();
  expect(screen.queryByRole('alert')).not.toBeInTheDocument();
  await waitFor(() => expect(title()).toHaveFocus());
  expect(document.body).not.toHaveFocus();
});

// One request per press, and none while one is on its way: a second press during the first sends nothing.
test('while a Retry is on its way, Retry is busy and a second press sends nothing', async () => {
  const user = userEvent.setup();
  answer = tooMany;
  await renderPage();
  await screen.findByRole('alert');

  const second = held(() => Response.json(valid));
  answer = () => second.answered;
  await user.click(screen.getByRole('button', { name: 'Erneut versuchen' }));
  await waitFor(() => expect(verifications()).toHaveLength(2));

  const retry = screen.getByRole('button', { name: 'Erneut versuchen' });
  expect(retry.hasAttribute('disabled') || retry.getAttribute('aria-busy') === 'true').toBe(true);
  await user.click(retry);
  second.release();

  expect(await screen.findByRole('heading', { level: 2, name: 'Gültig' })).toBeInTheDocument();
  expect(verifications()).toEqual([verifyPath(), verifyPath()]);
});

// A screen reader announces an alert as it is inserted: the same limit again is a new alert, not the old one kept.
test('Retry over the limit answered by the limit again announces a new alert', async () => {
  const user = userEvent.setup();
  answer = tooMany;
  await renderPage();
  const first = await screen.findByRole('alert');

  await user.click(screen.getByRole('button', { name: 'Erneut versuchen' }));
  await waitFor(() => expect(verifications()).toHaveLength(2));

  await waitFor(() => expect(screen.getByRole('alert')).not.toBe(first));
  expect(screen.getByRole('alert')).toHaveTextContent(limitText);
  expect(first.isConnected).toBe(false);
});

// The answer is untrusted input (#51's contract can grow, a proxy can mangle it): an answer the page cannot read is
// shown as a failure, with Retry, never as a raw message key or a blank page.
test('an answer with a validity the page does not know reads as a failure, with Retry', async () => {
  answer = () => Response.json({ ...valid, validity: 'Revoked' });
  await renderPage();

  expect(await screen.findByRole('alert')).toHaveTextContent(loadFailedText);
  expect(screen.getByRole('button', { name: 'Erneut versuchen' })).toBeInTheDocument();
  expect(document.body.textContent).not.toContain('certification.validity');
  expect(screen.queryByRole('heading', { level: 2, name: 'Gültig' })).not.toBeInTheDocument();
});

test('an answer with a malformed day reads as a failure, with Retry, and the page stays', async () => {
  answer = () => Response.json({ ...valid, issuedOn: '24.11.2026' });
  await renderPage();

  expect(await screen.findByRole('alert')).toHaveTextContent(loadFailedText);
  expect(screen.getByRole('button', { name: 'Erneut versuchen' })).toBeInTheDocument();
  expect(title()).toBeInTheDocument();
});

// A live region announces a change of its text, not its own insertion: the status is there from the first render and
// stays, and only its text changes.
test('the status region is there from the first render and says it is loading while the answer is on its way', async () => {
  const answered = held(() => Response.json(valid));
  answer = () => answered.answered;
  await renderPage();

  const status = screen.getByRole('status');
  await waitFor(() => expect(status).toHaveTextContent('Wird geladen…'));
  answered.release();

  expect(await screen.findByRole('heading', { level: 2, name: 'Gültig' })).toBeInTheDocument();
  expect(status).toBeInTheDocument();
  expect(screen.getByRole('status')).toBe(status);
  expect(status).not.toHaveTextContent('Wird geladen…');
});

// The axe check beside the page (VerifyCertificatePage.axe.test.ts) stands in for the client and cannot answer with a
// refusal; here the host refuses, as it does.
describe.each(languages)('in %s', (language) => {
  beforeEach(() => {
    setLanguage(language, { remember: false });
  });

  test.each([
    { shown: 'a code the host does not know', refused: () => problem(404, 'certification.certificate-not-verifiable') },
    { shown: 'the limit', refused: tooMany },
    { shown: 'a failure of the host', refused: () => new Response('', { status: 500 }) },
  ])('the page passes axe showing $shown', async ({ refused }) => {
    answer = refused;
    const { container } = await renderPage();
    await screen.findByRole('heading', { level: 1 });
    await waitFor(() => expect(verifications()).toHaveLength(1));
    await waitFor(() =>
      expect(screen.queryByRole('alert') ?? screen.queryByRole('heading', { level: 2 })).toBeInTheDocument(),
    );

    expect(await axe(container)).toHaveNoViolations();
  });
});

// Review of #317. The page itself asks again only on Retry: going back to it after a refusal, while the query is still
// cached, must not spend the limit again (TanStack retries a failed query when a component mounts it).
test('after the limit, opening the page again sends nothing by itself', async () => {
  answer = tooMany;
  const { router, queryClient } = await renderPage();
  await screen.findByRole('alert');
  cleanup();

  render(VerifyCertificatePage, { global: { plugins: [router, [VueQueryPlugin, { queryClient }]] } });

  expect(await screen.findByRole('alert')).toHaveTextContent(limitText);
  expect(queryClient.isFetching()).toBe(0);
  expect(verifications()).toEqual([verifyPath()]);
});

// Review of #317. Whether a certificate is valid changes with the German day: a tab left open past midnight asks again
// once when it regains focus, and only then; a refusal is never asked again by itself.
describe('when the window regains focus', () => {
  beforeEach(() => {
    vi.useFakeTimers({ toFake: ['Date'] });
    vi.setSystemTime(new Date('2026-11-24T22:30:00Z')); // 23:30 in Germany
  });
  afterEach(() => {
    vi.useRealTimers();
  });

  test('on a later German day, the page asks the host again, once', async () => {
    const { queryClient } = await renderPage();
    await screen.findByRole('heading', { level: 2, name: 'Gültig' });

    vi.setSystemTime(new Date('2026-11-24T23:30:00Z')); // 00:30 on the 25th in Germany
    queryClient.getQueryCache().onFocus();
    await waitFor(() => expect(verifications()).toEqual([verifyPath(), verifyPath()]));
    await waitFor(() => expect(queryClient.isFetching()).toBe(0));

    queryClient.getQueryCache().onFocus();
    expect(queryClient.isFetching()).toBe(0);
    expect(verifications()).toEqual([verifyPath(), verifyPath()]);
  });

  test('on the same German day, it does not', async () => {
    const { queryClient } = await renderPage();
    await screen.findByRole('heading', { level: 2, name: 'Gültig' });

    vi.setSystemTime(new Date('2026-11-24T22:59:00Z')); // 23:59 in Germany
    queryClient.getQueryCache().onFocus();

    expect(queryClient.isFetching()).toBe(0);
    expect(verifications()).toEqual([verifyPath()]);
  });

  test('after the limit, a later German day does not ask again by itself either', async () => {
    answer = tooMany;
    const { queryClient } = await renderPage();
    await screen.findByRole('alert');

    vi.setSystemTime(new Date('2026-11-24T23:30:00Z'));
    queryClient.getQueryCache().onFocus();

    expect(queryClient.isFetching()).toBe(0);
    expect(verifications()).toEqual([verifyPath()]);
  });
});
