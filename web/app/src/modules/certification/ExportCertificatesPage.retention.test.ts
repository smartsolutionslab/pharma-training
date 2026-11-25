import { QueryClient, VueQueryPlugin } from '@tanstack/vue-query';
import { cleanup, fireEvent, render, screen, waitFor, within } from '@testing-library/vue';
import userEvent from '@testing-library/user-event';
import { createPinia, type Pinia } from 'pinia';
import { afterEach, beforeEach, describe, expect, test, vi } from 'vitest';
import { createMemoryHistory } from 'vue-router';
import {
  type CertificationRetentionPeriodDto,
  getGetCertificationPharmaciesIdRetentionPeriodQueryKey,
  getGetOrganisationPharmaciesMineQueryKey,
  type MyPharmacyDto,
} from '@/api/generated';
import { setLanguage, t } from '@/i18n';
import { createAppRouter } from '@/router';
import { useSelectedPharmacyStore } from '@/stores/selectedPharmacy';
import ExportCertificatesPage from './ExportCertificatesPage.vue';

// The pharmacy's retention period on the managers' export page (#419; ADR 0034, amended by ADR 0039). Below the export
// form, a section of its own shows how many years the current pharmacy keeps its certificates in the export, whether
// that is the default of five, that the period applies retroactively, and a field to change it. The section shows only
// once the host has answered GET /certification/pharmacies/{id}/retention-period for the current pharmacy: a refusal or
// a failure of that read hides it without a word, since the host decides who may read it (ADR 0003) and the export
// above reports a membership that is gone itself. A refusal of the read is final; it is not asked for again.

type Fetch = (input: RequestInfo | URL, init?: RequestInit) => Promise<Response>;

const notify = vi.hoisted(() => ({ success: vi.fn(), failure: vi.fn() }));
vi.mock('@/lib/notify', () => ({ notify }));

const markt: MyPharmacyDto = {
  id: '0198c1a0-0000-7000-8000-0000000000a1',
  name: 'Apotheke am Markt',
  membershipId: '0198c1a0-0000-7000-8000-0000000000b1',
  role: 'Manager',
  jobFunction: 'Pharmacist',
  canManageMembers: true,
  canAssignCourses: true,
};

/** A second pharmacy the same manager works in, which keeps its certificates for a period of its own. */
const dom: MyPharmacyDto = {
  ...markt,
  id: '0198c1a0-0000-7000-8000-0000000000a2',
  name: 'Apotheke am Dom',
  membershipId: '0198c1a0-0000-7000-8000-0000000000b2',
};

const retentionPath = /^\/api\/certification\/pharmacies\/([0-9a-f-]*)\/retention-period$/;

/** What the fake host answers; a test changes it before the page asks. */
interface HostState {
  memberships: MyPharmacyDto[];
  /** Each pharmacy's period, as GET …/retention-period answers it. */
  periods: Record<string, CertificationRetentionPeriodDto>;
  /** The answer to GET /certification/pharmacies/{id}/retention-period. */
  read: (pharmacyId: string) => Promise<Response>;
  /** The answer to PUT /certification/pharmacies/{id}/retention-period, given the request's body. */
  save: (pharmacyId: string, body: string) => Promise<Response>;
}

/** Problem details as the host sends a module's refusal: its English message as the title, and its code. */
function refusal(status: number, code: string, title: string): Response {
  return new Response(JSON.stringify({ title, status, code }), {
    status,
    headers: { 'Content-Type': 'application/problem+json' },
  });
}

function hostFrom(state: HostState): Fetch {
  return async (input, init) => {
    const url = new URL(String(input), 'http://localhost');
    const method = init?.method ?? 'GET';
    if (method === 'GET' && url.pathname === '/api/organisation/pharmacies/mine') {
      return Response.json({ items: state.memberships, totalCount: state.memberships.length, page: 1, pageSize: 100 });
    }
    const retention = retentionPath.exec(url.pathname);
    if (method === 'GET' && retention !== null) {
      return state.read(retention[1] ?? '');
    }
    if (method === 'PUT' && retention !== null) {
      return state.save(retention[1] ?? '', typeof init?.body === 'string' ? init.body : '');
    }
    return new Response('', { status: 404 });
  };
}

