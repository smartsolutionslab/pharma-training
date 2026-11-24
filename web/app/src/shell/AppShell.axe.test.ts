import { QueryClient, VueQueryPlugin } from '@tanstack/vue-query';
import { render, screen, within } from '@testing-library/vue';
import userEvent from '@testing-library/user-event';
import { createPinia } from 'pinia';
import { afterEach, beforeEach, describe, expect, test, vi } from 'vitest';
import { axe } from 'vitest-axe';
import { computed, type Plugin } from 'vue';
import { createMemoryHistory } from 'vue-router';
import type { MyPharmacyDto, PagedResultOfMyPharmacyDto } from '@/api/generated';
import App from '@/App.vue';
import { signedInAsDeveloper } from '@/auth/signedInAsDeveloper';
import { type Auth, authKey } from '@/auth/useAuth';
import { createAppRouter } from '@/router';
import { languages, setLanguage, t } from '@/i18n';

// The user menu asks the host's /me for the name; here it answers at once.
vi.mock('@/api/useCurrentUser', () => ({ useCurrentUser: () => computed(() => 'developer') }));

// The developer's pharmacies, as GET /organisation/pharmacies/mine answers them through useMyPharmacies.
const pharmacies = await vi.hoisted(async () => {
  const { ref } = await import('vue');
  return {
    data: ref<PagedResultOfMyPharmacyDto | undefined>(undefined),
    isPending: ref(false),
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

beforeEach(() => {
  pharmacies.data.value = { items: [markt, dom], totalCount: 2, page: 1, pageSize: 100 };
  pharmacies.isPending.value = false;
  pharmacies.isError.value = false;
  localStorage.setItem('pharmacy', markt.id);
});

afterEach(() => {
  document.documentElement.removeAttribute('class');
  localStorage.clear();
  vi.unstubAllGlobals();
});

// The shell renders for the local user developer, signed in, as the app shows it, in the first of two pharmacies.
async function renderShell(path = '/') {
  const router = createAppRouter(createMemoryHistory());
  await router.push(path);
  return render(App, { global: { plugins: [router, createPinia(), signedInAsDeveloper] } }).container;
}

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

// A public page (#54) in the public layout, for a visitor signed out: the verification page, whose request to the host
// waits for an answer that never comes (the page's own states are its own axe check's).
async function renderPublicLayout() {
  vi.stubGlobal('fetch', () => new Promise<Response>(() => undefined));
  const router = createAppRouter(createMemoryHistory());
  await router.push('/verify/AbCdEfGhIjKlMnOpQrStUvWxYz012-_9');
  return render(App, {
    global: { plugins: [router, createPinia(), [VueQueryPlugin, { queryClient: new QueryClient() }], signedOut] },
  }).container;
}

const signedOut: Plugin = {
  install(app) {
    const auth: Auth = {
      user: computed(() => null),
      isAuthenticated: computed(() => false),
      isLoading: computed(() => false),
      error: computed(() => null),
      activeNavigator: computed(() => null),
      signinRedirect: () => Promise.resolve(),
      signoutRedirect: () => Promise.resolve(),
    };
    app.provide(authKey, auth);
  },
};

// The pharmacy switcher's name for the developer's first pharmacy, in the language shown.
const markets = () => t('shell.pharmacy.current', { name: 'Apotheke am Markt' });

// Each language in turn: the texts differ, and so may what axe finds in them.
describe.each(languages)('in %s', (language) => {
  beforeEach(() => {
    setLanguage(language, { remember: false });
  });

  // Whichever test renders first in a file also pays for warming up jsdom, Vue and axe.
  test.each(themes)(
    'the app shell has no accessibility violations axe can find in the $name theme',
    { timeout: 20_000 },
    async ({ switchOn }) => {
      switchOn();
      const container = await renderShell();
      await screen.findByRole('button', { name: markets() });

      expect(await axe(container)).toHaveNoViolations();
    },
  );

  test.each(themes)(
    'the open command palette has no accessibility violations axe can find in the $name theme',
    async ({ switchOn }) => {
      switchOn();
      const user = userEvent.setup();
      await renderShell();
      await screen.findByRole('button', { name: markets() });

      await user.keyboard('{Control>}k{/Control}');
      await screen.findByRole('dialog', { name: t('shell.palette.title') });

      expect(await axe(document.body)).toHaveNoViolations();
    },
  );

  test.each(themes)(
    'the open pharmacy switcher has no accessibility violations axe can find in the $name theme',
    async ({ switchOn }) => {
      switchOn();
      const user = userEvent.setup();
      await renderShell();

      await user.click(await screen.findByRole('button', { name: markets() }));
      await screen.findByRole('menu', { name: t('shell.pharmacy.list') });

      expect(await axe(document.body)).toHaveNoViolations();
    },
  );

  test('the prompt to choose a pharmacy has no accessibility violations axe can find', async () => {
    localStorage.removeItem('pharmacy');
    const container = await renderShell();
    await screen.findByRole('heading', { name: t('home.choosePharmacy') });

    expect(await axe(container)).toHaveNoViolations();
  });

  test('the collapsed sidebar leaves no accessibility violations axe can find', async () => {
    const user = userEvent.setup();
    const container = await renderShell();
    await screen.findByRole('button', { name: markets() });

    await user.click(within(screen.getByRole('banner')).getByRole('button', { name: t('ui.sidebar.toggle') }));

    expect(await axe(container)).toHaveNoViolations();
  });

  test.each(themes)(
    'the public layout has no accessibility violations axe can find in the $name theme',
    async ({ switchOn }) => {
      switchOn();
      const container = await renderPublicLayout();
      await screen.findByRole('heading', { level: 1, name: t('modules.certification.entries.verify.title') });
      // The public layout, not the shell: its header holds the language group.
      expect(within(screen.getByRole('banner')).getByRole('group', { name: t('language.label') })).toBeInTheDocument();

      expect(await axe(container)).toHaveNoViolations();
    },
  );

  test('a page that is not available has no accessibility violations axe can find', async () => {
    localStorage.setItem('pharmacy', dom.id);
    const container = await renderShell('/organisation');
    await screen.findByRole('heading', { name: t('shell.gate.notAvailable') });

    expect(await axe(container)).toHaveNoViolations();
  });
});
