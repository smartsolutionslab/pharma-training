import { QueryClient, VueQueryPlugin } from '@tanstack/vue-query';
import { cleanup, render, screen } from '@testing-library/vue';
import userEvent from '@testing-library/user-event';
import { createPinia } from 'pinia';
import { afterEach, beforeEach, describe, expect, test, vi } from 'vitest';
import { axe } from 'vitest-axe';
import { defineComponent, h, type VNode } from 'vue';
import { createMemoryHistory, type RouteLocationNormalizedLoaded, RouterView } from 'vue-router';
import { signedInAsDeveloper } from '@/auth/signedInAsDeveloper';
import { languages, setLanguage, t } from '@/i18n';
import { createAppRouter } from '@/router';
import { refusals } from './AcceptInvitationPage.data';

// The screen's axe check. It asks nothing of what the screen shows, only that axe finds no violations in it: what the
// screen does is its own tests' (AcceptInvitationPage.test.ts). AcceptInvitationPage.data.ts has no example: the page
// calls no query hook of the generated client. It is checked in each of the states it shows: without a token, ready to
// accept a kept token (nothing posted before the user presses Accept the invitation), while the invitation is accepted,
// and refused as the data file's refusals say (expired, for another address).

// The invitation mail's link: its route keeps the token for this tab and the page posts it.
const token = 'k7Yq2_Lm-9XwPz4RtV8sNc1HjD6bFgUe0OiA5yQrTmW';

// The host answers a posted token with the refusal the test names; otherwise it does not answer, and a posted token
// stays on its way.
const answered = vi.hoisted(() => ({
  refusal: undefined as { status: number; body: Record<string, unknown> } | undefined,
}));
vi.stubGlobal('fetch', async (_input: RequestInfo | URL, init?: RequestInit) => {
  const refusal = answered.refusal;
  if (init?.method === 'POST' && refusal !== undefined) {
    return Response.json(refusal.body, { status: refusal.status });
  }
  return new Promise<Response>(() => undefined);
});

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
  answered.refusal = undefined;
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

/** The ready state's button, pressed: the only way the page posts the token. */
async function accept() {
  await userEvent.setup().click(await screen.findByRole('button', { name: t('organisation.accept.ready.accept') }));
}

// Each state of the page, by the address it is opened at, what the user does there, and what shows once it is there.
const states: {
  state: string;
  address: string;
  refusal?: { status: number; body: Record<string, unknown> };
  act?: () => Promise<void>;
  shown: () => Promise<HTMLElement>;
}[] = [
  {
    state: 'without a token',
    address: '/invitations/accept',
    shown: () => screen.findByRole('heading', { level: 1, name: t('organisation.accept.missing.title') }),
  },
  {
    state: 'ready to accept a kept token',
    address: `/invitations/accept#token=${token}`,
    shown: () => screen.findByRole('button', { name: t('organisation.accept.ready.accept') }),
  },
  {
    state: 'while the invitation is accepted',
    address: `/invitations/accept#token=${token}`,
    act: accept,
    shown: () => screen.findByText(t('organisation.accept.accepting')),
  },
  {
    state: 'for an expired invitation',
    address: `/invitations/accept#token=${token}`,
    refusal: refusals.expired,
    act: accept,
    shown: () => screen.findByRole('heading', { level: 1, name: t('organisation.accept.expired.title') }),
  },
  {
    state: 'for an invitation to another address',
    address: `/invitations/accept#token=${token}`,
    refusal: refusals.anotherAddress,
    act: accept,
    shown: () => screen.findByRole('heading', { level: 1, name: t('organisation.accept.anotherAddress.title') }),
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
      async ({ address, refusal, act, shown }) => {
        answered.refusal = refusal;
        switchOn();
        const container = await renderPage(address);
        await act?.();

        expect(await shown()).toBeInTheDocument();
        expect(await axe(container)).toHaveNoViolations();
      },
    );
  });
});