const host = vi.fn<Fetch>();
vi.stubGlobal('fetch', host);

let state: HostState;

/** The reads of a retention period the host received, by their path. */
const retentionReads = () =>
  host.mock.calls
    .filter(([input, init]) => (init?.method ?? 'GET') === 'GET' && String(input).includes('/retention-period'))
    .map(([input]) => String(input));

/** The periods the host was asked to store, as the client sent them. */
const saves = () =>
  host.mock.calls
    .filter(([input, init]) => init?.method === 'PUT' && String(input).includes('/retention-period'))
    .map(([input, init]) => ({ url: String(input), body: init?.body }));

/** The reads of the memberships the host received. */
const membershipReads = () =>
  host.mock.calls.filter(([input]) => String(input).startsWith('/api/organisation/pharmacies/mine')).length;

beforeEach(() => {
  localStorage.clear();
  state = {
    memberships: [markt],
    periods: {
      [markt.id]: { years: 5, isDefault: true },
      [dom.id]: { years: 12, isDefault: false },
    },
    read: async (pharmacyId) => {
      const period = state.periods[pharmacyId];
      return period === undefined
        ? refusal(
            404,
            'certification.pharmacy-not-found',
            'You are not a member of this pharmacy, or it does not exist.',
          )
        : Response.json(period);
    },
    // As the host does (ADR 0039): the period stored, no longer the default, and 204 without a body.
    save: async (pharmacyId, body) => {
      const { years } = JSON.parse(body) as { years: number };
      state.periods[pharmacyId] = { years, isDefault: false };
      return new Response(null, { status: 204 });
    },
  };
  host.mockImplementation(hostFrom(state));
  notify.success.mockReset();
  notify.failure.mockReset();
});
afterEach(() => {
  setLanguage('de-DE', { remember: false });
  vi.restoreAllMocks();
  host.mockReset();
  localStorage.clear();
});
// Without Vitest's globals, Testing Library does not unmount between tests by itself.
afterEach(cleanup);

/**
 * The page, with a Pinia and a query client of its own. Unlike ExportCertificatesPage.test.ts, the client retries a
 * failed read three times without a pause, as the app's client retries one: a read the section must not ask again (a
 * refusal) shows as asked more than once.
 */
async function renderPage(options: { pinia?: Pinia } = {}) {
  const router = createAppRouter(createMemoryHistory());
  await router.push('/certification/export');
  const queryClient = new QueryClient({
    defaultOptions: { queries: { retry: 3, retryDelay: 0 }, mutations: { retry: false } },
  });
  const pinia = options.pinia ?? createPinia();
  const { unmount } = render(ExportCertificatesPage, {
    global: { plugins: [router, pinia, [VueQueryPlugin, { queryClient }]] },
  });
  await screen.findByRole('heading', { level: 1, name: 'Nachweise exportieren' });
  return { router, queryClient, pinia, unmount };
}

/** The read of the pharmacy's period, as the query cache holds it once the page asked. */
const readOf = (queryClient: QueryClient, pharmacy: MyPharmacyDto = markt) =>
  queryClient.getQueryState(getGetCertificationPharmaciesIdRetentionPeriodQueryKey(pharmacy.id));

const note =
  'Die Frist gilt rückwirkend für alle Bestätigungen: Verlängern Sie sie, sind ältere Bestätigungen wieder im Export enthalten; verkürzen Sie sie, fehlen die älteren. Gelöscht wird nichts.';

const section = () => screen.findByRole('region', { name: 'Aufbewahrungsfrist' });
const yearsField = () => screen.getByLabelText('Aufbewahrungsfrist in Jahren');
const saveButton = () => screen.getByRole('button', { name: 'Frist speichern' });
const noSection = () => expect(screen.queryByRole('heading', { name: 'Aufbewahrungsfrist' })).toBeNull();
/** How many presses of "Frist speichern" the section has checked, and so sent or dropped. */
const pressesChecked = () => document.querySelector('[data-presses-checked]')?.getAttribute('data-presses-checked');

