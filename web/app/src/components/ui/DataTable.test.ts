import { render, screen, within } from '@testing-library/vue';
import userEvent from '@testing-library/user-event';
import { expect, test, vi } from 'vitest';
import { h } from 'vue';
import { setLanguage, t } from '@/i18n';
import DataTable from './DataTable.vue';
import { columnsOf } from './data-table-columns';

interface Person {
  name: string;
  team: string;
}

const people = columnsOf<Person>();
const columns = people.columns([
  people.accessor('name', { header: 'Name' }),
  people.accessor('team', { header: 'Team' }),
]);
const data: Person[] = [
  { name: 'first', team: 'blue' },
  { name: 'second', team: 'green' },
];
const none: Person[] = [];
const empty = '<p>No people yet.</p>';
const summaryOf = (first: string, last: string, total: string) => t('dataTable.showing', { first, last, total });

test('the table shows a header and one row per item', () => {
  // A generic component is rendered for one row type, as a template does it from its props.
  render(DataTable<Person>, { props: { columns, data } });

  expect(screen.getByRole('columnheader', { name: 'Name' })).toBeInTheDocument();
  expect(screen.getAllByRole('row')).toHaveLength(3);
});

test('while its query is pending the table is busy, says it is loading and shows skeleton rows in its shape', () => {
  const { container } = render(DataTable<Person>, {
    props: { columns, data: none, loading: true },
    slots: { empty },
  });

  const table = screen.getByRole('table', { name: t('dataTable.loading'), busy: true });
  expect(within(table).getByRole('columnheader', { name: 'Team' })).toBeInTheDocument();
  const skeletonRows = container.querySelectorAll('tbody tr');
  expect(skeletonRows.length).toBeGreaterThan(0);
  for (const row of skeletonRows) {
    expect(row.querySelectorAll('td [data-slot="skeleton"]')).toHaveLength(2);
  }
  expect(screen.queryByText('No people yet.')).not.toBeInTheDocument();
});

test('a table without rows shows its empty state in place of them', () => {
  render(DataTable<Person>, { props: { columns, data: none }, slots: { empty } });

  const table = screen.getByRole('table');
  expect(table).not.toHaveAttribute('aria-busy');
  const cell = within(table).getByRole('cell', { name: 'No people yet.' });
  expect(cell).toHaveAttribute('colspan', '2');
});

test('a table with rows shows no empty state', () => {
  render(DataTable<Person>, { props: { columns, data }, slots: { empty } });

  expect(screen.getByRole('cell', { name: 'second' })).toBeInTheDocument();
  expect(screen.queryByText('No people yet.')).not.toBeInTheDocument();
});

test('the header stays in view while the rows scroll under it', () => {
  render(DataTable<Person>, { props: { columns, data } });

  for (const header of screen.getAllByRole('columnheader')) {
    expect(header).toHaveClass('sticky', 'top-0');
  }
});

test('the density toggle switches the rows between comfortable and compact from the keyboard and says which is on', async () => {
  const user = userEvent.setup();
  render(DataTable<Person>, { props: { columns, data } });
  const density = screen.getByRole('group', { name: t('dataTable.density') });
  const comfortable = within(density).getByRole('button', { name: t('dataTable.comfortable') });
  const compact = within(density).getByRole('button', { name: t('dataTable.compact') });
  expect(comfortable).toHaveAttribute('aria-pressed', 'true');
  expect(screen.getByRole('table')).toHaveAttribute('data-density', 'comfortable');

  compact.focus();
  await user.keyboard('{Enter}');

  expect(compact).toHaveAttribute('aria-pressed', 'true');
  expect(comfortable).toHaveAttribute('aria-pressed', 'false');
  expect(screen.getByRole('table')).toHaveAttribute('data-density', 'compact');
});

test('the columns control hides and shows columns from the keyboard and always keeps one', async () => {
  const user = userEvent.setup();
  render(DataTable<Person>, { props: { columns, data } });
  const button = screen.getByRole('button', { name: t('dataTable.columns') });
  expect(button).toHaveAttribute('aria-expanded', 'false');
  expect(screen.queryByRole('checkbox')).not.toBeInTheDocument();

  button.focus();
  await user.keyboard('{Enter}');
  expect(button).toHaveAttribute('aria-expanded', 'true');
  const shown = screen.getByRole('group', { name: t('dataTable.visibleColumns') });
  expect(within(shown).getByRole('checkbox', { name: 'Name' })).toBeChecked();
  await user.tab();
  await user.tab();
  expect(within(shown).getByRole('checkbox', { name: 'Team' })).toHaveFocus();
  await user.keyboard(' ');

  expect(screen.queryByRole('columnheader', { name: 'Team' })).not.toBeInTheDocument();
  expect(screen.queryByRole('cell', { name: 'blue' })).not.toBeInTheDocument();
  expect(within(shown).getByRole('checkbox', { name: 'Name' })).toBeDisabled();

  await user.keyboard('{Escape}');
  expect(button).toHaveAttribute('aria-expanded', 'false');
  expect(button).toHaveFocus();

  // Name, the one column shown, cannot be hidden and is skipped: the first stop is Team.
  await user.keyboard('{Enter}');
  await user.tab();
  expect(within(shown).getByRole('checkbox', { name: 'Team' })).toHaveFocus();
  await user.keyboard(' ');
  expect(screen.getByRole('columnheader', { name: 'Team' })).toBeInTheDocument();
});

