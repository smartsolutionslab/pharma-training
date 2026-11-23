import { QueryClient, VueQueryPlugin } from '@tanstack/vue-query';
import { cleanup, fireEvent, render, screen } from '@testing-library/vue';
import userEvent from '@testing-library/user-event';
import { createPinia } from 'pinia';
import { afterEach, beforeEach, describe, expect, test, vi } from 'vitest';
import { axe } from 'vitest-axe';
import { createMemoryHistory } from 'vue-router';
import { signedInAsDeveloper } from '@/auth/signedInAsDeveloper';
import { languages, setLanguage, t } from '@/i18n';
import { createAppRouter } from '@/router';
import * as data from './AssignCoursePage.data';
import AssignCoursePage from './AssignCoursePage.vue';

// The screen's axe check, generated with it. It runs whatever the screen's own component test does, so a check skipped
// or commented out there never leaves the screen unchecked. It asks nothing of what the screen shows, only that axe
// finds no violations in it: what the screen does is its own tests'.

// The page renders on its own, without a query client or the host. Each hook of the generated client answers as if its
// request were still on its way or, once the page is shown with data, with its example in the data file: examples, or
// emptyExamples for the page with nothing to show.
// A mutation resolves with its answer in answers, once it is sent: the steps after it can be checked too.
const shown = vi.hoisted(() => ({
  examples: undefined as Record<string, unknown> | undefined,
  answers: {} as Record<string, unknown>,
}));
vi.mock('@/api/generated', async (importOriginal) => {
  const { ref } = await import('vue');
  const state = (data: unknown, loading: boolean) => ({
    data: ref(data),
    error: ref(null),
    isLoading: ref(loading),
    isPending: ref(loading),
    isFetching: ref(loading),
    isSuccess: ref(!loading),
    isError: ref(false),
    status: ref(loading ? 'pending' : 'success'),
    refetch: vi.fn(),
    mutate: vi.fn(),
    mutateAsync: vi.fn(),
  });
  const answer = (hook: string) =>
    shown.examples !== undefined && hook in shown.examples
      ? state(shown.examples[hook], false)
      : state(undefined, true);
  // A mutation nobody has triggered is idle, not saving: the page shows as it first does.
  const idle = (hook: string) => ({
    data: ref(undefined),
    error: ref(null),
    isIdle: ref(true),
    isPending: ref(false),
    isSuccess: ref(false),
    isError: ref(false),
    status: ref('idle'),
    mutate: vi.fn(),
    mutateAsync: vi.fn(async () => shown.answers[hook]),
    reset: vi.fn(),
  });
  const generated = await importOriginal<Record<string, unknown>>();
  const isMutation = (hook: string) => `get${hook.slice('use'.length)}MutationOptions` in generated;
  return Object.fromEntries(
    Object.entries(generated).map(([name, value]) => {
      if (!/^use[A-Z]/.test(name)) {
        return [name, value];
      }
      return [name, isMutation(name) ? () => idle(name) : () => answer(name)];
    }),
  );
});

const { examples } = data;
// A data file written before it had an empty example has none: the page is then not checked with nothing to show.
const emptyExamples = (data as { emptyExamples?: Record<string, unknown> }).emptyExamples ?? {};

// Whatever else asks the host waits for an answer that never comes.
vi.stubGlobal('fetch', () => new Promise<Response>(() => undefined));

// Without Vitest's globals, Testing Library does not unmount between tests by itself.
afterEach(cleanup);
afterEach(() => {
  shown.examples = undefined;
  shown.answers = {};
});

// The app's other themes: each check runs again in each, switched on as the app switches it before the screen renders.
const themes = [
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
});

