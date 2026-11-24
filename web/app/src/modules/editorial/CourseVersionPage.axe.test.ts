import { QueryClient, VueQueryPlugin } from '@tanstack/vue-query';
import { cleanup, render, screen, within } from '@testing-library/vue';
import { afterEach, beforeEach, describe, expect, test, vi } from 'vitest';
import { axe } from 'vitest-axe';
import { createMemoryHistory } from 'vue-router';
import { signedInAsDeveloper } from '@/auth/signedInAsDeveloper';
import { languages, setLanguage, t } from '@/i18n';
import { createAppRouter } from '@/router';
import * as data from './CourseVersionPage.data';
import CourseVersionPage from './CourseVersionPage.vue';

// The screen's axe check, generated with it. It runs whatever the screen's own component test does, so a check skipped
// or commented out there never leaves the screen unchecked. It asks nothing of what the screen shows, only that axe
// finds no violations in it: what the screen does is its own tests'.

// The page renders on its own, without the host: a query client of its own holds nothing, since the page writes the
// version's cache when it reorders. Each hook of the generated client answers as if its
// request were still on its way or, once the page is shown with data, with its example in the data file: examples, or
// emptyExamples for the page with nothing to show.
const shown = vi.hoisted(() => ({ examples: undefined as Record<string, unknown> | undefined }));
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
  const idle = () => ({
    data: ref(undefined),
    error: ref(null),
    isIdle: ref(true),
    isPending: ref(false),
    isSuccess: ref(false),
    isError: ref(false),
    status: ref('idle'),
    mutate: vi.fn(),
    mutateAsync: vi.fn(),
    reset: vi.fn(),
  });
  const generated = await importOriginal<Record<string, unknown>>();
  const isMutation = (hook: string) => `get${hook.slice('use'.length)}MutationOptions` in generated;
  return Object.fromEntries(
    Object.entries(generated).map(([name, value]) => {
      if (!/^use[A-Z]/.test(name)) {
        return [name, value];
      }
      return [name, isMutation(name) ? idle : () => answer(name)];
    }),
  );
});

const { examples } = data;
// A version in review and a published one (#26): the workflow's other steps, each with no dialog open.
const workflowExamples = [
  ['in review', data.inReviewExamples],
  ['published', data.publishedExamples],
] as const;
// A data file written before it had an empty example has none: the page is then not checked with nothing to show.
const emptyExamples = (data as { emptyExamples?: Record<string, unknown> }).emptyExamples ?? {};

// Whatever else asks the host waits for an answer that never comes.
vi.stubGlobal('fetch', () => new Promise<Response>(() => undefined));