test("a row's actions sit in its last cell, show on hover or focus, and are reached with the keyboard", async () => {
  const user = userEvent.setup();
  const edit = vi.fn<(person: Person) => void>();
  render(DataTable<Person>, {
    props: { columns, data },
    slots: {
      actions: ({ row }: { row: Person }) =>
        h('button', { type: 'button', onClick: () => edit(row) }, `Edit ${row.name}`),
    },
  });

  expect(screen.getByRole('columnheader', { name: t('dataTable.actions') })).toBeInTheDocument();
  const second = screen.getByRole('row', { name: /^second/ });
  const lastCell = within(second).getAllByRole('cell').at(-1);
  expect(lastCell).toContainElement(screen.getByRole('button', { name: 'Edit second' }));
  expect(second).toHaveClass('group');
  expect(lastCell?.firstElementChild).toHaveClass(
    'opacity-0',
    'group-hover:opacity-100',
    'group-focus-within:opacity-100',
  );

  // Past the table's own controls (Comfortable, Compact, Columns), the first row's action is next.
  for (let control = 0; control < 4; control++) {
    await user.tab();
  }
  expect(screen.getByRole('button', { name: 'Edit first' })).toHaveFocus();
  await user.keyboard('{Enter}');
  expect(edit).toHaveBeenCalledWith(data[0]);
});

test('with a page of a longer list the table says which rows it shows of how many, and screen readers hear it with the table', () => {
  render(DataTable<Person>, { props: { columns, data, paging: { page: 2, pageSize: '2', totalCount: '312' } } });

  expect(screen.getByText(summaryOf('3', '4', '312'))).toBeInTheDocument();
  expect(screen.getByRole('table')).toHaveAccessibleDescription(summaryOf('3', '4', '312'));
});

test('the table says nothing of its page while it loads or when it has no rows', async () => {
  const paging = { page: 1, pageSize: 20, totalCount: 0 };
  const { rerender } = render(DataTable<Person>, { props: { columns, data: none, loading: true, paging } });
  expect(screen.queryByText(/\d+–\d+/)).not.toBeInTheDocument();

  await rerender({ columns, data: none, loading: false, paging });
  expect(screen.queryByText(/\d+–\d+/)).not.toBeInTheDocument();
  expect(screen.getByRole('table')).not.toHaveAttribute('aria-describedby');
});

// A full page of a long list: the numbers in the summary are formatted for the language, as n(..., 'integer') does.
const fullPage: Person[] = Array.from({ length: 25 }, (_, index) => ({ name: `person ${index + 1}`, team: 'blue' }));
const longList = { page: 1, pageSize: 25, totalCount: 1312 };

test('in German the table says which rows it shows of how many, with German digit grouping', () => {
  render(DataTable<Person>, { props: { columns, data: fullPage, paging: longList } });

  expect(screen.getByText(summaryOf('1', '25', '1.312'))).toBeInTheDocument();
  expect(screen.getByText('Einträge 1–25 von 1.312')).toBeInTheDocument();
  expect(screen.getByRole('table')).toHaveAccessibleDescription('Einträge 1–25 von 1.312');
});

test('in English the table says which rows it shows of how many, with English digit grouping', () => {
  setLanguage('en-GB', { remember: false });
  render(DataTable<Person>, { props: { columns, data: fullPage, paging: longList } });

  expect(screen.getByText(summaryOf('1', '25', '1,312'))).toBeInTheDocument();
  expect(screen.getByText('Showing 1–25 of 1,312')).toBeInTheDocument();
});