// The router is on the screen's route before it mounts, and the screen has rendered when render returns.
// The screen renders for the local user developer, signed in, without the sign-in plugin: the check sees the screen as
// the developer does.
async function renderPage() {
  const router = createAppRouter(createMemoryHistory());
  await router.push('/compliance/assign');

  // The page reads the current pharmacy (a Pinia store over the memberships) and the query client (it reads the
  // members of a job function page by page, and marks the catalog stale after a refusal).
  const { container } = render(AssignCoursePage, {
    global: {
      plugins: [router, createPinia(), [VueQueryPlugin, { queryClient: new QueryClient() }], signedInAsDeveloper],
    },
  });
  // The screen speaks the language of the moment: axe checks what that language's user hears.
  expect(
    screen.getByRole('heading', { level: 1, name: t('modules.compliance.pages.assign.title') }),
  ).toBeInTheDocument();
  return container;
}

/** The assignment's answer: Anna assigned; Ben refused in the UI's own words, Carla in the host's. */
const assignment = {
  assigned: [
    { personId: '0198c1a0-0000-7000-8000-0000000000e1', assignmentId: '0198c1a0-0000-7000-8000-0000000000f1' },
  ],
  refused: [
    {
      personId: '0198c1a0-0000-7000-8000-0000000000e2',
      code: 'compliance.assignment-already-active',
      message: 'Die Schulung ist bereits aktiv zugewiesen.',
    },
    {
      personId: '0198c1a0-0000-7000-8000-0000000000e3',
      code: 'compliance.something-new',
      message: 'Etwas Neues ist geschehen.',
    },
  ],
};

/** With the example data, the course chosen and Anna, Ben and Carla selected: on to the due date step. */
async function toDueStep(user: ReturnType<typeof userEvent.setup>) {
  await user.click(
    screen.getByRole('button', { name: t('compliance.assign.course.choose', { title: 'Hygiene in der Apotheke' }) }),
  );
  await user.click(screen.getByRole('button', { name: t('compliance.assign.next') }));
  await screen.findByRole('heading', { level: 2, name: t('compliance.assign.learners.title') });
  for (const name of ['Anna Albers', 'Ben Becker', 'Carla Claasen']) {
    await user.click(await screen.findByRole('checkbox', { name: t('compliance.assign.learners.select', { name }) }));
  }
  await user.click(screen.getByRole('button', { name: t('compliance.assign.next') }));
  await screen.findByRole('heading', { level: 2, name: t('compliance.assign.due.title') });
}

/** On from the due date step, with a due date and the yearly recurrence, to the review. */
async function toReviewStep(user: ReturnType<typeof userEvent.setup>) {
  await toDueStep(user);
  await fireEvent.update(screen.getByLabelText(t('compliance.assign.due.date')), '2027-03-15');
  await user.click(screen.getByRole('checkbox', { name: t('compliance.assign.due.repeat') }));
  await user.click(screen.getByRole('button', { name: t('compliance.assign.next') }));
  await screen.findByRole('heading', { level: 2, name: t('compliance.assign.review.title') });
}