describe('the retention period section', () => {
  test('with the default of five years shows its heading, "Derzeit 5 Jahre (Standard)", the note and the field at 5', async () => {
    await renderPage();

    const region = await section();
    expect(within(region).getByRole('heading', { level: 2, name: 'Aufbewahrungsfrist' })).toBeInTheDocument();
    expect(region).toHaveTextContent('Derzeit 5 Jahre (Standard)');
    expect(within(region).getByText(note)).toBeInTheDocument();
    expect(yearsField()).toHaveValue(5);
  });

  test('names the pharmacy in its intro', async () => {
    await renderPage();

    const region = await section();
    expect(region).toHaveTextContent(
      'Wie viele Jahre nach ihrer Ausstellung die Teilnahmebestätigungen von Apotheke am Markt im Export enthalten sind.',
    );
  });

  test('stands below the export form', async () => {
    await renderPage();

    const region = await section();
    const exportButton = screen.getByRole('button', { name: t('certification.export.submit') });
    expect(exportButton.compareDocumentPosition(region) & Node.DOCUMENT_POSITION_FOLLOWING).toBeTruthy();
    expect(region).not.toContainElement(exportButton);
  });

  test('with a period of seven years the pharmacy chose shows "Derzeit 7 Jahre", without "Standard", and the field at 7', async () => {
    state.periods[markt.id] = { years: 7, isDefault: false };
    await renderPage();

    const region = await section();
    expect(region).toHaveTextContent('Derzeit 7 Jahre');
    expect(region).not.toHaveTextContent('Standard');
    expect(yearsField()).toHaveValue(7);
  });

  test('takes whole numbers of years from 2 to 30 in its field', async () => {
    await renderPage();
    await section();

    const field = yearsField();
    expect(field).toHaveAttribute('id', 'retention-years');
    expect(field).toHaveAttribute('type', 'number');
    expect(field).toHaveAttribute('inputmode', 'numeric');
    expect(field).toHaveAttribute('step', '1');
    expect(field).toHaveAttribute('min', '2');
    expect(field).toHaveAttribute('max', '30');
  });

  test('describes the field with the period now and the retroactive note', async () => {
    await renderPage();
    await section();

    expect(yearsField()).toHaveAccessibleDescription(expect.stringContaining('Derzeit 5 Jahre (Standard)'));
    expect(yearsField()).toHaveAccessibleDescription(expect.stringContaining(note));
    expect(yearsField()).not.toHaveAttribute('aria-invalid', 'true');
  });

  test('switched to English, shows "Retention period", "Currently 5 years (default)" and the English note', async () => {
    await renderPage();
    await section();

    setLanguage('en-GB', { remember: false });

    const region = await screen.findByRole('region', { name: 'Retention period' });
    expect(within(region).getByRole('heading', { level: 2, name: 'Retention period' })).toBeInTheDocument();
    expect(region).toHaveTextContent('Currently 5 years (default)');
    expect(region).toHaveTextContent(
      'The period applies retroactively to every certificate: lengthening it includes older certificates in the export again; shortening it leaves the older ones out. Nothing is deleted.',
    );
    expect(screen.getByLabelText('Retention period in years')).toHaveValue(5);
  });

  test("switching to another pharmacy shows that pharmacy's period", async () => {
    state.memberships = [markt, dom];
    const pinia = createPinia();
    useSelectedPharmacyStore(pinia).choose(markt.id);
    await renderPage({ pinia });
    const region = await section();
    expect(region).toHaveTextContent('Derzeit 5 Jahre (Standard)');

    useSelectedPharmacyStore(pinia).choose(dom.id);

    await waitFor(() =>
      expect(screen.getByRole('region', { name: 'Aufbewahrungsfrist' })).toHaveTextContent('Derzeit 12 Jahre'),
    );
    expect(screen.getByRole('region', { name: 'Aufbewahrungsfrist' })).not.toHaveTextContent('Standard');
    await waitFor(() => expect(yearsField()).toHaveValue(12));
    expect(retentionReads()).toContain(`/api/certification/pharmacies/${dom.id}/retention-period`);
  });
});

