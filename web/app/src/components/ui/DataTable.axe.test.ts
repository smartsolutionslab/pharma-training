import { Inbox } from '@lucide/vue';
import { render, screen } from '@testing-library/vue';
import userEvent from '@testing-library/user-event';
import { afterEach, beforeEach, describe, expect, test } from 'vitest';
import { axe } from 'vitest-axe';
import { h } from 'vue';
import { languages, setLanguage, t } from '@/i18n';
import DataTable from './DataTable.vue';
import { columnsOf } from './data-table-columns';
import { EmptyState } from './empty-state';

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
const empty = () =>
  h(EmptyState, null, {
    icon: () => h(Inbox),
    default: () => 'No people yet.',
    action: () => h('button', { type: 'button' }, 'Add a person'),
  });

afterEach(() => {
  document.documentElement.removeAttribute('class');
  localStorage.clear();
});

const themes = [
  { name: 'light', switchOn: () => undefined },
  {
    name: 'dark',
    // As the theme toggle does: the choice stored, the class on <html>.
    switchOn: () => {
      localStorage.setItem('theme', 'dark');
      document.documentElement.classList.add('dark');
    },
  },
];

const states = [
  { state: 'while it loads', props: { columns, data: none, loading: true } },
  { state: 'when it is empty', props: { columns, data: none } },
  { state: 'with rows', props: { columns, data } },
  {
    state: 'with actions, a page and columns open',
    props: { columns, data, paging: { page: 1, pageSize: 25, totalCount: 312 } },
    open: true,
  },
  {
    state: 'with a pager on a middle page',
    props: { columns, data, paging: { page: 2, pageSize: 2, totalCount: 45 } },
    pager: true,
  },
];
const actions = ({ row }: { row: Person }) => h('button', { type: 'button' }, `Edit ${row.name}`);

// Each language in turn: the texts differ, and so may what axe finds in them.
describe.each(languages)('in %s', (language) => {
  beforeEach(() => {
    setLanguage(language, { remember: false });
  });

  // Whichever test renders first in a file also pays for warming up jsdom, Vue and axe.
  test.each(
    states.flatMap(({ state, props, open = false, pager = false }) =>
      themes.map(({ name, switchOn }) => ({ state, props, open, pager, name, switchOn })),
    ),
  )(
    'the table has no accessibility violations axe can find $state in the $name theme',
    { timeout: 20_000 },
    async ({ props, open, pager, switchOn }) => {
      switchOn();
      // A generic component is rendered for one row type, as a template does it from its props.
      const { container } = render(DataTable<Person>, { props, slots: open ? { empty, actions } : { empty } });
      // The table axe checks is the one in this language.
      expect(screen.getByRole('group', { name: t('dataTable.density') })).toBeInTheDocument();
      // The pager axe checks is there, in this language.
      if (pager) {
        expect(screen.getByRole('navigation', { name: t('dataTable.pagination') })).toBeInTheDocument();
      }
      if (open) {
        await userEvent.click(screen.getByRole('button', { name: t('dataTable.columns') }));
      }

      expect(await axe(container)).toHaveNoViolations();
    },
  );
});
