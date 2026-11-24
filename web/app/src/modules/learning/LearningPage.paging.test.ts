import { cleanup, screen, waitFor, within } from '@testing-library/vue';
import userEvent from '@testing-library/user-event';
import { createPinia } from 'pinia';
import { afterEach, beforeEach, describe, expect, test, vi } from 'vitest';
import { setLanguage, t } from '@/i18n';
import { useSelectedPharmacyStore } from '@/stores/selectedPharmacy';
import {
  answering,
  assignmentsOf,
  assignmentsPath,
  type Fetch,
  markt,
  marktManager,
  myCoursesHost,
  never,
  type MyCoursesHost,
  pagedCourses,
  problem,
  readOf,
  readsOf,
  renderMyCoursesPage,
  ufer,
} from './myCoursesFixtures';

// My courses (#38, T4) with nothing to show, with more than one page, and across pharmacies. The page is the only state
// in the address (/learning?page=2), so Reload and Back keep it; a page past the last is replaced by the last. After
// paging, and after a Retry that succeeds, the focus is on the list's heading, never on a button that has just gone or
// become disabled.

const fetch = vi.fn<Fetch>();
vi.stubGlobal('fetch', fetch);

let host: MyCoursesHost;

beforeEach(() => {
  localStorage.clear();
  host = myCoursesHost();
  fetch.mockImplementation(answering(host));
});
afterEach(() => {
  fetch.mockReset();
  localStorage.clear();
});
// Without Vitest's globals, Testing Library does not unmount between tests by itself.
afterEach(cleanup);

const nothing = () => Response.json(assignmentsOf([]));
const listHeading = () => screen.getByRole('heading', { level: 2, name: 'Zugewiesene Schulungen' });
const emptyText = (pharmacy = markt.name) => `Ihnen ist in ${pharmacy} derzeit keine Schulung zugewiesen.`;

/** The empty state saying this text. */
async function emptyStateSaying(text: string): Promise<HTMLElement> {
  const said = await screen.findByText(text);
  const empty = said.closest<HTMLElement>('[data-slot="empty-state"]');
  expect(empty, `${text} is in an empty state`).not.toBeNull();
  return empty ?? said;
}

describe('with nothing to show', () => {
  test('says no course is assigned in the pharmacy, and offers an employee nothing to do', async () => {
    host.assignments = async () => nothing();
    await renderMyCoursesPage();

    expect(t('learning.myCourses.empty', { pharmacy: markt.name })).toBe(emptyText());
    const empty = await emptyStateSaying(emptyText());
    expect(within(empty).queryByRole('link')).not.toBeInTheDocument();
    expect(within(empty).queryByRole('button')).not.toBeInTheDocument();
    expect(screen.queryByRole('listitem')).not.toBeInTheDocument();
  });

  test('offers someone who may assign courses the way to assign one', async () => {
    host.memberships = [marktManager];
    host.assignments = async () => nothing();
    await renderMyCoursesPage();

    const empty = await emptyStateSaying(emptyText());
    expect(within(empty).getByRole('link', { name: 'Schulung zuweisen' })).toHaveAttribute(
      'href',
      '/compliance/assign',
    );
  });

  test('is worded in English once switched', async () => {
    setLanguage('en-GB', { remember: false });
    host.memberships = [marktManager];
    host.assignments = async () => nothing();
    await renderMyCoursesPage();

    const empty = await emptyStateSaying('No course is currently assigned to you in Apotheke am Markt.');
    expect(within(empty).getByRole('link', { name: 'Assign a course' })).toHaveAttribute('href', '/compliance/assign');
  });
});