describe('the section is not shown', () => {
  test('while the read of the period is on its way', async () => {
    state.read = () => new Promise<Response>(() => undefined);
    await renderPage();

    await waitFor(() =>
      expect(retentionReads()).toEqual([`/api/certification/pharmacies/${markt.id}/retention-period`]),
    );
    noSection();
    expect(screen.queryByLabelText('Aufbewahrungsfrist in Jahren')).toBeNull();
  });

  // ADR 0003: an employee's read is refused with capability-missing; the section hides, and a refusal is final, so it
  // is asked once, not four times.
  test('to a member whose role may not manage the period, which is asked once and says nothing', async () => {
    state.read = async () =>
      refusal(403, 'certification.capability-missing', 'Your role in this pharmacy does not allow this.');
    const { queryClient } = await renderPage();

    await waitFor(() => expect(readOf(queryClient)?.status).toBe('error'));
    expect(retentionReads()).toHaveLength(1);
    noSection();
    expect(screen.queryByRole('alert')).toBeNull();
    expect(notify.failure).not.toHaveBeenCalled();
  });

  test.each([
    [
      'a 404 pharmacy-not-found',
      async () =>
        refusal(
          404,
          'certification.pharmacy-not-found',
          'You are not a member of this pharmacy, or it does not exist.',
        ),
    ],
    ['a 500', async () => new Response('', { status: 500 })],
    ['a rejected fetch', () => Promise.reject(new TypeError('Failed to fetch'))],
  ])('after %s, and no error from the section shows', async (_name, answer) => {
    state.read = answer;
    const { queryClient } = await renderPage();

    await waitFor(() => expect(readOf(queryClient)?.status).toBe('error'));
    expect(retentionReads().length).toBeGreaterThan(0);
    noSection();
    expect(screen.queryByRole('alert')).toBeNull();
    expect(notify.failure).not.toHaveBeenCalled();
  });

  test('while no pharmacy is current, and no period is read until one is', async () => {
    state.memberships = [];
    const { queryClient } = await renderPage();
    // The memberships are read, and nothing more is on its way.
    await waitFor(() => {
      expect(membershipReads()).toBeGreaterThan(0);
      expect(queryClient.isFetching()).toBe(0);
    });
    const readsBefore = membershipReads();

    expect(retentionReads()).toEqual([]);
    noSection();

    state.memberships = [markt];
    await queryClient.invalidateQueries({ queryKey: getGetOrganisationPharmaciesMineQueryKey() });

    await section();
    expect(membershipReads()).toBeGreaterThan(readsBefore);
    expect(retentionReads()).toEqual([`/api/certification/pharmacies/${markt.id}/retention-period`]);
  });
});

// Saving (#419, T4): the field's period goes to the host for the current pharmacy as PUT …/retention-period with
// { years }, which answers 204 without a body (ADR 0039), so the page reads the period again for what it shows. The
// button stays enabled while the request is out, so it keeps the keyboard's focus, as the export's does; a press then
// sends nothing.

/** Puts years in the field as a user does: emptied, then typed. */
async function enterYears(user: ReturnType<typeof userEvent.setup>, years: string) {
  await user.clear(yearsField());
  if (years !== '') {
    await user.type(yearsField(), years);
  }
}

/** A save the host answers only when the test says so, with 204 and the period stored. */
function heldSave() {
  let answer: () => void = () => undefined;
  state.save = (pharmacyId, body) =>
    new Promise<Response>((resolve) => {
      answer = () => {
        state.periods[pharmacyId] = { years: (JSON.parse(body) as { years: number }).years, isDefault: false };
        resolve(new Response(null, { status: 204 }));
      };
    });
  return { answer: () => answer() };
}

