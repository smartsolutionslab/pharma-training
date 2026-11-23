import { cleanup, screen, waitFor, within } from '@testing-library/vue';
import userEvent from '@testing-library/user-event';
import { afterEach, beforeEach, describe, expect, test, vi } from 'vitest';
import { setLanguage, t } from '@/i18n';
import {
  type Fetch,
  answering,
  emergency,
  hygiene,
  matrixHost,
  type MatrixHost,
  matrixOf,
  matrixPath,
  membersOnly,
  now,
  pagedTeam,
  privacy,
  readsOf,
  renderMatrixPage,
  teamMatrix,
} from './matrixFixtures';

// The team matrix's table (#36, T3): a table of its own, not DataTable, with a column header per course and a row header
// per person, so a screen reader names each cell by both. Every status shows as an icon, a word and a date; the colour
// only repeats the word (the issue's done-when). The legend, the empty states, the summary and the pager.

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

const region = () => screen.getByRole('region', { name: t('compliance.matrix.table.label') });
const table = () => within(region()).getByRole('table');

/** The row header of a member, once the matrix shows: it holds the button named after them. */
const rowHeaderOf = async (name: string) => screen.findByRole('rowheader', { name: new RegExp(`^${name}`) });

/** The cell of a member in a course's column: the row's cell under that column header. */
async function cellOf(name: string, course: string): Promise<HTMLElement> {
  const row = (await rowHeaderOf(name)).closest('tr');
  if (row === null) {
    throw new Error(`${name} has no row`);
  }
  const columns = within(table()).getAllByRole('columnheader');
  const column = columns.findIndex((header) => header.textContent?.includes(course) === true);
  expect(column, `a column ${course}`).toBeGreaterThan(0);
  const cell = row.children.item(column);
  if (!(cell instanceof HTMLElement)) {
    throw new Error(`${name} has no cell under ${course}`);
  }
  return cell;
}

/** The status icons of an element: lucide's SVGs, which name their icon in their class. */
const iconsIn = (element: Element) => Array.from(element.querySelectorAll('svg'));

/** Every status, its word, its icon (lucide's class), and the cell that shows it in the team on 2026-11-23. */
const statuses = [
  ['Open', 'Offen', 'lucide-circle', 'Anna Albers', hygiene.title, 'fällig 15.03.2027'],
  ['InProgress', 'In Bearbeitung', 'lucide-circle-dot', 'Anna Albers', privacy.title, 'fällig 10.01.2027'],
  ['Locked', 'Gesperrt', 'lucide-lock', 'Berta Braun', emergency.title, 'fällig 31.12.2026'],
  ['Overdue', 'Überfällig', 'lucide-circle-alert', 'Berta Braun', hygiene.title, 'fällig 22.11.2026'],
  ['Completed', 'Abgeschlossen', 'lucide-circle-check', 'Berta Braun', privacy.title, 'gültig bis 01.06.2027'],
  [
    'CompletedLate',
    'Verspätet abgeschlossen',
    'lucide-circle-check',
    'Carl Claasen',
    emergency.title,
    'gültig bis 10.02.2027',
  ],
  ['Expired', 'Abgelaufen', 'lucide-calendar-x', 'Carl Claasen', privacy.title, 'gültig bis 22.11.2026'],
] as const;

const words = statuses.map(([, word]) => word);

