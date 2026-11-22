import { QueryClient, VueQueryPlugin } from '@tanstack/vue-query';
import { cleanup, render, screen, waitFor, within } from '@testing-library/vue';
import userEvent from '@testing-library/user-event';
import { afterEach, beforeEach, describe, expect, test, vi } from 'vitest';
import { createMemoryHistory } from 'vue-router';
import type { EditorialCourseVersionDto, EditorialTestBlueprintDto } from '@/api/generated';
import { setLanguage, t } from '@/i18n';
import { createAppRouter } from '@/router';
import CourseTestPage from './CourseTestPage.vue';
import { refusal, versionId, versionOf } from './courseTestFixtures';

// The test's blueprint (#25, S2 and T3): its count of questions, passing score and validity, shown as a list of terms;
// in a draft changed in a form of its own (PUT /editorial/course-versions/{id}/blueprint), checked before it is sent.

const notify = vi.hoisted(() => ({ success: vi.fn(), failure: vi.fn() }));
vi.mock('@/lib/notify', () => ({ notify }));

// The host: GET /editorial/course-versions/{id} answers the version; PUT …/blueprint answers what the test says, by
// default 200, taking the blueprint over as the host would.
const host = vi.fn<(input: RequestInfo | URL, init?: RequestInit) => Promise<Response>>();
vi.stubGlobal('fetch', host);

let version: EditorialCourseVersionDto;
let saving: (body: EditorialTestBlueprintDto) => Promise<Response>;

beforeEach(() => {
  version = versionOf('Draft');
  saving = async (body) => {
    version = { ...version, blueprint: body };
    return Response.json({});
  };
  host.mockImplementation(async (input, init) => {
    const url = new URL(String(input), 'http://localhost');
    const method = init?.method ?? 'GET';
    if (method === 'GET' && url.pathname === `/api/editorial/course-versions/${versionId}`) {
      return Response.json(version);
    }
    if (method === 'PUT' && url.pathname === `/api/editorial/course-versions/${versionId}/blueprint`) {
      return saving(JSON.parse(String(init?.body)) as EditorialTestBlueprintDto);
    }
    return new Response('', { status: 404 });
  });
  notify.success.mockReset();
  notify.failure.mockReset();
});
afterEach(() => host.mockReset());
afterEach(cleanup);

/** What the page sent to change the version, as the host received it. */
const changes = () =>
  host.mock.calls
    .filter(([, init]) => (init?.method ?? 'GET') !== 'GET')
    .map(([input, init]) => ({
      method: init?.method,
      path: new URL(String(input), 'http://localhost').pathname.replace(/^\/api/, ''),
      body: init?.body === undefined ? undefined : (JSON.parse(String(init.body)) as unknown),
    }));
const reads = () => host.mock.calls.filter(([, init]) => (init?.method ?? 'GET') === 'GET');

async function renderPage() {
  const router = createAppRouter(createMemoryHistory());
  await router.push(`/editorial/versions/${versionId}/test`);
  const queryClient = new QueryClient({ defaultOptions: { queries: { retry: false }, mutations: { retry: false } } });
  const rendered = render(CourseTestPage, { global: { plugins: [router, [VueQueryPlugin, { queryClient }]] } });
  await screen.findByRole('heading', { level: 1, name: t('editorial.test.heading', { title: version.title }) });
  return rendered;
}

/** The blueprint's section, below its heading. */
const section = () => {
  const heading = screen.getByRole('heading', { level: 2, name: t('editorial.test.blueprint.title') });
  return heading.closest('section') ?? document.body;
};

/** What the blueprint's list of terms shows, term by term, its spaces as a reader sees them. */
function shown(): Record<string, string> {
  const list = section().querySelector('dl');
  expect(list).not.toBeNull();
  return Object.fromEntries(
    [...(list?.querySelectorAll('dt') ?? [])].map((term) => [
      (term.textContent ?? '').trim(),
      (term.nextElementSibling?.textContent ?? '').replace(/\s+/g, ' ').trim(),
    ]),
  );
}

const field = {
  questions: () => screen.getByLabelText(t('editorial.test.blueprint.questions')),
  passingScore: () => screen.getByLabelText(t('editorial.test.blueprint.passingScore')),
  validity: () => screen.getByLabelText(t('editorial.test.blueprint.validity')),
};

async function openForm(user: ReturnType<typeof userEvent.setup>) {
  await user.click(screen.getByRole('button', { name: t('editorial.test.blueprint.edit') }));
  return await screen.findByRole('button', { name: t('editorial.test.blueprint.submit') });
}

