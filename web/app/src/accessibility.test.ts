import { render } from '@testing-library/vue';
import { expect, test, vi } from 'vitest';
import { axe } from 'vitest-axe';
import { createPinia } from 'pinia';
import { createMemoryHistory } from 'vue-router';
import { signedInAsDeveloper } from './auth/signedInAsDeveloper';
import App from './App.vue';
import { createAppRouter } from './router';

// The user menu has its own test; here it stands in, so this test checks the start page.
vi.mock('@/auth/UserMenu.vue', async () => {
  const { h } = await import('vue');
  return { default: { render: () => h('p', 'The user menu') } };
});

// The shell asks the host for the user's pharmacies; here the user belongs to one, at once.
vi.mock('@/shell/useMyPharmacies', async () => {
  const { ref } = await import('vue');
  const pharmacy = {
    id: '0198c1a0-0000-7000-8000-0000000000a1',
    name: 'Apotheke am Markt',
    membershipId: '0198c1a0-0000-7000-8000-0000000000b1',
    role: 'Manager',
    jobFunction: 'Pharmacist',
    canManageMembers: true,
    canAssignCourses: true,
  };
  return {
    useMyPharmacies: () => ({
      data: ref({ items: [pharmacy], totalCount: 1, page: 1, pageSize: 100 }),
      isPending: ref(false),
      isError: ref(false),
      refetch: () => Promise.resolve(),
    }),
  };
});

test('the start page has no accessibility violations axe can find', async () => {
  const router = createAppRouter(createMemoryHistory());
  await router.push('/');

  const { container } = render(App, { global: { plugins: [router, createPinia(), signedInAsDeveloper] } });

  expect(await axe(container)).toHaveNoViolations();
});
