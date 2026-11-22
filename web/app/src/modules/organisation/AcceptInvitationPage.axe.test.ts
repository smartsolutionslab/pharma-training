import { QueryClient, VueQueryPlugin } from '@tanstack/vue-query';
import { cleanup, render, screen } from '@testing-library/vue';
import { createPinia } from 'pinia';
import { afterEach, beforeEach, describe, expect, test, vi } from 'vitest';
import { axe } from 'vitest-axe';
import { defineComponent, h, type VNode } from 'vue';
import { createMemoryHistory, type RouteLocationNormalizedLoaded, RouterView } from 'vue-router';
import { signedInAsDeveloper } from '@/auth/signedInAsDeveloper';
import { languages, setLanguage, t } from '@/i18n';
import { createAppRouter } from '@/router';

// The screen's axe check. It asks nothing of what the screen shows, only that axe finds no violations in it: what the
// screen does is its own tests' (AcceptInvitationPage.test.ts). AcceptInvitationPage.data.ts has no example: the page
// calls no query hook of the generated client. It is checked in each of the states it shows: without a token, and
// while the invitation is accepted.

// The invitation mail's link: its route keeps the token for this tab and the page posts it.
const token = 'k7Yq2_Lm-9XwPz4RtV8sNc1HjD6bFgUe0OiA5yQrTmW';

// The host does not answer: a posted token stays on its way.
vi.stubGlobal('fetch', () => new Promise<Response>(() => undefined));

// Without Vitest's globals, Testing Library does not unmount between tests by itself.
afterEach(cleanup);

// The app's other themes: each check runs again in each, switched on as the app switches it before the screen renders.
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

afterEach(() => {
  document.documentElement.removeAttribute('class');
  localStorage.clear();
  sessionStorage.clear();
});

// The page as the router shows it at its address.
const AtTheLink = defineComponent({
  setup() {
    return () =>
      h(RouterView, null, {
        default: ({ Component, route }: { Component: VNode | undefined; route: RouteLocationNormalizedLoaded }) =>
          route.path === '/invitations/accept' ? Component : null,
      });
  },
});

// The screen renders for the local user developer, signed in, without the sign-in plugin: the check sees the screen as
// the developer does.
async function renderPage(address: string) {
  const router = createAppRouter(createMemoryHistory());
  await router.push(address);
  const { container } = render(AtTheLink, {
    global: {
      plugins: [router, createPinia(), [VueQueryPlugin, { queryClient: new QueryClient() }], signedInAsDeveloper],
    },
  });
  return container;
}

// Each state of the page, by the address it is opened at and what shows once it is there.
const states = [
  {
    state: 'without a token',
    address: '/invitations/accept',
    shown: () => screen.findByRole('heading', { level: 1, name: t('organisation.accept.missing.title') }),
  },
  {
    state: 'while the invitation is accepted',
    address: `/invitations/accept#token=${token}`,
    shown: () => screen.findByText(t('organisation.accept.accepting')),
  },
];

// Each language in turn: the texts differ, and so may what axe finds in them.
describe.each(languages)('in %s', (language) => {
  beforeEach(() => {
    setLanguage(language, { remember: false });
  });

  describe.each(themes)('in the $name theme', ({ switchOn }) => {
    test.each(states)(
      'the Accept an invitation page has no accessibility violations axe can find $state',
      async ({ address, shown }) => {
        switchOn();
        const container = await renderPage(address);

        expect(await shown()).toBeInTheDocument();
        expect(await axe(container)).toHaveNoViolations();
      },
    );
  });
});