describe('the pager', () => {
  test('one page has a summary but no pager', async () => {
    await renderMyCoursesPage();

    expect(await screen.findByText('Einträge 1–5 von 5')).toBeInTheDocument();
    expect(screen.queryByRole('navigation', { name: t('dataTable.pagination') })).not.toBeInTheDocument();
  });

  test('a full page of 50 has no pager either', async () => {
    host.assignments = pagedCourses(50);
    await renderMyCoursesPage();

    expect(await screen.findByText('Einträge 1–50 von 50')).toBeInTheDocument();
    expect(screen.queryByRole('navigation', { name: t('dataTable.pagination') })).not.toBeInTheDocument();
  });

  test('the summary describes the list', async () => {
    host.assignments = pagedCourses(60);
    await renderMyCoursesPage();

    const summary = await screen.findByText('Einträge 1–50 von 60');
    expect(summary.id).not.toBe('');
    expect(screen.getByRole('list')).toHaveAttribute('aria-describedby', summary.id);
    expect(screen.getByRole('list')).toHaveAccessibleDescription('Einträge 1–50 von 60');
  });

  test('Next shows the second page in the address and from the host, and moves the focus to the list’s heading', async () => {
    const user = userEvent.setup();
    host.assignments = pagedCourses(60);
    const { router } = await renderMyCoursesPage();

    await screen.findByText('Einträge 1–50 von 60');
    const pager = screen.getByRole('navigation', { name: 'Seiten' });
    await user.click(within(pager).getByRole('button', { name: 'Nächste Seite' }));

    await waitFor(() => expect(router.currentRoute.value.query).toEqual({ page: '2' }));
    await waitFor(() => expect(readsOf(fetch, assignmentsPath)).toContainEqual(readOf(2)));
    expect(await screen.findByText('Einträge 51–60 von 60')).toBeInTheDocument();
    expect(await screen.findByRole('heading', { level: 3, name: 'Schulung 51' })).toBeInTheDocument();
    expect(listHeading()).toHaveAttribute('tabindex', '-1');
    expect(listHeading()).toHaveFocus();
  });

  test('Previous goes back to the first page and moves the focus to the list’s heading', async () => {
    const user = userEvent.setup();
    host.assignments = pagedCourses(60);
    const { router } = await renderMyCoursesPage({ address: '/learning?page=2' });

    await screen.findByText('Einträge 51–60 von 60');
    await user.click(screen.getByRole('button', { name: 'Vorherige Seite' }));

    await waitFor(() => expect(router.currentRoute.value.query).toEqual({}));
    expect(await screen.findByText('Einträge 1–50 von 60')).toBeInTheDocument();
    expect(listHeading()).toHaveFocus();
  });

  test('a page in the address is asked of the host when the page opens', async () => {
    host.assignments = pagedCourses(60);
    await renderMyCoursesPage({ address: '/learning?page=2' });

    expect(await screen.findByText('Einträge 51–60 von 60')).toBeInTheDocument();
    expect(readsOf(fetch, assignmentsPath)[0]).toEqual(readOf(2));
  });

  test('a page past the last one shows the last page and replaces it in the address', async () => {
    host.assignments = pagedCourses(60);
    const { router } = await renderMyCoursesPage({ from: '/', address: '/learning?page=9' });

    expect(await screen.findByText('Einträge 51–60 von 60')).toBeInTheDocument();
    expect(router.currentRoute.value.query).toEqual({ page: '2' });
    expect(readsOf(fetch, assignmentsPath)).toContainEqual(readOf(2));
    // Replaced, not pushed: Back leaves the page instead of returning to the page that does not exist.
    router.back();
    await waitFor(() => expect(router.currentRoute.value.path).toBe('/'));
  });

  test('a later page with nothing to show replaces the address with the first page', async () => {
    host.assignments = async () => nothing();
    const { router } = await renderMyCoursesPage({ from: '/', address: '/learning?page=2' });

    await waitFor(() => expect(router.currentRoute.value.fullPath).toBe('/learning'));
    expect(await emptyStateSaying(emptyText())).toBeInTheDocument();
    router.back();
    await waitFor(() => expect(router.currentRoute.value.path).toBe('/'));
  });

  test('is named and worded in English once switched', async () => {
    setLanguage('en-GB', { remember: false });
    host.assignments = pagedCourses(60);
    await renderMyCoursesPage();

    expect(await screen.findByText('Showing 1–50 of 60')).toBeInTheDocument();
    const pager = screen.getByRole('navigation', { name: 'Pages' });
    expect(within(pager).getByRole('button', { name: 'Next page' })).toBeInTheDocument();
  });
});

describe('another pharmacy', () => {
  /** The page on the Apotheke am Markt, chosen among the caller's two pharmacies. */
  async function renderOnMarkt(address = '/learning') {
    host.memberships = [markt, ufer];
    const pinia = createPinia();
    const store = useSelectedPharmacyStore(pinia);
    store.choose(markt.id);
    const rendered = await renderMyCoursesPage({ address, pinia });
    return { store, ...rendered };
  }

  test('choosing another pharmacy lists its courses, from its first page', async () => {
    host.assignments = pagedCourses(60);
    const { store, router } = await renderOnMarkt('/learning?page=2');

    await screen.findByText('Einträge 51–60 von 60');
    expect(readsOf(fetch, assignmentsPath)).toContainEqual(readOf(2, markt));

    store.choose(ufer.id);

    await waitFor(() => expect(readsOf(fetch, assignmentsPath)).toContainEqual(readOf(1, ufer)));
    expect(readsOf(fetch, assignmentsPath)).not.toContainEqual(readOf(2, ufer));
    await waitFor(() => expect(router.currentRoute.value.query).toEqual({}));
    expect(
      await screen.findByText('Ihre Schulungen in Apotheke am Ufer: Überfällige zuerst, dann nach Fälligkeit.'),
    ).toBeInTheDocument();
  });

  // From #341's review: the courses of the pharmacy left are not shown, with their Start links, under the one chosen
  // while its own load.
  test('choosing another pharmacy does not show the first one’s courses while its own load', async () => {
    host.assignments = (pharmacyId) =>
      pharmacyId === ufer.id ? never() : Promise.resolve(Response.json(assignmentsOf()));
    const { store } = await renderOnMarkt();
    expect(await screen.findAllByRole('link', { name: /^Starten: |^Fortsetzen: / })).not.toHaveLength(0);

    store.choose(ufer.id);

    await waitFor(() => expect(readsOf(fetch, assignmentsPath)).toContainEqual(readOf(1, ufer)));
    await screen.findByText('Ihre Schulungen in Apotheke am Ufer: Überfällige zuerst, dann nach Fälligkeit.');
    expect(screen.queryAllByRole('link', { name: /^Starten: |^Fortsetzen: / })).toEqual([]);
  });

  test('the empty state names the pharmacy chosen', async () => {
    host.assignments = async () => nothing();
    const { store } = await renderOnMarkt();

    await emptyStateSaying(emptyText());
    store.choose(ufer.id);

    expect(await emptyStateSaying(emptyText(ufer.name))).toBeInTheDocument();
  });
});

describe('the focus', () => {
  test('stays where it is when the page opens', async () => {
    await renderMyCoursesPage();

    await screen.findByRole('list');
    expect(listHeading()).not.toHaveFocus();
    expect(document.activeElement).toBe(document.body);
  });

  test('moves to the list’s heading after a Retry that succeeds, since the button that held it is gone', async () => {
    const user = userEvent.setup();
    host.assignments = async () => problem(500);
    await renderMyCoursesPage();

    const retry = await screen.findByRole('button', { name: 'Erneut versuchen' });
    host.assignments = async () => Response.json(assignmentsOf());
    await user.click(retry);

    await waitFor(() => expect(screen.queryByRole('alert')).not.toBeInTheDocument());
    await waitFor(() => expect(listHeading()).toHaveFocus());
  });
});