test("the table's own controls speak German by default", async () => {
  const user = userEvent.setup();
  render(DataTable<Person>, {
    props: { columns, data },
    slots: { actions: ({ row }: { row: Person }) => h('button', { type: 'button' }, `Edit ${row.name}`) },
  });

  const density = screen.getByRole('group', { name: 'Zeilenabstand' });
  expect(within(density).getByRole('button', { name: 'Normal' })).toBeInTheDocument();
  expect(within(density).getByRole('button', { name: 'Kompakt' })).toBeInTheDocument();
  expect(screen.getByRole('columnheader', { name: 'Aktionen' })).toBeInTheDocument();
  await user.click(screen.getByRole('button', { name: 'Spalten' }));
  expect(screen.getByRole('group', { name: 'Sichtbare Spalten' })).toBeInTheDocument();
});

test('while its query is pending the table says so in German', () => {
  render(DataTable<Person>, { props: { columns, data: none, loading: true } });

  expect(screen.getByRole('table', { name: 'Wird geladen…', busy: true })).toBeInTheDocument();
});

test('the table follows a change of language while it is shown', async () => {
  render(DataTable<Person>, { props: { columns, data: fullPage, paging: longList } });
  const german = { density: t('dataTable.density'), summary: summaryOf('1', '25', '1.312') };

  setLanguage('en-GB', { remember: false });

  expect(await screen.findByText('Showing 1–25 of 1,312')).toBeInTheDocument();
  expect(screen.getByRole('group', { name: t('dataTable.density') })).toBeInTheDocument();
  expect(screen.queryByRole('group', { name: german.density })).not.toBeInTheDocument();
  expect(screen.queryByText(german.summary)).not.toBeInTheDocument();
});

// The pager: given the page its rows are of and a list longer than a page, the table pages through it below its rows.
// It does not page itself: it asks for the page with update:page, and the list's owner (the page's query, its address)
// answers with that page's rows.
const twentyRows: Person[] = Array.from({ length: 20 }, (_, index) => ({ name: `person ${index + 21}`, team: 'blue' }));
const pager = () => screen.getByRole('navigation', { name: t('dataTable.pagination') });
const previous = () => within(pager()).getByRole('button', { name: t('dataTable.previous') });
const next = () => within(pager()).getByRole('button', { name: t('dataTable.next') });

test('on a middle page of a longer list the pager goes back and forth, asking for the page by its number', async () => {
  const user = userEvent.setup();
  const { emitted } = render(DataTable<Person>, {
    props: { columns, data: twentyRows, paging: { page: 2, pageSize: 20, totalCount: 45 } },
  });

  expect(previous()).toBeEnabled();
  expect(next()).toBeEnabled();
  await user.click(next());
  expect(emitted()['update:page']).toEqual([[3]]);

  await user.click(previous());
  expect(emitted()['update:page']).toEqual([[3], [1]]);
});

test('the pager takes the numbers as the host sends them, as strings too', async () => {
  const user = userEvent.setup();
  const { emitted } = render(DataTable<Person>, {
    props: { columns, data: twentyRows, paging: { page: '2', pageSize: '20', totalCount: '45' } },
  });

  await user.click(next());

  expect(emitted()['update:page']).toEqual([[3]]);
});

test('on the first page the pager cannot go back, on the last it cannot go on, and one page has none', async () => {
  const { rerender } = render(DataTable<Person>, {
    props: { columns, data: twentyRows, paging: { page: 1, pageSize: 20, totalCount: 45 } },
  });
  expect(previous()).toBeDisabled();
  expect(next()).toBeEnabled();

  await rerender({ columns, data, paging: { page: 3, pageSize: 20, totalCount: 45 } });

  expect(previous()).toBeEnabled();
  expect(next()).toBeDisabled();

  // A list that fits on one page has no pager.
  await rerender({ columns, data, paging: { page: 1, pageSize: 20, totalCount: 2 } });

  expect(screen.getByRole('table')).toBeInTheDocument();
  expect(screen.queryByRole('navigation')).not.toBeInTheDocument();
  expect(screen.queryByRole('button', { name: t('dataTable.next') })).not.toBeInTheDocument();
});

test('the pager speaks German by default and follows a change of language', async () => {
  render(DataTable<Person>, {
    props: { columns, data: twentyRows, paging: { page: 2, pageSize: 20, totalCount: 45 } },
  });

  const german = screen.getByRole('navigation', { name: 'Seiten' });
  expect(within(german).getByRole('button', { name: 'Vorherige Seite' })).toBeEnabled();
  expect(within(german).getByRole('button', { name: 'Nächste Seite' })).toBeEnabled();

  setLanguage('en-GB', { remember: false });

  const english = await screen.findByRole('navigation', { name: 'Pages' });
  expect(within(english).getByRole('button', { name: 'Previous page' })).toBeInTheDocument();
  expect(within(english).getByRole('button', { name: 'Next page' })).toBeInTheDocument();
});
