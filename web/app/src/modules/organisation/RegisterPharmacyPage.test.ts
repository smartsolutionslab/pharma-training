import { QueryClient, VueQueryPlugin } from '@tanstack/vue-query';
import { cleanup, render, screen, waitFor, within } from '@testing-library/vue';
import userEvent from '@testing-library/user-event';
import { createPinia } from 'pinia';
import { afterEach, beforeEach, expect, test, vi } from 'vitest';
import { createMemoryHistory } from 'vue-router';
import { createAppRouter } from '@/router';
import RegisterPharmacyPage from './RegisterPharmacyPage.vue';

const notify = vi.hoisted(() => ({ success: vi.fn(), failure: vi.fn() }));
vi.mock('@/lib/notify', () => ({ notify }));

// The host, as POST /organisation/pharmacies and GET /organisation/pharmacies/mine reach it through the API client:
// the user belongs to no pharmacy, and a registration answers what the test says.
const host = vi.fn<(input: RequestInfo | URL, init?: RequestInit) => Promise<Response>>();
vi.stubGlobal('fetch', host);

/** The new pharmacy's id, as the host answers a registration: 201 with the id and its address. */
const created = '0198c1a0-0000-7000-8000-0000000000a9';
const registered = async () =>
  Response.json(created, { status: 201, headers: { Location: `/organisation/pharmacies/${created}` } });
let registration: () => Promise<Response> = registered;

