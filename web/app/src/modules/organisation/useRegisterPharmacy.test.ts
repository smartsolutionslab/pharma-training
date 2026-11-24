import { QueryClient, VueQueryPlugin } from '@tanstack/vue-query';
import { cleanup, render, screen, waitFor } from '@testing-library/vue';
import userEvent from '@testing-library/user-event';
import { createPinia, type Pinia } from 'pinia';
import { afterEach, beforeEach, expect, test, vi } from 'vitest';
import { defineComponent, h } from 'vue';
import { createMemoryHistory } from 'vue-router';
import type { MyPharmacyDto } from '@/api/generated';
import { i18n, setLanguage, t } from '@/i18n';
import { createAppRouter } from '@/router';
import { useCurrentPharmacy } from '@/shell/useCurrentPharmacy';
import { useSelectedPharmacyStore } from '@/stores/selectedPharmacy';
import RegisterPharmacyPage from './RegisterPharmacyPage.vue';

const notify = vi.hoisted(() => ({ success: vi.fn(), failure: vi.fn() }));
vi.mock('@/lib/notify', () => ({ notify }));

const markt: MyPharmacyDto = {
  id: '0198c1a0-0000-7000-8000-0000000000a1',
  name: 'Apotheke am Markt',
  membershipId: '0198c1a0-0000-7000-8000-0000000000b1',
  role: 'Employee',
  jobFunction: 'Pta',
  canManageMembers: false,
  canAssignCourses: false,
};
const founded: MyPharmacyDto = {
  id: '0198c1a0-0000-7000-8000-0000000000a9',
  name: 'Neue Apotheke',
  membershipId: '0198c1a0-0000-7000-8000-0000000000b9',
  role: 'Manager',
  jobFunction: 'Pharmacist',
  canManageMembers: true,
  canAssignCourses: true,
};

// The host, as POST /organisation/pharmacies and GET /organisation/pharmacies/mine reach it through the API client: the
// user works in one pharmacy, and once they have registered another, the next read of their memberships holds both
// (the founder's membership is saved with the pharmacy).
const host = vi.fn<(input: RequestInfo | URL, init?: RequestInit) => Promise<Response>>();
vi.stubGlobal('fetch', host);

beforeEach(() => {
  localStorage.clear();
  let memberships = [markt];
  host.mockImplementation(async (_input, init) => {
    if (init?.method === 'POST') {
      memberships = [markt, founded];
      return Response.json(founded.id, {
        status: 201,
        headers: { Location: `/organisation/pharmacies/${founded.id}` },
      });
    }
    return Response.json({ items: memberships, totalCount: memberships.length, page: 1, pageSize: 100 });
  });
  notify.success.mockReset();
  notify.failure.mockReset();
});
afterEach(() => {
  host.mockReset();
  localStorage.clear();
});
// Without Vitest's globals, Testing Library does not unmount between tests by itself.
afterEach(cleanup);

// Stands in for the app shell: it names the pharmacy the user works in, as the switcher does, beside the page.
const ShellStandIn = defineComponent({
  setup() {
    const { current } = useCurrentPharmacy();
    return () => h('div', [h('p', `Current pharmacy: ${current.value?.name ?? 'none'}`), h(RegisterPharmacyPage)]);
  },
});

async function renderInShell(pinia: Pinia = createPinia()) {
  const router = createAppRouter(createMemoryHistory());
  await router.push('/organisation/register');
  const queryClient = new QueryClient({ defaultOptions: { queries: { retry: false }, mutations: { retry: false } } });
  render(ShellStandIn, { global: { plugins: [router, pinia, [VueQueryPlugin, { queryClient }]] } });
  return { router };
}

/** Every pharmacy the store held as chosen, from now on. */
function choicesOf(pinia: Pinia) {
  const store = useSelectedPharmacyStore(pinia);
  const choices: (string | null)[] = [];
  store.$subscribe(() => choices.push(store.pharmacyId), { flush: 'sync' });
  return choices;
}

/** Registers "Neue Apotheke" through the page, as the user does. */
async function registerNeueApotheke() {
  const user = userEvent.setup();
  expect(await screen.findByText('Current pharmacy: Apotheke am Markt')).toBeInTheDocument();
  await user.click(screen.getByRole('textbox', { name: t('organisation.register.name') }));
  await user.paste('Neue Apotheke');
  await user.selectOptions(
    screen.getByRole('combobox', { name: t('organisation.register.jobFunction') }),
    t('glossary.jobFunction.Pharmacist'),
  );
  await user.click(screen.getByRole('button', { name: t('organisation.register.submit') }));
}