describe('the table', () => {
  test('scrolls inside a focusable region labelled as the team matrix, so the keyboard can scroll it', async () => {
    await renderMatrixPage();

    await rowHeaderOf('Anna Albers');
    expect(region()).toHaveAttribute('tabindex', '0');
    expect(region()).toHaveAccessibleName('Team-Matrix: Mitglieder nach Schulungen');
    expect(region()).toHaveClass('overflow-auto');
    expect(within(region()).getByRole('table')).toBeInTheDocument();
  });

  test('is not the data grid: it has no Columns control and no density toggle', async () => {
    await renderMatrixPage();

    await rowHeaderOf('Anna Albers');
    expect(screen.queryByRole('button', { name: t('dataTable.columns') })).not.toBeInTheDocument();
    expect(screen.queryByRole('group', { name: t('dataTable.density') })).not.toBeInTheDocument();
  });

  test('heads each course’s column with its title and its own rate, after the member column', async () => {
    await renderMatrixPage();

    await rowHeaderOf('Anna Albers');
    const headers = within(table()).getAllByRole('columnheader');
    expect(headers.map((header) => header.getAttribute('scope'))).toEqual(['col', 'col', 'col', 'col']);
    expect(headers[0]).toHaveTextContent('Mitglied');
    expect(headers[1]).toHaveTextContent('Datenschutz in der Apotheke');
    expect(headers[1]).toHaveTextContent('Quote 67 %');
    expect(headers[2]).toHaveTextContent('Hygiene in der Apotheke');
    expect(headers[2]).toHaveTextContent('Quote 50 %');
    expect(headers[3]).toHaveTextContent('Notfallmanagement');
    expect(headers[3]).toHaveTextContent('Quote 100 %');
  });

  test('says a column’s rate in English once switched', async () => {
    setLanguage('en-GB', { remember: false });
    await renderMatrixPage();

    await rowHeaderOf('Anna Albers');
    const headers = within(table()).getAllByRole('columnheader');
    expect(headers[0]).toHaveTextContent('Member');
    expect(headers[2]).toHaveTextContent('Rate 50%');
  });

  test('a column without a rate says so with a dash', async () => {
    host.matrix = async () =>
      Response.json({ ...teamMatrix(), courses: [{ ...hygiene, compliantAssignments: 0, complianceRate: null }] });
    await renderMatrixPage();

    await rowHeaderOf('Anna Albers');
    expect(within(table()).getAllByRole('columnheader')[1]).toHaveTextContent('Quote –');
  });

  test('heads each member’s row with a button named after them and their short job function', async () => {
    await renderMatrixPage();

    const rows = [
      ['Anna Albers', 'Apotheker'],
      ['Berta Braun', 'PKA'],
      ['Carl Claasen', 'PTA'],
      ['Dora Dietz', 'Azubi'],
    ] as const;
    for (const [name, jobFunction] of rows) {
      const header = await rowHeaderOf(name);
      expect(header.tagName).toBe('TH');
      expect(header).toHaveAttribute('scope', 'row');
      expect(within(header).getByRole('button', { name: `${name}, Schulungsstand anzeigen` })).toHaveTextContent(name);
      expect(header).toHaveTextContent(jobFunction);
    }
    expect(within(table()).getAllByRole('rowheader')).toHaveLength(4);
  });

  test('keeps the member column and the course headers in view while the region scrolls', async () => {
    await renderMatrixPage();

    const header = await rowHeaderOf('Anna Albers');
    expect(header).toHaveClass('sticky', 'left-0', 'bg-background');
    const [corner, ...courses] = within(table()).getAllByRole('columnheader');
    expect(corner).toHaveClass('sticky', 'top-0', 'left-0', 'z-20', 'bg-background');
    for (const course of courses) {
      expect(course).toHaveClass('sticky', 'top-0', 'bg-background');
    }
  });
});

