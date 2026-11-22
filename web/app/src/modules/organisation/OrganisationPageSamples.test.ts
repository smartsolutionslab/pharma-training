import { cleanup, render, screen, waitFor } from '@testing-library/vue';
import userEvent from '@testing-library/user-event';
import { afterEach, beforeEach, expect, test, vi } from 'vitest';
import { i18n, setLanguage, t } from '@/i18n';
import OrganisationPage from './OrganisationPage.vue';
import { route } from './routes';

// The page's data comes through useOrganisationSamples, whose own test reads the host and the optimistic update; here the
// samples, and what creating one answers, are the test's.
const samples = vi.hoisted(() => ({
  list: {
    samples: undefined as { id: string; name: string }[] | undefined,
    page: undefined as { totalCount: number; page: number; pageSize: number } | undefined,
    isLoading: false,
    isError: false,
  },
  create: vi.fn<(name: string) => Promise<boolean>>(),
}));
vi.mock('./useOrganisationSamples', () => ({
  useOrganisationSamples: () => samples.list,
  useCreateOrganisationSample: () => ({ create: samples.create, isSaving: false }),
}));
vi.mock('./useCanManage', () => ({ useCanManage: () => 'refused' }));
const notify = vi.hoisted(() => ({ success: vi.fn(), failure: vi.fn() }));
vi.mock('@/lib/notify', () => ({ notify }));

const ada = { id: '0198c1a0-0000-7000-8000-000000000001', name: 'Ada' };
const bea = { id: '0198c1a0-0000-7000-8000-000000000002', name: 'Bea' };

beforeEach(() => {
  samples.list = { samples: [ada, bea], page: undefined, isLoading: false, isError: false };
  samples.create.mockReset();
  notify.success.mockReset();
  notify.failure.mockReset();
});
// Without Vitest's globals, Testing Library does not unmount between tests by itself.
afterEach(cleanup);

const nameField = () => screen.getByRole('textbox', { name: t('samples.form.name') });
const submit = () => screen.getByRole('button', { name: t('samples.form.submit') });

test('the page is headed by its title in the language of the moment, and names its samples section', () => {
  render(OrganisationPage);

  expect(screen.getByRole('heading', { level: 1, name: t(route.titleKey) })).toBeInTheDocument();
  expect(screen.getByRole('region', { name: t('samples.title') })).toBeInTheDocument();
  expect(screen.getByRole('form', { name: t('samples.form.label') })).toBeInTheDocument();
  expect(screen.getByRole('columnheader', { name: t('samples.form.name') })).toBeInTheDocument();
});

test('the page lists the samples by name', async () => {
  render(OrganisationPage);

  expect(await screen.findByText('Ada')).toBeInTheDocument();
  expect(await screen.findByText('Bea')).toBeInTheDocument();
  expect(screen.queryByText(t('samples.empty'))).not.toBeInTheDocument();
});

test('below the samples the page says which of how many it shows, from the page the host answered', () => {
  samples.list = {
    samples: [ada, bea],
    page: { totalCount: 42, page: 1, pageSize: 20 },
    isLoading: false,
    isError: false,
  };
  render(OrganisationPage);

  const { n } = i18n.global;
  expect(screen.getByRole('table')).toHaveAccessibleDescription(
    t('dataTable.showing', { first: n(1, 'integer'), last: n(2, 'integer'), total: n(42, 'integer') }),
  );
});

test('while the samples load the page shows skeleton rows', () => {
  samples.list = { samples: undefined, page: undefined, isLoading: true, isError: false };

  const { container } = render(OrganisationPage);

  expect(container.querySelector('[aria-busy="true"]')).not.toBeNull();
  expect(container.querySelector('[data-slot="skeleton"]')).not.toBeNull();
  expect(screen.getByText(t('dataTable.loading'))).toBeInTheDocument();
  expect(screen.queryByText(t('samples.empty'))).not.toBeInTheDocument();
});

test('samples that cannot be loaded are reported', () => {
  samples.list = { samples: undefined, page: undefined, isLoading: false, isError: true };
  render(OrganisationPage);

  expect(screen.getByRole('alert')).toHaveTextContent(t('samples.loadFailed'));
});

test('without samples the page says so and its action leads to the form', async () => {
  samples.list = { samples: [], page: undefined, isLoading: false, isError: false };
  render(OrganisationPage);

  expect(screen.getByText(t('samples.empty'))).toBeVisible();
  await userEvent.click(screen.getByRole('button', { name: t('samples.createFirst') }));

  expect(nameField()).toHaveFocus();
});

test('a created sample is reported', async () => {
  samples.create.mockResolvedValue(true);
  render(OrganisationPage);

  await userEvent.type(nameField(), 'Cleo');
  await userEvent.click(submit());

  await waitFor(() => expect(notify.success).toHaveBeenCalledWith(t('samples.created', { name: 'Cleo' })));
  expect(samples.create).toHaveBeenCalledWith('Cleo');
  await waitFor(() => expect(nameField()).toHaveValue(''));
});

test('a sample the server refuses is reported as a failure', async () => {
  samples.create.mockResolvedValue(false);
  render(OrganisationPage);

  await userEvent.type(nameField(), 'Cleo');
  await userEvent.click(submit());

  await waitFor(() => expect(notify.failure).toHaveBeenCalledWith(t('samples.createFailed', { name: 'Cleo' })));
  expect(nameField()).toHaveValue('Cleo');
});

test('a blank name is refused before anything is sent', async () => {
  render(OrganisationPage);

  await userEvent.type(nameField(), '   ');
  await userEvent.click(submit());

  expect(await screen.findByRole('alert')).toHaveTextContent(t('samples.form.nameRequired'));
  expect(samples.create).not.toHaveBeenCalled();
});

test('a name of 101 characters is refused before anything is sent', async () => {
  render(OrganisationPage);

  await userEvent.click(nameField());
  await userEvent.paste('A'.repeat(101));
  await userEvent.click(submit());

  expect(await screen.findByRole('alert')).toHaveTextContent(t('samples.form.nameTooLong'));
  expect(samples.create).not.toHaveBeenCalled();
});

// The page follows a switch of the language while it is shown, and zod's messages are resolved when the form
// validates, not when the module loads: a refusal shown after the switch speaks the new language.
test('switched to English, the page, its form and its refusals speak English', async () => {
  render(OrganisationPage);
  expect(screen.getByRole('heading', { level: 1, name: t(route.titleKey) })).toBeInTheDocument();

  setLanguage('en-GB', { remember: false });

  expect(await screen.findByRole('heading', { level: 1, name: t(route.titleKey) })).toBeInTheDocument();
  expect(screen.getByRole('heading', { level: 2, name: t('samples.title') })).toBeInTheDocument();
  await userEvent.click(submit());
  expect(await screen.findByRole('alert')).toHaveTextContent(t('samples.form.nameRequired'));
  expect(t('samples.form.nameRequired')).not.toBe(i18n.global.t('samples.form.nameRequired', {}, { locale: 'de-DE' }));
});
