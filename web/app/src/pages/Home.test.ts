import { cleanup, render, screen, within } from '@testing-library/vue';
import userEvent from '@testing-library/user-event';
import { createPinia } from 'pinia';
import { afterEach, beforeEach, expect, test, vi } from 'vitest';
import { createMemoryHistory } from 'vue-router';
import type { MyPharmacyDto, PagedResultOfMyPharmacyDto } from '@/api/generated';
import { setLanguage, t } from '@/i18n';
import { createAppRouter } from '@/router';
import Home from './Home.vue';

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
vi.mock('@/shell/useMyPharmacies', () => ({ useMyPharmacies: () => pharmacies }));

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
});
afterEach(() => {
  cleanup();
  localStorage.clear();
});

let router = createAppRouter(createMemoryHistory());
const renderHome = () => {
  router = createAppRouter(createMemoryHistory());
  return render(Home, { global: { plugins: [router, createPinia()] } });
};

const prompt = () => screen.queryByRole('heading', { name: t('home.choosePharmacy') });

test('with several pharmacies and none chosen the start page asks the user to choose one', async () => {
  const user = userEvent.setup();
  answer([markt, dom]);
  renderHome();

  // A section labelled by its heading, so a screen reader lists it as a region.
  const choices = within(await screen.findByRole('region', { name: t('home.choosePharmacy') }));
  expect(choices.getByRole('heading', { name: t('home.choosePharmacy') })).toBeInTheDocument();
  expect(choices.getAllByRole('button').map((button) => button.textContent.trim())).toEqual([
    expect.stringContaining('Apotheke am Markt'),
    expect.stringContaining('Apotheke am Dom'),
  ]);
  // Each with the role it has there, as the glossary names it.
  expect(choices.getByRole('button', { name: /Apotheke am Markt/ })).toHaveTextContent(
    t('glossary.membershipRole.Manager'),
  );
  expect(choices.getByRole('button', { name: /Apotheke am Dom/ })).toHaveTextContent(
    t('glossary.membershipRole.Employee'),
  );

  await user.click(choices.getByRole('button', { name: /Apotheke am Dom/ }));

  await vi.waitFor(() => expect(prompt()).not.toBeInTheDocument());
  expect(localStorage.getItem('pharmacy')).toBe(dom.id);
  expect(screen.getByRole('heading', { name: t('app.name'), level: 1 })).toBeInTheDocument();
});

// The prompt is there only while the user has several pharmacies and none chosen; it asks nothing otherwise.
test('the start page stops asking once the memberships no longer call for a choice', async () => {
  answer([markt, dom]);
  renderHome();
  expect(await screen.findByRole('heading', { name: t('home.choosePharmacy') })).toBeInTheDocument();

  answer([dom]);
  await vi.waitFor(() => expect(prompt()).not.toBeInTheDocument());

  answer([markt, dom]);
  expect(await screen.findByRole('heading', { name: t('home.choosePharmacy') })).toBeInTheDocument();
  answer([]);
  await vi.waitFor(() => expect(prompt()).not.toBeInTheDocument());

  answer([markt, dom]);
  expect(await screen.findByRole('heading', { name: t('home.choosePharmacy') })).toBeInTheDocument();
  pharmacies.data.value = undefined;
  pharmacies.isPending.value = true;
  await vi.waitFor(() => expect(prompt()).not.toBeInTheDocument());
});

test('a pharmacy chosen on an earlier visit is not asked for again, where a visit without one is', async () => {
  answer([markt, dom]);
  localStorage.setItem('pharmacy', markt.id);
  const chosen = renderHome();
  localStorage.removeItem('pharmacy');
  const unchosen = renderHome();

  expect(
    await within(unchosen.container as HTMLElement).findByRole('heading', { name: t('home.choosePharmacy') }),
  ).toBeInTheDocument();
  expect(
    within(chosen.container as HTMLElement).queryByRole('heading', { name: t('home.choosePharmacy') }),
  ).not.toBeInTheDocument();
});