// The memberships are read again before the new pharmacy is chosen: chosen among memberships that do not hold it yet,
// it would be forgotten at once, and with two pharmacies none would be current.
test('a registered pharmacy becomes the current one and stays, the user is told, and led to its team page', async () => {
  const user = userEvent.setup();
  const { router } = await renderInShell();
  expect(await screen.findByText('Current pharmacy: Apotheke am Markt')).toBeInTheDocument();

  await user.click(screen.getByRole('textbox', { name: t('organisation.register.name') }));
  await user.paste('  Neue Apotheke ');
  await user.selectOptions(
    screen.getByRole('combobox', { name: t('organisation.register.jobFunction') }),
    t('glossary.jobFunction.Pharmacist'),
  );
  await user.click(screen.getByRole('button', { name: t('organisation.register.submit') }));

  await waitFor(() => expect(router.currentRoute.value.path).toBe('/organisation'));
  expect(notify.success).toHaveBeenCalledExactlyOnceWith(
    t('organisation.register.registered', { name: 'Neue Apotheke' }),
  );
  expect(notify.failure).not.toHaveBeenCalled();
  expect(await screen.findByText('Current pharmacy: Neue Apotheke')).toBeInTheDocument();
  expect(localStorage.getItem('pharmacy')).toBe(founded.id);
  // The memberships were read again after the registration, not only before it.
  const registration = host.mock.calls.findIndex(([, init]) => init?.method === 'POST');
  expect(host.mock.calls.slice(registration + 1).some(([, init]) => init?.method !== 'POST')).toBe(true);
});

// The pharmacy exists, but the page cannot open it: chosen among memberships that do not hold it, it would be
// forgotten at once. It is not chosen; the user is told where to find it and sent to the start page.
test.each([
  ['the memberships cannot be read again', () => Response.json({ title: 'Broken.' }, { status: 500 })],
  [
    'the memberships read again do not hold it',
    () => Response.json({ items: [markt], totalCount: 1, page: 1, pageSize: 100 }),
  ],
])(
  'a registered pharmacy that cannot be opened because %s is not chosen, and the user is told to choose it',
  async (_reason, readAfterRegistration) => {
    let registered = false;
    host.mockImplementation(async (_input, init) => {
      if (init?.method === 'POST') {
        registered = true;
        return Response.json(founded.id, { status: 201 });
      }
      return registered
        ? readAfterRegistration()
        : Response.json({ items: [markt], totalCount: 1, page: 1, pageSize: 100 });
    });
    const pinia = createPinia();
    const choices = choicesOf(pinia);
    const { router } = await renderInShell(pinia);

    await registerNeueApotheke();

    await waitFor(() => expect(router.currentRoute.value.path).toBe('/'));
    expect(notify.success).toHaveBeenCalledExactlyOnceWith(
      t('organisation.register.registeredNotOpened', { name: 'Neue Apotheke' }),
    );
    expect(notify.failure).not.toHaveBeenCalled();
    expect(choices).not.toContain(founded.id);
    expect(localStorage.getItem('pharmacy')).not.toBe(founded.id);
    expect(screen.getByText('Current pharmacy: Apotheke am Markt')).toBeInTheDocument();
  },
);

// A refusal is told by its code in the catalog's words (#147, ADR 0007), the host's invalid-argument included; the
// host's own words only for a module code the catalog does not know. A refusal without a code, a network failure or a
// 5xx is told in the action's own words: the framework's English titles never reach the user.
test.each([
  [
    "a 400 with the module's problem details and a detail",
    () =>
      new Response(
        JSON.stringify({
          title: 'Bad Request',
          status: 400,
          detail: 'SERVER WORDS',
          code: 'organisation.invalid-pharmacy-name',
        }),
        { status: 400, headers: { 'Content-Type': 'application/problem+json' } },
      ),
    () => 'Der Name einer Apotheke hat 1 bis 120 Zeichen und enthält keine Steuer- oder Richtungszeichen.',
  ],
  [
    "a 403 with the module's problem title only",
    () =>
      new Response(JSON.stringify({ title: 'SERVER WORDS', status: 403, code: 'organisation.identity-incomplete' }), {
        status: 403,
        headers: { 'Content-Type': 'application/problem+json' },
      }),
    () => 'Ihr Konto nennt keinen Namen oder keine gültige E-Mail-Adresse. Ergänzen Sie Ihr Profil.',
  ],
  [
    'a 409 with a module code the catalog does not know',
    () =>
      new Response(
        JSON.stringify({ title: 'Conflict', status: 409, detail: 'SERVER WORDS', code: 'organisation.zzz-unknown' }),
        { status: 409, headers: { 'Content-Type': 'application/problem+json' } },
      ),
    () => 'SERVER WORDS',
  ],
  [
    "a 500 with the framework's problem title",
    () =>
      new Response(
        JSON.stringify({
          type: 'https://tools.ietf.org/html/rfc9110#section-15.6.1',
          title: 'An error occurred while processing your request.',
          status: 500,
        }),
        { status: 500, headers: { 'Content-Type': 'application/problem+json' } },
      ),
    () => t('organisation.register.failed'),
  ],
  [
    'a 400 binding failure without a code',
    () =>
      new Response(JSON.stringify({ title: 'Bad Request', status: 400 }), {
        status: 400,
        headers: { 'Content-Type': 'application/problem+json' },
      }),
    () => t('organisation.register.failed'),
  ],
  [
    'a 400 from a failed guard',
    () =>
      new Response(
        JSON.stringify({
          title: 'The request is invalid.',
          status: 400,
          detail: "The value of 'name' must not be null or white space.",
          code: 'invalid-argument',
        }),
        { status: 400, headers: { 'Content-Type': 'application/problem+json' } },
      ),
    () => 'Eine Angabe ist ungültig. Prüfen Sie Ihre Eingaben und versuchen Sie es erneut.',
  ],
  ['a 500 without a body', () => new Response('', { status: 500 }), () => t('organisation.register.failed')],
  [
    'a 502 with a plain-text body',
    () => new Response('Bad Gateway', { status: 502, headers: { 'Content-Type': 'text/plain' } }),
    () => t('organisation.register.failed'),
  ],
])(
  'a registration refused with %s is reported in the right words, and nothing is chosen',
  async (_how, answer, said) => {
    host.mockImplementation(async (_input, init) =>
      init?.method === 'POST' ? answer() : Response.json({ items: [markt], totalCount: 1, page: 1, pageSize: 100 }),
    );
    const pinia = createPinia();
    const choices = choicesOf(pinia);
    const { router } = await renderInShell(pinia);

    await registerNeueApotheke();

    await waitFor(() => expect(notify.failure).toHaveBeenCalledExactlyOnceWith(said()));
    expect(notify.success).not.toHaveBeenCalled();
    expect(router.currentRoute.value.path).toBe('/organisation/register');
    expect(choices).not.toContain(founded.id);
  },
);

