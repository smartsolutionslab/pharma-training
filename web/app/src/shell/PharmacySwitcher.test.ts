import { cleanup, render, screen, within } from '@testing-library/vue';
import userEvent from '@testing-library/user-event';
import { createPinia } from 'pinia';
import { afterEach, beforeEach, expect, test, vi } from 'vitest';
import { defineComponent, h } from 'vue';
import { createMemoryHistory } from 'vue-router';
import type { MyPharmacyDto, PagedResultOfMyPharmacyDto } from '@/api/generated';
import { SidebarProvider } from '@/components/ui/sidebar';
import { setLanguage, t } from '@/i18n';
import { createAppRouter } from '@/router';
import PharmacySwitcher from './PharmacySwitcher.vue';

// GET /organisation/pharmacies/mine as useMyPharmacies answers it: TanStack Query's refs, which a test sets.
const pharmacies = await vi.hoisted(async () => {
  const { ref } = await import('vue');
  return {
    data: ref<PagedResultOfMyPharmacyDto | undefined>(undefined),
    isPending: ref(true),
    isError: ref(false),
    refetch: vi.fn(),
  };
});
vi.mock('./useMyPharmacies', () => ({ useMyPharmacies: () => pharmacies }));

const markt: MyPharmacyDto = {
  id: '0198c1a0-0000-7000-8000-0000000000a1',
  name: 'Apotheke am Markt',
  membershipId: '0198c1a0-0000-7000-8000-0000000000b1',
  role: 'Manager',
  jobFunction: 'Pharmacist',
  canManageMembers: true,
  canAssignCourses: true,
};
const dom: MyPharmacyDto = {
  id: '0198c1a0-0000-7000-8000-0000000000a2',
  name: 'Apotheke am Dom',
  membershipId: '0198c1a0-0000-7000-8000-0000000000b2',
  role: 'Employee',
  jobFunction: 'Pta',
  canManageMembers: false,
  canAssignCourses: false,
};

function answer(items: MyPharmacyDto[]) {
  pharmacies.data.value = { items, totalCount: items.length, page: 1, pageSize: 100 };
  pharmacies.isPending.value = false;
  pharmacies.isError.value = false;
}

beforeEach(() => {
  localStorage.clear();
  pharmacies.data.value = undefined;
  pharmacies.isPending.value = true;
  pharmacies.isError.value = false;
  pharmacies.refetch.mockReset();
});
afterEach(() => {
  cleanup();
  localStorage.clear();
});

// The switcher sits in the sidebar's header, so it renders inside the sidebar's provider; each render is a page loaded
// afresh, with a new Pinia, on the start page.
const InSidebar = defineComponent({ render: () => h(SidebarProvider, null, () => h(PharmacySwitcher)) });
let router = createAppRouter(createMemoryHistory());
const current = (name: string) => t('shell.pharmacy.current', { name });
const manager = () => t('glossary.membershipRole.Manager');
const employee = () => t('glossary.membershipRole.Employee');

const renderSwitcher = () => {
  router = createAppRouter(createMemoryHistory());
  return render(InSidebar, { global: { plugins: [router, createPinia()] } });
};

test('with several pharmacies the switcher names the chosen one and its role', () => {
  localStorage.setItem('pharmacy', markt.id);
  answer([markt, dom]);

  renderSwitcher();

  const trigger = screen.getByRole('button', { name: current('Apotheke am Markt') });
  expect(trigger).toHaveTextContent(manager());
});

test('the user switches to another pharmacy, and it stays chosen after a reload', async () => {
  const user = userEvent.setup();
  localStorage.setItem('pharmacy', markt.id);
  answer([markt, dom]);
  const { unmount } = renderSwitcher();

  await user.click(screen.getByRole('button', { name: current('Apotheke am Markt') }));
  const menu = await screen.findByRole('menu', { name: t('shell.pharmacy.list') });
  expect(within(menu).getByRole('menuitemradio', { name: /Apotheke am Markt/ })).toHaveAttribute(
    'aria-checked',
    'true',
  );
  expect(within(menu).getByRole('menuitemradio', { name: /Apotheke am Dom/ })).toHaveAttribute('aria-checked', 'false');
  await user.click(within(menu).getByRole('menuitemradio', { name: /Apotheke am Dom/ }));

  const switched = await screen.findByRole('button', { name: current('Apotheke am Dom') });
  expect(switched).toHaveTextContent(employee());
  await vi.waitFor(() => expect(screen.queryByRole('menu')).not.toBeInTheDocument());
  unmount();

  renderSwitcher();
  expect(screen.getByRole('button', { name: current('Apotheke am Dom') })).toBeInTheDocument();
});

test('with several pharmacies and none chosen the switcher asks for one', async () => {
  const user = userEvent.setup();
  answer([markt, dom]);
  renderSwitcher();

  await user.click(screen.getByRole('button', { name: t('shell.pharmacy.choose') }));
  const menu = await screen.findByRole('menu', { name: t('shell.pharmacy.list') });
  await user.click(within(menu).getByRole('menuitemradio', { name: /Apotheke am Markt/ }));

  expect(await screen.findByRole('button', { name: current('Apotheke am Markt') })).toBeInTheDocument();
});