describe('saving a period', () => {
  test('typing 7 and pressing "Frist speichern" sends one PUT with {"years":7} for the current pharmacy', async () => {
    const user = userEvent.setup();
    state.memberships = [markt, dom];
    const pinia = createPinia();
    useSelectedPharmacyStore(pinia).choose(dom.id);
    await renderPage({ pinia });
    await section();
    await waitFor(() => expect(yearsField()).toHaveValue(12));

    await enterYears(user, '7');
    await user.click(saveButton());

    await waitFor(() => expect(notify.success).toHaveBeenCalled());
    expect(saves()).toEqual([{ url: `/api/certification/pharmacies/${dom.id}/retention-period`, body: '{"years":7}' }]);
  });

  test('says "Die Aufbewahrungsfrist beträgt jetzt 7 Jahre." after the 204', async () => {
    const user = userEvent.setup();
    await renderPage();
    await section();

    await enterYears(user, '7');
    await user.click(saveButton());

    await waitFor(() => expect(notify.success).toHaveBeenCalledWith('Die Aufbewahrungsfrist beträgt jetzt 7 Jahre.'));
    expect(notify.success).toHaveBeenCalledTimes(1);
    expect(notify.failure).not.toHaveBeenCalled();
  });

  test('switched to English, says "The retention period is now 7 years."', async () => {
    const user = userEvent.setup();
    await renderPage();
    await section();
    setLanguage('en-GB', { remember: false });

    // After the switch the field is labelled in English; typing into the German label could not find it.
    const field = await screen.findByLabelText('Retention period in years');
    await user.clear(field);
    await user.type(field, '7');
    await user.click(await screen.findByRole('button', { name: 'Save period' }));

    await waitFor(() => expect(notify.success).toHaveBeenCalledWith('The retention period is now 7 years.'));
  });

  test('reads the period again and shows "Derzeit 7 Jahre" without "Standard", the field at 7', async () => {
    const user = userEvent.setup();
    await renderPage();
    const region = await section();
    expect(region).toHaveTextContent('Derzeit 5 Jahre (Standard)');
    const readsBefore = retentionReads().length;

    await enterYears(user, '7');
    await user.click(saveButton());

    await waitFor(() => expect(retentionReads().length).toBeGreaterThan(readsBefore));
    expect(retentionReads().at(-1)).toBe(`/api/certification/pharmacies/${markt.id}/retention-period`);
    await waitFor(() =>
      expect(screen.getByRole('region', { name: 'Aufbewahrungsfrist' })).toHaveTextContent('Derzeit 7 Jahre'),
    );
    expect(screen.getByRole('region', { name: 'Aufbewahrungsfrist' })).not.toHaveTextContent('Standard');
    expect(yearsField()).toHaveValue(7);
  });

  test('sends once on a double click, the button aria-disabled, busy and reading "Wird gespeichert…" while it is sent', async () => {
    const user = userEvent.setup();
    const held = heldSave();
    await renderPage();
    await section();

    await enterYears(user, '7');
    await user.dblClick(saveButton());

    const saving = await screen.findByRole('button', { name: 'Wird gespeichert…' });
    expect(saving).not.toBeDisabled();
    expect(saving).toHaveAttribute('aria-disabled', 'true');
    expect(saving).toHaveAttribute('aria-busy', 'true');
    expect(saves()).toHaveLength(1);
    held.answer();
    await waitFor(() => expect(notify.success).toHaveBeenCalled());
    expect(saves()).toHaveLength(1);
    await waitFor(() => expect(saveButton()).not.toHaveAttribute('aria-busy', 'true'));
    expect(saveButton()).toBeEnabled();
    expect(saveButton()).not.toHaveAttribute('aria-disabled', 'true');
  });

  test('announces "Wird gespeichert…" in a status region while the period is sent', async () => {
    const user = userEvent.setup();
    const held = heldSave();
    await renderPage();
    await section();

    await enterYears(user, '7');
    await user.click(saveButton());

    await waitFor(() =>
      expect(screen.getAllByRole('status').map((status) => status.textContent?.trim())).toContain('Wird gespeichert…'),
    );
    held.answer();
    await waitFor(() => expect(notify.success).toHaveBeenCalled());
    await waitFor(() =>
      expect(screen.getAllByRole('status').map((status) => status.textContent?.trim())).not.toContain(
        'Wird gespeichert…',
      ),
    );
  });

  test('keeps the focus on the button pressed from the keyboard while the period is sent and after it', async () => {
    const user = userEvent.setup();
    const held = heldSave();
    await renderPage();
    await section();
    await enterYears(user, '7');
    saveButton().focus();

    await user.keyboard('{Enter}');

    const saving = await screen.findByRole('button', { name: 'Wird gespeichert…' });
    expect(saving).toHaveFocus();
    expect(saving).toHaveAttribute('aria-disabled', 'true');

    await user.keyboard('{Enter}');
    expect(saves()).toHaveLength(1);
    expect(saving).toHaveFocus();

    held.answer();
    await waitFor(() => expect(notify.success).toHaveBeenCalled());
    await waitFor(() => expect(saveButton()).toHaveFocus());
    expect(saves()).toHaveLength(1);
  });
});

