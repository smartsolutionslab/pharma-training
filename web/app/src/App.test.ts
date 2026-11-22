import userEvent from '@testing-library/user-event';
import { cleanup, render, screen, waitFor } from '@testing-library/vue';
import { afterEach, expect, test, vi } from 'vitest';
import { createPinia } from 'pinia';
import { createMemoryHistory } from 'vue-router';
import { toast } from 'vue-sonner';
import { setLanguage, t } from '@/i18n';
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

// Without Vitest's globals, Testing Library does not unmount between tests by itself.
afterEach(() => {
  cleanup();
  toast.dismiss();
  Reflect.deleteProperty(document, 'startViewTransition');
});

test('the start page names the application', async () => {
  const router = createAppRouter(createMemoryHistory());
  await router.push('/');

  render(App, { global: { plugins: [router, createPinia(), signedInAsDeveloper] } });

  expect(screen.getByRole('heading', { name: 'pharma-training' })).toBeInTheDocument();
});

/** The app's router, as src/providers/router.ts makes it, at an address the Home link leaves. */
async function renderAwayFromHome() {
  const router = createAppRouter(createMemoryHistory());
  await router.push('/?from=elsewhere');
  render(App, { global: { plugins: [router, createPinia(), signedInAsDeveloper] } });
  return router;
}

test('the Home link goes to the start page in a view transition where the browser has the API', async () => {
  // A stand-in for the browser's API: it runs the update at once, as a transition that is skipped does.
  const startViewTransition = vi.fn((update: () => unknown) => {
    const done = Promise.resolve(update()).then(() => undefined);
    return { finished: done, ready: done, updateCallbackDone: done, skipTransition: () => undefined };
  });
  Object.defineProperty(document, 'startViewTransition', { value: startViewTransition, configurable: true });
  const router = await renderAwayFromHome();

  await userEvent.click(screen.getByRole('link', { name: t('shell.home') }));

  await waitFor(() => expect(router.currentRoute.value.fullPath).toBe('/'));
  expect(startViewTransition).toHaveBeenCalledOnce();
  expect(screen.getByRole('heading', { name: 'pharma-training' })).toBeInTheDocument();
});

test('without the View Transitions API the Home link goes to the start page as before', async () => {
  expect('startViewTransition' in document).toBe(false);
  const router = await renderAwayFromHome();

  await userEvent.click(screen.getByRole('link', { name: t('shell.home') }));

  await waitFor(() => expect(router.currentRoute.value.fullPath).toBe('/'));
  expect(screen.getByRole('heading', { name: 'pharma-training' })).toBeInTheDocument();
});

// The toasts' region, and a toast's close button where one has it, are named in the language shown.
test('the toasts are named in the language shown', async () => {
  const router = createAppRouter(createMemoryHistory());
  await router.push('/');
  render(App, { global: { plugins: [router, createPinia(), signedInAsDeveloper] } });

  // vue-sonner adds the hotkey that reaches the region to its name.
  expect(screen.getByRole('region', { name: `${t('toasts.region')} alt+T` })).toBeInTheDocument();
  expect(screen.getByRole('region', { name: 'Benachrichtigungen alt+T' })).toBeInTheDocument();
  toast('Gespeichert.', { closeButton: true });
  expect(await screen.findByRole('button', { name: t('toasts.close') })).toBeInTheDocument();
  expect(screen.getByRole('button', { name: 'Benachrichtigung schließen' })).toBeInTheDocument();
});

test('in English the toasts are named in English', async () => {
  setLanguage('en-GB', { remember: false });
  const router = createAppRouter(createMemoryHistory());
  await router.push('/');
  render(App, { global: { plugins: [router, createPinia(), signedInAsDeveloper] } });

  expect(screen.getByRole('region', { name: 'Notifications alt+T' })).toBeInTheDocument();
  toast('Saved.', { closeButton: true });
  expect(await screen.findByRole('button', { name: 'Close notification' })).toBeInTheDocument();
});
