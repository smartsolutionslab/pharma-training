import { cleanup, render, screen, within } from '@testing-library/vue';
import userEvent from '@testing-library/user-event';
import { createPinia } from 'pinia';
import { afterEach, beforeEach, expect, test, vi } from 'vitest';
import type { MyPharmacyDto, PagedResultOfMyPharmacyDto } from '@/api/generated';
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

const renderHome = () => render(Home, { global: { plugins: [createPinia()] } });

const prompt = () => screen.queryByRole('heading', { name: 'Choose your pharmacy' });

test('with several pharmacies and none chosen the start page asks the user to choose one', async () => {
  const user = userEvent.setup();
  answer([markt, dom]);
  renderHome();

  // A section labelled by its heading, so a screen reader lists it as a region.
  const choices = within(await screen.findByRole('region', { name: 'Choose your pharmacy' }));
  expect(choices.getByRole('heading', { name: 'Choose your pharmacy' })).toBeInTheDocument();
  expect(choices.getAllByRole('button').map((button) => button.textContent.trim())).toEqual([
    expect.stringContaining('Apotheke am Markt'),
    expect.stringContaining('Apotheke am Dom'),
  ]);

  await user.click(choices.getByRole('button', { name: /Apotheke am Dom/ }));

  await vi.waitFor(() => expect(prompt()).not.toBeInTheDocument());
  expect(localStorage.getItem('pharmacy')).toBe(dom.id);
  expect(screen.getByRole('heading', { name: 'pharma-training', level: 1 })).toBeInTheDocument();
});

// The prompt is there only while the user has several pharmacies and none chosen; it asks nothing otherwise.
test('the start page stops asking once the memberships no longer call for a choice', async () => {
  answer([markt, dom]);
  renderHome();
  expect(await screen.findByRole('heading', { name: 'Choose your pharmacy' })).toBeInTheDocument();

  answer([dom]);
  await vi.waitFor(() => expect(prompt()).not.toBeInTheDocument());

  answer([markt, dom]);
  expect(await screen.findByRole('heading', { name: 'Choose your pharmacy' })).toBeInTheDocument();
  answer([]);
  await vi.waitFor(() => expect(prompt()).not.toBeInTheDocument());

  answer([markt, dom]);
  expect(await screen.findByRole('heading', { name: 'Choose your pharmacy' })).toBeInTheDocument();
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

  expect(await within(unchosen.container).findByRole('heading', { name: 'Choose your pharmacy' })).toBeInTheDocument();
  expect(within(chosen.container).queryByRole('heading', { name: 'Choose your pharmacy' })).not.toBeInTheDocument();
});