async function enter(user: ReturnType<typeof userEvent.setup>, questions: string, percent: string, months: string) {
  for (const [input, value] of [
    [field.questions(), questions],
    [field.passingScore(), percent],
    [field.validity(), months],
  ] as const) {
    await user.clear(input);
    if (value !== '') {
      await user.type(input, value);
    }
  }
}

test('the blueprint shows its count, passing score as a percentage and validity in months', async () => {
  await renderPage();

  expect(shown()).toEqual({
    'Anzahl Fragen': '10',
    'Bestehensgrenze (%)': '80 %',
    'Gültigkeit (Monate)': '12 Monate',
  });
});

test('the blueprint shows its count, passing score as a percentage and validity in months, in English', async () => {
  await renderPage();

  setLanguage('en-GB', { remember: false });

  await screen.findByRole('heading', { level: 2, name: 'Test blueprint' });
  expect(shown()).toEqual({
    'Number of questions': '10',
    'Passing score (%)': '80%',
    'Validity (months)': '12 months',
  });
});

// The host types its numbers as number | string.
test('a blueprint the host sends as strings is read as numbers, one month in the singular', async () => {
  version = versionOf('Draft', { blueprint: { questions: '1', passingScorePercent: '0', validityMonths: '1' } });
  await renderPage();

  expect(shown()).toEqual({
    [t('editorial.test.blueprint.questions')]: '1',
    [t('editorial.test.blueprint.passingScore')]: '0 %',
    [t('editorial.test.blueprint.validity')]: 'ein Monat',
  });
});