// Each language in turn: the texts differ, and so may what axe finds in them.
describe.each(languages)('in %s', (language) => {
  beforeEach(() => {
    setLanguage(language, { remember: false });
  });

  test('the Assign a course page has no accessibility violations axe can find', async () => {
    const container = await renderPage();

    expect(await axe(container)).toHaveNoViolations();
  });

  test.each(themes)(
    'the Assign a course page has no accessibility violations axe can find in the $name theme',
    async ({ switchOn }) => {
      switchOn();
      const container = await renderPage();

      expect(await axe(container)).toHaveNoViolations();
    },
  );

  // Until AssignCoursePage.data.ts has an example (AGENTS.md), the page is checked only as it loads.
  test.runIf(Object.keys(examples).length > 0)(
    'the Assign a course page passes axe with its example data',
    async () => {
      shown.examples = examples;
      const container = await renderPage();

      expect(await axe(container)).toHaveNoViolations();
    },
  );

  test.runIf(Object.keys(examples).length > 0).each(themes)(
    'the Assign a course page passes axe with its example data in the $name theme',
    async ({ switchOn }) => {
      shown.examples = examples;
      switchOn();
      const container = await renderPage();

      expect(await axe(container)).toHaveNoViolations();
    },
  );

  // Until AssignCoursePage.data.ts has an empty example (AGENTS.md), the page is not checked with nothing to show.
  test.runIf(Object.keys(emptyExamples).length > 0)(
    'the Assign a course page passes axe when it has nothing to show',
    async () => {
      shown.examples = emptyExamples;
      const container = await renderPage();

      expect(await axe(container)).toHaveNoViolations();
    },
  );

  test.runIf(Object.keys(emptyExamples).length > 0).each(themes)(
    'the Assign a course page passes axe when it has nothing to show in the $name theme',
    async ({ switchOn }) => {
      shown.examples = emptyExamples;
      switchOn();
      const container = await renderPage();

      expect(await axe(container)).toHaveNoViolations();
    },
  );

  // The steps after the first, with the example data: the course chosen, then the learners shown.
  test.each([{ name: 'light', switchOn: () => undefined }, ...themes])(
    'the Assign a course page passes axe on the learners step with its example data, in the $name theme',
    async ({ switchOn }) => {
      shown.examples = examples;
      switchOn();
      const user = userEvent.setup();
      const container = await renderPage();

      await user.click(
        screen.getByRole('button', {
          name: t('compliance.assign.course.choose', { title: 'Hygiene in der Apotheke' }),
        }),
      );
      await user.click(screen.getByRole('button', { name: t('compliance.assign.next') }));
      await screen.findByRole('heading', { level: 2, name: t('compliance.assign.learners.title') });

      expect(await axe(container)).toHaveNoViolations();
    },
  );

  // The course step as it shows after Next without a course: refused, with its alert.
  test.each([{ name: 'light', switchOn: () => undefined }, ...themes])(
    'the Assign a course page has no accessibility violations axe can find with Next refused, in the $name theme',
    async ({ switchOn }) => {
      shown.examples = examples;
      switchOn();
      const user = userEvent.setup();
      const container = await renderPage();

      await user.click(screen.getByRole('button', { name: t('compliance.assign.next') }));
      await screen.findByRole('alert');

      expect(await axe(container)).toHaveNoViolations();
    },
  );
  // Follow-ups of #37 (#239): the steps after the learners, with the example data, the day fixed.
  describe('after the learners', () => {
    beforeEach(() => {
      vi.useFakeTimers({ toFake: ['Date'] });
      vi.setSystemTime(new Date('2026-11-23T11:00:00Z'));
    });
    afterEach(() => {
      vi.useRealTimers();
    });

    test.each([{ name: 'light', switchOn: () => undefined }, ...themes])(
      'the Assign a course page passes axe on the due date step with a field error, in the $name theme',
      async ({ switchOn }) => {
        shown.examples = examples;
        switchOn();
        const user = userEvent.setup();
        const container = await renderPage();
        await toDueStep(user);

        await user.click(screen.getByRole('button', { name: t('compliance.assign.next') }));
        expect(await screen.findByRole('alert')).toHaveTextContent(t('compliance.assign.due.required'));

        expect(await axe(container)).toHaveNoViolations();
      },
    );

    test.each([{ name: 'light', switchOn: () => undefined }, ...themes])(
      'the Assign a course page passes axe on the review, in the $name theme',
      async ({ switchOn }) => {
        shown.examples = examples;
        switchOn();
        const user = userEvent.setup();
        const container = await renderPage();

        await toReviewStep(user);
        expect(screen.getAllByRole('term')).not.toEqual([]);

        expect(await axe(container)).toHaveNoViolations();
      },
    );

    test.each([{ name: 'light', switchOn: () => undefined }, ...themes])(
      'the Assign a course page passes axe on the result with people refused, in the $name theme',
      async ({ switchOn }) => {
        shown.examples = examples;
        shown.answers = { usePostCompliancePharmaciesIdAssignments: assignment };
        switchOn();
        const user = userEvent.setup();
        const container = await renderPage();
        await toReviewStep(user);

        await user.click(screen.getByRole('button', { name: t('compliance.assign.submit') }));
        await screen.findByRole('heading', { level: 2, name: t('compliance.assign.result.title') });
        expect(screen.getByRole('list', { name: t('compliance.assign.result.refused') })).toBeInTheDocument();

        expect(await axe(container)).toHaveNoViolations();
      },
    );
  });
});
