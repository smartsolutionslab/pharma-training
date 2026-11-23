import { cleanup, screen, waitFor, within } from '@testing-library/vue';
import userEvent from '@testing-library/user-event';
import { afterEach, beforeEach, expect, test, vi } from 'vitest';
import { setLanguage } from '@/i18n';
import {
  type Fetch,
  answering,
  emergency,
  hygiene,
  matrixHost,
  type MatrixHost,
  matrixPath,
  minePath,
  now,
  privacy,
  readsOf,
  renderMatrixPage,
  teamMatrix,
} from './matrixFixtures';

// The click through to a person (#36, T5): the name in a row's header opens a side sheet with that member's courses,
// every course assigned to them whatever the course filter, from the row already loaded: no request and no address of
// its own. The sheet takes the focus, keeps it, and gives it back to the name on Escape.

const fetch = vi.fn<Fetch>();
vi.stubGlobal('fetch', fetch);

let host: MatrixHost;

beforeEach(() => {
  vi.useFakeTimers({ toFake: ['Date'] });
  vi.setSystemTime(now);
  localStorage.clear();
  host = matrixHost();
  host.matrix = async () => Response.json(teamMatrix());
  fetch.mockImplementation(answering(host));
});
afterEach(() => {
  vi.useRealTimers();
  fetch.mockReset();
  localStorage.clear();
});
// Without Vitest's globals, Testing Library does not unmount between tests by itself.
afterEach(cleanup);

const nameButton = (name: string, label = 'Schulungsstand anzeigen') =>
  screen.findByRole('button', { name: `${name}, ${label}` });

/** Opens a member's sheet with their name's button, as a manager does. */
async function open(name: string, label?: string) {
  const user = userEvent.setup();
  const button = await nameButton(name, label);
  await user.click(button);
  const dialog = await screen.findByRole('dialog', { name });
  return { user, button, dialog };
}

/** A course's section in the sheet, headed by its title. */
function sectionOf(dialog: HTMLElement, title: string): HTMLElement {
  const heading = within(dialog).getByRole('heading', { level: 3, name: title });
  const section = heading.closest('section');
  if (section === null) {
    throw new Error(`${title} has no section`);
  }
  return section;
}

/** What a section's list says for a term, or undefined when it has no such term. */
function definitionOf(section: HTMLElement, term: string): string | undefined {
  const terms = within(section)
    .queryAllByRole('term')
    .filter((element) => element.textContent?.trim() === term);
  expect(terms.length, `${term} at most once`).toBeLessThanOrEqual(1);
  const [found] = terms;
  if (found === undefined) {
    return undefined;
  }
  const definition = found.nextElementSibling;
  expect(definition?.tagName).toBe('DD');
  return definition?.textContent?.trim();
}

test('the name opens a sheet titled with it, describing the job function and how many courses are assigned', async () => {
  await renderMatrixPage();

  const { dialog } = await open('Berta Braun');

  expect(dialog).toHaveAccessibleName('Berta Braun');
  expect(dialog).toHaveAccessibleDescription(
    'PKA (pharmazeutisch-kaufmännischer Angestellter) · 3 Schulungen zugewiesen',
  );
});

test('the sheet has a section per assigned course, in the matrix’s column order', async () => {
  await renderMatrixPage();

  const { dialog } = await open('Berta Braun');

  expect(
    within(dialog)
      .getAllByRole('heading', { level: 3 })
      .map((heading) => heading.textContent),
  ).toEqual([privacy.title, hygiene.title, emergency.title]);
});

test('a course’s section says its status in words with its icon, its due date, and that it was never completed', async () => {
  await renderMatrixPage();

  const { dialog } = await open('Berta Braun');

  const overdue = sectionOf(dialog, hygiene.title);
  expect(overdue).toHaveTextContent('Überfällig');
  const icon = overdue.querySelector('svg');
  expect(icon?.classList.contains('lucide-circle-alert')).toBe(true);
  expect(icon).toHaveAttribute('aria-hidden', 'true');
  expect(definitionOf(overdue, 'Fällig am')).toBe('22.11.2026');
  expect(definitionOf(overdue, 'Zuletzt abgeschlossen')).toBe('Noch nie');
  expect(definitionOf(overdue, 'Gültig bis')).toBeUndefined();
});

