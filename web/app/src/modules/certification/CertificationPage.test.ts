import { QueryClient, VueQueryPlugin } from '@tanstack/vue-query';
import { cleanup, render, screen, waitFor, within } from '@testing-library/vue';
import userEvent from '@testing-library/user-event';
import { afterEach, beforeEach, expect, test, vi } from 'vitest';
import { createMemoryHistory } from 'vue-router';
import type { CertificationCertificateDto } from '@/api/generated';
import { setLanguage, t } from '@/i18n';
import { createAppRouter } from '@/router';
import CertificationPage from './CertificationPage.vue';

const notify = vi.hoisted(() => ({ success: vi.fn(), failure: vi.fn() }));
vi.mock('@/lib/notify', () => ({ notify }));

// The host, as GET /certification/me/certificates and GET /certification/certificates/{id}/pdf reach it through the
// generated client, and storage, which serves the URL the host signed. Each answers as the test says.
const host = vi.fn<(input: RequestInfo | URL, init?: RequestInit) => Promise<Response>>();
vi.stubGlobal('fetch', host);

const hygiene: CertificationCertificateDto = {
  certificateId: '0198c1a0-0000-7000-8000-0000000000e1',
  courseTitle: 'Hygiene in der Apotheke',
  issuedOn: '2026-01-15',
  validUntil: '2027-01-15',
  validity: 'Valid',
  documentReady: true,
};
const privacy: CertificationCertificateDto = {
  certificateId: '0198c1a0-0000-7000-8000-0000000000e2',
  courseTitle: 'Datenschutz',
  issuedOn: '2024-03-02',
  validUntil: '2025-03-02',
  validity: 'Expired',
  documentReady: false,
};

const signedUrl = 'https://storage.example/certification/certificates/0198c1a0-0000-7000-8000-0000000000e1.pdf?sig=x';
const pdfBytes = '%PDF-1.7 fake';

let listing: (page: number) => Promise<Response>;
let documentUrl: () => Promise<Response>;

const problem = (status: number, code: string, detail: string) =>
  Response.json({ status, code, title: 'Refused', detail }, { status });

beforeEach(() => {
  listing = async (page) => Response.json({ items: [hygiene, privacy], totalCount: 2, page, pageSize: 20 });
  documentUrl = async () => Response.json({ url: signedUrl, expiresAt: '2026-11-24T12:05:00Z' });
  host.mockImplementation(async (input) => {
    const url = new URL(String(input), 'http://localhost');
    if (url.href === signedUrl) {
      return new Response(pdfBytes, { status: 200, headers: { 'Content-Type': 'application/pdf' } });
    }
    if (url.pathname === '/api/certification/me/certificates') {
      return listing(Number(url.searchParams.get('page') ?? '1'));
    }
    if (url.pathname === `/api/certification/certificates/${hygiene.certificateId}/pdf`) {
      return documentUrl();
    }
    return new Response('', { status: 404 });
  });
  notify.success.mockReset();
  notify.failure.mockReset();
});
afterEach(() => host.mockReset());
afterEach(() => vi.restoreAllMocks());
// Without Vitest's globals, Testing Library does not unmount between tests by itself.
afterEach(cleanup);

/** The lists the host was asked for, by their query. */
const listsAsked = () =>
  host.mock.calls
    .map(([input]) => new URL(String(input), 'http://localhost'))
    .filter((url) => url.pathname === '/api/certification/me/certificates')
    .map((url) => Object.fromEntries(url.searchParams));

/** The page at its address, with a query client of its own that does not retry, so a failure shows at once. */
async function renderAt(path = '/certification') {
  const router = createAppRouter(createMemoryHistory());
  await router.push(path);
  const queryClient = new QueryClient({ defaultOptions: { queries: { retry: false }, mutations: { retry: false } } });
  render(CertificationPage, { global: { plugins: [router, [VueQueryPlugin, { queryClient }]] } });
  return { router };
}

/** The row of the course in the table. */
const rowOf = async (course: string) => (await screen.findByRole('cell', { name: course })).closest('tr');

/**
 * Catches what the page saves: the browser saves a blob through a link with a download name, which jsdom cannot; the
 * link's name and the blob's bytes are kept instead.
 */