beforeEach(() => {
  localStorage.clear();
  registration = registered;
  host.mockImplementation(async (_input, init) =>
    init?.method === 'POST' ? registration() : Response.json({ items: [], totalCount: 0, page: 1, pageSize: 100 }),
  );
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

/** The page at its address, with a query client of its own that does not retry, so a failure shows at once. */
async function renderPage() {
  const router = createAppRouter(createMemoryHistory());
  await router.push('/organisation/register');
  const queryClient = new QueryClient({ defaultOptions: { queries: { retry: false }, mutations: { retry: false } } });
  render(RegisterPharmacyPage, { global: { plugins: [router, createPinia(), [VueQueryPlugin, { queryClient }]] } });
  return { router };
}

const nameField = () => screen.getByRole('textbox', { name: 'Pharmacy name' });
const jobFunctionField = () => screen.getByRole('combobox', { name: 'Your job function' });
const submit = () => screen.getByRole('button', { name: 'Register pharmacy' });

/** Types a name (pasted, so a long one is quick) and picks a job function by its label. */
async function fillIn(user: ReturnType<typeof userEvent.setup>, name: string, jobFunction: string) {
  await user.click(nameField());
  await user.paste(name);
  await user.selectOptions(jobFunctionField(), jobFunction);
}

test('the page says what registering means and asks for the pharmacy name and the job function', async () => {
  await renderPage();

  const main = within(screen.getByRole('main'));
  const heading = main.getByRole('heading', { name: 'Register a pharmacy', level: 1 });
  expect(heading).toHaveClass('text-title');
  expect(
    main.getByText('You will manage the pharmacy: invite your team and assign their training.'),
  ).toBeInTheDocument();
  const form = main.getByRole('form', { name: 'Register a pharmacy' });
  expect(form).toHaveAttribute('novalidate');
  expect(nameField()).toHaveAttribute('id', 'pharmacy-name');
  expect(nameField()).toHaveAttribute('autocomplete', 'organization');
  expect(nameField()).toHaveValue('');
  expect(jobFunctionField()).toHaveAttribute('id', 'pharmacy-job-function');
  expect(jobFunctionField()).toHaveValue('');
  const options = within(jobFunctionField()).getAllByRole('option');
  expect(options.map((option) => [option.textContent.trim(), option.getAttribute('value')])).toEqual([
    ['Choose…', ''],
    ['Pharmacist', 'Pharmacist'],
    ['PTA (pharmaceutical-technical assistant)', 'Pta'],
    ['PKA (pharmaceutical-commercial employee)', 'Pka'],
    ['Trainee', 'Trainee'],
    ['Other', 'Other'],
  ]);
  expect(within(jobFunctionField()).getByRole('option', { name: 'Choose…' })).toBeDisabled();
  expect(submit()).toHaveAttribute('type', 'submit');
  expect(submit()).toBeEnabled();
});

test('an empty form is refused with a message below each field, and nothing is sent', async () => {
  const user = userEvent.setup();
  await renderPage();

  await user.click(submit());

  const alerts = await screen.findAllByRole('alert');
  expect(alerts.map((alert) => alert.textContent.trim())).toEqual([
    'Give the pharmacy a name.',
    'Choose what you do in the pharmacy.',
  ]);
  for (const alert of alerts) {
    expect(alert.tagName).toBe('P');
    expect(alert).toHaveClass('text-caption', 'text-destructive');
  }
  expect(nameField()).toHaveAttribute('aria-invalid', 'true');
  expect(nameField()).toHaveAccessibleDescription('Give the pharmacy a name.');
  expect(jobFunctionField()).toHaveAttribute('aria-invalid', 'true');
  expect(jobFunctionField()).toHaveAccessibleDescription('Choose what you do in the pharmacy.');
  expect(posted()).toEqual([]);
});

test('a name of spaces only is no name', async () => {
  const user = userEvent.setup();
  await renderPage();
  await fillIn(user, '   ', 'Pharmacist');

  await user.click(submit());

  expect(await screen.findByRole('alert')).toHaveTextContent('Give the pharmacy a name.');
  expect(nameField()).toHaveAccessibleDescription('Give the pharmacy a name.');
  expect(jobFunctionField()).not.toHaveAttribute('aria-invalid', 'true');
  expect(posted()).toEqual([]);
});

// PharmacyName on the host: at most 120 characters after trimming.
test('a name of 121 characters is refused, and nothing is sent', async () => {
  const user = userEvent.setup();
  await renderPage();
  await fillIn(user, 'A'.repeat(121), 'Pharmacist');

  await user.click(submit());

  expect(await screen.findByRole('alert')).toHaveTextContent("A pharmacy's name is at most 120 characters.");
  expect(posted()).toEqual([]);
});

test('a name of 120 characters is registered', async () => {
  const user = userEvent.setup();
  await renderPage();
  await fillIn(user, 'A'.repeat(120), 'Pharmacist');

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
  await fillIn(user, 'Apotheke\u0000am Markt', 'Pharmacist');

  await user.click(submit());

  expect(await screen.findByRole('alert')).toHaveTextContent("A pharmacy's name cannot contain a nul character.");
  expect(posted()).toEqual([]);
});

test('the page posts the trimmed name and the chosen job function', async () => {
  const user = userEvent.setup();
  await renderPage();
  await fillIn(user, '  Apotheke am Markt  ', 'PTA (pharmaceutical-technical assistant)');

  await user.click(submit());

  await waitFor(() =>
    expect(posted()).toEqual([
      { url: '/api/organisation/pharmacies', body: { name: 'Apotheke am Markt', jobFunction: 'Pta' } },
    ]),
  );
});

test.each([
  [400, 'The pharmacy could not be registered: check its name and your job function.'],
  [403, 'Your account has no name or email address, so it cannot register a pharmacy.'],
  [500, 'The pharmacy could not be registered. Try again.'],
])(
  'a registration the host answers with %i is reported, and the user stays with what they entered',
  async (status, message) => {
    const user = userEvent.setup();
    registration = async () => Response.json({ title: 'Refused.' }, { status });
    const { router } = await renderPage();
    await fillIn(user, 'Apotheke am Markt', 'Pharmacist');

    await user.click(submit());

    await waitFor(() => expect(notify.failure).toHaveBeenCalledWith(message));
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
  await fillIn(user, 'Apotheke am Markt', 'Pharmacist');

  await user.click(submit());

  await waitFor(() => expect(submit()).toBeDisabled());
  answer(Response.json(created, { status: 201 }));
  await waitFor(() => expect(router.currentRoute.value.path).toBe('/organisation'));
});