// Without Vitest's globals, Testing Library does not unmount between tests by itself.
afterEach(cleanup);
afterEach(() => {
  shown.examples = undefined;
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
const versionId = '0198c1a0-0000-7000-8000-0000000000c1';

/** The steps the workflow section offers a version of each status the examples show. */
const stepsShown: Record<string, ('submit' | 'publish' | 'return' | 'retire')[]> = {
  Draft: ['submit'],
  InReview: ['publish', 'return'],
  Published: ['retire'],
};

async function renderPage() {
  const router = createAppRouter(createMemoryHistory());
  await router.push(`/editorial/versions/${versionId}`);

  const { container } = render(CourseVersionPage, {
    global: { plugins: [router, signedInAsDeveloper, [VueQueryPlugin, { queryClient: new QueryClient() }]] },
  });
  // The screen speaks the language of the moment: axe checks what that language's user hears. Shown with a version, it
  // is headed by the version's title and names its chapters; while it loads, it shows skeletons only.
  const shownVersion = shown.examples?.useGetEditorialCourseVersionsId as { title: string; status: string } | undefined;
  if (shownVersion !== undefined) {
    expect(screen.getByRole('heading', { level: 1, name: shownVersion.title })).toBeInTheDocument();
    expect(screen.getByRole('heading', { level: 2, name: t('editorial.chapters.title') })).toBeInTheDocument();
    // The workflow section shows the version's steps (#26), and axe checks them: a draft's checklist and Submit, a
    // version in review's checklist, Publish and Return, a published one's Retire. No dialog is open.
    expect(screen.getByRole('heading', { level: 2, name: t('editorial.workflow.title') })).toBeInTheDocument();
    const steps = stepsShown[shownVersion.status] ?? [];
    expect(steps).not.toHaveLength(0);
    for (const step of steps) {
      expect(screen.getByRole('button', { name: t(`editorial.workflow.${step}`) })).toBeInTheDocument();
    }
    expect(screen.queryByRole('list', { name: t('editorial.workflow.checklist.title') }) !== null).toBe(
      shownVersion.status !== 'Published',
    );
    expect(screen.queryByRole('dialog')).toBeNull();
    // The return history (#201) shows in every status, and axe checks it: its rows where the example has returns, its
    // empty state where it has none.
    const history = screen.getByRole('region', { name: t('editorial.returns.title') });
    const returns = shown.examples?.useGetEditorialCourseVersionsIdReturns as { items: unknown[] } | undefined;
    if (returns !== undefined) {
      const rows = within(within(history).getByRole('table')).getAllByRole('row').slice(1);
      if (returns.items.length > 0) {
        expect(rows).toHaveLength(returns.items.length);
      } else {
        expect(within(history).getByText(t('editorial.returns.empty'))).toBeInTheDocument();
      }
    }
  }
  return container;
}

// Each language in turn: the texts differ, and so may what axe finds in them.
describe.each(languages)('in %s', (language) => {
  beforeEach(() => {
    setLanguage(language, { remember: false });
  });

  test('the course version page has no accessibility violations axe can find', async () => {
    const container = await renderPage();

    expect(await axe(container)).toHaveNoViolations();
  });

  test.each(themes)(
    'the course version page has no accessibility violations axe can find in the $name theme',
    async ({ switchOn }) => {
      switchOn();
      const container = await renderPage();

      expect(await axe(container)).toHaveNoViolations();
    },
  );

  // Until CourseVersionPage.data.ts has an example (AGENTS.md), the page is checked only as it loads.
  test.runIf(Object.keys(examples).length > 0)('the course version page passes axe with its example data', async () => {
    shown.examples = examples;
    const container = await renderPage();

    expect(await axe(container)).toHaveNoViolations();
  });

  test.runIf(Object.keys(examples).length > 0).each(themes)(
    'the course version page passes axe with its example data in the $name theme',
    async ({ switchOn }) => {
      shown.examples = examples;
      switchOn();
      const container = await renderPage();

      expect(await axe(container)).toHaveNoViolations();
    },
  );

  test.each(workflowExamples)(
    'the course version page passes axe with a version %s',
    async (_status, shownExamples) => {
      shown.examples = shownExamples;
      const container = await renderPage();

      expect(await axe(container)).toHaveNoViolations();
    },
  );

  test.each(
    workflowExamples.flatMap(([status, shownExamples]) =>
      themes.map(({ name, switchOn }) => ({ status, shownExamples, name, switchOn })),
    ),
  )(
    'the course version page passes axe with a version $status in the $name theme',
    async ({ shownExamples, switchOn }) => {
      shown.examples = shownExamples;
      switchOn();
      const container = await renderPage();

      expect(await axe(container)).toHaveNoViolations();
    },
  );

  // Until CourseVersionPage.data.ts has an empty example (AGENTS.md), the page is not checked with nothing to show.
  test.runIf(Object.keys(emptyExamples).length > 0)(
    'the course version page passes axe when it has nothing to show',
    async () => {
      shown.examples = emptyExamples;
      const container = await renderPage();

      expect(await axe(container)).toHaveNoViolations();
    },
  );

  test.runIf(Object.keys(emptyExamples).length > 0).each(themes)(
    'the course version page passes axe when it has nothing to show in the $name theme',
    async ({ switchOn }) => {
      shown.examples = emptyExamples;
      switchOn();
      const container = await renderPage();

      expect(await axe(container)).toHaveNoViolations();
    },
  );
});