function catchSaves() {
  const saved: { name: string; href: string }[] = [];
  const blobs = new Map<string, Blob>();
  vi.spyOn(URL, 'createObjectURL').mockImplementation((blob) => {
    const href = `blob:certificate-${blobs.size + 1}`;
    blobs.set(href, blob as Blob);
    return href;
  });
  vi.spyOn(URL, 'revokeObjectURL').mockImplementation(() => undefined);
  vi.spyOn(HTMLAnchorElement.prototype, 'click').mockImplementation(function (this: HTMLAnchorElement) {
    saved.push({ name: this.download, href: this.getAttribute('href') ?? '' });
  });
  return { saved, blobs };
}

test('the page is headed Meine Teilnahmebestätigungen and shows no samples and no Manage section', async () => {
  await renderAt();

  expect(screen.getByRole('heading', { level: 1, name: 'Meine Teilnahmebestätigungen' })).toBeInTheDocument();
  await rowOf(hygiene.courseTitle);
  expect(screen.queryByRole('heading', { name: t('samples.title') })).not.toBeInTheDocument();
  expect(screen.queryByRole('heading', { name: t('samples.manage') })).not.toBeInTheDocument();
  expect(screen.queryByRole('form', { name: t('samples.form.label') })).not.toBeInTheDocument();
  expect(host.mock.calls.map(([input]) => String(input))).not.toContainEqual(
    expect.stringContaining('/certification/samples'),
  );
});

test("it asks the host for the signed-in user's certificates, the first page at the default size", async () => {
  await renderAt();

  await rowOf(hygiene.courseTitle);
  expect(listsAsked()).toEqual([{ page: '1', pageSize: '20' }]);
});

test('each certificate shows its course, the days it was issued and is valid until, and whether it is valid', async () => {
  await renderAt();

  const ready = await rowOf(hygiene.courseTitle);
  const pending = await rowOf(privacy.courseTitle);
  if (ready === null || pending === null) {
    throw new Error('Each certificate has a row.');
  }
  expect(screen.getByRole('columnheader', { name: 'Schulung' })).toBeInTheDocument();
  expect(screen.getByRole('columnheader', { name: 'Ausgestellt am' })).toBeInTheDocument();
  expect(screen.getByRole('columnheader', { name: 'Gültig bis' })).toBeInTheDocument();
  expect(within(ready).getByText('15.01.2026')).toBeInTheDocument();
  expect(within(ready).getByText('15.01.2027')).toBeInTheDocument();
  expect(within(ready).getByText('Gültig')).toBeInTheDocument();
  expect(within(pending).getByText('02.03.2024')).toBeInTheDocument();
  expect(within(pending).getByText('02.03.2025')).toBeInTheDocument();
  expect(within(pending).getByText('Abgelaufen')).toBeInTheDocument();
});

test('a certificate whose document is ready offers its PDF; one that is not says it is being prepared', async () => {
  await renderAt();

  const ready = await rowOf(hygiene.courseTitle);
  const pending = await rowOf(privacy.courseTitle);
  if (ready === null || pending === null) {
    throw new Error('Each certificate has a row.');
  }
  expect(
    within(ready).getByRole('button', { name: 'PDF herunterladen für Hygiene in der Apotheke' }),
  ).toBeInTheDocument();
  expect(within(ready).queryByText('Wird erstellt')).not.toBeInTheDocument();
  expect(within(pending).queryByRole('button', { name: /PDF herunterladen/ })).not.toBeInTheDocument();
  expect(within(pending).getByText('Wird erstellt')).toBeInTheDocument();
});

test('a certificate whose document is ready downloads its PDF, named after the course, from the URL the host signed', async () => {
  const { saved, blobs } = catchSaves();
  await renderAt();

  await userEvent.click(await screen.findByRole('button', { name: 'PDF herunterladen für Hygiene in der Apotheke' }));

  await waitFor(() => expect(saved).toHaveLength(1));
  expect(saved[0]?.name).toBe('Hygiene in der Apotheke.pdf');
  const blob = blobs.get(saved[0]?.href ?? '');
  expect(await blob?.text()).toBe(pdfBytes);
  // The URL is the host's to sign: asked with the user's token through the generated client, then read from storage.
  const asked = host.mock.calls.map(([input]) => new URL(String(input), 'http://localhost').href);
  expect(asked).toContain(`http://localhost/api/certification/certificates/${hygiene.certificateId}/pdf`);
  expect(asked).toContain(signedUrl);
  expect(notify.failure).not.toHaveBeenCalled();
});

