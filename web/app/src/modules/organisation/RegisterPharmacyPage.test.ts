import { QueryClient, VueQueryPlugin } from '@tanstack/vue-query';
import { cleanup, render, screen, waitFor, within } from '@testing-library/vue';
import userEvent from '@testing-library/user-event';
import { createPinia, type Pinia } from 'pinia';
import { afterEach, beforeEach, expect, test, vi } from 'vitest';
import { defineComponent, h } from 'vue';
import { createMemoryHistory } from 'vue-router';
import { getGetOrganisationPharmaciesMineQueryKey, type MyPharmacyDto } from '@/api/generated';
import { i18n, setLanguage, t } from '@/i18n';
import { createAppRouter } from '@/router';
import { useMyPharmacies } from '@/shell/useMyPharmacies';
import { useSelectedPharmacyStore } from '@/stores/selectedPharmacy';
import RegisterPharmacyPage from './RegisterPharmacyPage.vue';

const notify = vi.hoisted(() => ({ success: vi.fn(), failure: vi.fn() }));
vi.mock('@/lib/notify', () => ({ notify }));

// The host, as POST /organisation/pharmacies and GET /organisation/pharmacies/mine reach it through the API client:
// the user belongs to no pharmacy, and a registration answers what the test says. Once the host has registered one, the
// next read of the memberships holds it (the founder's membership is saved with the pharmacy).
const host = vi.fn<(input: RequestInfo | URL, init?: RequestInit) => Promise<Response>>();
vi.stubGlobal('fetch', host);

/** The new pharmacy's id, as the host answers a registration: 201 with the id and its address. */
const created = '0198c1a0-0000-7000-8000-0000000000a9';
const registered = async () =>
  Response.json(created, { status: 201, headers: { Location: `/organisation/pharmacies/${created}` } });
let registration: () => Promise<Response> = registered;

/** The founder's membership of the new pharmacy, as the host answers it once the pharmacy is registered. */
const founded: MyPharmacyDto = {
  id: created,
  name: 'Apotheke am Markt',
  membershipId: '0198c1a0-0000-7000-8000-0000000000b9',
  role: 'Manager',
  jobFunction: 'Pharmacist',
  canManageMembers: true,
  canAssignCourses: true,
};
const membershipsOf = (items: MyPharmacyDto[]) =>
  Response.json({ items, totalCount: items.length, page: 1, pageSize: 100 });