describe('a cell', () => {
  test.each(statuses)(
    '%s shows the word %s, its date line and its icon, hidden from screen readers',
    async (_status, word, icon, name, course, line) => {
      await renderMatrixPage();

      const cell = await cellOf(name, course);
      expect(cell.tagName).toBe('TD');
      expect(cell).toHaveTextContent(word);
      expect(cell).toHaveTextContent(line);
      const [svg, ...more] = iconsIn(cell);
      expect(more).toEqual([]);
      expect(svg?.classList.contains(icon)).toBe(true);
      expect(svg).toHaveAttribute('aria-hidden', 'true');
    },
  );

  test.each([
    ['Berta Braun', hygiene.title],
    ['Carl Claasen', privacy.title],
  ])('of %s in %s needs action and is highlighted by a border, not only by colour', async (name, course) => {
    await renderMatrixPage();

    expect(await cellOf(name, course)).toHaveClass('border-l-4', 'border-destructive');
  });

  test('a completion still valid today is no highlighted cell', async () => {
    await renderMatrixPage();

    expect(await cellOf('Berta Braun', privacy.title)).not.toHaveClass('border-destructive');
  });

  test('a course not assigned to a member reads “Nicht zugewiesen” to a screen reader, and a dash to the eye', async () => {
    await renderMatrixPage();

    const cell = await cellOf('Anna Albers', emergency.title);
    const said = within(cell).getByText('Nicht zugewiesen');
    expect(said).toHaveClass('sr-only');
    const dash = within(cell).getByText('—');
    expect(dash).toHaveAttribute('aria-hidden', 'true');
    expect(iconsIn(cell)).toEqual([]);
  });

  test('says its status and date in English once switched', async () => {
    setLanguage('en-GB', { remember: false });
    await renderMatrixPage();

    expect(await cellOf('Berta Braun', hygiene.title)).toHaveTextContent('Overdue');
    expect(await cellOf('Berta Braun', hygiene.title)).toHaveTextContent('due 22/11/2026');
    expect(await cellOf('Carl Claasen', privacy.title)).toHaveTextContent('Expired');
    expect(await cellOf('Carl Claasen', privacy.title)).toHaveTextContent('valid until 22/11/2026');
    expect(await cellOf('Anna Albers', emergency.title)).toHaveTextContent('Not assigned');
  });

  // The done-when: no status is shown by colour alone. Whatever classes a cell has, its text says its status.
  test('every cell says its status in words, whatever its classes', async () => {
    await renderMatrixPage();

    await rowHeaderOf('Anna Albers');
    const cells = within(table()).getAllByRole('cell');
    expect(cells).toHaveLength(12);
    for (const cell of cells) {
      const bare = cell.cloneNode(true) as HTMLElement;
      for (const styled of [bare, ...Array.from(bare.querySelectorAll('[class]'))]) {
        styled.removeAttribute('class');
      }
      const said = [...words, 'Nicht zugewiesen'].filter((word) =>
        new RegExp(`(^|[^\\p{L}])${word}($|[^\\p{L}])`, 'u').test(bare.textContent ?? ''),
      );
      expect(said, cell.textContent ?? '').toHaveLength(1);
    }
  });

  test('colours a status only with the theme’s tokens, never a palette colour of its own', async () => {
    await renderMatrixPage();

    await rowHeaderOf('Anna Albers');
    const own = /\b(?:text|bg|border)-(?:red|green|amber|yellow|orange|emerald|lime|blue|sky|rose)-\d{2,3}\b/;
    for (const element of Array.from(table().querySelectorAll('[class]'))) {
      expect(element.getAttribute('class') ?? '').not.toMatch(own);
    }
  });
});

describe('the legend', () => {
  test('lists all seven statuses, each with its icon and its word, above the table', async () => {
    await renderMatrixPage();

    await rowHeaderOf('Anna Albers');
    const legend = screen.getByRole('list', { name: 'Legende' });
    const items = within(legend).getAllByRole('listitem');
    expect(items.map((item) => item.textContent?.trim())).toEqual(words);
    for (const [index, [, , icon]] of statuses.entries()) {
      const [svg] = iconsIn(items[index] as HTMLElement);
      expect(svg?.classList.contains(icon)).toBe(true);
      expect(svg).toHaveAttribute('aria-hidden', 'true');
    }
    expect(legend.compareDocumentPosition(table()) & Node.DOCUMENT_POSITION_FOLLOWING).toBeTruthy();
  });

  test('is called Key in English and names the statuses in English', async () => {
    setLanguage('en-GB', { remember: false });
    await renderMatrixPage();

    const legend = await screen.findByRole('list', { name: 'Key' });
    expect(
      within(legend)
        .getAllByRole('listitem')
        .map((item) => item.textContent?.trim()),
    ).toEqual(['Open', 'In progress', 'Locked', 'Overdue', 'Completed', 'Completed late', 'Expired']);
  });
});

