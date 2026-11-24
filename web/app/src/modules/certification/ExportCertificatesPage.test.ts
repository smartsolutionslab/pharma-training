import { QueryClient, VueQueryPlugin } from '@tanstack/vue-query';
import { cleanup, fireEvent, render, screen, waitFor } from '@testing-library/vue';
import userEvent from '@testing-library/user-event';
import { createPinia, type Pinia } from 'pinia';
import { afterEach, beforeEach, describe, expect, test, vi } from 'vitest';
import { createMemoryHistory } from 'vue-router';
import type { MyPharmacyDto } from '@/api/generated';
import { t } from '@/i18n';
import { createAppRouter } from '@/router';
import ExportCertificatesPage from './ExportCertificatesPage.vue';

// The managers' certificate export (#297, T3): a manager of the current pharmacy picks the issue days from and to,
// the last twelve months in the German day to begin with, and downloads the ZIP the host answers (#52, ADR 0032). The
// file takes the host's name, else the host's own pattern; the request goes once, however often the button is pressed;
// a range the host would refuse shows the field's error and sends nothing.

type Fetch = (input: RequestInfo | URL, init?: RequestInit) => Promise<Response>;

const notify = vi.hoisted(() => ({ success: vi.fn(), failure: vi.fn() }));
vi.mock('@/lib/notify', () => ({ notify }));

/** 24 November 2026, 11:00 in Germany: the default range is 2025-11-25 to 2026-11-24. */
const now = new Date('2026-11-24T10:00:00Z');

const markt: MyPharmacyDto = {
  id: '0198c1a0-0000-7000-8000-0000000000a1',
  name: 'Apotheke am Markt',
  membershipId: '0198c1a0-0000-7000-8000-0000000000b1',
  role: 'Manager',
  jobFunction: 'Pharmacist',
  canManageMembers: true,
  canAssignCourses: true,
};

const exportPath = (pharmacy: MyPharmacyDto = markt) =>
  `/api/certification/pharmacies/${pharmacy.id}/certificates/export`;

/** The bytes a ZIP starts with. */
const zipBytes = new Uint8Array([0x50, 0x4b, 0x03, 0x04]);

/** The ZIP as the host streams it, with the file name it gives, or without a Content-Disposition. */
function archive(fileName?: string): Response {
  const headers: Record<string, string> = { 'Content-Type': 'application/zip' };
  if (fileName !== undefined) {
    headers['Content-Disposition'] = `attachment; filename=${fileName}; filename*=UTF-8''${fileName}`;
  }
  return new Response(zipBytes, { status: 200, headers });
}

/** What the fake host answers; a test changes it before the page asks. */
interface HostState {
  memberships: MyPharmacyDto[];
  /** The answer to GET /certification/pharmacies/{id}/certificates/export. */
  export: (url: URL) => Promise<Response>;
}

function hostFrom(state: HostState): Fetch {
  return async (input, init) => {
    const url = new URL(String(input), 'http://localhost');
    const method = init?.method ?? 'GET';
    if (method === 'GET' && url.pathname === '/api/organisation/pharmacies/mine') {
      return Response.json({ items: state.memberships, totalCount: state.memberships.length, page: 1, pageSize: 100 });
    }
    if (method === 'GET' && /^\/api\/certification\/pharmacies\/[0-9a-f-]+\/certificates\/export$/.test(url.pathname)) {
      return state.export(url);
    }
    return new Response('', { status: 404 });
  };
}

/** The export requests the host received, as the client sent them. */
function exportsOf(host: { mock: { calls: Parameters<Fetch>[] } }): string[] {
  return host.mock.calls.map(([input]) => String(input)).filter((url) => url.includes('/certificates/export'));
}

const host = vi.fn<Fetch>();
vi.stubGlobal('fetch', host);

let state: HostState;

// jsdom has no object URLs and would navigate on an anchor's click: both are spied on, and each saved file is recorded.
const createObjectURL = vi.fn<(blob: Blob) => string>();
const revokeObjectURL = vi.fn<(url: string) => void>();
let saved: { href: string | null; download: string; connected: boolean }[];

beforeEach(() => {
  vi.useFakeTimers({ toFake: ['Date'] });
  vi.setSystemTime(now);
  localStorage.clear();
  state = { memberships: [markt], export: async () => archive('teilnahmebestaetigungen_vom_host.zip') };
  host.mockImplementation(hostFrom(state));
  notify.success.mockReset();
  notify.failure.mockReset();
  let objectUrls = 0;
  createObjectURL.mockReset().mockImplementation(() => `blob:export-${++objectUrls}`);
  revokeObjectURL.mockReset();
  URL.createObjectURL = createObjectURL;
  URL.revokeObjectURL = revokeObjectURL;
  saved = [];
  vi.spyOn(HTMLAnchorElement.prototype, 'click').mockImplementation(function (this: HTMLAnchorElement) {
    saved.push({ href: this.getAttribute('href'), download: this.download, connected: this.isConnected });
  });
});
afterEach(() => {
  vi.useRealTimers();
  vi.restoreAllMocks();
  host.mockReset();
  localStorage.clear();
});
// Without Vitest's globals, Testing Library does not unmount between tests by itself.
afterEach(cleanup);