test('a completed course’s section says when it was completed and until when it is valid', async () => {
  await renderMatrixPage();

  const { dialog } = await open('Berta Braun');

  const completed = sectionOf(dialog, privacy.title);
  expect(completed).toHaveTextContent('Abgeschlossen');
  expect(definitionOf(completed, 'Fällig am')).toBe('30.06.2026');
  expect(definitionOf(completed, 'Zuletzt abgeschlossen')).toBe('01.06.2026');
  expect(definitionOf(completed, 'Gültig bis')).toBe('01.06.2027');
});

test('an expired completion is shown as expired in the sheet too', async () => {
  await renderMatrixPage();

  const { dialog } = await open('Carl Claasen');

  const expired = sectionOf(dialog, privacy.title);
  expect(expired).toHaveTextContent('Abgelaufen');
  expect(expired.querySelector('svg')?.classList.contains('lucide-calendar-x')).toBe(true);
  expect(definitionOf(expired, 'Gültig bis')).toBe('22.11.2026');
});

test('the sheet lists every course of the member, whatever the course filter', async () => {
  await renderMatrixPage({ address: `/compliance?course=${hygiene.courseId}` });

  const { dialog } = await open('Berta Braun');

  expect(within(dialog).getAllByRole('heading', { level: 3 })).toHaveLength(3);
});

test('a member without any course is told so', async () => {
  await renderMatrixPage();

  const { dialog } = await open('Dora Dietz');

  expect(dialog).toHaveAccessibleDescription('Azubi · Keine Schulung zugewiesen');
  expect(dialog).toHaveTextContent('Diesem Mitglied ist hier keine Schulung zugewiesen.');
  expect(within(dialog).queryAllByRole('heading', { level: 3 })).toEqual([]);
});

test('the sheet takes the focus, Escape closes it, and the focus returns to the name that opened it', async () => {
  await renderMatrixPage();

  const { user, button, dialog } = await open('Anna Albers');
  await waitFor(() => expect(dialog).toContainElement(document.activeElement as HTMLElement));

  await user.keyboard('{Escape}');

  await waitFor(() => expect(screen.queryByRole('dialog')).not.toBeInTheDocument());
  await waitFor(() => expect(button).toHaveFocus());
});

test('opening the sheet asks the host nothing', async () => {
  await renderMatrixPage();
  await nameButton('Berta Braun');
  const matrixReads = readsOf(fetch, matrixPath()).length;
  const mineReads = readsOf(fetch, minePath).length;

  await open('Berta Braun');

  expect(readsOf(fetch, matrixPath())).toHaveLength(matrixReads);
  expect(readsOf(fetch, minePath)).toHaveLength(mineReads);
  expect(fetch.mock.calls.every(([, init]) => (init?.method ?? 'GET') === 'GET')).toBe(true);
});

test('opening the sheet leaves the address as it is', async () => {
  const { router } = await renderMatrixPage({ address: '/compliance?jobFunction=Pka' });

  await open('Berta Braun');

  expect(router.currentRoute.value.fullPath).toBe('/compliance?jobFunction=Pka');
});

test('the sheet speaks English once switched', async () => {
  setLanguage('en-GB', { remember: false });
  await renderMatrixPage();

  const { dialog } = await open('Berta Braun', 'show training status');

  expect(dialog).toHaveAccessibleDescription('PKA (pharmaceutical-commercial employee) · 3 courses assigned');
  const overdue = sectionOf(dialog, hygiene.title);
  expect(overdue).toHaveTextContent('Overdue');
  expect(definitionOf(overdue, 'Due')).toBe('22/11/2026');
  expect(definitionOf(overdue, 'Last completed')).toBe('Never');
});