describe('with no course assigned', () => {
  test('the page says so instead of a table, and offers to assign one', async () => {
    host.matrix = async () =>
      Response.json({
        ...matrixOf([], []),
        assignments: 0,
        compliantAssignments: 0,
        overdueAssignments: 0,
        complianceRate: null,
      });
    await renderMatrixPage();

    const said = await screen.findByText('In dieser Apotheke ist noch keine Schulung zugewiesen.');
    const empty = said.closest<HTMLElement>('[data-slot="empty-state"]');
    expect(empty).not.toBeNull();
    expect(within(empty ?? said).getByRole('link', { name: 'Schulung zuweisen' })).toHaveAttribute(
      'href',
      '/compliance/assign',
    );
    expect(screen.queryByRole('table')).not.toBeInTheDocument();
  });

  test('a manager who may not assign courses is told so without the way to assign one', async () => {
    host.memberships = [membersOnly];
    host.matrix = async () => Response.json({ ...matrixOf([], []), complianceRate: null });
    await renderMatrixPage();

    const said = await screen.findByText(t('compliance.matrix.empty.noCourses'));
    const empty = said.closest<HTMLElement>('[data-slot="empty-state"]');
    expect(empty).not.toBeNull();
    expect(within(empty ?? said).queryByRole('link')).not.toBeInTheDocument();
  });
});

describe('the pager', () => {
  test('one page has a summary but no pager', async () => {
    await renderMatrixPage();

    expect(await screen.findByText('Einträge 1–4 von 4')).toBeInTheDocument();
    expect(screen.queryByRole('navigation', { name: t('dataTable.pagination') })).not.toBeInTheDocument();
  });

  test('the summary describes the region', async () => {
    host.matrix = pagedTeam(60);
    await renderMatrixPage();

    const summary = await screen.findByText('Einträge 1–50 von 60');
    expect(summary.id).not.toBe('');
    expect(region()).toHaveAttribute('aria-describedby', summary.id);
    expect(region()).toHaveAccessibleDescription('Einträge 1–50 von 60');
  });

  test('Next shows the second page in the address and from the host, and moves the focus to the region', async () => {
    const user = userEvent.setup();
    host.matrix = pagedTeam(60);
    const { router } = await renderMatrixPage();

    await screen.findByText('Einträge 1–50 von 60');
    const pager = screen.getByRole('navigation', { name: 'Seiten' });
    await user.click(within(pager).getByRole('button', { name: 'Nächste Seite' }));

    await waitFor(() => expect(router.currentRoute.value.query).toEqual({ page: '2' }));
    await waitFor(() => expect(readsOf(fetch, matrixPath())).toContainEqual({ page: '2', pageSize: '50' }));
    expect(await screen.findByText('Einträge 51–60 von 60')).toBeInTheDocument();
    expect(await rowHeaderOf('Mitglied 51')).toBeInTheDocument();
    expect(region()).toHaveFocus();
  });

  test('Previous goes back to the first page and moves the focus to the region', async () => {
    const user = userEvent.setup();
    host.matrix = pagedTeam(60);
    const { router } = await renderMatrixPage({ address: '/compliance?page=2' });

    await screen.findByText('Einträge 51–60 von 60');
    await user.click(screen.getByRole('button', { name: 'Vorherige Seite' }));

    await waitFor(() => expect(router.currentRoute.value.query).toEqual({}));
    expect(await screen.findByText('Einträge 1–50 von 60')).toBeInTheDocument();
    expect(region()).toHaveFocus();
  });

  test('a page past the last one shows the last page and replaces it in the address', async () => {
    host.matrix = pagedTeam(60);
    const { router } = await renderMatrixPage({ from: '/', address: '/compliance?page=9' });

    expect(await screen.findByText('Einträge 51–60 von 60')).toBeInTheDocument();
    expect(router.currentRoute.value.query).toEqual({ page: '2' });
    expect(readsOf(fetch, matrixPath())).toContainEqual({ page: '2', pageSize: '50' });
    // Replaced, not pushed: Back leaves the matrix instead of returning to the page that does not exist.
    router.back();
    await waitFor(() => expect(router.currentRoute.value.path).toBe('/'));
  });

  test('is named and worded in English once switched', async () => {
    setLanguage('en-GB', { remember: false });
    host.matrix = pagedTeam(60);
    await renderMatrixPage();

    expect(await screen.findByText('Showing 1–50 of 60')).toBeInTheDocument();
    const pager = screen.getByRole('navigation', { name: 'Pages' });
    expect(within(pager).getByRole('button', { name: 'Next page' })).toBeInTheDocument();
  });
});
