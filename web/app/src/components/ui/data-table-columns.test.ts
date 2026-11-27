import { render, screen, within } from '@testing-library/vue';
import userEvent from '@testing-library/user-event';
import { expect, test } from 'vitest';
import { setLanguage, t } from '@/i18n';
import DataTable from './DataTable.vue';
import { columnsOf, translatedHeader } from './data-table-columns';

interface Member {
  name: string;
  pharmacy: string;
}

// Headers from the catalogs: translatedHeader(key) gives the column its header and the Columns control its label, each
// in the language shown.
const members = columnsOf<Member>();
const columns = members.columns([
  members.accessor('name', translatedHeader('samples.form.name')),
  members.accessor('pharmacy', translatedHeader('shell.pharmacy.region')),
]);
const data: Member[] = [{ name: 'Anna', pharmacy: 'Apotheke am Markt' }];
const rowId = (row: Member) => row.name;

test('translatedHeader gives a column its header from the catalog and keeps the key for the Columns control', () => {
  const column = translatedHeader('shell.pharmacy.region');

  expect(column.meta).toEqual({ labelKey: 'shell.pharmacy.region' });
  expect(column.header()).toBe(t('shell.pharmacy.region'));
  expect(column.header()).toBe('Apotheke');
});

test('a translated column is headed in the language shown', () => {
  render(DataTable<Member>, { props: { columns, rowId, data } });

  expect(screen.getByRole('columnheader', { name: t('shell.pharmacy.region') })).toBeInTheDocument();
  expect(screen.getByRole('columnheader', { name: 'Apotheke' })).toBeInTheDocument();
});

test('the Columns control names a translated column as its header does', async () => {
  const user = userEvent.setup();
  render(DataTable<Member>, { props: { columns, rowId, data } });

  await user.click(screen.getByRole('button', { name: t('dataTable.columns') }));

  const shown = within(screen.getByRole('group', { name: t('dataTable.visibleColumns') }));
  expect(shown.getByRole('checkbox', { name: t('samples.form.name') })).toBeChecked();
  expect(shown.getByRole('checkbox', { name: t('shell.pharmacy.region') })).toBeChecked();
  expect(shown.getByRole('checkbox', { name: 'Apotheke' })).toBeInTheDocument();
  expect(shown.queryByRole('checkbox', { name: 'pharmacy' })).not.toBeInTheDocument();
});

test('a translated column follows a change of language, in its header and in the Columns control', async () => {
  const user = userEvent.setup();
  render(DataTable<Member>, { props: { columns, rowId, data } });

  setLanguage('en-GB', { remember: false });

  expect(await screen.findByRole('columnheader', { name: 'Pharmacy' })).toBeInTheDocument();
  expect(screen.queryByRole('columnheader', { name: 'Apotheke' })).not.toBeInTheDocument();
  await user.click(screen.getByRole('button', { name: t('dataTable.columns') }));
  const shown = within(screen.getByRole('group', { name: t('dataTable.visibleColumns') }));
  expect(shown.getByRole('checkbox', { name: 'Pharmacy' })).toBeInTheDocument();
});