describe('in a draft', () => {
  test("the form opens with the version's blueprint in number fields with a numeric keyboard", async () => {
    const user = userEvent.setup();
    await renderPage();

    await openForm(user);

    for (const [input, value] of [
      [field.questions(), 10],
      [field.passingScore(), 80],
      [field.validity(), 12],
    ] as const) {
      expect(input).toHaveAttribute('type', 'number');
      expect(input).toHaveAttribute('inputmode', 'numeric');
      expect(input).toHaveValue(value);
    }
    expect(screen.getByRole('button', { name: 'Vorgaben speichern' })).toBeInTheDocument();
  });

  test("a draft's blueprint is saved and the version reloaded", async () => {
    const user = userEvent.setup();
    await renderPage();
    await openForm(user);

    await enter(user, '20', '75', '24');
    await user.click(screen.getByRole('button', { name: t('editorial.test.blueprint.submit') }));

    await waitFor(() =>
      expect(changes()).toEqual([
        {
          method: 'PUT',
          path: `/editorial/course-versions/${versionId}/blueprint`,
          body: { questions: 20, passingScorePercent: 75, validityMonths: 24 },
        },
      ]),
    );
    await waitFor(() => expect(notify.success).toHaveBeenCalledWith(t('editorial.test.blueprint.saved')));
    expect(notify.success).toHaveBeenCalledWith('Die Testvorgaben wurden gespeichert.');
    await waitFor(() =>
      expect(screen.queryByRole('button', { name: t('editorial.test.blueprint.submit') })).not.toBeInTheDocument(),
    );
    await waitFor(() => expect(reads().length).toBeGreaterThan(1));
    await waitFor(() =>
      expect(shown()).toEqual({
        'Anzahl Fragen': '20',
        'Bestehensgrenze (%)': '75 %',
        'Gültigkeit (Monate)': '24 Monate',
      }),
    );
    expect(notify.failure).not.toHaveBeenCalled();
  });

  test('a form left with Abbrechen sends nothing', async () => {
    const user = userEvent.setup();
    await renderPage();
    await openForm(user);

    await enter(user, '20', '75', '24');
    await user.click(screen.getByRole('button', { name: t('editorial.cancel') }));

    await waitFor(() =>
      expect(screen.queryByRole('button', { name: t('editorial.test.blueprint.submit') })).not.toBeInTheDocument(),
    );
    expect(changes()).toEqual([]);
    expect(shown()[t('editorial.test.blueprint.questions')]).toBe('10');
  });

  test('0 questions, 101 percent and 61 months are refused with their messages before anything is sent', async () => {
    const user = userEvent.setup();
    await renderPage();
    await openForm(user);

    await enter(user, '0', '101', '61');
    await user.click(screen.getByRole('button', { name: t('editorial.test.blueprint.submit') }));

    await waitFor(() =>
      expect(screen.getAllByRole('alert').map((alert) => alert.textContent.trim())).toEqual([
        t('editorial.test.blueprint.questionsInvalid', { min: '1', max: '50' }),
        t('editorial.test.blueprint.passingScoreInvalid', { min: '0', max: '100' }),
        t('editorial.test.blueprint.validityInvalid', { min: '1', max: '60' }),
      ]),
    );
    expect(screen.getAllByRole('alert').map((alert) => alert.textContent.trim())).toEqual([
      'Ein Test hat 1 bis 50 Fragen.',
      'Die Bestehensgrenze liegt zwischen 0 und 100 Prozent.',
      'Die Gültigkeit liegt zwischen 1 und 60 Monaten.',
    ]);
    expect(changes()).toEqual([]);
  });

  test('the messages follow a switch to English', async () => {
    const user = userEvent.setup();
    await renderPage();
    await openForm(user);
    await enter(user, '0', '101', '61');
    await user.click(screen.getByRole('button', { name: t('editorial.test.blueprint.submit') }));
    await screen.findByText('Ein Test hat 1 bis 50 Fragen.');

    setLanguage('en-GB', { remember: false });

    expect(await screen.findByText('A test has 1 to 50 questions.')).toBeVisible();
    expect(screen.getByText('The passing score is between 0 and 100 percent.')).toBeVisible();
    expect(screen.getByText('Validity is between 1 and 60 months.')).toBeVisible();
  });

  test('an emptied field asks for a whole number', async () => {
    const user = userEvent.setup();
    await renderPage();
    await openForm(user);

    await enter(user, '', '80', '12');
    await user.click(screen.getByRole('button', { name: t('editorial.test.blueprint.submit') }));

    expect(await screen.findByRole('alert')).toHaveTextContent(t('editorial.test.blueprint.required'));
    expect(screen.getByRole('alert')).toHaveTextContent('Geben Sie eine ganze Zahl an.');
    expect(changes()).toEqual([]);
  });

  // A module's refusal is reported in the host's words, chosen by its code (ApiError.serverMessage).
  test.each([
    [409, 'editorial.not-a-draft', 'Die Version ist kein Entwurf mehr und kann nicht geändert werden.'],
    [400, 'editorial.invalid-validity', 'Die Gültigkeit liegt zwischen 1 und 60 Monaten.'],
  ])('a refusal keeps the form open and reports the host’s message, %i %s', async (status, code, detail) => {
    saving = async () => refusal(status, code, detail);
    const user = userEvent.setup();
    await renderPage();
    await openForm(user);

    await enter(user, '20', '75', '24');
    await user.click(screen.getByRole('button', { name: t('editorial.test.blueprint.submit') }));

    await waitFor(() => expect(notify.failure).toHaveBeenCalledWith(detail));
    expect(notify.success).not.toHaveBeenCalled();
    expect(screen.getByRole('button', { name: t('editorial.test.blueprint.submit') })).toBeInTheDocument();
    expect(field.questions()).toHaveValue(20);
    expect(field.passingScore()).toHaveValue(75);
    expect(field.validity()).toHaveValue(24);
    // The version is read again after a refusal too: what the page shows may no longer be so.
    await waitFor(() => expect(reads().length).toBeGreaterThan(1));
  });

  test("a failure without the host's words reports the catalog's", async () => {
    saving = async () => new Response('', { status: 500 });
    const user = userEvent.setup();
    await renderPage();
    await openForm(user);

    await user.click(screen.getByRole('button', { name: t('editorial.test.blueprint.submit') }));

    await waitFor(() => expect(notify.failure).toHaveBeenCalledWith(t('editorial.test.blueprint.saveFailed')));
    expect(notify.failure).toHaveBeenCalledWith('Die Testvorgaben konnten nicht gespeichert werden.');
    expect(screen.getByRole('button', { name: t('editorial.test.blueprint.submit') })).toBeInTheDocument();
  });

  test('speaks German by default', async () => {
    await renderPage();

    expect(screen.getByRole('heading', { level: 2, name: 'Testvorgaben' })).toBeInTheDocument();
    expect(within(section()).getByRole('button', { name: 'Vorgaben ändern' })).toBeInTheDocument();
  });
});

describe.each(['InReview' as const, 'Published' as const])('a version %s', (status) => {
  test('a non-draft offers no change, and shows its blueprint', async () => {
    version = versionOf(status);
    const { container } = await renderPage();

    expect(shown()).toEqual({
      'Anzahl Fragen': '10',
      'Bestehensgrenze (%)': '80 %',
      'Gültigkeit (Monate)': '12 Monate',
    });
    expect(screen.queryByRole('button', { name: t('editorial.test.blueprint.edit') })).not.toBeInTheDocument();
    expect(container.querySelector('input')).toBeNull();
  });
});