// Refusals (#419, T5; ADR 0027). A period outside 2 to 30 whole years is refused at the field before anything is sent,
// with the sentence the host's own 400 has in the catalog, and the focus goes to the field, where it is read out; the
// host's 400 shows the same there. The route's other refusals go to a toast, and the period is read again: refused now,
// the section goes, which is why it says nothing itself. Anything else says the period could not be saved, and the
// field keeps its value, so pressing again retries. There is no conflict to handle: the last write wins (ADR 0039).

const invalidText = 'Die Aufbewahrungsfrist ist eine ganze Zahl von 2 bis 30 Jahren.';
const failedText = 'Die Aufbewahrungsfrist konnte nicht gespeichert werden. Versuchen Sie es erneut.';

describe('a period the form refuses', () => {
  test.each([
    ['31', '31'],
    ['1', '1'],
    ['an empty field', ''],
    ['2.5', '2.5'],
  ])('%s shows the refusal at the field, moves the focus there and sends nothing', async (_name, years) => {
    const user = userEvent.setup();
    await renderPage();
    const region = await section();

    await fireEvent.update(yearsField(), years);
    await user.click(saveButton());

    expect(await within(region).findByRole('alert')).toHaveTextContent(invalidText);
    expect(yearsField()).toHaveAttribute('aria-invalid', 'true');
    expect(yearsField()).toHaveAccessibleDescription(expect.stringContaining(invalidText));
    await waitFor(() => expect(yearsField()).toHaveFocus());
    expect(saves()).toEqual([]);
    expect(notify.success).not.toHaveBeenCalled();
    expect(notify.failure).not.toHaveBeenCalled();
  });

  test('switched to English, shows the English text', async () => {
    const user = userEvent.setup();
    await renderPage();
    await section();
    setLanguage('en-GB', { remember: false });

    await fireEvent.update(await screen.findByLabelText('Retention period in years'), '31');
    await user.click(await screen.findByRole('button', { name: 'Save period' }));

    expect(await screen.findByRole('alert')).toHaveTextContent(
      'The retention period is a whole number of years from 2 to 30.',
    );
    expect(saves()).toEqual([]);
  });

  test('a save pressed just before choosing a pharmacy whose period is not read yet never stores it for that pharmacy', async () => {
    const user = userEvent.setup();
    state.memberships = [markt, dom];
    let releaseDom: () => void = () => undefined;
    const domRead = new Promise<void>((resolve) => {
      releaseDom = resolve;
    });
    const readPeriod = state.read;
    state.read = async (pharmacyId) => {
      if (pharmacyId === dom.id) {
        await domRead;
      }
      return readPeriod(pharmacyId);
    };
    const pinia = createPinia();
    useSelectedPharmacyStore(pinia).choose(markt.id);
    await renderPage({ pinia });
    await section();
    await enterYears(user, '7');

    await fireEvent.click(saveButton());
    useSelectedPharmacyStore(pinia).choose(dom.id);
    await waitFor(() => expect(retentionReads()).toContain(`/api/certification/pharmacies/${dom.id}/retention-period`));
    // The press's check ends while the new pharmacy's period is still on its way, which is the window it must not
    // send in; then that period arrives.
    await waitFor(() => expect(pressesChecked()).toBe('1'));
    noSection();
    releaseDom();

    await waitFor(() => expect(yearsField()).toHaveValue(12));
    expect(saves().filter((save) => save.url.includes(dom.id))).toEqual([]);
    expect(state.periods[dom.id]).toEqual({ years: 12, isDefault: false });
  });

  test('is cleared when another pharmacy is chosen, the field at that pharmacy’s period', async () => {
    const user = userEvent.setup();
    state.memberships = [markt, dom];
    const pinia = createPinia();
    useSelectedPharmacyStore(pinia).choose(markt.id);
    await renderPage({ pinia });
    const region = await section();
    await fireEvent.update(yearsField(), '31');
    await user.click(saveButton());
    expect(await within(region).findByRole('alert')).toHaveTextContent(invalidText);

    useSelectedPharmacyStore(pinia).choose(dom.id);

    await waitFor(() => expect(yearsField()).toHaveValue(12));
    await waitFor(() => expect(yearsField()).not.toHaveAttribute('aria-invalid', 'true'));
    expect(screen.queryByRole('alert')).toBeNull();
    expect(yearsField()).not.toHaveAccessibleDescription(expect.stringContaining(invalidText));
  });
});