test('a download the host refuses because the document is not ready yet says so, and saves nothing', async () => {
  const { saved } = catchSaves();
  documentUrl = async () => problem(409, 'certification.document-not-ready', 'The document is not stored yet.');
  await renderAt();

  await userEvent.click(await screen.findByRole('button', { name: 'PDF herunterladen für Hygiene in der Apotheke' }));

  await waitFor(() =>
    expect(notify.failure).toHaveBeenCalledWith(
      'Die Teilnahmebestätigung für Hygiene in der Apotheke wird noch erstellt. Versuchen Sie es gleich noch einmal.',
    ),
  );
  expect(saved).toEqual([]);
});

test('a download that fails otherwise says the PDF could not be downloaded', async () => {
  const { saved } = catchSaves();
  documentUrl = async () => new Response('', { status: 502 });
  await renderAt();

  await userEvent.click(await screen.findByRole('button', { name: 'PDF herunterladen für Hygiene in der Apotheke' }));

  await waitFor(() =>
    expect(notify.failure).toHaveBeenCalledWith(
      'Das PDF für Hygiene in der Apotheke konnte nicht heruntergeladen werden.',
    ),
  );
  expect(saved).toEqual([]);
});

test('without certificates the page says so and leads to the courses', async () => {
  listing = async (page) => Response.json({ items: [], totalCount: 0, page, pageSize: 20 });
  await renderAt();

  expect(await screen.findByText('Sie haben noch keine Teilnahmebestätigung.')).toBeInTheDocument();
  expect(screen.getByRole('link', { name: 'Zu meinen Schulungen' })).toHaveAttribute('href', '/learning');
});

test('a token without a subject reads that the account is incomplete', async () => {
  listing = async () => problem(403, 'certification.identity-incomplete', 'The access token names no person.');
  await renderAt();

  expect(await screen.findByRole('alert')).toHaveTextContent(
    'Ihre Anmeldung nennt keine Person. Melden Sie sich erneut an.',
  );
});

test('another failure reads that the certificates could not be loaded', async () => {
  listing = async () => new Response('', { status: 500 });
  await renderAt();

  expect(await screen.findByRole('alert')).toHaveTextContent(
    'Ihre Teilnahmebestätigungen konnten nicht geladen werden.',
  );
});

test('a list longer than a page pages through it, and the page is in the address', async () => {
  listing = async (page) => Response.json({ items: [hygiene], totalCount: 45, page, pageSize: 20 });
  const { router } = await renderAt();
  await rowOf(hygiene.courseTitle);

  await userEvent.click(screen.getByRole('button', { name: t('dataTable.next') }));

  await waitFor(() => expect(router.currentRoute.value.query).toEqual({ page: '2' }));
  await waitFor(() => expect(listsAsked()).toContainEqual({ page: '2', pageSize: '20' }));
});

test('a page in the address is asked for, and an invalid one falls back to the first', async () => {
  await renderAt('/certification?page=3');
  await rowOf(hygiene.courseTitle);
  expect(listsAsked()).toEqual([{ page: '3', pageSize: '20' }]);

  cleanup();
  host.mockClear();
  await renderAt('/certification?page=-1');
  await rowOf(hygiene.courseTitle);
  expect(listsAsked()).toEqual([{ page: '1', pageSize: '20' }]);
});

test('the page speaks English once switched', async () => {
  setLanguage('en-GB', { remember: false });
  await renderAt();

  expect(screen.getByRole('heading', { level: 1, name: 'My certificates' })).toBeInTheDocument();
  const ready = await rowOf(hygiene.courseTitle);
  const pending = await rowOf(privacy.courseTitle);
  if (ready === null || pending === null) {
    throw new Error('Each certificate has a row.');
  }
  expect(within(ready).getByText('15/01/2026')).toBeInTheDocument();
  expect(within(ready).getByRole('button', { name: 'Download PDF for Hygiene in der Apotheke' })).toBeInTheDocument();
  expect(within(pending).getByText('Being prepared')).toBeInTheDocument();
});