test('a refusal without a server message is told in the language of the moment', async () => {
  host.mockImplementation(async (_input, init) =>
    init?.method === 'POST'
      ? Promise.reject(new TypeError('Failed to fetch'))
      : Response.json({ items: [markt], totalCount: 1, page: 1, pageSize: 100 }),
  );
  setLanguage('en-GB', { remember: false });
  await renderInShell();

  await registerNeueApotheke();

  await waitFor(() => expect(notify.failure).toHaveBeenCalledExactlyOnceWith(t('organisation.register.failed')));
  expect(t('organisation.register.failed')).not.toBe(
    i18n.global.t('organisation.register.failed', {}, { locale: 'de-DE' }),
  );
});

test.each([
  ['de-DE', 'Der Name einer Apotheke hat 1 bis 120 Zeichen und enthält keine Steuer- oder Richtungszeichen.'],
  ['en-GB', "A pharmacy's name has 1 to 120 characters and contains no control or direction characters."],
] as const)(
  'a registration refused with invalid-pharmacy-name is told in %s in the catalog words, never the host words',
  async (language, said) => {
    setLanguage(language, { remember: false });
    host.mockImplementation(async (_input, init) =>
      init?.method === 'POST'
        ? new Response(
            JSON.stringify({
              title: 'Bad Request',
              status: 400,
              detail: 'SERVER WORDS',
              code: 'organisation.invalid-pharmacy-name',
            }),
            { status: 400, headers: { 'Content-Type': 'application/problem+json' } },
          )
        : Response.json({ items: [markt], totalCount: 1, page: 1, pageSize: 100 }),
    );
    await renderInShell();

    await registerNeueApotheke();

    await waitFor(() => expect(notify.failure).toHaveBeenCalledExactlyOnceWith(said));
    expect(String(notify.failure.mock.calls)).not.toContain('SERVER WORDS');
  },
);

// The host's words for this code speak of a refusal it shares with accepting an invitation; the page has words of its
// own for it, in the language of the moment, which tell the user what to do before registering again.
test.each([
  [
    'de-DE',
    'Ihre E-Mail-Adresse ist noch nicht bestätigt. Bestätigen Sie sie über den Link in der Bestätigungs-E-Mail, und registrieren Sie die Apotheke dann erneut.',
  ],
  [
    'en-GB',
    'Your e-mail address has not been verified yet. Verify it with the link in the verification e-mail, then register the pharmacy again.',
  ],
] as const)(
  "a registration refused because the e-mail address is not verified is told in %s in the catalog's words, not the host's",
  async (language, said) => {
    setLanguage(language, { remember: false });
    host.mockImplementation(async (_input, init) =>
      init?.method === 'POST'
        ? new Response(
            JSON.stringify({
              title: 'Your e-mail address has not been verified. Verify it, then try again.',
              status: 403,
              code: 'organisation.email-not-verified',
            }),
            { status: 403, headers: { 'Content-Type': 'application/problem+json' } },
          )
        : Response.json({ items: [markt], totalCount: 1, page: 1, pageSize: 100 }),
    );
    const pinia = createPinia();
    const choices = choicesOf(pinia);
    const { router } = await renderInShell(pinia);

    await registerNeueApotheke();

    await waitFor(() => expect(notify.failure).toHaveBeenCalledExactlyOnceWith(said));
    expect(notify.success).not.toHaveBeenCalled();
    expect(router.currentRoute.value.path).toBe('/organisation/register');
    expect(choices).not.toContain(founded.id);
  },
);