beforeEach(() => {
  localStorage.clear();
  registration = registered;
  let memberships: MyPharmacyDto[] = [];
  host.mockImplementation(async (_input, init) => {
    if (init?.method === 'POST') {
      const response = await registration();
      if (response.status === 201) {
        memberships = [founded];
      }
      return response;
    }
    return membershipsOf(memberships);
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

/** What the page posted, as the host received it. */
const posted = () =>
  host.mock.calls
    .filter(([, init]) => init?.method === 'POST')
    .map(([input, init]) => ({ url: String(input), body: JSON.parse(String(init?.body)) as unknown }));

/** The reads of the memberships the host received after the registration, in order. */
const membershipReadsAfterRegistration = () => {
  const registration = host.mock.calls.findIndex(([, init]) => init?.method === 'POST');
  return registration < 0
    ? []
    : host.mock.calls.slice(registration + 1).filter(([input, init]) => {
        return init?.method !== 'POST' && String(input).includes('/api/organisation/pharmacies/mine');
      });
};

// Stands in for the app shell, which always reads the memberships (the pharmacy switcher), beside the page.
const ShellStandIn = defineComponent({
  setup() {
    useMyPharmacies();
    return () => h(RegisterPharmacyPage);
  },
});

/**
 * The page at its address, alone or beside a stand-in for the shell, with a query client of its own that does not
 * retry, so a failure shows at once.
 */
async function renderPage(options: { inShell?: boolean; pinia?: Pinia } = {}) {
  const router = createAppRouter(createMemoryHistory());
  await router.push('/organisation/register');
  const queryClient = new QueryClient({ defaultOptions: { queries: { retry: false }, mutations: { retry: false } } });
  const pinia = options.pinia ?? createPinia();
  render(options.inShell === true ? ShellStandIn : RegisterPharmacyPage, {
    global: { plugins: [router, pinia, [VueQueryPlugin, { queryClient }]] },
  });
  return { router, queryClient, pinia };
}

const nameField = () => screen.getByRole('textbox', { name: t('organisation.register.name') });
const jobFunctionField = () => screen.getByRole('combobox', { name: t('organisation.register.jobFunction') });
const submit = () => screen.getByRole('button', { name: t('organisation.register.submit') });

/** Types a name (pasted, so a long one is quick) and picks a job function by its label. */
async function fillIn(user: ReturnType<typeof userEvent.setup>, name: string, jobFunction: string) {
  await user.click(nameField());
  await user.paste(name);
  await user.selectOptions(jobFunctionField(), jobFunction);
}

test('the page says what registering means and asks for the pharmacy name and the job function', async () => {
  await renderPage();

  const main = within(screen.getByRole('main'));
  const heading = main.getByRole('heading', { name: t('organisation.register.title'), level: 1 });
  expect(heading).toHaveClass('text-title');
  expect(main.getByText(t('organisation.register.intro'))).toBeInTheDocument();
  const form = main.getByRole('form', { name: t('organisation.register.title') });
  expect(form).toHaveAttribute('novalidate');
  expect(nameField()).toHaveAttribute('id', 'pharmacy-name');
  expect(nameField()).toHaveAttribute('autocomplete', 'organization');
  expect(nameField()).toHaveValue('');
  expect(jobFunctionField()).toHaveAttribute('id', 'pharmacy-job-function');
  expect(jobFunctionField()).toHaveValue('');
  const options = within(jobFunctionField()).getAllByRole('option');
  expect(options.map((option) => [option.textContent.trim(), option.getAttribute('value')])).toEqual([
    [t('organisation.register.choose'), ''],
    [t('glossary.jobFunction.Pharmacist'), 'Pharmacist'],
    [t('glossary.jobFunction.Pta'), 'Pta'],
    [t('glossary.jobFunction.Pka'), 'Pka'],
    [t('glossary.jobFunction.Trainee'), 'Trainee'],
    [t('glossary.jobFunction.Other'), 'Other'],
  ]);
  expect(within(jobFunctionField()).getByRole('option', { name: t('organisation.register.choose') })).toBeDisabled();
  expect(submit()).toHaveAttribute('type', 'submit');
  expect(submit()).toBeEnabled();
});

// Both fields must be filled in; a screen reader says so before the user submits, not only after a refusal.
test('both fields say they are required', async () => {
  await renderPage();

  expect(nameField()).toHaveAttribute('aria-required', 'true');
  expect(jobFunctionField()).toHaveAttribute('aria-required', 'true');
});

test('an empty form is refused with a message below each field, and nothing is sent', async () => {
  const user = userEvent.setup();
  await renderPage();

  await user.click(submit());

  const alerts = await screen.findAllByRole('alert');
  expect(alerts.map((alert) => alert.textContent.trim())).toEqual([
    t('organisation.register.nameRequired'),
    t('organisation.register.jobFunctionRequired'),
  ]);
  for (const alert of alerts) {
    expect(alert.tagName).toBe('P');
    expect(alert).toHaveClass('text-caption', 'text-destructive');
  }
  expect(nameField()).toHaveAttribute('aria-invalid', 'true');
  expect(nameField()).toHaveAccessibleDescription(t('organisation.register.nameRequired'));
  expect(jobFunctionField()).toHaveAttribute('aria-invalid', 'true');
  expect(jobFunctionField()).toHaveAccessibleDescription(t('organisation.register.jobFunctionRequired'));
  expect(posted()).toEqual([]);
});

test('a name of spaces only is no name', async () => {
  const user = userEvent.setup();
  await renderPage();
  await fillIn(user, '   ', t('glossary.jobFunction.Pharmacist'));

  await user.click(submit());

  expect(await screen.findByRole('alert')).toHaveTextContent(t('organisation.register.nameRequired'));
  expect(nameField()).toHaveAccessibleDescription(t('organisation.register.nameRequired'));
  expect(jobFunctionField()).not.toHaveAttribute('aria-invalid', 'true');
  expect(posted()).toEqual([]);
});

// PharmacyName on the host: at most 120 characters after trimming.
test('a name of 121 characters is refused, and nothing is sent', async () => {
  const user = userEvent.setup();
  await renderPage();
  await fillIn(user, 'A'.repeat(121), t('glossary.jobFunction.Pharmacist'));

  await user.click(submit());

  expect(await screen.findByRole('alert')).toHaveTextContent(t('organisation.register.nameTooLong'));
  expect(posted()).toEqual([]);
});

test('a name of 120 characters is registered', async () => {
  const user = userEvent.setup();
  await renderPage();
  await fillIn(user, 'A'.repeat(120), t('glossary.jobFunction.Pharmacist'));

  await user.click(submit());

  await waitFor(() =>
    expect(posted()).toEqual([
      { url: '/api/organisation/pharmacies', body: { name: 'A'.repeat(120), jobFunction: 'Pharmacist' } },
    ]),
  );
  expect(screen.queryByRole('alert')).not.toBeInTheDocument();
});

test('a name with a nul character is refused, and nothing is sent', async () => {
  const user = userEvent.setup();
  await renderPage();
  await fillIn(user, 'Apotheke\u0000am Markt', t('glossary.jobFunction.Pharmacist'));

  await user.click(submit());

  expect(await screen.findByRole('alert')).toHaveTextContent(t('organisation.register.nameHasNul'));
  expect(posted()).toEqual([]);
});

test('the page posts the trimmed name and the chosen job function', async () => {
  const user = userEvent.setup();
  await renderPage();
  await fillIn(user, '  Apotheke am Markt  ', t('glossary.jobFunction.Pta'));

  await user.click(submit());

  await waitFor(() =>
    expect(posted()).toEqual([
      { url: '/api/organisation/pharmacies', body: { name: 'Apotheke am Markt', jobFunction: 'Pta' } },
    ]),
  );
});

// A module's own words are shown as the host sent them (ApiError.serverMessage); the catalog speaks when it sent none,
// and for a framework's problem: a 5xx's English title is not the user's to read.
test.each([
  [
    400,
    () =>
      new Response(
        JSON.stringify({
          title: 'Bad Request',
          status: 400,
          detail: 'Der Name ist ungültig.',
          code: 'organisation.invalid-pharmacy-name',
        }),
        { status: 400, headers: { 'Content-Type': 'application/problem+json' } },
      ),
    () => 'Der Name ist ungültig.',
  ],
  [
    403,
    () =>
      new Response(
        JSON.stringify({ title: 'Ihr Konto hat keinen Namen.', status: 403, code: 'organisation.identity-incomplete' }),
        { status: 403, headers: { 'Content-Type': 'application/problem+json' } },
      ),
    () => 'Ihr Konto hat keinen Namen.',
  ],
  [500, () => new Response('', { status: 500 }), () => t('organisation.register.failed')],
  [
    500,
    () =>
      new Response(JSON.stringify({ title: 'An error occurred while processing your request.', status: 500 }), {
        status: 500,
        headers: { 'Content-Type': 'application/problem+json' },
      }),
    () => t('organisation.register.failed'),
  ],
])(
  'a registration the host answers with %i is reported, and the user stays with what they entered',
  async (_status, answer, message) => {
    const user = userEvent.setup();
    registration = async () => answer();
    const { router } = await renderPage();
    await fillIn(user, 'Apotheke am Markt', t('glossary.jobFunction.Pharmacist'));

    await user.click(submit());

    await waitFor(() => expect(notify.failure).toHaveBeenCalledWith(message()));
    expect(notify.failure).toHaveBeenCalledOnce();
    expect(notify.success).not.toHaveBeenCalled();
    expect(router.currentRoute.value.path).toBe('/organisation/register');
    expect(nameField()).toHaveValue('Apotheke am Markt');
    expect(jobFunctionField()).toHaveValue('Pharmacist');
    expect(submit()).toBeEnabled();
    expect(localStorage.getItem('pharmacy')).toBeNull();
  },
);

test('the button is disabled while the registration is on its way', async () => {
  const user = userEvent.setup();
  let answer: (response: Response) => void = () => undefined;
  registration = () =>
    new Promise<Response>((resolve) => {
      answer = resolve;
    });
  const { router } = await renderPage();
  await fillIn(user, 'Apotheke am Markt', t('glossary.jobFunction.Pharmacist'));

  await user.click(submit());

  await waitFor(() => expect(submit()).toBeDisabled());
  answer(Response.json(created, { status: 201 }));
  await waitFor(() => expect(router.currentRoute.value.path).toBe('/organisation'));
});

// The memberships are read again after the host registered the pharmacy, before the page moves on: until it has, the
// button stays disabled, or a second click would register a second pharmacy of the same name.
test('the button stays disabled until the page has moved on, also while the memberships are read again', async () => {
  const user = userEvent.setup();
  let memberships: MyPharmacyDto[] = [];
  let answerRead: (response: Response) => void = () => undefined;
  host.mockImplementation(async (_input, init) => {
    if (init?.method === 'POST') {
      memberships = [founded];
      return registered();
    }
    if (memberships.length === 0) {
      return membershipsOf(memberships);
    }
    // The read after the registration waits until the test answers it.
    return new Promise<Response>((resolve) => {
      answerRead = resolve;
    });
  });
  const { router } = await renderPage({ inShell: true });
  await fillIn(user, 'Apotheke am Markt', t('glossary.jobFunction.Pharmacist'));

  await user.click(submit());

  await waitFor(() => expect(membershipReadsAfterRegistration()).toHaveLength(1));
  expect(submit()).toBeDisabled();
  await user.click(submit());
  expect(posted()).toHaveLength(1);
  expect(router.currentRoute.value.path).toBe('/organisation/register');

  answerRead(membershipsOf([founded]));
  await waitFor(() => expect(router.currentRoute.value.path).toBe('/organisation'));
  expect(posted()).toHaveLength(1);
});

// 201 says the pharmacy exists, whatever the body: the page must not ask the user to register it again. Without its id
// it cannot choose it, so the user is told it was registered and sent to the start page, where the switcher lists it.
test('a registration the host answers with 201 but no id is reported as registered, and the user is sent home', async () => {
  const user = userEvent.setup();
  const markt: MyPharmacyDto = { ...founded, id: '0198c1a0-0000-7000-8000-0000000000a1', name: 'Apotheke im Dorf' };
  localStorage.setItem('pharmacy', markt.id);
  let memberships = [markt];
  registration = async () => Response.json('x', { status: 201 });
  host.mockImplementation(async (_input, init) => {
    if (init?.method === 'POST') {
      memberships = [markt, { ...founded, name: 'Neue Apotheke' }];
      return registration();
    }
    return membershipsOf(memberships);
  });
  const pinia = createPinia();
  const { router } = await renderPage({ inShell: true, pinia });
  await waitFor(() => expect(host).toHaveBeenCalled());
  await fillIn(user, 'Neue Apotheke', t('glossary.jobFunction.Pharmacist'));

  await user.click(submit());

  await waitFor(() => expect(router.currentRoute.value.path).toBe('/'));
  expect(notify.failure).not.toHaveBeenCalled();
  expect(notify.success).toHaveBeenCalledExactlyOnceWith(
    t('organisation.register.registered', { name: 'Neue Apotheke' }),
  );
  expect(useSelectedPharmacyStore(pinia).pharmacyId).toBe(markt.id);
  expect(localStorage.getItem('pharmacy')).toBe(markt.id);
  // The memberships were invalidated: the shell read them again, and the new pharmacy is among them.
  expect(membershipReadsAfterRegistration().length).toBeGreaterThan(0);
});

// Without the shell, nobody watches the memberships: the page reads them itself before it chooses the new pharmacy, so
// the choice holds once the shell reads the memberships from the cache.
test('a registered pharmacy is chosen and opened when nothing else reads the memberships', async () => {
  const user = userEvent.setup();
  const pinia = createPinia();
  const { router, queryClient } = await renderPage({ pinia });
  await fillIn(user, 'Apotheke am Markt', t('glossary.jobFunction.Pharmacist'));

  await user.click(submit());

  await waitFor(() => expect(router.currentRoute.value.path).toBe('/organisation'));
  expect(notify.success).toHaveBeenCalledExactlyOnceWith(
    t('organisation.register.registered', { name: 'Apotheke am Markt' }),
  );
  expect(notify.failure).not.toHaveBeenCalled();
  expect(useSelectedPharmacyStore(pinia).pharmacyId).toBe(created);
  expect(membershipReadsAfterRegistration().length).toBeGreaterThan(0);
  const cached = queryClient
    .getQueriesData<{ items: MyPharmacyDto[] }>({ queryKey: getGetOrganisationPharmaciesMineQueryKey() })
    .flatMap(([, data]) => data?.items ?? []);
  expect(cached.map((membership) => membership.id)).toContain(created);
});

// The page follows a switch of the language while it is shown: its texts and the job functions at once, and the
// refusals, since zod's messages are resolved when the form validates, not when the module loads.
test('switched to English, the page, its job functions and its refusals speak English', async () => {
  const user = userEvent.setup();
  await renderPage();
  await user.click(submit());
  expect(await screen.findAllByRole('alert')).toHaveLength(2);

  setLanguage('en-GB', { remember: false });

  expect(await screen.findByRole('heading', { name: t('organisation.register.title'), level: 1 })).toBeInTheDocument();
  expect(t('organisation.register.title')).not.toBe(
    i18n.global.t('organisation.register.title', {}, { locale: 'de-DE' }),
  );
  expect(within(jobFunctionField()).getByRole('option', { name: t('glossary.jobFunction.Trainee') })).toHaveValue(
    'Trainee',
  );
  await user.click(submit());
  await waitFor(() =>
    expect(screen.getAllByRole('alert').map((alert) => alert.textContent.trim())).toEqual([
      t('organisation.register.nameRequired'),
      t('organisation.register.jobFunctionRequired'),
    ]),
  );
});

// A message already shown follows a switch of the language too: the user does not have to submit again to read it.
test('a refusal already shown follows a switch to English without submitting again', async () => {
  const user = userEvent.setup();
  await renderPage();
  await user.click(submit());
  expect(await screen.findByText('Geben Sie den Namen der Apotheke an.')).toBeInTheDocument();
  expect(screen.getByText('Wählen Sie Ihre Tätigkeit in der Apotheke.')).toBeInTheDocument();

  setLanguage('en-GB', { remember: false });

  await waitFor(() =>
    expect(screen.getAllByRole('alert').map((alert) => alert.textContent.trim())).toEqual([
      t('organisation.register.nameRequired'),
      t('organisation.register.jobFunctionRequired'),
    ]),
  );
  expect(t('organisation.register.nameRequired')).not.toBe('Geben Sie den Namen der Apotheke an.');
  expect(nameField()).toHaveAccessibleDescription(t('organisation.register.nameRequired'));
  expect(posted()).toEqual([]);
});

// The German texts themselves, not only their keys: a key that resolved to the wrong text would pass the t() queries.
test('the page speaks German by default', async () => {
  await renderPage();

  const main = within(screen.getByRole('main'));
  expect(main.getByRole('heading', { name: 'Apotheke registrieren', level: 1 })).toBeInTheDocument();
  expect(main.getByRole('textbox', { name: 'Name der Apotheke' })).toBe(nameField());
  expect(main.getByRole('combobox', { name: 'Ihre Tätigkeit' })).toBe(jobFunctionField());
  expect(main.getByRole('button', { name: 'Registrieren' })).toBe(submit());
  expect(within(jobFunctionField()).getByRole('option', { name: 'Sonstige' })).toHaveValue('Other');
});