test('with one pharmacy the switcher names it and its role, with nothing to switch', () => {
  answer([dom]);

  renderSwitcher();

  expect(screen.getByText('Apotheke am Dom')).toBeInTheDocument();
  expect(screen.getByText(employee())).toBeInTheDocument();
  expect(screen.queryByRole('button')).not.toBeInTheDocument();
});

test('without a pharmacy the switcher says so', () => {
  answer([]);

  renderSwitcher();

  expect(screen.getByText(t('shell.pharmacy.none'))).toBeInTheDocument();
  expect(screen.queryByRole('button')).not.toBeInTheDocument();
});

test('while the pharmacies load the switcher is a skeleton', () => {
  const { container } = renderSwitcher();

  expect(container.querySelector('[data-slot="skeleton"]')).not.toBeNull();
  expect(screen.queryByRole('button')).not.toBeInTheDocument();
  expect(screen.queryByRole('alert')).not.toBeInTheDocument();
});

test('when the pharmacies could not be loaded the switcher says so and tries again', async () => {
  const user = userEvent.setup();
  pharmacies.isPending.value = false;
  pharmacies.isError.value = true;
  renderSwitcher();

  expect(screen.getByRole('alert')).toHaveTextContent(t('shell.pharmacy.failed'));
  await user.click(screen.getByRole('button', { name: t('shell.retry') }));

  expect(pharmacies.refetch).toHaveBeenCalledOnce();
});

const pharmacyRegion = () => within(screen.getByRole('region', { name: t('shell.pharmacy.region') }));

// Whatever the user's memberships, a new pharmacy is one link away, beside them and not inside the menu.
test.each([
  ['no pharmacy', []],
  ['one pharmacy', [dom]],
  ['several pharmacies', [markt, dom]],
])('with %s the switcher leads to the page that registers one', async (_, memberships: MyPharmacyDto[]) => {
  const user = userEvent.setup();
  answer(memberships);
  renderSwitcher();

  const link = pharmacyRegion().getByRole('link', { name: t('shell.pharmacy.register') });
  expect(link).toHaveAttribute('href', '/organisation/register');
  await user.click(link);

  await vi.waitFor(() => expect(router.currentRoute.value.path).toBe('/organisation/register'));
});

// Not while it is a skeleton, nor beside the alert that the pharmacies could not be loaded: only once they are known.
test.each([
  ['load', () => undefined],
  [
    'could not be loaded',
    () => {
      pharmacies.isPending.value = false;
      pharmacies.isError.value = true;
    },
  ],
])('while the pharmacies %s the switcher has no link to register one, until they are known', async (_, before) => {
  before();
  renderSwitcher();
  expect(pharmacyRegion().queryByRole('link', { name: t('shell.pharmacy.register') })).not.toBeInTheDocument();

  answer([]);

  expect(await pharmacyRegion().findByRole('link', { name: t('shell.pharmacy.register') })).toBeInTheDocument();
});

// The role is the API's enum value, shown through the glossary: in German as docs/domain/README.md names it.
test('the switcher speaks German by default and names the role as the glossary does', async () => {
  const user = userEvent.setup();
  localStorage.setItem('pharmacy', markt.id);
  answer([markt, dom]);
  renderSwitcher();

  const region = within(screen.getByRole('region', { name: 'Apotheke' }));
  const trigger = region.getByRole('button', { name: 'Apotheke: Apotheke am Markt' });
  expect(trigger).toHaveTextContent('Apothekenleiter');
  expect(trigger).not.toHaveTextContent('Manager');
  expect(region.getByRole('link', { name: 'Apotheke registrieren' })).toBeInTheDocument();

  await user.click(trigger);
  const menu = await screen.findByRole('menu', { name: 'Apotheken' });
  expect(within(menu).getByRole('menuitemradio', { name: /Apotheke am Dom/ })).toHaveTextContent('Mitarbeiter');
});

test('with one pharmacy the role is named in the language shown', () => {
  setLanguage('en-GB', { remember: false });
  answer([markt]);

  renderSwitcher();

  expect(screen.getByText('Pharmacy manager')).toBeInTheDocument();
  expect(screen.getByRole('region', { name: 'Pharmacy' })).toBeInTheDocument();
});

test('without a pharmacy, or when they could not be loaded, the switcher says so in German', async () => {
  answer([]);
  const { unmount } = renderSwitcher();
  expect(screen.getByText('Sie sind noch in keiner Apotheke Mitglied.')).toBeInTheDocument();
  unmount();

  pharmacies.isError.value = true;
  pharmacies.data.value = undefined;
  renderSwitcher();
  expect(await screen.findByRole('alert')).toHaveTextContent('Die Apotheken konnten nicht geladen werden.');
  expect(screen.getByRole('button', { name: 'Erneut versuchen' })).toBeInTheDocument();
});

test('with several pharmacies and none chosen the switcher asks for one in German', () => {
  answer([markt, dom]);

  renderSwitcher();

  expect(screen.getByRole('button', { name: 'Apotheke wählen' })).toHaveTextContent('Apotheke wählen');
});