// The chosen button goes away with the prompt; focus moves to the page's heading rather than falling to the body.
test('after choosing a pharmacy the focus is on the start page heading, not lost', async () => {
  const user = userEvent.setup();
  answer([markt, dom]);
  renderHome();
  const choices = within(await screen.findByRole('region', { name: t('home.choosePharmacy') }));

  await user.click(choices.getByRole('button', { name: /Apotheke am Markt/ }));

  await vi.waitFor(() => expect(prompt()).not.toBeInTheDocument());
  await vi.waitFor(() => expect(screen.getByRole('heading', { name: t('app.name'), level: 1 })).toHaveFocus());
  expect(document.activeElement).not.toBe(document.body);
});

const noPharmacy = () => screen.queryByRole('region', { name: t('home.noPharmacy.title') });

// A user who belongs to no pharmacy learns the two ways in: registering their own, or an invitation from its manager.
test('a user of no pharmacy is led to register one, or to ask for an invitation', async () => {
  const user = userEvent.setup();
  answer([]);
  renderHome();

  const section = within(await screen.findByRole('region', { name: t('home.noPharmacy.title') }));
  expect(section.getByRole('heading', { name: t('home.noPharmacy.title') })).toBeInTheDocument();
  expect(section.getByText(t('home.noPharmacy.text'))).toBeInTheDocument();
  const link = section.getByRole('link', { name: t('shell.pharmacy.register') });
  expect(link).toHaveAttribute('href', '/organisation/register');

  await user.click(link);

  await vi.waitFor(() => expect(router.currentRoute.value.path).toBe('/organisation/register'));
});

// Until the memberships are known to be none (still loading, failed to load, or some), nobody is told they have none.
test.each([
  ['load', () => undefined],
  [
    'could not be loaded',
    () => {
      pharmacies.isPending.value = false;
      pharmacies.isError.value = true;
    },
  ],
  ['are one pharmacy', () => answer([dom])],
  ['are several pharmacies', () => answer([markt, dom])],
])('while the memberships %s the start page does not say there are none, until there are', async (_, before) => {
  before();
  renderHome();
  expect(noPharmacy()).not.toBeInTheDocument();
  expect(screen.queryByRole('link', { name: t('shell.pharmacy.register') })).not.toBeInTheDocument();

  answer([]);

  expect(await screen.findByRole('region', { name: t('home.noPharmacy.title') })).toBeInTheDocument();
});

test('the start page speaks German by default', async () => {
  answer([markt, dom]);
  const { unmount } = renderHome();

  const choices = within(await screen.findByRole('region', { name: 'Wählen Sie Ihre Apotheke' }));
  expect(choices.getByRole('button', { name: /Apotheke am Markt/ })).toHaveTextContent('Apothekenleiter');
  expect(choices.getByRole('button', { name: /Apotheke am Dom/ })).toHaveTextContent('Mitarbeiter');
  unmount();

  answer([]);
  renderHome();
  const section = within(await screen.findByRole('region', { name: 'Noch keine Apotheke' }));
  expect(
    section.getByText(
      'Sie sind noch in keiner Apotheke Mitglied. Registrieren Sie Ihre Apotheke, oder bitten Sie deren Apothekenleiter um eine Einladung.',
    ),
  ).toBeInTheDocument();
  expect(section.getByRole('link', { name: 'Apotheke registrieren' })).toHaveAttribute(
    'href',
    '/organisation/register',
  );
});

test('in English the start page asks for a pharmacy in English and names the roles as the glossary does', async () => {
  setLanguage('en-GB', { remember: false });
  answer([markt, dom]);
  renderHome();

  const choices = within(await screen.findByRole('region', { name: 'Choose your pharmacy' }));
  expect(choices.getByRole('button', { name: /Apotheke am Markt/ })).toHaveTextContent('Pharmacy manager');
  expect(choices.getByRole('button', { name: /Apotheke am Dom/ })).toHaveTextContent('Employee');
});