describe('a refusal of the host', () => {
  test('of the period shows its text at the field and moves the focus there', async () => {
    const user = userEvent.setup();
    state.save = async () =>
      refusal(400, 'certification.invalid-retention-period', 'The retention period is from 2 to 30 years.');
    await renderPage();
    const region = await section();

    await enterYears(user, '7');
    await user.click(saveButton());

    await waitFor(() => expect(yearsField()).toHaveAccessibleDescription(expect.stringContaining(invalidText)));
    expect(yearsField()).toHaveAttribute('aria-invalid', 'true');
    expect(within(region).getByRole('alert')).toHaveTextContent(invalidText);
    await waitFor(() => expect(yearsField()).toHaveFocus());
    expect(saves()).toHaveLength(1);
    expect(notify.success).not.toHaveBeenCalled();
    expect(notify.failure).not.toHaveBeenCalled();
  });

  test('capability-missing says "Ihre Rolle in dieser Apotheke erlaubt das nicht." and the section goes once the period is refused too', async () => {
    const user = userEvent.setup();
    const capabilityMissing = () =>
      refusal(403, 'certification.capability-missing', 'Your role in this pharmacy does not allow this.');
    state.save = async () => {
      // The manager became an employee: the host now refuses the read as well.
      state.read = async () => capabilityMissing();
      return capabilityMissing();
    };
    await renderPage();
    await section();
    const readsBefore = retentionReads().length;

    await enterYears(user, '7');
    await user.click(saveButton());

    await waitFor(() =>
      expect(notify.failure).toHaveBeenCalledWith('Ihre Rolle in dieser Apotheke erlaubt das nicht.'),
    );
    await waitFor(() => expect(retentionReads().length).toBeGreaterThan(readsBefore));
    await waitFor(() => noSection());
    expect(screen.queryByRole('alert')).toBeNull();
    expect(notify.success).not.toHaveBeenCalled();
  });

  test('pharmacy-not-found says its text and reads the period and the memberships again', async () => {
    const user = userEvent.setup();
    const notFound = () =>
      refusal(404, 'certification.pharmacy-not-found', 'You are not a member of this pharmacy, or it does not exist.');
    state.save = async () => {
      state.read = async () => notFound();
      return notFound();
    };
    await renderPage();
    await section();
    const readsBefore = retentionReads().length;
    const membershipsBefore = membershipReads();

    await enterYears(user, '7');
    await user.click(saveButton());

    await waitFor(() =>
      expect(notify.failure).toHaveBeenCalledWith('Sie sind nicht Mitglied dieser Apotheke, oder es gibt sie nicht.'),
    );
    await waitFor(() => expect(retentionReads().length).toBeGreaterThan(readsBefore));
    await waitFor(() => expect(membershipReads()).toBeGreaterThan(membershipsBefore));
    await waitFor(() => noSection());
    expect(notify.success).not.toHaveBeenCalled();
  });

  test.each([
    ['a 500', async () => new Response('', { status: 500 })],
    ['a rejected fetch', () => Promise.reject(new TypeError('Failed to fetch'))],
  ])(
    '%s says the period could not be saved, the field keeps its value, and pressing again sends again',
    async (_name, answer) => {
      const user = userEvent.setup();
      const stored = state.save;
      state.save = answer;
      await renderPage();
      await section();

      await enterYears(user, '7');
      await user.click(saveButton());

      await waitFor(() => expect(notify.failure).toHaveBeenCalledWith(failedText));
      expect(saves()).toHaveLength(1);
      expect(yearsField()).toHaveValue(7);
      expect(yearsField()).not.toHaveAttribute('aria-invalid', 'true');
      expect(screen.getByRole('region', { name: 'Aufbewahrungsfrist' })).toBeInTheDocument();

      state.save = stored;
      await user.click(saveButton());

      await waitFor(() => expect(saves()).toHaveLength(2));
      await waitFor(() => expect(notify.success).toHaveBeenCalledWith('Die Aufbewahrungsfrist beträgt jetzt 7 Jahre.'));
      expect(saves()[1]?.body).toBe('{"years":7}');
    },
  );
});
