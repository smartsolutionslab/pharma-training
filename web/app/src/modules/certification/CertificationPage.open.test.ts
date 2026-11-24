import { QueryClient, VueQueryPlugin } from '@tanstack/vue-query';
import { cleanup, render, screen, waitFor } from '@testing-library/vue';
import userEvent from '@testing-library/user-event';
import { createPinia } from 'pinia';
import { afterEach, beforeEach, expect, test, vi } from 'vitest';
import { createMemoryHistory } from 'vue-router';
import type { HeldCertificateDto } from '@/api/generated';
import { signedInAsDeveloper } from '@/auth/signedInAsDeveloper';
import { setLanguage } from '@/i18n';
import { createAppRouter } from '@/router';
import CertificationPage from './CertificationPage.vue';
import { documentNavigation } from './openCertificate';

const notify = vi.hoisted(() => ({ success: vi.fn(), failure: vi.fn() }));
vi.mock('@/lib/notify', () => ({ notify }));

// Opening a certificate's PDF (#296, Z1): the host signs a short-lived URL on the stored document, and the tab goes
// there, where the browser shows the PDF inline, as #54 decided. The host answers through the generated client; the
// tab's navigation is the page's seam, documentNavigation, which the test watches instead of leaving jsdom's page.
const host = vi.fn<(input: RequestInfo | URL, init?: RequestInit) => Promise<Response>>();
vi.stubGlobal('fetch', host);

const hygiene: HeldCertificateDto = {
  certificateId: '0198c1a0-0000-7000-8000-0000000000e1',
  courseTitle: 'Hygiene in der Apotheke',
  issuedOn: '2026-01-15',
  validUntil: '2027-01-15',
  validity: 'Valid',
  documentReady: true,
};

const signedUrl = 'https://storage.example/certification/certificates/0198c1a0-0000-7000-8000-0000000000e1.pdf?sig=x';

let documentUrl: () => Promise<Response>;

const problem = (status: number, code: string, detail: string) =>
  Response.json({ status, code, title: 'Refused', detail }, { status });

beforeEach(() => {
  documentUrl = async () => Response.json({ url: signedUrl, expiresAt: '2026-11-24T12:05:00Z' });
  host.mockImplementation(async (input) => {
    const url = new URL(String(input), 'http://localhost');
    if (url.pathname === '/api/certification/me/certificates') {
      return Response.json({ items: [hygiene], totalCount: 1, page: 1, pageSize: 20 });
    }
    if (url.pathname === `/api/certification/certificates/${hygiene.certificateId}/pdf`) {
      return documentUrl();
    }
    return new Response('', { status: 404 });
  });
  notify.failure.mockReset();
});
afterEach(() => host.mockReset());
afterEach(() => vi.restoreAllMocks());
// Without Vitest's globals, Testing Library does not unmount between tests by itself.
afterEach(cleanup);

/** The page, with a query client of its own that does not retry, and the tab's navigation watched. */
async function renderPage() {
  const open = vi.spyOn(documentNavigation, 'open').mockImplementation(() => undefined);
  const router = createAppRouter(createMemoryHistory());
  await router.push('/certification');
  const queryClient = new QueryClient({ defaultOptions: { queries: { retry: false }, mutations: { retry: false } } });
  render(CertificationPage, {
    global: { plugins: [router, createPinia(), signedInAsDeveloper, [VueQueryPlugin, { queryClient }]] },
  });
  return { open };
}

test('a certificate whose document is ready opens its PDF in this tab, at the URL the host signed', async () => {
  const { open } = await renderPage();

  await userEvent.click(await screen.findByRole('button', { name: 'PDF öffnen für Hygiene in der Apotheke' }));

  await waitFor(() => expect(open).toHaveBeenCalledWith(signedUrl));
  expect(open).toHaveBeenCalledTimes(1);
  // The URL is asked for with the user's token, through the generated client; storage is the browser's to read.
  const asked = host.mock.calls.map(([input]) => new URL(String(input), 'http://localhost').href);
  expect(asked).toContain(`http://localhost/api/certification/certificates/${hygiene.certificateId}/pdf`);
  expect(asked).not.toContain(signedUrl);
  expect(notify.failure).not.toHaveBeenCalled();
});

test('a PDF the host refuses because the document is not ready yet says so, and the tab stays', async () => {
  documentUrl = async () => problem(409, 'certification.document-not-ready', 'The document is not stored yet.');
  const { open } = await renderPage();

  await userEvent.click(await screen.findByRole('button', { name: 'PDF öffnen für Hygiene in der Apotheke' }));

  await waitFor(() =>
    expect(notify.failure).toHaveBeenCalledWith(
      'Die Teilnahmebestätigung für Hygiene in der Apotheke wird noch erstellt. Versuchen Sie es gleich noch einmal.',
    ),
  );
  expect(open).not.toHaveBeenCalled();
});

test('a PDF that fails otherwise says it could not be opened, and the tab stays', async () => {
  documentUrl = async () => new Response('', { status: 502 });
  const { open } = await renderPage();

  await userEvent.click(await screen.findByRole('button', { name: 'PDF öffnen für Hygiene in der Apotheke' }));

  await waitFor(() =>
    expect(notify.failure).toHaveBeenCalledWith('Das PDF für Hygiene in der Apotheke konnte nicht geöffnet werden.'),
  );
  expect(open).not.toHaveBeenCalled();
});

test('in English the failure reads in English', async () => {
  setLanguage('en-GB', { remember: false });
  documentUrl = async () => new Response('', { status: 502 });
  await renderPage();

  await userEvent.click(await screen.findByRole('button', { name: 'Open PDF for Hygiene in der Apotheke' }));

  await waitFor(() =>
    expect(notify.failure).toHaveBeenCalledWith('The PDF for Hygiene in der Apotheke could not be opened.'),
  );
});

// The tab follows only a web address the host signed: anything else it answers is a failure like any other, and the
// tab never goes there.
test.each([
  ['a script', 'javascript:alert(document.cookie)'],
  ['a data URL', 'data:text/html;base64,PHNjcmlwdD5hbGVydCgxKTwvc2NyaXB0Pg=='],
  ['a file', 'file:///etc/passwd'],
  ['something that is no URL', 'not a url'],
])('a signed URL that is %s says the PDF could not be opened, and the tab stays', async (_name, url) => {
  documentUrl = async () => Response.json({ url, expiresAt: '2026-11-24T12:05:00Z' });
  const { open } = await renderPage();

  await userEvent.click(await screen.findByRole('button', { name: 'PDF öffnen für Hygiene in der Apotheke' }));

  await waitFor(() =>
    expect(notify.failure).toHaveBeenCalledWith('Das PDF für Hygiene in der Apotheke konnte nicht geöffnet werden.'),
  );
  expect(open).not.toHaveBeenCalled();
});

test('a signed URL over plain http is followed too', async () => {
  const plain = 'http://storage.example/certification/certificates/e1.pdf?sig=x';
  documentUrl = async () => Response.json({ url: plain, expiresAt: '2026-11-24T12:05:00Z' });
  const { open } = await renderPage();

  await userEvent.click(await screen.findByRole('button', { name: 'PDF öffnen für Hygiene in der Apotheke' }));

  await waitFor(() => expect(open).toHaveBeenCalledWith(plain));
  expect(notify.failure).not.toHaveBeenCalled();
});