/** The page, with a Pinia and a query client of its own that does not retry; it reads the current pharmacy as the shell does. */
async function renderPage(options: { pinia?: Pinia } = {}) {
  const router = createAppRouter(createMemoryHistory());
  await router.push('/certification/export');
  const queryClient = new QueryClient({ defaultOptions: { queries: { retry: false }, mutations: { retry: false } } });
  const pinia = options.pinia ?? createPinia();
  render(ExportCertificatesPage, { global: { plugins: [router, pinia, [VueQueryPlugin, { queryClient }]] } });
  await screen.findByRole('heading', { level: 1, name: 'Nachweise exportieren' });
  return { router, queryClient, pinia };
}

const fromField = () => screen.getByLabelText(t('certification.export.from'));
const toField = () => screen.getByLabelText(t('certification.export.to'));
const submitButton = () => screen.getByRole('button', { name: t('certification.export.submit') });

describe('the page', () => {
  test('is headed "Nachweise exportieren"', async () => {
    await renderPage();

    expect(screen.getByRole('heading', { level: 1 })).toHaveTextContent('Nachweise exportieren');
  });

  test('names the pharmacy in its intro', async () => {
    await renderPage();

    expect(
      await screen.findByText(t('certification.export.intro', { pharmacy: 'Apotheke am Markt' })),
    ).toBeInTheDocument();
  });

  test('holds the last twelve months in the German day to begin with', async () => {
    await renderPage();

    await waitFor(() => expect(fromField()).toHaveValue('2025-11-25'));
    expect(toField()).toHaveValue('2026-11-24');
  });
});

describe('exporting', () => {
  test('asks the host once for the range in the current pharmacy', async () => {
    const user = userEvent.setup();
    await renderPage();

    await user.click(submitButton());

    await waitFor(() => expect(notify.success).toHaveBeenCalled());
    expect(exportsOf(host)).toEqual([`${exportPath()}?from=2025-11-25&to=2026-11-24`]);
  });

  test("saves the ZIP under the host's file name through a temporary link, then lets the object URL go", async () => {
    const user = userEvent.setup();
    await renderPage();

    await user.click(submitButton());

    await waitFor(() => expect(revokeObjectURL).toHaveBeenCalled());
    expect(createObjectURL).toHaveBeenCalledTimes(1);
    expect(createObjectURL.mock.calls[0]?.[0]).toBeInstanceOf(Blob);
    expect(saved).toEqual([
      { href: 'blob:export-1', download: 'teilnahmebestaetigungen_vom_host.zip', connected: true },
    ]);
    expect(revokeObjectURL).toHaveBeenCalledWith('blob:export-1');
    expect(document.querySelector('a[download]')).toBeNull();
  });

  test('says "Der Export wurde heruntergeladen."', async () => {
    const user = userEvent.setup();
    await renderPage();

    await user.click(submitButton());

    await waitFor(() => expect(notify.success).toHaveBeenCalledWith('Der Export wurde heruntergeladen.'));
    expect(notify.failure).not.toHaveBeenCalled();
  });

  test("saves an answer without a disposition under the host's own pattern for the range", async () => {
    const user = userEvent.setup();
    state.export = async () => archive();
    await renderPage();

    await user.click(submitButton());

    await waitFor(() => expect(saved).toHaveLength(1));
    expect(saved[0]?.download).toBe('teilnahmebestaetigungen_2025-11-25_2026-11-24.zip');
  });

  test('sends once on a double click, the button disabled and reading "Wird erstellt…" while it is sent', async () => {
    const user = userEvent.setup();
    let answer: (response: Response) => void = () => undefined;
    state.export = () => new Promise<Response>((resolve) => (answer = resolve));
    await renderPage();

    await user.dblClick(submitButton());

    const preparing = await screen.findByRole('button', { name: 'Wird erstellt…' });
    expect(preparing).toBeDisabled();
    expect(exportsOf(host)).toHaveLength(1);
    answer(archive('teilnahmebestaetigungen_vom_host.zip'));
    await waitFor(() => expect(notify.success).toHaveBeenCalled());
    expect(exportsOf(host)).toHaveLength(1);
    expect(submitButton()).toBeEnabled();
  });
});

describe('a range the host would refuse', () => {
  test('with to before from shows "Das Ende darf nicht vor dem Beginn liegen." at to and sends nothing', async () => {
    const user = userEvent.setup();
    await renderPage();
    await waitFor(() => expect(fromField()).toHaveValue('2025-11-25'));

    await fireEvent.update(toField(), '2025-11-24');
    await user.click(submitButton());

    expect(await screen.findByRole('alert')).toHaveTextContent('Das Ende darf nicht vor dem Beginn liegen.');
    expect(toField()).toHaveAttribute('aria-invalid', 'true');
    expect(toField()).toHaveAccessibleDescription('Das Ende darf nicht vor dem Beginn liegen.');
    expect(fromField()).not.toHaveAttribute('aria-invalid', 'true');
    expect(exportsOf(host)).toEqual([]);
    expect(saved).toEqual([]);
  });

  test('of six years shows "Ein Export umfasst höchstens fünf Jahre." at to and sends nothing', async () => {
    const user = userEvent.setup();
    await renderPage();
    await waitFor(() => expect(fromField()).toHaveValue('2025-11-25'));

    await fireEvent.update(fromField(), '2020-11-24');
    await user.click(submitButton());

    expect(await screen.findByRole('alert')).toHaveTextContent('Ein Export umfasst höchstens fünf Jahre.');
    expect(toField()).toHaveAttribute('aria-invalid', 'true');
    expect(toField()).toHaveAccessibleDescription('Ein Export umfasst höchstens fünf Jahre.');
    expect(exportsOf(host)).toEqual([]);
    expect(saved).toEqual([]);
  });
});
